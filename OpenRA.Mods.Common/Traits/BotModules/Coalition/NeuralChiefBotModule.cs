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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OpenRA.Mods.Common.Commander.Model;
using OpenRA.Mods.Common.Commander.Staff;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	[TraitLocation(SystemActors.Player)]
	[Desc("Consults a trained network for the chief's stance, replacing the scripted thresholds.",
		"Disabled by default: without a model server answering, the scripted chief keeps command.")]
	public sealed class NeuralChiefBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Base URL of the model server. Empty disables this module entirely.")]
		public readonly string Url = "";

		[Desc("Ticks between consultations. The strategic decision changes on the order of a minute,",
			"so this is deliberately slow - and the measured cost of deciding more often is severe.")]
		public readonly int Interval = 250;

		[Desc("Request timeout in milliseconds. A timeout leaves the scripted chief in command for",
			"that cycle rather than stalling the simulation.")]
		public readonly int TimeoutMilliseconds = 2000;

		[Desc("Log every consultation, not just changes.")]
		public readonly bool Verbose = false;

		public override object Create(ActorInitializer init) { return new NeuralChiefBotModule(this); }
	}

	/// <summary>
	/// <para>
	/// Asks a trained network what stance to take, and hands the answer to the staff.
	/// </para>
	/// <para>
	/// The network is consulted out of process over HTTP, which is the right trade at this cadence:
	/// a strategic decision is made a few times a minute, so the round trip costs nothing, and a
	/// model that crashes or hangs cannot take the simulation with it. A timeout simply leaves the
	/// scripted chief in command for that cycle, which is the correct fallback - the bot plays worse
	/// rather than not at all.
	/// </para>
	/// <para>
	/// It is off unless a URL is configured, and that matters more than it sounds. Everything the
	/// network improves is measured against the scripted chief, so the scripted chief has to remain
	/// a working, selectable opponent rather than being deleted the moment something replaces it.
	/// </para>
	/// </summary>
	public sealed class NeuralChiefBotModule : ConditionalTrait<NeuralChiefBotModuleInfo>, IBotTick
	{
		readonly HttpClient http = new();
		CommanderStaffBotModule staff;
		CommanderCalibrationBotModule calibration;
		bool inFlight;
		int lastStance = -1;
		int consulted;
		int disagreements;

		public NeuralChiefBotModule(NeuralChiefBotModuleInfo info)
			: base(info)
		{
			http.Timeout = TimeSpan.FromMilliseconds(Math.Max(100, info.TimeoutMilliseconds));
		}

		/// <summary>
		/// The configured URL with any surrounding quotes stripped.
		/// </summary>
		/// <remarks>
		/// OpenRA's yaml loader does not treat quotes as string delimiters, so `Url: "http://..."`
		/// arrives with the quote characters still attached and produces an invalid URI. The request
		/// then throws and is swallowed by the fallback, so the module looks disabled rather than
		/// misconfigured - which cost a while to find. Tolerating it is cheaper than expecting
		/// everyone to remember.
		/// </remarks>
		string Url => Info.Url?.Trim().Trim('"');

		/// <summary>
		/// The stance the network last returned, or null when it has not answered recently.
		/// </summary>
		/// <remarks>
		/// Deliberately EXPIRES. Without that, one consultation silently governs every later
		/// decision - the chief reads this on each cycle, so a single answer becomes the
		/// permanent stance for the rest of the match. It also hides itself: the telemetry only
		/// logs when the recommendation CHANGES, so a stale answer applied a hundred times reads
		/// in the log as one override.
		/// </remarks>
		public Stance? Recommendation =>
			recommendation.HasValue && world != null
				&& world.WorldTick - recommendedTick <= Info.Interval * 2
					? recommendation
					: null;

		Stance? recommendation;
		int recommendedTick = int.MinValue;
		World world;

		/// <summary>The network's read of the position, 0-1, above a half meaning ahead.</summary>
		public float Value { get; private set; } = 0.5f;

		void IBotTick.BotTick(IBot bot)
		{
			if (IsTraitDisabled || string.IsNullOrEmpty(Url))
				return;

			world = bot.Player.World;
			if (world.WorldTick % Info.Interval != 0 || inFlight)
				return;

			staff ??= bot.Player.PlayerActor.TraitsImplementing<CommanderStaffBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);

			var database = staff?.Database;
			if (database?.Catalogue == null)
				return;

			calibration ??= bot.Player.PlayerActor.TraitsImplementing<CommanderCalibrationBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);

			var state = calibration?.LatestState;
			if (state == null)
				return;

			var resources = bot.Player.PlayerActor.TraitOrDefault<PlayerResources>();
			var idle = bot.Player.PlayerActor.TraitsImplementing<ProductionQueue>()
				.Count(q => q.Enabled && q.CurrentItem() == null);

			var sample = StateExport.Capture(
				database, state, database.Catalogue, world.WorldTick,
				resources?.GetCashAndResources() ?? 0,
				resources?.Earned ?? 0,
				resources?.Spent ?? 0,
				idle);

			inFlight = true;
			_ = ConsultAsync(world, sample);
		}

		async Task ConsultAsync(World world, StateExport.Sample sample)
		{
			try
			{
				var payload = Body(sample);
				using var content = new StringContent(payload, Encoding.UTF8, "application/json");
				using var response = await http.PostAsync($"{Info.Url.TrimEnd('/')}/evaluate", content)
					.ConfigureAwait(false);

				if (!response.IsSuccessStatusCode)
					return;

				var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
				using var document = JsonDocument.Parse(text);
				var root = document.RootElement;

				if (!root.TryGetProperty("stance", out var stanceElement))
					return;

				var stance = stanceElement.GetInt32();
				if (stance < 0 || stance > (int)Stance.Recover)
					return;

				recommendation = (Stance)stance;
				recommendedTick = world.WorldTick;
				if (root.TryGetProperty("value", out var valueElement))
					Value = (float)valueElement.GetDouble();

				consulted++;
				if (root.TryGetProperty("disagreed", out var d) && d.GetBoolean())
					disagreements++;

				if (Info.Verbose || stance != lastStance)
				{
					lastStance = stance;
					CoalitionTelemetry.Log(world,
						$"Neural chief: {recommendation} at value {Value:F2} " +
						$"({consulted} consultations, search differed on {disagreements})");
				}
			}
			catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
			{
				// No server, a slow one, or a malformed answer. The scripted chief keeps command,
				// which is the whole reason this is advisory rather than authoritative.
			}
			finally
			{
				inFlight = false;
			}
		}

		static string Body(StateExport.Sample sample)
		{
			var builder = new StringBuilder(4096);
			builder.Append("{\"entities\":");
			AppendMatrix(builder, sample.Entities);
			builder.Append(",\"regions\":");
			AppendMatrix(builder, sample.Regions);
			builder.Append(",\"globals\":");
			AppendVector(builder, sample.Globals);
			builder.Append('}');
			return builder.ToString();
		}

		static void AppendMatrix(StringBuilder builder, IReadOnlyList<float[]> rows)
		{
			builder.Append('[');
			for (var i = 0; i < rows.Count; i++)
			{
				if (i > 0)
					builder.Append(',');

				AppendVector(builder, rows[i]);
			}

			builder.Append(']');
		}

		static void AppendVector(StringBuilder builder, IReadOnlyList<float> values)
		{
			builder.Append('[');
			for (var i = 0; i < values.Count; i++)
			{
				if (i > 0)
					builder.Append(',');

				builder.Append(values[i].ToString("R", CultureInfo.InvariantCulture));
			}

			builder.Append(']');
		}
	}
}
