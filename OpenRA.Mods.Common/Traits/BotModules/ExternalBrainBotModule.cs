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

using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Consult an external model server for enhanced team-wide bot decisions. Every allied bot posts an " +
		"identical team snapshot (units, buildings, resources, shared enemy intel); the server caches one team plan " +
		"per consultation round and returns it to every bot, which applies its own share. Requests are asynchronous " +
		"with a timeout, so a missing or slow server falls back to the scripted bot brain. " +
		"Note: model-driven orders are not deterministic and will desync multiplayer games and replays - use in " +
		"single player or with replays disabled.")]
	[TraitLocation(SystemActors.Player)]
	public sealed class ExternalBrainBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Base URL of the model server, e.g. http://127.0.0.1:8765. Leave empty to disable the external brain.")]
		public readonly string ExternalBrainUrl = null;

		[Desc("Minimum real-time gap in seconds between model consultations. The next request (and a fresh radar " +
			"capture) is only sent this long after the previous analysis was received, giving the game a break.")]
		public readonly int ExternalBrainBreakSeconds = 15;

		[Desc("HTTP request timeout in milliseconds. A timed-out request falls back to the scripted brain.")]
		public readonly int ExternalBrainTimeout = 2000;

		public override object Create(ActorInitializer init) { return new ExternalBrainBotModule(this, init); }
	}

	public sealed class ExternalBrainBotModule : ConditionalTrait<ExternalBrainBotModuleInfo>, IBotTick
	{
		sealed class TeamState
		{
			public int Round { get; set; }
			public string Self { get; set; }
			public string ScreenshotPath { get; set; }
			public MemberState[] Team { get; set; }
			public UnitState[] Enemies { get; set; }
		}

		sealed class MemberState
		{
			public string Player { get; set; }
			public int Cash { get; set; }
			public UnitState[] Units { get; set; }
			public UnitState[] Structures { get; set; }
		}

		sealed class UnitState
		{
			public string Type { get; set; }
			public int X { get; set; }
			public int Y { get; set; }
			public int HealthPercent { get; set; }
		}

		readonly ExternalBrainBotModuleInfo info;
		readonly HttpClient http = new();

		static readonly JsonSerializerOptions SnapshotOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

		string pendingPlan;
		bool requestInFlight;
		int lastCompletedTick = int.MinValue;

		public ExternalBrainBotModule(ExternalBrainBotModuleInfo info, ActorInitializer init)
			: base(info)
		{
			this.info = info;
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (IsTraitDisabled || string.IsNullOrEmpty(info.ExternalBrainUrl))
				return;

			var world = bot.Player.World;

			// Apply a plan that was received asynchronously since the last tick.
			if (pendingPlan != null)
			{
				ApplyPlan(bot, pendingPlan);
				pendingPlan = null;
				lastCompletedTick = world.WorldTick;
			}

			var tick = world.WorldTick;

			// Pace consultations: only ask the model again after the configured break has passed since
			// its last analysis arrived, and never while a request is still in flight.
			var breakTicks = world.Timestep > 0 ? (int)(info.ExternalBrainBreakSeconds * 1000.0 / world.Timestep) : info.ExternalBrainBreakSeconds;
			if (requestInFlight || tick - lastCompletedTick < breakTicks)
				return;

			// Capture a fresh full-map radar image right before consulting the model.
			var radar = bot.Player.PlayerActor.TraitsImplementing<RadarCaptureBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);
			radar?.CaptureNow(bot);

			requestInFlight = true;
			var state = BuildSnapshot(bot, tick, breakTicks);
			_ = RequestPlanAsync(state);
		}

		/// <summary>
		/// Serializes the team state into a JSON snapshot. Every allied bot computes the same aggregation
		/// from the shared world state, so the model server receives one consistent picture per round.
		/// </summary>
		static string BuildSnapshot(IBot bot, int tick, int breakTicks)
		{
			var player = bot.Player;
			var world = player.World;

			var team = world.Players
				.Where(p => p.PlayerActor.TraitsImplementing<ModularBot>().Any(b => b.IsEnabled)
					&& player.RelationshipWith(p) == PlayerRelationship.Ally)
				.ToArray();

			var round = breakTicks > 0 ? tick / breakTicks : 0;

			var members = team.Select(p => new MemberState
			{
				Player = p.InternalName,
				Cash = p.PlayerActor.TraitOrDefault<PlayerResources>()?.GetCashAndResources() ?? 0,
				Units = TeamActors(world, p, false).Select(a => new UnitState
				{
					Type = a.Info.Name,
					X = a.Location.X,
					Y = a.Location.Y,
					HealthPercent = HealthPercent(a)
				}).ToArray(),
				Structures = TeamActors(world, p, true).Select(a => new UnitState
				{
					Type = a.Info.Name,
					X = a.Location.X,
					Y = a.Location.Y,
					HealthPercent = HealthPercent(a)
				}).ToArray()
			}).ToArray();

			// Enemy intel is shared through allied shroud: an enemy is reported if any team member has
			// explored its position (radar-style team awareness).
			var enemies = world.Actors
				.Where(a => a.IsInWorld && !a.IsDead && a.Owner != player
					&& player.RelationshipWith(a.Owner) == PlayerRelationship.Enemy
					&& team.Any(ally => ally.Shroud.IsExplored(a.CenterPosition)))
				.Select(a => new UnitState
				{
					Type = a.Info.Name,
					X = a.Location.X,
					Y = a.Location.Y,
					HealthPercent = HealthPercent(a)
				})
				.ToArray();

			var state = new TeamState
			{
				Round = round,
				Self = player.InternalName,
				ScreenshotPath = player.PlayerActor.TraitsImplementing<RadarCaptureBotModule>()
					.Select(m => m.LastCapturePath)
					.FirstOrDefault(path => path != null),
				Team = members,
				Enemies = enemies
			};

			return JsonSerializer.Serialize(state, SnapshotOptions);
		}

		static IOrderedEnumerable<Actor> TeamActors(World world, Player owner, bool structures)
		{
			return world.Actors
				.Where(a => a.IsInWorld && !a.IsDead && a.Owner == owner && a.Info.HasTraitInfo<BuildingInfo>() == structures)
				.OrderBy(a => a.ActorID);
		}

		static int HealthPercent(Actor a)
		{
			var health = a.TraitOrDefault<IHealth>();
			return health == null ? 100 : health.HP * 100 / health.MaxHP;
		}

		/// <summary>Posts the snapshot to the model server. The result is consumed on the game thread.</summary>
		async Task RequestPlanAsync(string state)
		{
			try
			{
				using var timeout = new CancellationTokenSource(info.ExternalBrainTimeout);
				using var content = new StringContent(state, Encoding.UTF8, "application/json");
				var response = await http.PostAsync(info.ExternalBrainUrl, content, timeout.Token).ConfigureAwait(false);
				var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
				pendingPlan = body;
			}
			catch
			{
				// The scripted brain continues to make decisions while the model server is unavailable.
			}
			finally
			{
				requestInFlight = false;
			}
		}

		/// <summary>Routes the model's intent through the coalition commander, which merges it with the
		/// deterministic plan. Falls back to the strategic brain directly if no commander is present.</summary>
		static void ApplyPlan(IBot bot, string planJson)
		{
			var commander = bot.Player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);
			if (commander != null)
			{
				commander.ApplyLlmIntent(planJson);
				return;
			}

			var brain = bot.Player.PlayerActor.TraitsImplementing<StrategicBrainBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);
			brain?.ApplyTeamPlan(planJson);
		}
	}
}
