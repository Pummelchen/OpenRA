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

using System;
using System.IO;

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	/// <summary>
	/// Appends strategic decisions to a human-readable replay-style log in the support directory,
	/// so the coalition's reasoning can be audited after a match. The log is held open behind a
	/// single <see cref="StreamWriter"/> instead of reopening the file on every decision, which
	/// avoids an open/append/close syscall per event at high event rates. Each line is flushed
	/// immediately so tail and the headless summary always see the latest decision.
	/// </summary>
	public static class CoalitionTelemetry
	{
		static readonly System.Threading.Lock Sync = new();
		static StreamWriter writer;
		static string writerPath;

		public static void Log(World world, string message)
		{
			var line = $"[{world.WorldTick * world.Timestep / 1000f:000.0}] {message}";
			Console.WriteLine($"AI: {line}");
			try
			{
				lock (Sync)
				{
					var path = Path.Combine(Platform.SupportDir, "ai-telemetry.log");
					if (writer == null || writerPath != path)
					{
						writer?.Dispose();
						writer = new StreamWriter(path, append: true) { AutoFlush = true };
						writerPath = path;
					}

					writer.WriteLine(line);
				}
			}
			catch (IOException)
			{
				// Telemetry is best-effort; the console line above still records the decision.
			}
		}

		/// <summary>Flushes and closes the telemetry stream so the file handle is released.</summary>
		public static void Flush()
		{
			lock (Sync)
			{
				writer?.Flush();
				writer?.Dispose();
				writer = null;
				writerPath = null;
			}
		}
	}
}
