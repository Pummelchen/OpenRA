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

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	[Desc("Coalition command center. Builds the shared world model (blackboard), runs the deterministic " +
		"strategic commander, merges optional LLM intent, and applies coalition directives to the local " +
		"strategic brain. All allied bots compute the identical blackboard, so decisions stay deterministic " +
		"and synchronized.")]
	[TraitLocation(SystemActors.Player)]
	public sealed class CoalitionCommandCenterBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Interval (in ticks) between blackboard rebuilds.")]
		public readonly int BlackboardInterval = 40;

		[Desc("Interval (in ticks) between command decisions.")]
		public readonly int CommandInterval = 100;

		[Desc("Enemy actor types classified as infantry.")]
		public readonly FrozenSet<string> InfantryTypes = [];

		[Desc("Enemy actor types classified as armored.")]
		public readonly FrozenSet<string> ArmorTypes = [];

		[Desc("Enemy actor types classified as air.")]
		public readonly FrozenSet<string> AirTypes = [];

		[Desc("Enemy actor types classified as naval.")]
		public readonly FrozenSet<string> NavalTypes = [];

		[Desc("Own actor types treated as economy/support (never committed to combat).")]
		public readonly FrozenSet<string> SupportTypes = [];

		[Desc("Scarce special-operation assets (Tanya, spies, engineers...).")]
		public readonly FrozenSet<string> SpecialTypes = [];

		[Desc("Units the coalition prefers to produce (deterministic commander).")]
		public readonly FrozenSet<string> ArmyPriority = [];

		[Desc("Counter units prioritized when enemy air is observed.")]
		public readonly FrozenSet<string> AntiAirUnits = [];

		[Desc("Counter units prioritized when enemy armor is observed.")]
		public readonly FrozenSet<string> AntiArmorUnits = [];

		[Desc("Terrain types that count as water for naval feasibility decisions.")]
		public readonly FrozenSet<string> WaterTerrainTypes = new HashSet<string> { "Water" }.ToFrozenSet();

		[Desc("Minimum size (in cells) of a contiguous explored water body before naval production is " +
			"considered worthwhile. A shipyard on a tiny lake is wasted, so below this threshold no naval " +
			"corps is assigned and coordinated strikes do not wait for ships.")]
		public readonly int BigWaterMinimumCells = 100;

		public override object Create(ActorInitializer init) { return new CoalitionCommandCenterBotModule(this, init); }
	}

	public sealed class CoalitionCommandCenterBotModule : ConditionalTrait<CoalitionCommandCenterBotModuleInfo>, IBotTick
	{
		sealed class LlmIntent
		{
			public string Posture { get; set; }
			public string[] Produce { get; set; }
			public bool Retreat { get; set; }
			public LlmMission[] Missions { get; set; }
		}

		sealed class LlmMission
		{
			public string Type { get; set; }
			public int X { get; set; }
			public int Y { get; set; }
			public int Priority { get; set; }
		}

		readonly CoalitionCommandCenterBotModuleInfo info;
		readonly MissionManager missions = new();

		Player player;
		World world;
		StrategicBrainBotModule brain;
		CoalitionBlackboard blackboard;
		LlmIntent llmIntent;
		int lastBlackboardTick;
		int lastCommandTick;
		string lastPosture;

		static readonly JsonSerializerOptions IntentOptions = new() { PropertyNameCaseInsensitive = true };

		public CoalitionCommandCenterBotModule(CoalitionCommandCenterBotModuleInfo info, ActorInitializer init)
			: base(info)
		{
			this.info = info;
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (IsTraitDisabled)
				return;

			player = bot.Player;
			world = player.World;
			brain = player.PlayerActor.TraitsImplementing<StrategicBrainBotModule>().FirstOrDefault(m => !m.IsTraitDisabled);

			var tick = world.WorldTick;
			if (tick - lastBlackboardTick >= info.BlackboardInterval)
			{
				lastBlackboardTick = tick;
				blackboard = new CoalitionBlackboard(world, player, TeamPlayers(), Classify,
					info.WaterTerrainTypes, info.BigWaterMinimumCells);
				UpdateOpponentModel();
			}

			if (tick - lastCommandTick >= info.CommandInterval && blackboard != null)
			{
				lastCommandTick = tick;
				RunCommand();
			}
		}

		/// <summary>All allied players with an enabled bot, including this one.</summary>
		Player[] TeamPlayers()
		{
			return world.Players.Where(p =>
				p.PlayerActor.TraitsImplementing<ModularBot>().Any(b => b.IsEnabled) &&
				player.RelationshipWith(p) == PlayerRelationship.Ally).ToArray();
		}

		UnitClass Classify(Actor a)
		{
			if (a.Info.HasTraitInfo<BuildingInfo>())
				return UnitClass.Structure;
			if (info.AirTypes.Contains(a.Info.Name))
				return UnitClass.Air;
			if (info.NavalTypes.Contains(a.Info.Name))
				return UnitClass.Naval;
			if (info.ArmorTypes.Contains(a.Info.Name))
				return UnitClass.Armor;
			if (info.InfantryTypes.Contains(a.Info.Name))
				return UnitClass.Infantry;
			return UnitClass.Support;
		}

		/// <summary>
		/// The deterministic coalition commander: derives a posture, creates and updates missions from
		/// the blackboard, merges optional LLM intent, and applies the resulting directives to the
		/// local strategic brain.
		/// </summary>
		void RunCommand()
		{
			var coalitionArmy = blackboard.CoalitionArmyStrength;
			var enemyArmy = blackboard.EnemyArmyStrength;
			var ratio = coalitionArmy <= 0 ? 0 : enemyArmy / coalitionArmy;

			// Advance the mission lifecycle.
			missions.Update(blackboard, coalitionArmy, enemyArmy);

			// Mission creation driven by the force balance, intel, and LLM intent.
			var wantAttack = ratio < 0.8f || llmIntent?.Posture == "attack";
			var wantDefend = ratio > 1.2f || llmIntent?.Posture == "defend" || llmIntent?.Posture == "turtle";

			if (wantAttack && blackboard.EnemyRegion >= 0)
			{
				var target = BestScoredTarget() ?? RegionCenter(blackboard.EnemyRegion);
				EnsureMission(MissionType.Attack, 90, target, "Destroy enemy concentration");
			}

			if (wantDefend)
				EnsureMission(MissionType.Defend, 80, RegionCenter(blackboard.HomeRegion), "Hold the base");

			// Reconnaissance: if the enemy position is unknown, probe the least-explored nearby region.
			if (blackboard.EnemyRegion < 0)
			{
				var reconTarget = LeastExploredRegionNear();
				if (reconTarget != null)
					EnsureMission(MissionType.Recon, 40, reconTarget, "Locate the enemy");
			}

			// Deception: once an attack is staged, keep a feint active against another enemy-facing region.
			// Enemy models that over-respond to raids make feints more valuable.
			if (missions.Missions.Any(m => m.Type == MissionType.Attack && m.Status == MissionStatus.Executing)
				&& !missions.Missions.Any(m => m.Type == MissionType.Feint))
			{
				var feintTarget = FeintRegionTarget();
				if (feintTarget != null)
					EnsureMission(MissionType.Feint, blackboard.Opponent.MovesWholeArmyToDefend ? 75 : 60, feintTarget, "Divert enemy attention");
			}

			// Bait: an over-responsive enemy is lured by a small exposed force into an ambush position
			// halfway to our base, where the main army waits to pounce.
			if (missions.Missions.Any(m => m.Type == MissionType.Attack && m.Status == MissionStatus.Executing)
				&& (blackboard.Opponent.MovesWholeArmyToDefend || blackboard.Opponent.RespondsStronglyToRaids)
				&& !missions.Missions.Any(m => m.Type == MissionType.Bait))
			{
				var home = RegionCenter(blackboard.HomeRegion);
				var enemy = RegionCenter(blackboard.EnemyRegion);
				if (home != null && enemy != null)
					EnsureMission(MissionType.Bait, 55, home.Value + (enemy.Value - home.Value) / 2, "Lure the enemy into an ambush");
			}

			// Special operations: if a scarce asset is available and enemy structures are known, insert
			// it against the least-observed enemy region (lowest static-defense and vision threat).
			if (!missions.Missions.Any(m => m.Type == MissionType.SpecialOps || m.Type == MissionType.Transport))
			{
				var specialTarget = SpecialOpsTarget();
				if (specialTarget != null)
					EnsureMission(MissionType.SpecialOps, 70, specialTarget, "Special insertion");
			}

			// LLM-intended missions override/expand the deterministic set.
			if (llmIntent?.Missions != null)
				foreach (var lm in llmIntent.Missions)
				{
					var type = ParseMissionType(lm.Type);
					if (type != null)
						EnsureMission(type.Value, lm.Priority > 0 ? lm.Priority : 50, new CPos(lm.X, lm.Y), "LLM directive");
				}

			if (llmIntent?.Retreat == true)
				EnsureMission(MissionType.Retreat, 100, null, "Withdraw");

			// Capability-driven production from observed enemy composition.
			var produceJson = BuildProduceJson();

			// Corps role assignment: specialize this bot within the coalition (naval/main/escort).
			var rolesJson = AssignRole();

			// Coalition force summary (army = air + naval + land; structures and support excluded),
			// consumed by the brain's coordinated-attack gate.
			var forceJson = BuildForceJson();

			// Build and apply the execution directives. The attack tick is fixed at mission creation,
			// so every allied bot reads the same launch window and the waves hit together (time-on-target).
			var attack = missions.Missions.FirstOrDefault(m =>
				(m.Type == MissionType.Attack || m.Type == MissionType.Counterattack || m.Type == MissionType.Raid)
				&& m.Status == MissionStatus.Executing);
			var attackTick = attack != null ? attack.CreatedTick + 400 : -1;
			var directiveJson = missions.BuildDirectiveJson(blackboard, produceJson, llmIntent?.Retreat == true, rolesJson, forceJson, attackTick);
			if (llmIntent != null)
				CoalitionTelemetry.Log(world,
					$"LLM intent applied: posture={llmIntent.Posture ?? "none"} missions={llmIntent.Missions?.Length ?? 0} produce={llmIntent.Produce?.Length ?? 0} retreat={llmIntent.Retreat}");
			llmIntent = null;

			var strategy = directiveJson.Contains("\"strategy\":\"attack\"") ? "attack"
				: directiveJson.Contains("\"strategy\":\"defend\"") ? "defend" : "build";
			if (lastPosture != strategy)
			{
				lastPosture = strategy;
				blackboard.AddEvent("posture_change", null, strategy);
				CoalitionTelemetry.Log(world, $"Posture {strategy}; coalition {blackboard.CoalitionArmyStrength:0} vs enemy {blackboard.EnemyArmyStrength:0}");
			}

			brain?.ApplyTeamPlan(directiveJson);
		}

		/// <summary>Updates the opponent model from observed enemy composition and deployment patterns.</summary>
		void UpdateOpponentModel()
		{
			var total = blackboard.EnemyIntel.Count;
			if (total == 0)
				return;

			blackboard.Opponent.ArmorBias = blackboard.EnemyIntel.Count(i => i.Class == UnitClass.Armor) * 1f / total;
			blackboard.Opponent.AirBias = blackboard.EnemyIntel.Count(i => i.Class == UnitClass.Air) * 1f / total;

			// If many enemy sightings sit away from their base region, the enemy tends to commit its
			// whole army to defending - a signal that feints will draw forces away from the main push.
			blackboard.Opponent.MovesWholeArmyToDefend = blackboard.EnemyRegion >= 0
				&& blackboard.EnemyIntel.Count(i => blackboard.RegionOf(i.LastSeenCell).Index != blackboard.EnemyRegion) * 2 > total;

			// Playstyle from the scouted shape: an army that outnumbers its own structures is pressing
			// (rush), structures without a matching army are turtling.
			var structures = blackboard.EnemyIntel.Count(i => i.Class == UnitClass.Structure);
			blackboard.Opponent.ExpansionCount = structures;
			var army = total - structures;
			blackboard.Opponent.Playstyle = army >= 8 && structures <= 2 ? "rush"
				: structures >= 5 && army <= structures ? "turtle" : "balanced";

			// Predicted build from the most advanced scouted structure.
			var build = "unknown";
			foreach (var intel in blackboard.EnemyIntel.Where(i => i.Class == UnitClass.Structure))
			{
				switch (intel.Actor.Info.Name)
				{
					case "afld":
					case "hpad":
						build = "air";
						break;
					case "spen":
					case "syrd":
						build = "naval";
						break;
					case "dome":
					case "atek":
					case "stek":
						build = "tech";
						break;
					case "weap":
						build = "armor";
						break;
				}
			}

			blackboard.Opponent.PredictedBuild = build;
		}

		/// <summary>Selects the least-observed enemy structure position for a special insertion.</summary>
		CPos? SpecialOpsTarget()
		{
			var hasAsset = world.Actors.Any(a => a.IsInWorld && !a.IsDead && a.Owner == player && info.SpecialTypes.Contains(a.Info.Name));
			if (!hasAsset)
				return null;

			var home = RegionCenter(blackboard.HomeRegion);
			CPos? best = null;
			var bestScore = float.MaxValue;
			foreach (var intel in blackboard.EnemyIntel.Where(i => i.Class == UnitClass.Structure))
			{
				var region = blackboard.RegionOf(intel.LastSeenCell);
				var threat = region.Threats[(int)CoalitionCapability.StaticDefense]
					+ region.Threats[(int)CoalitionCapability.VisionExposure];
				if (home != null)
					threat += 2f * CombatEstimator.RouteRisk(blackboard, home.Value, intel.LastSeenCell);

				if (threat < bestScore)
				{
					bestScore = threat;
					best = intel.LastSeenCell;
				}
			}

			return best;
		}

		/// <summary>Highest-value enemy structure target, adjusted for the approach route risk.</summary>
		CPos? BestScoredTarget()
		{
			var home = RegionCenter(blackboard.HomeRegion);
			CPos? best = null;
			var bestScore = float.MinValue;
			foreach (var intel in blackboard.EnemyIntel.Where(i => i.Class == UnitClass.Structure))
			{
				var value = CombatEstimator.TargetValue(intel.Actor, Classify);
				var risk = home != null ? CombatEstimator.RouteRisk(blackboard, home.Value, intel.LastSeenCell) : 0;
				var score = value - 2f * risk;
				if (score > bestScore)
				{
					bestScore = score;
					best = intel.LastSeenCell;
				}
			}

			return best;
		}

		static MissionType? ParseMissionType(string type)
		{
			switch ((type ?? string.Empty).ToLowerInvariant())
			{
				case "attack":
					return MissionType.Attack;
				case "defend":
					return MissionType.Defend;
				case "recon":
					return MissionType.Recon;
				case "raid":
					return MissionType.Raid;
				case "feint":
					return MissionType.Feint;
				case "retreat":
					return MissionType.Retreat;
				case "transport":
					return MissionType.Transport;
				case "counterattack":
					return MissionType.Counterattack;
				case "specialops":
					return MissionType.SpecialOps;
				case "bait":
					return MissionType.Bait;
				default:
					return null;
			}
		}

		/// <summary>Reuses an active mission of the type (refreshing its target) or creates a new one.</summary>
		void EnsureMission(MissionType type, int priority, CPos? target, string objective)
		{
			var existing = missions.Missions.FirstOrDefault(m => m.Type == type && (m.Status == MissionStatus.Ready || m.Status == MissionStatus.Executing));
			if (existing != null)
			{
				if (target != null)
					existing.Target = target;
				existing.Priority = priority;
				return;
			}

			missions.CreateMission(type, priority, target, objective, createdTick: world.WorldTick);
			blackboard.AddEvent("mission_created", target, $"{type}:{objective}");
		}

		string BuildProduceJson()
		{
			string[] units = null;
			if (blackboard.EnemyIntel.Any(i => i.Class == UnitClass.Air))
				units = info.AntiAirUnits.ToArray();
			else if (blackboard.EnemyIntel.Any(i => i.Class == UnitClass.Armor))
				units = info.AntiArmorUnits.ToArray();

			if (units == null || units.Length == 0)
				return null;

			return "[\"" + string.Join("\",\"", units) + "\"]";
		}

		/// <summary>
		/// Assigns this bot a corps role within the coalition: the strongest naval builder becomes
		/// the naval corps, the largest army becomes the main corps, everyone else escorts. Without a
		/// explored water body big enough for a navy, no naval corps is assigned at all.
		/// </summary>
		string AssignRole()
		{
			var mine = blackboard.Forces.FirstOrDefault(f => f.Owner == player.InternalName);
			if (mine == null || blackboard.Forces.Count == 0)
				return null;

			if (!blackboard.HasBigWater)
			{
				// No usable water: no shipyards, no naval production, and no naval corps. Everyone
				// fights as main/escort so the coalition does not invest in a navy it cannot use.
				var armyMax = blackboard.Forces.Max(f => f.TotalUnits);
				return "{\"" + player.InternalName + "\":\"" + (mine.TotalUnits == armyMax && mine.TotalUnits > 0 ? "main" : "escort") + "\"}";
			}

			var teamNavalMax = blackboard.Forces.Max(f => f.Counts[(int)UnitClass.Naval]);
			var teamMax = blackboard.Forces.Max(f => f.TotalUnits);

			string role;
			if (teamNavalMax == 0)
			{
				// No navy yet: fix a naval corps to a deterministic team member so shipyards and naval
				// production actually get built (otherwise nobody is naval, so nobody builds a navy).
				var ordered = blackboard.Forces.OrderBy(f => f.Owner).ToArray();
				role = ordered.Length > 1 && mine.Owner == ordered[1].Owner ? "naval" : "escort";
			}
			else if (mine.Counts[(int)UnitClass.Naval] > 0 && mine.Counts[(int)UnitClass.Naval] == teamNavalMax)
				role = "naval";
			else if (mine.TotalUnits == teamMax && mine.TotalUnits > 0)
				role = "main";
			else
				role = "escort";

			return "{\"" + player.InternalName + "\":\"" + role + "\"}";
		}

		/// <summary>Summarizes the coalition army for the brain's coordinated-attack gate.</summary>
		string BuildForceJson()
		{
			var counts = new int[6];
			foreach (var force in blackboard.Forces)
				for (var c = 0; c < 4; c++)
					counts[c] += force.Counts[c];

			var air = counts[(int)UnitClass.Air];
			var naval = counts[(int)UnitClass.Naval];
			var land = counts[(int)UnitClass.Infantry] + counts[(int)UnitClass.Armor];

			// "water" tells the brain whether a big explored water body exists. Without it the mixed-arms
			// gate must not demand a naval arm, and naval production is skipped.
			return $"{{\"army\":{air + naval + land},\"air\":{air},\"naval\":{naval},\"land\":{land},\"water\":{(blackboard.HasBigWater ? "true" : "false")}}}";
		}

		/// <summary>Returns the region with the least friendly coverage.</summary>
		CPos? LeastExploredRegionNear()
		{
			CoalitionRegion best = null;
			var bestCoverage = 1f;
			for (var i = 0; i < blackboard.Regions.Length; i++)
			{
				var coverage = blackboard.Regions[i].FriendlyControl;
				if (coverage < bestCoverage)
				{
					best = blackboard.Regions[i];
					bestCoverage = coverage;
				}
			}

			return best == null ? null : RegionCenter(best.Index);
		}

		/// <summary>Picks a distinct enemy-facing region for the feint (not the main attack target).</summary>
		CPos? FeintRegionTarget()
		{
			var attack = missions.Missions.FirstOrDefault(m => m.Type == MissionType.Attack && m.Target != null);
			for (var i = 0; i < blackboard.Regions.Length; i++)
			{
				if (blackboard.Regions[i].EnemyPressure > 0 && (attack == null || attack.Target == null || blackboard.RegionOf(attack.Target.Value).Index != i))
					return RegionCenter(i);
			}

			return null;
		}

		CPos? RegionCenter(int regionIndex)
		{
			if (regionIndex < 0 || regionIndex >= blackboard.Regions.Length)
				return null;
			var bounds = blackboard.Regions[regionIndex].Bounds;
			return new CPos((bounds.Left + bounds.Right) / 2, (bounds.Top + bounds.Bottom) / 2);
		}

		/// <summary>Routes a raw LLM intent reply (command.intent.v1 subset) into the next command.</summary>
		public void ApplyLlmIntent(string intentJson)
		{
			if (blackboard is null || string.IsNullOrEmpty(intentJson))
				return;

			try
			{
				var intent = JsonSerializer.Deserialize<LlmIntent>(intentJson, IntentOptions);
				if (intent is not null)
					llmIntent = intent;
			}
			catch
			{
				// Invalid intent is ignored; the deterministic commander remains authoritative.
			}
		}
	}
}
