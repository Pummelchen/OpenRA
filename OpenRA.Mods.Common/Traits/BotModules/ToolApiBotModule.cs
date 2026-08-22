#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Serves the LLM tool API over HTTP: engine-validated tool endpoints (estimate_engagement, " +
		"plan_routes, score_targets, ...) computed from the live coalition blackboard. The endpoint is " +
		"read-only - tool calls never issue orders - so serving it cannot desync a game. The model " +
		"server forwards the commander's function calls here; without it the commander simply plans " +
		"without tools.")]
	[TraitLocation(SystemActors.Player)]
	public sealed class ToolApiBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("TCP port the tool API listens on (127.0.0.1 only).")]
		public readonly int ToolApiPort = 8766;

		public override object Create(ActorInitializer init) { return new ToolApiBotModule(this, init); }
	}

	public sealed class ToolApiBotModule : ConditionalTrait<ToolApiBotModuleInfo>, IBotTick, INotifyActorDisposing
	{
		readonly ToolApiBotModuleInfo info;
		volatile HttpListener listener;
		Thread serverThread;
		volatile ToolContext cachedContext;
		volatile bool running;
		bool startupFailed;

		public ToolApiBotModule(ToolApiBotModuleInfo info, ActorInitializer init)
			: base(info)
		{
			this.info = info;
		}

		void IBotTick.BotTick(IBot bot)
		{
			// The tool API is best-effort: if the port is unavailable at startup, give up after one
			// attempt so a held port is not retried (and logged) every tick.
			if (IsTraitDisabled || startupFailed)
				return;

			var commander = bot.Player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);
			if (commander == null)
				return;

			// Refresh the tool context on the game thread; the listener thread only reads the latest
			// snapshot, so tool calls never race the game loop.
			cachedContext = commander.BuildToolContext();

			if (running)
				return;

			try
			{
				listener = new HttpListener();
				listener.Prefixes.Add($"http://127.0.0.1:{info.ToolApiPort}/");
				listener.Start();
				running = true;
				serverThread = new Thread(ServeLoop) { IsBackground = true, Name = "ToolApiServer" };
				serverThread.Start();
				CoalitionTelemetry.Log(bot.Player.World, $"Tool API listening on port {info.ToolApiPort}");
			}
			catch
			{
				startupFailed = true;
				CoalitionTelemetry.Log(bot.Player.World, $"Tool API failed to start on port {info.ToolApiPort}");
			}
		}

		void ServeLoop()
		{
			// Capture the listener locally so disposal (which nulls the field) cannot race the loop.
			var server = listener;
			while (running)
			{
				HttpListenerContext ctx;
				try
				{
					ctx = server.GetContext();
				}
				catch
				{
					break; // Listener stopped or failed; the loop ends.
				}

				// Hand requests off to the thread pool so a slow client cannot stall the accept loop.
				ThreadPool.QueueUserWorkItem(_ =>
				{
					try
					{
						Handle(ctx);
					}
					catch
					{
						// A malformed request must never crash the listener.
					}
					finally
					{
						try
						{
							ctx.Response.Close();
						}
						catch
						{
						}
					}
				});
			}
		}

		void INotifyActorDisposing.Disposing(Actor self)
		{
			// Release the HTTP listener so a later game (e.g. another headless run in the same
			// process) can rebind the port. The serve loop exits once running is cleared and the
			// listener is closed.
			running = false;
			try
			{
				listener?.Close();
			}
			catch
			{
				// The listener may already be stopped; disposal is best-effort.
			}

			listener = null;
		}

		void Handle(HttpListenerContext ctx)
		{
			var path = ctx.Request.Url.AbsolutePath;
			if (ctx.Request.HttpMethod == "GET" && path == "/health")
			{
				Write(ctx, "ok", "text/plain");
				return;
			}

			if (ctx.Request.HttpMethod != "POST" || path != "/tools")
			{
				ctx.Response.StatusCode = 404;
				return;
			}

			string body;
			using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8))
				body = reader.ReadToEnd();

			var context = cachedContext;
			var response = context == null
				? "{\"ok\":false,\"error\":\"NOT_READY\",\"message\":\"Engine state is not ready yet.\"}"
				: CommandToolApi.Execute(context, body);

			Write(ctx, response, "application/json");
		}

		static void Write(HttpListenerContext ctx, string content, string contentType)
		{
			var bytes = Encoding.UTF8.GetBytes(content);
			ctx.Response.ContentType = contentType;
			ctx.Response.ContentLength64 = bytes.Length;
			ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
		}
	}
}
