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
				blackboard = new CoalitionBlackboard(world, player, TeamPlayers(), Classify);
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
				var target = RegionCenter(blackboard.EnemyRegion);
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

			// Build and apply the execution directives.
			var directiveJson = missions.BuildDirectiveJson(blackboard, produceJson, llmIntent?.Retreat == true);
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
		}

		/// <summary>Selects the least-observed enemy structure position for a special insertion.</summary>
		CPos? SpecialOpsTarget()
		{
			var hasAsset = world.Actors.Any(a => a.IsInWorld && !a.IsDead && a.Owner == player && info.SpecialTypes.Contains(a.Info.Name));
			if (!hasAsset)
				return null;

			CPos? best = null;
			var bestThreat = float.MaxValue;
			foreach (var intel in blackboard.EnemyIntel.Where(i => i.Class == UnitClass.Structure))
			{
				var region = blackboard.RegionOf(intel.LastSeenCell);
				var threat = region.Threats[(int)CoalitionCapability.StaticDefense]
					+ region.Threats[(int)CoalitionCapability.VisionExposure];
				if (threat < bestThreat)
				{
					bestThreat = threat;
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

			missions.CreateMission(type, priority, target, objective);
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
