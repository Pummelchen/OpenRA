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

		[Desc("Difficulty 0-3: scales the consultation cadence (faster analysis at higher difficulty).")]
		public readonly int Difficulty = 3;

		[Desc("HTTP request timeout in milliseconds. A timed-out request falls back to the scripted brain.")]
		public readonly int ExternalBrainTimeout = 2000;

		public override object Create(ActorInitializer init) { return new ExternalBrainBotModule(this, init); }
	}

	public sealed class ExternalBrainBotModule : ConditionalTrait<ExternalBrainBotModuleInfo>, IBotTick
	{
		public const int MaxNotableUnits = 8;
		public const int MaxArmyGroups = 16;
		public const int MaxRecentEvents = 32;
		public const int MaxUncertainties = 32;
		public const int MaxEnemyTypes = 64;

		/// <summary>Deterministic context bound used by every variable-size snapshot section.</summary>
		public static T[] LimitContext<T>(IEnumerable<T> values, int limit, bool newest = false)
		{
			if (limit <= 0)
				return [];
			var array = values.ToArray();
			return (newest ? array.TakeLast(limit) : array.Take(limit)).ToArray();
		}

		/// <summary>Fair-fog external snapshots may report an actor only while it is currently visible.</summary>
		public static bool MayExposeEnemyActor(bool currentlyVisible)
		{
			return currentlyVisible;
		}

		sealed class TeamState
		{
			public int Round { get; set; }
			public string Self { get; set; }
			public string ScreenshotPath { get; set; }
			public MemberState[] Team { get; set; }
			public EnemyState Enemies { get; set; }
			public ForceState Force { get; set; }
			public EstimateState Estimate { get; set; }
			public ArmyGroupState[] ArmyGroups { get; set; }
			public string[] RecentEvents { get; set; }
			public string[] Uncertainties { get; set; }
		}

		sealed class ArmyGroupState
		{
			public string Owner { get; set; }
			public string Mission { get; set; }
			public string Role { get; set; }
			public string Status { get; set; }
			public int TotalUnits { get; set; }
			public float Strength { get; set; }
			public float Readiness { get; set; }
			public Dictionary<string, int> Composition { get; set; }
			public Dictionary<string, int> Capabilities { get; set; }
			public Dictionary<string, int> Activities { get; set; }

			/// <summary>Region index where this force group is concentrated, or -1.</summary>
			public int Region { get; set; } = -1;

			/// <summary>Cell coordinates of the force group's center, for LLM spatial reasoning.</summary>
			public int X { get; set; } = -1;
			public int Y { get; set; } = -1;

			/// <summary>Threats known in the force's region, keyed by capability name.</summary>
			public Dictionary<string, float> NearbyThreats { get; set; }
		}

		sealed class EstimateState
		{
			public float Friendly { get; set; }
			public float Enemy { get; set; }
			public float WinRatio { get; set; }

			/// <summary>0..1 fraction of coalition support powers ready, so the commander can plan with them.</summary>
			public float SupportPowerReadiness { get; set; }

			/// <summary>True when the coalition holds a ready strategic superweapon.</summary>
			public bool HasSuperweapon { get; set; }

			/// <summary>0..1: how often coalition deception (feints/baits) drew a measurable enemy response.</summary>
			public float DeceptionEffectiveness { get; set; }

			/// <summary>Total enemy units pulled out of position by successful deceptions.</summary>
			public int DeceptionEnemiesDrawn { get; set; }
		}

		sealed class MemberState
		{
			public string Player { get; set; }
			public int Cash { get; set; }

			// Compressed composition: per-type counts plus the average army health, so the model's
			// context is spent on aggregate shape instead of raw unit lists.
			public CountsState Units { get; set; }
			public CountsState Structures { get; set; }

			// A handful of units worth calling out: the most damaged, plus scarce special assets.
			public UnitState[] Notable { get; set; }
		}

		sealed class CountsState
		{
			public int Total { get; set; }
			public int ArmyHealth { get; set; }
			public Dictionary<string, int> ByType { get; set; }
		}

		sealed class EnemyState
		{
			public int Total { get; set; }
			public int X { get; set; }
			public int Y { get; set; }
			public Dictionary<string, int> ByType { get; set; }

			/// <summary>Intelligence honesty-ladder breakdown: observed / last-known / inferred / suspected counts.</summary>
			public Dictionary<string, int> ByStatus { get; set; }
		}

		sealed class ForceState
		{
			public int Army { get; set; }
			public int Air { get; set; }
			public int Naval { get; set; }
			public int Land { get; set; }
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
			if (IsTraitDisabled || string.IsNullOrEmpty(info.ExternalBrainUrl) || HeadlessSkirmish.DisableExternalBrain)
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
			// the last consultation, whether it succeeded or failed. lastCompletedTick starts at
			// int.MinValue as a sentinel: subtracting it would overflow, so treat the sentinel as
			// "a long time ago" to allow the first consultation immediately.
			var breakSeconds = info.ExternalBrainBreakSeconds * (1.5f - 0.25f * info.Difficulty);
			var breakTicks = world.Timestep > 0 ? (int)(breakSeconds * 1000.0 / world.Timestep) : info.ExternalBrainBreakSeconds;
			var sinceLast = lastCompletedTick == int.MinValue ? int.MaxValue : tick - lastCompletedTick;
			if (requestInFlight || sinceLast < breakTicks)
				return;

			// Capture a fresh full-map radar image right before consulting the model.
			var radar = bot.Player.PlayerActor.TraitsImplementing<RadarCaptureBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);
			radar?.CaptureNow(bot);

			// Record the attempt immediately so a failed consultation (server down, timeout) backs
			// off to the break interval instead of re-firing a snapshot and radar capture every tick.
			lastCompletedTick = tick;
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

			var members = team.Select(p =>
			{
				var units = TeamActors(world, p, false).ToArray();
				var structures = TeamActors(world, p, true).ToArray();
				var brain = p.PlayerActor.TraitsImplementing<StrategicBrainBotModule>().FirstOrDefault();
				var notableActors = units.Where(a => a.Info.Name == "mcv"
						|| brain?.Info.SpecialTypes.Contains(a.Info.Name) == true
						|| brain?.Info.TransportTypes.Contains(a.Info.Name) == true)
					.OrderBy(a => a.ActorID)
					.Concat(units.OrderBy(a => HealthPercent(a)))
					.DistinctBy(a => a.ActorID);

				return new MemberState
				{
					Player = p.InternalName,
					Cash = p.PlayerActor.TraitOrDefault<PlayerResources>()?.GetCashAndResources() ?? 0,
					Units = Summarize(units),
					Structures = Summarize(structures),
					Notable = LimitContext(notableActors, MaxNotableUnits)
						.Select(a => new UnitState
						{
							Type = a.Info.Name,
							X = a.Location.X,
							Y = a.Location.Y,
							HealthPercent = HealthPercent(a)
						}).ToArray()
				};
			}).ToArray();

			// Enemy intel is shared through allied vision: an enemy is reported only while at least one
			// team member can currently see it. Compressed to counts + centroid.
			var enemyActors = world.Actors
				.Where(a => a.IsInWorld && !a.IsDead && a.Owner != player
					&& a.OccupiesSpace != null
					&& player.RelationshipWith(a.Owner) == PlayerRelationship.Enemy
					&& MayExposeEnemyActor(team.Any(ally => ally.Shroud.IsVisible(a.CenterPosition))))
				.ToArray();

			var enemyByType = new Dictionary<string, int>();
			var sumX = 0L;
			var sumY = 0L;
			foreach (var a in enemyActors)
			{
				enemyByType.TryGetValue(a.Info.Name, out var n);
				enemyByType[a.Info.Name] = n + 1;
				sumX += a.Location.X;
				sumY += a.Location.Y;
			}

			enemyByType = enemyByType.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key)
				.Take(MaxEnemyTypes).ToDictionary(kv => kv.Key, kv => kv.Value);

			var enemies = new EnemyState
			{
				Total = enemyActors.Length,
				X = enemyActors.Length == 0 ? -1 : (int)(sumX / enemyActors.Length),
				Y = enemyActors.Length == 0 ? -1 : (int)(sumY / enemyActors.Length),
				ByType = enemyByType,
				ByStatus = CommanderStatusBreakdown(player)
			};

			var state = new TeamState
			{
				Round = round,
				Self = player.InternalName,
				ScreenshotPath = player.PlayerActor.TraitsImplementing<RadarCaptureBotModule>()
					.Select(m => m.LastCapturePath)
					.FirstOrDefault(path => path != null),
				Team = members,
				Enemies = enemies,
				Force = ComputeForce(world, player, team),
				Estimate = ComputeEstimate(world, player, team, enemyActors),
				ArmyGroups = CommanderArmyGroups(player),
				RecentEvents = CommanderRecentEvents(player),
				Uncertainties = CommanderUncertainties(player)
			};

			return JsonSerializer.Serialize(state, SnapshotOptions);
		}

		/// <summary>Lanchester-style power estimate of the coalition against the scouted enemy.</summary>
		static EstimateState ComputeEstimate(World world, Player player, Player[] team, Actor[] enemyActors)
		{
			var commander = player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>().FirstOrDefault();
			var infantry = commander?.Info.InfantryTypes ?? [];
			var armor = commander?.Info.ArmorTypes ?? [];
			var air = commander?.Info.AirTypes ?? [];
			var naval = commander?.Info.NavalTypes ?? [];

			UnitClass Classify(Actor a)
			{
				if (a.Info.HasTraitInfo<BuildingInfo>())
					return UnitClass.Structure;
				if (air.Contains(a.Info.Name))
					return UnitClass.Air;
				if (naval.Contains(a.Info.Name))
					return UnitClass.Naval;
				if (armor.Contains(a.Info.Name))
					return UnitClass.Armor;
				if (infantry.Contains(a.Info.Name))
					return UnitClass.Infantry;
				return UnitClass.Support;
			}

			var teamIds = team.Select(t => t.InternalName).ToHashSet();
			var friendly = world.Actors.Where(a =>
				!a.IsDead && a.IsInWorld && a.OccupiesSpace != null && teamIds.Contains(a.Owner.InternalName)
				&& !a.Info.HasTraitInfo<BuildingInfo>());
			var friendlyPower = CombatEstimator.ForcePower(friendly, Classify);
			var enemyPower = CombatEstimator.ForcePower(enemyActors, Classify);
			var (winRatio, _) = CombatEstimator.Estimate(friendlyPower, enemyPower);

			// Support-power readiness and the deception record from the blackboard so the commander
			// plans with them.
			var blackboard = commander?.Blackboard;
			return new EstimateState
			{
				Friendly = friendlyPower,
				Enemy = enemyPower,
				WinRatio = winRatio,
				SupportPowerReadiness = blackboard?.SupportPowerReadiness ?? 0f,
				HasSuperweapon = blackboard?.HasReadySuperweapon ?? false,
				DeceptionEffectiveness = blackboard?.DeceptionEffectiveness ?? 0f,
				DeceptionEnemiesDrawn = blackboard?.DeceptionEnemiesDrawn ?? 0
			};
		}

		/// <summary>Counts the commander's retained intel by honesty-ladder status, for the snapshot.</summary>
		static Dictionary<string, int> CommanderStatusBreakdown(Player player)
		{
			var breakdown = new Dictionary<string, int>();
			var commander = player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>().FirstOrDefault();
			if (commander?.Blackboard == null)
				return breakdown;

			foreach (var intel in commander.Blackboard.EnemyIntel)
			{
				var key = intel.Status.ToString().ToLowerInvariant();
				breakdown[key] = breakdown.GetValueOrDefault(key) + 1;
			}

			return breakdown;
		}

		/// <summary>Army-group records: per-owner force composition, capabilities, and assignment.</summary>
		static ArmyGroupState[] CommanderArmyGroups(Player player)
		{
			var commander = player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>().FirstOrDefault();
			var blackboard = commander?.Blackboard;
			if (blackboard == null)
				return [];

			return blackboard.Forces.OrderByDescending(f => f.TotalUnits).ThenBy(f => f.Owner)
				.Take(MaxArmyGroups).Select(f =>
			{
				var region = blackboard.RegionOf(f.Center);
				var nearbyThreats = new Dictionary<string, float>();
				for (var c = 0; c < region.Threats.Length; c++)
					if (region.Threats[c] > 0)
						nearbyThreats[CommandToolApi.CapabilityKeys[c]] = region.Threats[c];

				return new ArmyGroupState
				{
					Owner = f.Owner,
					Mission = f.MissionId,
					Role = f.Role,
					Status = f.Status.ToString().ToLowerInvariant(),
					TotalUnits = f.TotalUnits,
					Strength = f.Strength,
					Readiness = f.Readiness,
					Composition = new Dictionary<string, int>(f.ByType),
					Activities = new Dictionary<string, int>(f.ActivityCounts),
					Capabilities = Enumerable.Range(0, f.Capabilities.Length)
						.Where(c => f.Capabilities[c] > 0)
						.ToDictionary(c => CommandToolApi.FriendlyCapabilityKeys[c], c => 1),
					Region = region.Index,
					X = f.Center.X,
					Y = f.Center.Y,
					NearbyThreats = nearbyThreats
				};
			}).ToArray();
		}

		/// <summary>Recent strategically significant events, summarized for the snapshot.</summary>
		static string[] CommanderRecentEvents(Player player)
		{
			var commander = player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>().FirstOrDefault();
			return LimitContext(commander?.Blackboard?.Events
				.Select(e => $"{e.Type}{(e.Payload != null ? ":" + e.Payload : string.Empty)}")
				?? [], MaxRecentEvents, newest: true);
		}

		/// <summary>Unresolved intelligence uncertainties, summarized for the snapshot.</summary>
		static string[] CommanderUncertainties(Player player)
		{
			var commander = player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>().FirstOrDefault();
			var blackboard = commander?.Blackboard;
			if (blackboard == null)
				return [];

			return LimitContext(blackboard.EnemyIntel
				.Where(i => i.Status == IntelStatus.Suspected || i.Confidence < 0.5f)
				.Select(i => i.Status == IntelStatus.Suspected
					? $"suspected_enemy_in_region_{blackboard.RegionOf(i.LastSeenCell).Index}"
					: $"enemy_{i.Type}_position_conf{Math.Round(i.Confidence, 2)}")
				.Distinct(), MaxUncertainties);
		}

		/// <summary>Compresses an actor list into per-type counts plus average health.</summary>
		static CountsState Summarize(Actor[] actors)
		{
			var byType = new Dictionary<string, int>();
			var totalHealth = 0;
			foreach (var a in actors)
			{
				byType.TryGetValue(a.Info.Name, out var n);
				byType[a.Info.Name] = n + 1;
				totalHealth += HealthPercent(a);
			}

			return new CountsState
			{
				Total = actors.Length,
				ArmyHealth = actors.Length == 0 ? 0 : totalHealth / actors.Length,
				ByType = byType
			};
		}

		/// <summary>Coalition army split (air + naval + land), using the commander's classification lists.</summary>
		static ForceState ComputeForce(World world, Player player, Player[] team)
		{
			var commander = player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>().FirstOrDefault();
			var air = commander?.Info.AirTypes ?? [];
			var naval = commander?.Info.NavalTypes ?? [];

			var teamIds = team.Select(t => t.InternalName).ToHashSet();
			var force = new ForceState();
			foreach (var a in world.Actors)
			{
				if (a.IsDead || !a.IsInWorld || a.OccupiesSpace == null || !teamIds.Contains(a.Owner.InternalName))
					continue;

				if (a.Info.HasTraitInfo<BuildingInfo>())
					continue;

				if (air.Contains(a.Info.Name))
					force.Air++;
				else if (naval.Contains(a.Info.Name))
					force.Naval++;
				else
					force.Land++;
			}

			force.Army = force.Air + force.Naval + force.Land;
			return force;
		}

		static IOrderedEnumerable<Actor> TeamActors(World world, Player owner, bool structures)
		{
			return world.Actors
				.Where(a => a.IsInWorld && !a.IsDead && a.Owner == owner && a.OccupiesSpace != null && a.Info.HasTraitInfo<BuildingInfo>() == structures)
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

				// The configured URL is the server base; plans are served from /decide.
				var url = info.ExternalBrainUrl.EndsWith("/decide", StringComparison.Ordinal)
					? info.ExternalBrainUrl : info.ExternalBrainUrl.TrimEnd('/') + "/decide";
				var response = await http.PostAsync(url, content, timeout.Token).ConfigureAwait(false);
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
				CoalitionTelemetry.Log(bot.Player.World, $"LLM plan received: {PlanSummary(planJson)}");
				return;
			}

			var brain = bot.Player.PlayerActor.TraitsImplementing<StrategicBrainBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);
			brain?.ApplyTeamPlan(planJson);
			CoalitionTelemetry.Log(bot.Player.World, $"LLM plan received: {PlanSummary(planJson)}");
		}

		/// <summary>Compact one-line summary of a plan for the telemetry monitor.</summary>
		static string PlanSummary(string planJson)
		{
			try
			{
				using var doc = JsonDocument.Parse(planJson);
				var root = doc.RootElement;
				var posture = root.TryGetProperty("posture", out var p) ? p.GetString() : null;
				var missions = root.TryGetProperty("missions", out var m) ? m.GetArrayLength() : 0;
				var produce = root.TryGetProperty("produce", out var pr) ? pr.GetArrayLength() : 0;
				return $"posture={posture ?? "none"} missions={missions} produce={produce}";
			}
			catch
			{
				return "unparseable";
			}
		}
	}
}
