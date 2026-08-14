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

		[Desc("Counter units prioritized when enemy infantry is observed.")]
		public readonly FrozenSet<string> AntiInfantryUnits = [];

		[Desc("Units preferred when the coalition faces enemy naval or submarine capability (and water exists).")]
		public readonly FrozenSet<string> NavalPriority = [];

		[Desc("Terrain types that count as water for naval feasibility decisions.")]
		public readonly FrozenSet<string> WaterTerrainTypes = new HashSet<string> { "Water" }.ToFrozenSet();

		[Desc("Resource types that count as valuable for expansion scoring.")]
		public readonly FrozenSet<string> ValuableResourceTypes = new HashSet<string> { "Ore", "Gems" }.ToFrozenSet();

		[Desc("Enemy actor types that threaten artillery/indirect fire.")]
		public readonly FrozenSet<string> ArtilleryTypes = new HashSet<string> { "arty", "v2rl" }.ToFrozenSet();

		[Desc("Enemy actor types that are submarines.")]
		public readonly FrozenSet<string> SubmarineTypes = new HashSet<string> { "ss", "msub" }.ToFrozenSet();

		[Desc("Enemy actor types that detect stealth.")]
		public readonly FrozenSet<string> DetectionTypes = new HashSet<string> { "dog", "rdr" }.ToFrozenSet();

		[Desc("Enemy structures that represent support-power danger (superweapons).")]
		public readonly FrozenSet<string> SupportPowerStructures = new HashSet<string> { "iron", "pdox" }.ToFrozenSet();

		[Desc("Enemy structures that produce units, seeding reinforcement threat.")]
		public readonly FrozenSet<string> ProductionStructures = new HashSet<string>
		{
			"weap", "afld", "hpad", "spen", "syrd", "barr", "tent", "fact", "atek", "stek", "dome"
		}.ToFrozenSet();

		[Desc("Minimum size (in cells) of a contiguous explored water body before naval production is " +
			"considered worthwhile. A shipyard on a tiny lake is wasted, so below this threshold no naval " +
			"corps is assigned and coordinated strikes do not wait for ships.")]
		public readonly int BigWaterMinimumCells = 100;

		[Desc("How long (in ticks) a mobile enemy sighting is retained as LAST_KNOWN after it leaves " +
			"explored territory, before it is dropped back to UNKNOWN.")]
		public readonly int SightingMemoryTicks = 600;

		[Desc("Intelligence/fog advantage (0..3): 0 = fair fog (default), 2 = enemy structures are always " +
			"visible, 3 = omniscient (every enemy actor visible). Fair fog is the default.")]
		public readonly int Intelligence = 0;

		/// <summary>True at the top intelligence setting: the coalition sees every enemy actor.</summary>
		public bool IsOmniscient => Intelligence >= 3;

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
		readonly CoalitionOrderArbiter arbiter = new();
		CoalitionIntelTracker intelTracker;

		Player player;
		World world;
		StrategicBrainBotModule brain;
		CoalitionBlackboard blackboard;
		LlmIntent llmIntent;
		int lastBlackboardTick;
		int lastCommandTick;
		string lastPosture;
		StrategicPosture strategicPosture;

		/// <summary>The coalition's main effort: the single highest-value objective, re-selected
		/// every command tick so effort concentrates on one area instead of spreading evenly.</summary>
		CPos? mainEffort;

		// Durable opponent observations. The blackboard (and its opponent model) is rebuilt every
		// BlackboardInterval, so learned values must live here and be copied into each fresh model.
		int responseTimeSum;
		int responseTimeSamples;
		int lastWaveTick = int.MinValue;
		int raidContactTicks;

		// Durable peak unit count per owner, for casualty tracking across blackboard rebuilds.
		readonly Dictionary<string, int> peakForceUnits = [];

		// Durable event-transition state, so a signal wakes the commander only once per change.
		bool lastSuperweaponReady;
		int lastSpecialAssetCount;

		// Match-quality telemetry, sampled once per command.
		readonly CoalitionMatchMetrics matchMetrics = new();
		int lastMetricsSummaryTick = int.MinValue;
		int lastFloatingTick = int.MinValue;

		/// <summary>The current blackboard, for external consumers (LLM snapshot, tests).</summary>
		public CoalitionBlackboard Blackboard => blackboard;

		/// <summary>The tick of the most recent coalition attack wave, for response-time measurement.</summary>
		public int LastWaveTick => lastWaveTick;

		/// <summary>
		/// Records an enemy reaction to a coalition attack: the delay between our wave launch and
		/// the enemy's first response, in seconds. Durable across blackboard rebuilds.
		/// </summary>
		public void RecordEnemyResponse(int currentTick)
		{
			if (lastWaveTick < 0)
				return;

			var delayTicks = currentTick - lastWaveTick;
			if (delayTicks < 0)
				return;

			responseTimeSum += delayTicks;
			responseTimeSamples++;
		}

		/// <summary>Marks the coalition attack wave launch tick, resetting the response timer.</summary>
		public void MarkWaveLaunch(int tick)
		{
			lastWaveTick = tick;
		}

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

				// Feed the durable intel tracker with everything the coalition can see this tick (or
				// everything, in omniscient mode) and age it into the honesty ladder. The aged list is
				// seeded into the fresh blackboard so last-known/inferred intel survives the rebuild.
				intelTracker ??= new CoalitionIntelTracker(info.SightingMemoryTicks, (int)world.Timestep);
				var seedIntel = ObserveEnemies(tick);

				blackboard = new CoalitionBlackboard(world, player, TeamPlayers(), Classify,
					info.WaterTerrainTypes, info.BigWaterMinimumCells, info.ValuableResourceTypes,
					info.ArtilleryTypes, info.SubmarineTypes, info.DetectionTypes,
					info.SupportPowerStructures, info.ProductionStructures,
					brain?.Info.TransportTypes, brain?.Info.ScoutUnitTypes, info.AntiAirUnits, info.SpecialTypes,
					seedIntel, info.IsOmniscient);

				// The deception record is durable across blackboard rebuilds: it lives on the mission
				// manager and is copied into every fresh model for the planner and the LLM snapshot.
				blackboard.DeceptionAttempts = missions.DeceptionAttempts;
				blackboard.DeceptionSuccesses = missions.DeceptionSuccesses;
				blackboard.DeceptionEnemiesDrawn = missions.DeceptionEnemiesDrawn;
				UpdateForceCasualties();
				UpdateOpponentModel();

				// Event-driven review: material developments trigger an immediate command instead of
				// waiting for the next interval. Debounced to the blackboard interval so an event
				// storm (many discoveries in one tick) collapses into a single review.
				if (tick - lastCommandTick >= info.BlackboardInterval)
				{
					var trigger = ReviewTrigger();
					if (trigger != null)
					{
						lastCommandTick = tick;
						CoalitionTelemetry.Log(world, $"Event-driven review: {trigger}");
						RunCommand();
					}
				}
			}

			if (tick - lastCommandTick >= info.CommandInterval && blackboard != null)
			{
				lastCommandTick = tick;
				RunCommand();
			}
		}

		// State for detecting material events between reviews.
		readonly StrategicEventDetector eventDetector = new();

		/// <summary>
		/// Detects a material event worth an immediate strategic review, or null. Compares the current
		/// blackboard against the previous review: enemy base discovery, enemy composition changes,
		/// loss of allied production, loss of contact with the enemy, and discovery of a high-value
		/// enemy structure all wake the commander.
		/// </summary>
		string ReviewTrigger()
		{
			var enemyStructures = blackboard.EnemyIntel.Count(i => i.Class == UnitClass.Structure);
			var ownStructures = world.Actors.Count(a =>
				a.IsInWorld && !a.IsDead && a.Owner == player && a.Info.HasTraitInfo<BuildingInfo>());
			var highValueSeen = blackboard.EnemyIntel.Any(i =>
				TargetEvaluator.TechnologyValue(i.Type) > 0 || TargetEvaluator.EconomicValue(i.Type) > 0);

			var detected = eventDetector.Detect(blackboard.EnemyRegion, enemyStructures, ownStructures,
				blackboard.EnemyIntel.Count, highValueSeen);
			if (detected != null)
				return detected;

			// A ready strategic superweapon wakes the commander to plan a support-power strike.
			if (blackboard.HasReadySuperweapon && !lastSuperweaponReady)
			{
				lastSuperweaponReady = true;
				return "support power ready";
			}
			lastSuperweaponReady = blackboard.HasReadySuperweapon;

			// A newly available special asset (Tanya, spy, engineer) wakes special-operations planning.
			var specialCount = blackboard.SpecialAssets.Count;
			if (specialCount > lastSpecialAssetCount)
			{
				lastSpecialAssetCount = specialCount;
				return "special unit available";
			}
			lastSpecialAssetCount = specialCount;

			return null;
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
				// Main effort: concentrate the coalition on the single highest-value objective so
				// effort is not spread evenly across all fronts.
				var scored = BestScoredTarget();
				if (scored != mainEffort)
				{
					mainEffort = scored;
					blackboard.AddEvent("main_effort", scored, scored != null ? "concentrate on highest-value objective" : "no main effort");
					CoalitionTelemetry.Log(world, scored != null ? $"Main effort set to {scored.Value}" : "Main effort cleared: no scored target");
				}

				var target = scored ?? RegionCenter(blackboard.EnemyRegion);

				// A decisive edge turns the main effort into a breakthrough; a fair fight stays a
				// conventional attack. A heavily fortified enemy is besieged instead.
				var attackType = ratio < 0.5f ? MissionType.Breakthrough : MissionType.Attack;
				if (blackboard.Regions[blackboard.EnemyRegion].Threats[(int)CoalitionCapability.StaticDefense] > 0.7f)
					attackType = MissionType.Siege;
				EnsureMission(attackType, 90, target, "Destroy enemy concentration");
			}
			else
				mainEffort = null;

			// Additional offensive missions driven by intel: raids on economy/production, air and
			// support-power strikes, chokepoint seizure, and a flank to divide the defense.
			CreateOffensiveMissions(ratio);

			if (wantDefend)
				EnsureMission(MissionType.Defend, 80, RegionCenter(blackboard.HomeRegion), "Hold the base");

			// Reconnaissance: if the enemy position is unknown, probe the least-explored nearby region;
			// once it is known, run value-of-information-driven recon (deep, expansion search, defense probe).
			if (blackboard.EnemyRegion < 0)
			{
				var reconTarget = LeastExploredRegionNear();
				if (reconTarget != null)
					EnsureMission(MissionType.Recon, 40, reconTarget, "Locate the enemy");
			}
			else
				CreateReconMissions();

			// Specialized defense: mobile interception, anti-air umbrella, naval screen, and economy escort.
			CreateDefensiveMissions();

			// Deception: once an attack is staged, keep a feint active against another enemy-facing region.
			// Enemy models that over-respond to raids make feints more valuable, and the measured deception
			// record feeds back: feints that drew enemy responses are funded harder, while a string of
			// ignored feints (several attempts, zero responses) stops wasting forces on deception the
			// enemy does not honor.
			if (missions.Missions.Any(m => m.Type == MissionType.Attack && m.Status == MissionStatus.Executing)
				&& !missions.Missions.Any(m => m.Type == MissionType.Feint)
				&& !DeceptionSaturated())
			{
				var feintTarget = FeintRegionTarget();
				if (feintTarget != null)
					EnsureMission(MissionType.Feint, FeintPriority(), feintTarget, "Divert enemy attention");
			}

			// Bait: an over-responsive enemy is lured by a small exposed force into an ambush position
			// halfway to our base, where the main army waits to pounce.
			if (missions.Missions.Any(m => m.Type == MissionType.Attack && m.Status == MissionStatus.Executing)
				&& (blackboard.Opponent.MovesWholeArmyToDefend || blackboard.Opponent.RespondsStronglyToRaids)
				&& !missions.Missions.Any(m => m.Type == MissionType.Bait)
				&& !DeceptionSaturated())
			{
				var home = RegionCenter(blackboard.HomeRegion);
				var enemy = RegionCenter(blackboard.EnemyRegion);
				if (home != null && enemy != null)
					EnsureMission(MissionType.Bait, 55, home.Value + (enemy.Value - home.Value) / 2, "Lure the enemy into an ambush");
			}

			// Demonstration: a show of force against a second axis that never commits, to pin enemy
			// reserves while the main attack goes in elsewhere.
			if (missions.Missions.Any(m => m.Type == MissionType.Attack && m.Status == MissionStatus.Executing)
				&& !missions.Missions.Any(m => m.Type == MissionType.Demonstration)
				&& !DeceptionSaturated())
			{
				var demonstrationTarget = FeintRegionTarget();
				if (demonstrationTarget != null)
					EnsureMission(MissionType.Demonstration, 50, demonstrationTarget, "Show of force to pin reserves");
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
			{
				// Validate the commander's mission requests before execution: unknown types,
				// out-of-bounds targets, bad priorities, and duplicate missions are rejected with a
				// machine-readable reason and logged; the deterministic commander remains authoritative.
				var requests = llmIntent.Missions
					.Select(lm => (lm.Type, lm.X, lm.Y, lm.Priority))
					.ToList();
				var rejections = CommandValidator.ValidateMissions(requests, world.Map.MapSize.Width, world.Map.MapSize.Height);

				var rejected = rejections.Select(r => r.Index).ToHashSet();
				foreach (var (_, reason) in rejections)
					CoalitionTelemetry.Log(world, $"Command validator: {reason}");

				for (var i = 0; i < llmIntent.Missions.Length; i++)
				{
					if (rejected.Contains(i))
						continue;
					var lm = llmIntent.Missions[i];
					var type = ParseMissionType(lm.Type);
					if (type != null)
						EnsureMission(type.Value, lm.Priority > 0 ? lm.Priority : 50, new CPos(lm.X, lm.Y), "LLM directive");
				}
			}

			if (llmIntent?.Retreat == true)
				EnsureMission(MissionType.Retreat, 100, null, "Withdraw");

			// Assign forces to missions through the order arbiter: every active mission owns a force
			// at a priority, conflicting assignments are rejected with a machine-readable reason, and
			// completed or cancelled missions release their forces back to the pool.
			SyncForceAssignments();

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
				MissionManager.IsOffensive(m.Type) && m.Type != MissionType.AirStrike
				&& m.Type != MissionType.NavalStrike && m.Type != MissionType.SupportPowerStrike
				&& m.Status == MissionStatus.Executing);

			// Time-on-target accounts for travel distance (each route region adds a launch delay), and
			// a staged feint that has not yet drawn a response delays the main attack so the deception
			// has time to pull the enemy away first.
			var attackTick = -1;
			if (attack != null)
			{
				attackTick = attack.CreatedTick + 400 + attack.PlannedRegions.Length * 40;
				if (missions.Missions.Any(m => m.Type == MissionType.Feint) && missions.DeceptionSuccesses == 0)
					attackTick += 200;
			}

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

			// Strategic posture: the overall stance derived from the force balance and the enemy's
			// shape. It selects the target-scoring profile and whether the reserve is committed.
			var enemyRatio = blackboard.CoalitionArmyStrength <= 0 ? 1f : blackboard.EnemyArmyStrength / blackboard.CoalitionArmyStrength;
			var enemyStaticDefense = blackboard.EnemyRegion >= 0
				? blackboard.Regions[blackboard.EnemyRegion].Threats[(int)CoalitionCapability.StaticDefense]
				: 0f;
			var enemyEconomyStrong = blackboard.EnemyIntel.Any(i => i.Class == UnitClass.Structure && TargetEvaluator.EconomicValue(i.Type) > 0);
			var ownArmy = (int)blackboard.CoalitionArmyStrength;
			var newPosture = PostureSelection.Select(enemyRatio, enemyStaticDefense, ownArmy, enemyEconomyStrong);
			if (newPosture != strategicPosture)
			{
				strategicPosture = newPosture;
				CoalitionTelemetry.Log(world, $"Strategic posture: {strategicPosture.ToString().ToLowerInvariant()}");
			}

			brain?.ApplyTeamPlan(directiveJson);

			SampleMatchMetrics();
		}

		/// <summary>
		/// Samples coalition combat value, army idle fraction, cohesion, and cash for match-quality
		/// telemetry, and periodically logs the aggregated summary.
		/// </summary>
		void SampleMatchMetrics()
		{
			var teamIds = TeamPlayers().Select(p => p.InternalName).ToHashSet();
			var combatUnits = world.Actors.Where(a =>
				!a.IsDead && a.IsInWorld && a.OccupiesSpace != null && teamIds.Contains(a.Owner.InternalName)
				&& !a.Info.HasTraitInfo<BuildingInfo>()).ToArray();

			var friendlyValue = CombatEstimator.ForcePower(combatUnits, Classify);
			var enemyValue = blackboard.EnemyArmyStrength;
			var idle = combatUnits.Length == 0 ? 1f : combatUnits.Count(a => a.IsIdle) * 1f / combatUnits.Length;

			// Cohesion: how tightly the army clusters around its center (1 = perfectly together).
			var cohesion = 0f;
			if (combatUnits.Length > 1)
			{
				var center = combatUnits.Select(a => a.CenterPosition).Average();
				var maxDist = world.Map.MapSize.Width + world.Map.MapSize.Height;
				var avgDist = (float)(combatUnits.Average(a => (a.CenterPosition - center).Length) / 1024f);
				cohesion = Math.Max(0f, 1f - avgDist / Math.Max(1f, maxDist));
			}
			else if (combatUnits.Length == 1)
				cohesion = 1f;

			matchMetrics.Sample(friendlyValue, enemyValue, idle, cohesion, blackboard.CoalitionCash);
			matchMetrics.RecordEstimate(CombatEstimator.Estimate(friendlyValue, enemyValue).WinRatio);

			// Excess resource floating: a growing, unspent cash pile means production is not keeping up.
			if (blackboard.CoalitionCash > 12000 && (lastFloatingTick == int.MinValue || world.WorldTick - lastFloatingTick >= 6000))
			{
				lastFloatingTick = world.WorldTick;
				CoalitionTelemetry.Log(world, $"Excess cash floating: {blackboard.CoalitionCash}");
			}

			if (lastMetricsSummaryTick == int.MinValue || world.WorldTick - lastMetricsSummaryTick >= 6000)
			{
				lastMetricsSummaryTick = world.WorldTick;
				CoalitionTelemetry.Log(world, matchMetrics.Summary());
				CoalitionTelemetry.Log(world, missions.MissionSummary());
			}

			if (world.IsGameOver)
			{
				CoalitionTelemetry.Log(world, matchMetrics.Summary());
				CoalitionTelemetry.Log(world, missions.MissionSummary());
			}
		}

		/// <summary>
		/// Observes every enemy actor the coalition can see this tick (or everything, in omniscient
		/// mode) into the durable intel tracker, and returns the aged, status-tagged intel list.
		/// </summary>
		IReadOnlyList<EnemyIntel> ObserveEnemies(int tick)
		{
			var map = CoalitionMapAnalysis.ForMap(world, info.WaterTerrainTypes, info.ValuableResourceTypes);
			var team = TeamPlayers();

			foreach (var a in world.Actors)
			{
				if (a.IsDead || !a.IsInWorld || a.Owner == player || a.OccupiesSpace == null)
					continue;
				if (player.RelationshipWith(a.Owner) != PlayerRelationship.Enemy)
					continue;
				if (!SeesEnemy(a, team))
					continue;

				var cell = a.Location;
				intelTracker.Observe(a.Info.Name, Classify(a), RegionOfCell(map, cell), cell, tick);
			}

			return intelTracker.Age(tick);
		}

		/// <summary>
		/// The fog-of-war gate for enemy observation. Fair fog (levels 0-1) only sees explored cells;
		/// level 2 additionally reveals enemy structures (but not mobile units); level 3 is omniscient.
		/// </summary>
		bool SeesEnemy(Actor a, IEnumerable<Player> team)
		{
			if (info.IsOmniscient)
				return true;
			if (team.Any(ally => ally.Shroud.IsExplored(a.CenterPosition)))
				return true;
			if (info.Intelligence >= 2 && Classify(a) == UnitClass.Structure)
				return true;
			return false;
		}

		static int RegionOfCell(CoalitionMapAnalysis map, CPos cell)
		{
			foreach (var region in map.Regions)
				if (region.Bounds.Contains(cell.X, cell.Y))
					return region.Index;
			return 0;
		}

		/// <summary>
		/// Tracks per-owner casualties as the fraction of the peak unit count lost. The peak lives in
		/// the command center so it survives blackboard rebuilds; production growth only raises it.
		/// </summary>
		void UpdateForceCasualties()
		{
			foreach (var force in blackboard.Forces)
			{
				var peak = peakForceUnits.GetValueOrDefault(force.Owner);
				if (force.TotalUnits > peak)
					peakForceUnits[force.Owner] = force.TotalUnits;
				else if (peak > 0)
					force.CasualtyFraction = 1f - force.TotalUnits * 1f / peak;
			}
		}

		/// <summary>Updates the opponent model from observed enemy composition and deployment patterns.</summary>
		void UpdateOpponentModel()
		{
			var total = blackboard.EnemyIntel.Count;
			if (total == 0)
				return;

			var opponent = blackboard.Opponent;
			opponent.ArmorBias = blackboard.EnemyIntel.Count(i => i.Class == UnitClass.Armor) * 1f / total;
			opponent.AirBias = blackboard.EnemyIntel.Count(i => i.Class == UnitClass.Air) * 1f / total;
			opponent.InfantryBias = blackboard.EnemyIntel.Count(i => i.Class == UnitClass.Infantry) * 1f / total;
			opponent.NavalBias = blackboard.EnemyIntel.Count(i => i.Class == UnitClass.Naval) * 1f / total;
			opponent.StaticDefenseBias = blackboard.EnemyIntel.Count(i => i.Class == UnitClass.Structure) * 1f / total;

			// If many enemy sightings sit away from their base region, the enemy tends to commit its
			// whole army to defending - a signal that feints will draw forces away from the main push.
			opponent.MovesWholeArmyToDefend = blackboard.EnemyRegion >= 0
				&& blackboard.EnemyIntel.Count(i => blackboard.RegionOf(i.LastSeenCell).Index != blackboard.EnemyRegion) * 2 > total;

			// Preferred attack lane: the region most often hosting enemy sightings away from the base.
			// Tracks where the enemy tends to mass, so our defense can cover the likely axis.
			var laneCounts = new Dictionary<int, int>();
			foreach (var intel in blackboard.EnemyIntel)
			{
				var region = blackboard.RegionOf(intel.LastSeenCell).Index;
				laneCounts[region] = laneCounts.GetValueOrDefault(region) + 1;
			}

			var bestLane = -1;
			var bestLaneCount = 0;
			foreach (var kv in laneCounts)
				if (kv.Key != blackboard.HomeRegion && kv.Value > bestLaneCount)
				{
					bestLane = kv.Key;
					bestLaneCount = kv.Value;
				}

			opponent.PreferredAttackLane = bestLane;

			// Playstyle from the scouted shape: an army that outnumbers its own structures is pressing
			// (rush), structures without a matching army are turtling.
			var structures = blackboard.EnemyIntel.Count(i => i.Class == UnitClass.Structure);
			opponent.ExpansionCount = structures;
			var army = total - structures;
			opponent.Playstyle = OpponentModel.DerivePlaystyle(army, structures);

			// Predicted build from the most advanced scouted structure.
			var build = "unknown";
			foreach (var intel in blackboard.EnemyIntel.Where(i => i.Class == UnitClass.Structure))
			{
				var direction = OpponentModel.DerivePredictedBuild(intel.Type);
				if (direction != null)
					build = direction;
			}

			opponent.PredictedBuild = build;

			// Profile confidence grows with observations: a single sighting is nearly useless,
			// dozens of sightings make the biases trustworthy.
			opponent.Confidence = Math.Clamp(total / 20f, 0f, 1f);

			// Copy the durable learned values into the fresh model: response time from accumulated
			// wave-to-reaction delays, and raid sensitivity from how much enemy contact our raids
			// generate.
			if (responseTimeSamples > 0)
			{
				opponent.AverageResponseTime = responseTimeSum * world.Timestep / 1000f / responseTimeSamples;
				opponent.ResponseSamples = responseTimeSamples;
			}

			opponent.RespondsStronglyToRaids = raidContactTicks > 0;
		}

		/// <summary>
		/// Records enemy contact generated by our raids. Caller (the brain) reports how much enemy
		/// presence appeared near the raid target; sustained contact marks the enemy as raid-sensitive.
		/// </summary>
		public void RecordRaidContact(int enemyUnitsNearRaid)
		{
			if (enemyUnitsNearRaid >= 2)
				raidContactTicks++;
			else
				raidContactTicks = Math.Max(0, raidContactTicks - 1);
		}

		/// <summary>Selects the least-observed enemy structure position for a special insertion.</summary>
		CPos? SpecialOpsTarget()
		{
			var hasAsset = world.Actors.Any(a => a.IsInWorld && !a.IsDead && a.Owner == player && info.SpecialTypes.Contains(a.Info.Name));
			if (!hasAsset)
				return null;

			CPos? best = null;
			var bestScore = float.MinValue;
			var homeRegion = blackboard.HomeRegion;
			foreach (var intel in blackboard.EnemyIntel.Where(i => i.Class == UnitClass.Structure))
			{
				// Special insertions travel by stealth profile: the route cost already weights
				// vision exposure, detection, and chokepoint risk, so unreachable targets are
				// skipped rather than sending Tanya on a one-way trip into the sea.
				var targetRegion = blackboard.RegionOf(intel.LastSeenCell).Index;
				var route = CoalitionRoutePlanner.FindRoute(
					blackboard.MapAnalysis, blackboard.ThreatField(), homeRegion, targetRegion,
					MovementClass.Ground, RouteWeights.Stealth());
				if (!route.Found)
					continue;

				// Consequence-aware scoring: the target's strategic value minus the approach risk.
				var value = TargetEvaluator.EconomicValue(intel.Type)
					+ TargetEvaluator.ProductionValue(intel.Type)
					+ TargetEvaluator.TechnologyValue(intel.Type);
				var region = blackboard.Regions[targetRegion];
				var risk = region.Threats[(int)CoalitionCapability.StaticDefense]
					+ region.Threats[(int)CoalitionCapability.VisionExposure]
					+ route.Cost * 0.5f;
				var score = value * 2f - risk;

				if (score > bestScore)
				{
					bestScore = score;
					best = intel.LastSeenCell;
				}
			}

			return best;
		}

		/// <summary>
		/// Highest-value enemy structure target, scored by the full target model: strategic
		/// consequence (economy, production, technology, position, follow-on) minus approach cost
		/// (friendly losses, travel, reinforcement, counterattack risk) and intelligence
		/// uncertainty. Unreachable targets are skipped. The winner is the coalition's main effort.
		/// </summary>
		CPos? BestScoredTarget()
		{
			CPos? best = null;
			var bestScore = float.MinValue;
			var homeRegion = blackboard.HomeRegion;
			var weights = PostureSelection.TargetWeightsFor(strategicPosture);
			foreach (var intel in blackboard.EnemyIntel.Where(i => i.Class == UnitClass.Structure))
			{
				var targetRegion = blackboard.RegionOf(intel.LastSeenCell).Index;
				var route = CoalitionRoutePlanner.FindRoute(
					blackboard.MapAnalysis, blackboard.ThreatField(), homeRegion, targetRegion,
					MovementClass.Ground, RouteWeights.Assault());
				if (!route.Found)
					continue;

				var type = intel.Type;
				var (economy, production, technology) = TargetEvaluator.Classify(type);
				var uncertainty = intel.Confidence < 0.5f ? 1f : 0.3f;
				var reinforcementRisk = blackboard.Regions[targetRegion].Threats[(int)CoalitionCapability.Reinforcement];
				var counterattackRisk = blackboard.Regions[targetRegion].Threats[(int)CoalitionCapability.GroundAntiArmor];

				var breakdown = TargetEvaluator.Score(
					type, economy, production, technology, targetRegion, route.Cost,
					friendlyLossRisk: 0.2f, enemyReinforcementRisk: reinforcementRisk,
					enemyCounterattackRisk: counterattackRisk, uncertainty: uncertainty,
					blackboard.MapAnalysis, MovementClass.Ground, weights);

				var score = breakdown.Total;
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
				case "breakthrough":
					return MissionType.Breakthrough;
				case "siege":
					return MissionType.Siege;
				case "harassment":
					return MissionType.Harassment;
				case "economyraid":
				case "economy_raid":
					return MissionType.EconomyRaid;
				case "productionraid":
				case "production_raid":
					return MissionType.ProductionRaid;
				case "expansiondenial":
				case "expansion_denial":
					return MissionType.ExpansionDenial;
				case "chokepointseizure":
				case "chokepoint":
					return MissionType.ChokepointSeizure;
				case "flank":
					return MissionType.Flank;
				case "airstrike":
				case "air_strike":
					return MissionType.AirStrike;
				case "navalstrike":
				case "naval_strike":
					return MissionType.NavalStrike;
				case "supportpowerstrike":
				case "support_power":
					return MissionType.SupportPowerStrike;
				case "mobiledefense":
				case "mobile_defense":
					return MissionType.MobileDefense;
				case "antiairumbrella":
				case "aa_umbrella":
					return MissionType.AntiAirUmbrella;
				case "navalscreen":
				case "naval_screen":
					return MissionType.NavalScreen;
				case "delayingaction":
				case "delay":
					return MissionType.DelayingAction;
				case "evacuation":
				case "evacuate":
					return MissionType.Evacuation;
				case "escort":
					return MissionType.Escort;
				case "deeprecon":
				case "deep_recon":
					return MissionType.DeepRecon;
				case "airrecon":
				case "air_recon":
					return MissionType.AirRecon;
				case "navalrecon":
				case "naval_recon":
					return MissionType.NavalRecon;
				case "routerecon":
				case "route_recon":
					return MissionType.RouteRecon;
				case "expansionsearch":
				case "expansion_search":
					return MissionType.ExpansionSearch;
				case "defenseprobe":
				case "defense_probe":
					return MissionType.DefenseProbe;
				case "demonstration":
					return MissionType.Demonstration;
				case "decoytransport":
				case "decoy_transport":
					return MissionType.DecoyTransport;
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

		/// <summary>
		/// Capability-based production contract: the blackboard's per-region threat fields (derived
		/// from <see cref="CoalitionBlackboard.CapabilitiesFor"/> intel) are aggregated into one
		/// profile, and each material enemy capability is answered by its configured counter units -
		/// strongest threat first, skipping counters the coalition already fields. Returns null when
		/// no capability is material enough to contract against, letting the brain fall back to its
		/// base army composition.
		/// </summary>
		string BuildProduceJson()
		{
			var profile = ProductionContract.Aggregate(blackboard.Regions);
			var contracts = new (CoalitionCapability Capability, string[] CounterUnits)[]
			{
				(CoalitionCapability.AntiAir, info.AntiAirUnits.ToArray()),
				(CoalitionCapability.GroundAntiArmor, info.AntiArmorUnits.ToArray()),
				(CoalitionCapability.GroundAntiInfantry, info.AntiInfantryUnits.ToArray()),
				(CoalitionCapability.Naval, info.NavalPriority.ToArray()),
				(CoalitionCapability.Submarine, info.NavalPriority.ToArray())
			};

			// Friendly coverage: how many of each counter type the coalition already fields, so a
			// satisfied contract does not keep ordering counters.
			var fielded = new Dictionary<string, int>();
			var teamIds = TeamPlayers().Select(p => p.InternalName).ToHashSet();
			foreach (var a in world.Actors)
			{
				if (a.IsDead || !a.IsInWorld || a.OccupiesSpace == null || a.Info.HasTraitInfo<BuildingInfo>())
					continue;
				if (!teamIds.Contains(a.Owner.InternalName))
					continue;

				fielded[a.Info.Name] = fielded.GetValueOrDefault(a.Info.Name) + 1;
			}

			var units = ProductionContract.Resolve(profile, contracts, t => fielded.GetValueOrDefault(t), blackboard.HasBigWater);

			// Recon requirement: with the enemy position unknown, produce scouts to locate them.
			if (blackboard.EnemyRegion < 0 && brain?.Info.ScoutUnitTypes is { Count: > 0 } scoutTypes)
				units = units == null ? scoutTypes.ToArray() : scoutTypes.Concat(units).Distinct().ToArray();

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

		/// <summary>
		/// Keeps force assignments in sync with the active mission set: releases forces whose mission
		/// became terminal, assigns each active mission a force at its priority through the arbiter,
		/// and copies the resulting ownership back onto the blackboard's force groups.
		/// </summary>
		void SyncForceAssignments()
		{
			// Release forces held by missions that are no longer active.
			foreach (var mission in missions.Missions)
				if (mission.Status != MissionStatus.Executing && mission.Status != MissionStatus.Ready)
					arbiter.ReleaseMission(mission.Id);

			var ordered = blackboard.Forces.OrderByDescending(f => f.TotalUnits).ToArray();
			var main = ordered.FirstOrDefault();
			var secondary = ordered.Skip(1).FirstOrDefault();
			var transportOwner = blackboard.Transports.FirstOrDefault()?.Owner
				?? blackboard.SpecialAssets.FirstOrDefault()?.Owner;

			foreach (var mission in missions.Missions.Where(m => m.Status == MissionStatus.Executing || m.Status == MissionStatus.Ready))
			{
				var force = mission.Type switch
				{
					MissionType.Attack or MissionType.Raid or MissionType.Counterattack or MissionType.Breakthrough
						or MissionType.Siege or MissionType.ChokepointSeizure => main,
					MissionType.Harassment or MissionType.EconomyRaid or MissionType.ProductionRaid
						or MissionType.ExpansionDenial or MissionType.Flank or MissionType.Feint or MissionType.Bait => secondary ?? main,
					MissionType.Transport or MissionType.SpecialOps => blackboard.Forces.FirstOrDefault(f => f.Owner == transportOwner) ?? main,
					MissionType.Defend or MissionType.MobileDefense or MissionType.AntiAirUmbrella or MissionType.NavalScreen
						or MissionType.DelayingAction or MissionType.Evacuation or MissionType.Escort => main,
					_ => null
				};

				if (force == null)
					continue;

				foreach (var rejection in arbiter.Assign(mission.Id, RoleOf(mission.Type), PriorityOf(mission.Type), force.Owner))
					CoalitionTelemetry.Log(world, $"Order arbiter: {rejection}");

				mission.AssignedForces = arbiter.ForcesOf(mission.Id).ToList();
				EnrichMission(mission);
			}

			// Copy ownership back onto the fresh force groups.
			foreach (var force in blackboard.Forces)
			{
				force.MissionId = arbiter.MissionOf(force.Owner);
				force.Role = arbiter.RoleOf(force.Owner);
			}
		}

		/// <summary>Fills the mission's staging region and planned route from the blackboard's map analysis.</summary>
		void EnrichMission(CoalitionMission mission)
		{
			if (mission.Target != null && blackboard.HomeRegion >= 0)
			{
				var targetRegion = blackboard.RegionOf(mission.Target.Value).Index;
				var route = CoalitionRoutePlanner.FindRoute(blackboard.MapAnalysis, blackboard.ThreatField(),
					blackboard.HomeRegion, targetRegion, MovementClass.Ground, RouteWeights.Assault());
				mission.PlannedRegions = route.Found ? route.Regions : [];
			}

			var best = blackboard.HomeRegion;
			var bestRally = float.MinValue;
			foreach (var region in blackboard.Regions)
			{
				if (!ReachableFromHome(region.Index))
					continue;
				if (blackboard.MapAnalysis.RallyValue[region.Index] > bestRally)
				{
					bestRally = blackboard.MapAnalysis.RallyValue[region.Index];
					best = region.Index;
				}
			}

			mission.StagingRegion = best;
		}

		static ArbiterPriority PriorityOf(MissionType type)
		{
			return type switch
			{
				MissionType.Retreat => ArbiterPriority.Survival,
				MissionType.Transport or MissionType.SpecialOps => ArbiterPriority.SpecialMission,
				MissionType.Attack or MissionType.Raid or MissionType.Counterattack or MissionType.Breakthrough
					or MissionType.Siege or MissionType.ChokepointSeizure => ArbiterPriority.ActiveCombat,
				MissionType.Harassment or MissionType.EconomyRaid or MissionType.ProductionRaid
					or MissionType.ExpansionDenial or MissionType.Flank => ArbiterPriority.ActiveCombat,
				MissionType.AirStrike or MissionType.NavalStrike or MissionType.SupportPowerStrike => ArbiterPriority.SpecialMission,
				MissionType.Defend or MissionType.MobileDefense or MissionType.AntiAirUmbrella or MissionType.NavalScreen
					or MissionType.DelayingAction or MissionType.Evacuation or MissionType.Escort => ArbiterPriority.Defense,
				MissionType.Recon or MissionType.Feint or MissionType.Bait or MissionType.DeepRecon or MissionType.AirRecon
					or MissionType.NavalRecon or MissionType.RouteRecon or MissionType.ExpansionSearch or MissionType.DefenseProbe
					or MissionType.Demonstration or MissionType.DecoyTransport => ArbiterPriority.Recon,
				_ => ArbiterPriority.Staging
			};
		}

		static string RoleOf(MissionType type)
		{
			return type switch
			{
				MissionType.Attack or MissionType.Raid or MissionType.Counterattack or MissionType.Breakthrough
					or MissionType.Siege or MissionType.ChokepointSeizure => "main",
				MissionType.Harassment or MissionType.EconomyRaid or MissionType.ProductionRaid
					or MissionType.ExpansionDenial or MissionType.Flank => "flank",
				MissionType.AirStrike => "air",
				MissionType.NavalStrike => "naval",
				MissionType.SupportPowerStrike => "support",
				MissionType.Feint or MissionType.Demonstration => "feint",
				MissionType.Bait => "bait",
				MissionType.Transport or MissionType.SpecialOps or MissionType.DecoyTransport => "special",
				MissionType.Defend or MissionType.MobileDefense or MissionType.DelayingAction
					or MissionType.Evacuation or MissionType.Escort => "defend",
				MissionType.AntiAirUmbrella => "aa",
				MissionType.NavalScreen => "naval",
				MissionType.Recon or MissionType.DeepRecon or MissionType.AirRecon or MissionType.NavalRecon
					or MissionType.RouteRecon or MissionType.ExpansionSearch or MissionType.DefenseProbe => "recon",
				MissionType.Retreat => "retreat",
				_ => "support"
			};
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

			// Coalition reserve: the sum of each force's held-back reserve (the army the coalition
			// keeps uncommitted), so the brain and the LLM see how much is in reserve.
			var reserveFraction = Math.Max(1, brain?.Info.ScaledReserveFraction() ?? 4);
			var reserve = blackboard.Forces.Sum(f => f.TotalUnits / reserveFraction);

			// "water" tells the brain whether a big explored water body exists. Without it the mixed-arms
			// gate must not demand a naval arm, and naval production is skipped.
			return $"{{\"army\":{air + naval + land},\"air\":{air},\"naval\":{naval},\"land\":{land},\"reserve\":{reserve},\"water\":{(blackboard.HasBigWater ? "true" : "false")}}}";
		}

		/// <summary>
		/// Snapshots the live blackboard into the plain-data tool context consumed by the LLM tool
		/// API. Called on the game thread; the HTTP tool server only reads the snapshot, so tool calls
		/// never race the game loop.
		/// </summary>
		public ToolContext BuildToolContext()
		{
			if (blackboard == null)
				return null;

			var members = TeamPlayers();
			return new ToolContext
			{
				Tick = blackboard.Tick,
				Timestep = (int)world.Timestep,
				Regions = blackboard.Regions,
				Forces = blackboard.Forces.ToArray(),
				SpecialAssets = blackboard.SpecialAssets.ToArray(),
				Transports = blackboard.Transports.ToArray(),
				Facilities = blackboard.Facilities.ToArray(),
				Missions = missions.Missions.Select(m => new MissionState
				{
					Id = m.Id,
					Type = m.Type.ToString().ToLowerInvariant(),
					Status = m.Status.ToString().ToLowerInvariant(),
					Phase = m.Phase.ToString().ToLowerInvariant(),
					Target = m.Target,
					Priority = m.Priority,
					Readiness = m.Readiness,
					Progress = m.Progress,
					OutcomeReason = m.OutcomeReason
				}).ToArray(),
				EnemyIntel = blackboard.EnemyIntel.ToArray(),
				Events = blackboard.Events.ToArray(),
				Opponent = blackboard.Opponent,
				CoalitionCash = blackboard.CoalitionCash,
				MemberCash = members.ToDictionary(p => p.InternalName,
					p => p.PlayerActor.TraitOrDefault<PlayerResources>()?.GetCashAndResources() ?? 0),
				HomeRegion = blackboard.HomeRegion,
				EnemyRegion = blackboard.EnemyRegion,
				CoalitionArmyStrength = blackboard.CoalitionArmyStrength,
				EnemyArmyStrength = blackboard.EnemyArmyStrength,
				EnemyArmyCount = (int)blackboard.EnemyArmyCount,
				DeceptionEffectiveness = blackboard.DeceptionEffectiveness,
				DeceptionEnemiesDrawn = blackboard.DeceptionEnemiesDrawn,
				PowerProvided = blackboard.PowerProvided,
				PowerDrained = blackboard.PowerDrained,
				RefineryCount = blackboard.RefineryCount,
				HarvesterCount = blackboard.HarvesterCount,
				ActiveHarvesterCount = blackboard.ActiveHarvesterCount,
				ResourceCellsRemaining = blackboard.ResourceCellsRemaining,
				MapAnalysis = blackboard.MapAnalysis,
				ThreatField = blackboard.ThreatField()
			};
		}

		/// <summary>Returns the least explored region that is reachable from the home region on the ground.</summary>
		CPos? LeastExploredRegionNear()
		{
			CoalitionRegion best = null;
			var bestCoverage = 1f;
			for (var i = 0; i < blackboard.Regions.Length; i++)
			{
				// Skip regions the coalition cannot reach on the ground: reconning an island or a
				// sea body the army can never enter wastes scouts and produces unusable intel.
				if (!ReachableFromHome(i))
					continue;

				var coverage = blackboard.Regions[i].FriendlyControl;
				if (coverage < bestCoverage)
				{
					best = blackboard.Regions[i];
					bestCoverage = coverage;
				}
			}

			return best == null ? null : RegionCenter(best.Index);
		}

		/// <summary>
		/// Picks a distinct enemy-facing region for the feint. Prefers a region that is ground-adjacent
		/// to the main attack's region, so the feint threatens the same approach corridor from a second
		/// axis and forces the enemy to split its response.
		/// </summary>
		CPos? FeintRegionTarget()
		{
			var attack = missions.Missions.FirstOrDefault(m => m.Type == MissionType.Attack && m.Target != null);
			var attackRegion = attack?.Target != null ? blackboard.RegionOf(attack.Target.Value).Index : -1;
			for (var i = 0; i < blackboard.Regions.Length; i++)
			{
				if (blackboard.Regions[i].EnemyPressure <= 0)
					continue;

				if (attackRegion >= 0 && blackboard.MapAnalysis.IsAdjacent(MovementClass.Ground, attackRegion, i))
					return RegionCenter(i);
			}

			// Fall back to any enemy-facing region distinct from the main target.
			for (var i = 0; i < blackboard.Regions.Length; i++)
				if (blackboard.Regions[i].EnemyPressure > 0 && i != attackRegion)
					return RegionCenter(i);

			return null;
		}

		/// <summary>
		/// Feint priority grows with the opponent model and the measured deception record: feints that
		/// drew enemy responses are funded harder, ignored feints are cut back. Never-attempted
		/// deception is neutral, so the first feint is not penalized for having no history.
		/// </summary>
		int FeintPriority()
		{
			var effectiveness = missions.DeceptionAttempts == 0 ? 0.5f : blackboard.DeceptionEffectiveness;
			var basePriority = blackboard.Opponent.MovesWholeArmyToDefend ? 75 : 60;
			return (int)(basePriority + 15 * (2 * effectiveness - 1));
		}

		/// <summary>True after several deception attempts drew no response: the enemy ignores feints.</summary>
		bool DeceptionSaturated()
		{
			return missions.DeceptionAttempts >= 3 && missions.DeceptionSuccesses == 0;
		}

		/// <summary>
		/// Creates intel-driven offensive missions: economy/production raids, air and support-power
		/// strikes, chokepoint seizure, and a flank to divide the enemy defense.
		/// </summary>
		void CreateOffensiveMissions(float ratio)
		{
			if (blackboard.EnemyRegion < 0)
				return;

			var structures = blackboard.EnemyIntel.Where(i => i.Class == UnitClass.Structure).ToArray();
			if (structures.Length == 0)
				return;

			var economy = structures.Where(i => TargetEvaluator.EconomicValue(i.Type) > 0).ToArray();
			if (economy.Length > 0 && ratio < 1.2f && !missions.Missions.Any(m => m.Type == MissionType.EconomyRaid))
				EnsureMission(MissionType.EconomyRaid, 65, HighestScored(economy, i => TargetEvaluator.EconomicValue(i.Type)), "Raid enemy economy");

			var production = structures.Where(i => TargetEvaluator.ProductionValue(i.Type) > 0).ToArray();
			if (production.Length > 0 && ratio < 1.0f && !missions.Missions.Any(m => m.Type == MissionType.ProductionRaid))
				EnsureMission(MissionType.ProductionRaid, 65, HighestScored(production, i => TargetEvaluator.ProductionValue(i.Type)), "Raid enemy production");

			var enemyAA = blackboard.Regions[blackboard.EnemyRegion].Threats[(int)CoalitionCapability.AntiAir];
			var hasAir = blackboard.Forces.Any(f => f.Counts[(int)UnitClass.Air] > 0);
			var highValue = BestScoredTarget();
			if (hasAir && enemyAA < 0.5f && highValue != null && !missions.Missions.Any(m => m.Type == MissionType.AirStrike))
				EnsureMission(MissionType.AirStrike, 70, highValue, "Air strike on high-value target");

			// Shaping: soften enemy air defenses before a staged ground assault, even when AA is strong.
			var stagedAttack = missions.Missions.Any(m => MissionManager.IsOffensive(m.Type) && m.Status == MissionStatus.Executing
				&& m.Type != MissionType.AirStrike && m.Type != MissionType.NavalStrike && m.Type != MissionType.SupportPowerStrike);
			if (hasAir && enemyAA >= 0.5f && stagedAttack && !missions.Missions.Any(m => m.Type == MissionType.AirStrike))
				EnsureMission(MissionType.AirStrike, 65, RegionCenter(blackboard.EnemyRegion), "Soften enemy air defenses before the assault");

			if (blackboard.HasReadySuperweapon && highValue != null && !missions.Missions.Any(m => m.Type == MissionType.SupportPowerStrike))
				EnsureMission(MissionType.SupportPowerStrike, 95, highValue, "Support-power strike");

			var choke = ChokepointRegionNearEnemy();
			if (choke != null && !missions.Missions.Any(m => m.Type == MissionType.ChokepointSeizure))
				EnsureMission(MissionType.ChokepointSeizure, 60, choke, "Seize chokepoint");

			var attack = missions.Missions.FirstOrDefault(m => MissionManager.IsOffensive(m.Type) && m.Target != null && m.Status == MissionStatus.Executing);
			if (attack != null && !missions.Missions.Any(m => m.Type == MissionType.Flank))
			{
				var flankTarget = FlankRegionTarget(attack);
				if (flankTarget != null)
					EnsureMission(MissionType.Flank, 55, flankTarget, "Flank the enemy");
			}
		}

		static CPos? HighestScored(EnemyIntel[] intel, Func<EnemyIntel, float> value)
		{
			EnemyIntel best = null;
			var bestValue = float.MinValue;
			foreach (var i in intel)
			{
				var v = value(i);
				if (v > bestValue)
				{
					bestValue = v;
					best = i;
				}
			}

			return best?.LastSeenCell;
		}

		CPos? ChokepointRegionNearEnemy()
		{
			if (blackboard.EnemyRegion < 0)
				return null;

			for (var i = 0; i < blackboard.Regions.Length; i++)
				if (blackboard.MapAnalysis.IsChokepoint(MovementClass.Ground, blackboard.EnemyRegion, i))
					return RegionCenter(i);

			return null;
		}

		CPos? FlankRegionTarget(CoalitionMission attack)
		{
			var attackRegion = attack.Target != null ? blackboard.RegionOf(attack.Target.Value).Index : -1;
			for (var i = 0; i < blackboard.Regions.Length; i++)
			{
				if (blackboard.Regions[i].EnemyPressure <= 0 || i == attackRegion)
					continue;
				if (blackboard.MapAnalysis.IsAdjacent(MovementClass.Ground, blackboard.EnemyRegion, i))
					return RegionCenter(i);
			}

			return null;
		}

		/// <summary>
		/// Creates specialized defensive missions: mobile interception of enemies away from the base,
		/// an anti-air umbrella when enemy air is spotted, a naval screen when enemy ships are known,
		/// and an economy escort when the enemy raids harvesters.
		/// </summary>
		void CreateDefensiveMissions()
		{
			// Mobile defense: enemies sighted away from the base region are intercepted in the field.
			if (blackboard.HomeRegion >= 0 && !missions.Missions.Any(m => m.Type == MissionType.MobileDefense))
			{
				var away = blackboard.EnemyIntel.FirstOrDefault(i => i.Class != UnitClass.Structure
					&& blackboard.RegionOf(i.LastSeenCell).Index != blackboard.HomeRegion);
				if (away != null)
					EnsureMission(MissionType.MobileDefense, 50, away.LastSeenCell, "Intercept enemy away from base");
			}

			// Anti-air umbrella: enemy air presence demands AA concentration over the base.
			if (blackboard.EnemyIntel.Any(i => i.Class == UnitClass.Air) && !missions.Missions.Any(m => m.Type == MissionType.AntiAirUmbrella))
				EnsureMission(MissionType.AntiAirUmbrella, 55, RegionCenter(blackboard.HomeRegion), "Anti-air umbrella over the base");

			// Naval screen: enemy ships demand a coastal screen.
			if (blackboard.EnemyIntel.Any(i => i.Class == UnitClass.Naval) && blackboard.HasBigWater
				&& !missions.Missions.Any(m => m.Type == MissionType.NavalScreen))
				EnsureMission(MissionType.NavalScreen, 55, RegionCenter(blackboard.HomeRegion), "Naval screen");

			// Economy escort: a raid-sensitive enemy threatens harvesters.
			if (blackboard.Opponent.AttacksHarvesters && !missions.Missions.Any(m => m.Type == MissionType.Escort))
				EnsureMission(MissionType.Escort, 45, RegionCenter(blackboard.HomeRegion), "Escort harvesters");
		}

		/// <summary>
		/// Creates value-of-information-driven reconnaissance once the enemy is located: deep recon of
		/// the enemy rear, expansion search toward resource-rich unexplored ground, and defense probing
		/// of the enemy's perimeter.
		/// </summary>
		void CreateReconMissions()
		{
			var existing = missions.Missions.Any(m => MissionManager.IsRecon(m.Type));
			if (existing || blackboard.EnemyRegion < 0)
				return;

			// Deep recon: the least-explored region adjacent to the enemy base (their rear).
			var deep = BestReconRegion(r => blackboard.MapAnalysis.IsAdjacent(MovementClass.Ground, r, blackboard.EnemyRegion) ? 1f : 0f);
			if (deep != null)
				EnsureMission(MissionType.DeepRecon, 40, RegionCenter(deep.Value), "Deep reconnaissance of the enemy rear");

			// Expansion search: the least-explored region with the highest expansion value.
			if (!missions.Missions.Any(m => m.Type == MissionType.ExpansionSearch))
			{
				var expansion = BestReconRegion(r => -blackboard.MapAnalysis.ExpansionValue[r]);
				if (expansion != null)
					EnsureMission(MissionType.ExpansionSearch, 35, RegionCenter(expansion.Value), "Search for an expansion site");
			}

			// Defense probe: a lightly-observed enemy region with high static-defense threat.
			if (!missions.Missions.Any(m => m.Type == MissionType.DefenseProbe))
			{
				var probe = BestReconRegion(r => -blackboard.Regions[r].Threats[(int)CoalitionCapability.StaticDefense]);
				if (probe != null)
					EnsureMission(MissionType.DefenseProbe, 35, RegionCenter(probe.Value), "Probe enemy defenses");
			}
		}

		/// <summary>Returns the best unexplored, ground-reachable region by a value selector.</summary>
		int? BestReconRegion(Func<int, float> value)
		{
			int? best = null;
			var bestValue = float.MinValue;
			for (var i = 0; i < blackboard.Regions.Length; i++)
			{
				if (blackboard.Regions[i].FriendlyControl > 0.1f || !ReachableFromHome(i))
					continue;

				var v = value(i);
				if (v > bestValue)
				{
					bestValue = v;
					best = i;
				}
			}

			return best;
		}

		/// <summary>True when the region is in the same ground-connected component as the home region.</summary>
		bool ReachableFromHome(int regionIndex)
		{
			if (regionIndex < 0 || blackboard.HomeRegion < 0)
				return false;

			return blackboard.MapAnalysis.ComponentOf(MovementClass.Ground, regionIndex)
				== blackboard.MapAnalysis.ComponentOf(MovementClass.Ground, blackboard.HomeRegion);
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
				if (intent is null)
					return;

				// Validate the non-mission intent fields engine-side: an unknown posture is rejected
				// and cleared (the deterministic posture applies), and malformed production entries are
				// dropped. The deterministic commander remains authoritative on any rejection.
				var postureRejection = CommandValidator.ValidatePosture(intent.Posture);
				if (postureRejection != null)
				{
					CoalitionTelemetry.Log(world, $"Command validator: {postureRejection}");
					intent.Posture = null;
				}

				var produceRejections = CommandValidator.ValidateProduce(intent.Produce);
				foreach (var (_, reason) in produceRejections)
					CoalitionTelemetry.Log(world, $"Command validator: {reason}");

				if (produceRejections.Count > 0)
					intent.Produce = intent.Produce.Where((_, i) => !produceRejections.Any(r => r.Index == i)).ToArray();

				llmIntent = intent;
			}
			catch
			{
				// Invalid intent is ignored; the deterministic commander remains authoritative.
			}
		}
	}
}
