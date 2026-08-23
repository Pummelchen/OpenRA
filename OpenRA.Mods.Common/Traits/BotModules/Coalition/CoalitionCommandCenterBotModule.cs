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

		[Desc("Advance on a starting location inferred from public map data when the enemy base has not",
			"been found. Off by default: measured over a full opponent matrix this produced no wins and",
			"drew units away from reconnaissance. See AUDIT_REPORT.md for the measurements.")]
		public readonly bool AdvanceOnInferredBase = false;

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
		public readonly string[] ArmyPriority = [];

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

		/// <summary>The effective intelligence axis, overridable per-run by the headless harness.</summary>
		public int EffectiveIntelligence => HeadlessSkirmish.CommanderIntelligence ?? Intelligence;

		/// <summary>True at the top intelligence setting: the coalition sees every enemy actor.</summary>
		public bool IsOmniscient => EffectiveIntelligence >= 3;

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

			/// <summary>Mission types to cancel. Each matching active mission is cancelled and its forces released.</summary>
			public string[] CancelMissions { get; set; }

			/// <summary>Override the coalition's reserve fraction (1/N of the army held back). 0 = no override.</summary>
			public int ReserveFraction { get; set; }

			/// <summary>Required rationale when reducing the meaningful reserve below 15%.</summary>
			public string ReserveJustification { get; set; }

			/// <summary>Production capability directive: the LLM requests a specific capability (e.g. "anti_air", "anti_armor", "naval").</summary>
			public string RequestCapability { get; set; }

			/// <summary>Production unit-name override: the LLM directly specifies which units to prioritize.</summary>
			public string[] ProductionDirective { get; set; }

			/// <summary>Expansion priority override (0 = default, 1 = prioritize expansion, -1 = suppress expansion).</summary>
			public int ExpansionPriority { get; set; }

			/// <summary>Mission IDs to modify (cancel + recreate with new parameters). Each entry is a mission type string.</summary>
			public string[] ModifyMissions { get; set; }

			/// <summary>Force-to-mission assignments requested by the LLM. Each entry pairs a force player id with a mission id.</summary>
			public LlmForceAssignment[] AssignForce { get; set; }

			/// <summary>Force player ids to release from their current missions back to the pool.</summary>
			public string[] ReleaseForce { get; set; }
		}

		sealed class LlmMission
		{
			public string Type { get; set; }
			public int X { get; set; }
			public int Y { get; set; }
			public int Priority { get; set; }
		}

		sealed class LlmForceAssignment
		{
			public string ForceId { get; set; }
			public string MissionId { get; set; }
		}

		readonly CoalitionCommandCenterBotModuleInfo info;
		readonly MissionManager missions = new();
		readonly CoalitionOrderArbiter arbiter = new();
		CoalitionIntelTracker intelTracker;

		Player player;
		World world;
		StrategicBrainBotModule brain;

		/// <summary>The rebuilt decision layer. Null when the trait is absent, which leaves the old behaviour intact.</summary>
		CommanderPlanBotModule planner;
		LlmIntent llmIntent;
		int lastBlackboardTick;
		int lastCommandTick;
		string lastPosture;
		string lastOpponentProfile;
		StrategicPosture strategicPosture;

		/// <summary>The coalition's main effort: the single highest-value objective, re-selected
		/// every command tick so effort concentrates on one area instead of spreading evenly.</summary>
		CPos? mainEffort;

		// Durable opponent observations. The blackboard (and its opponent model) is rebuilt every
		// BlackboardInterval, so learned values must live here and be copied into each fresh model.
		int responseTimeSum;
		int responseTimeSamples;
		int raidContactTicks;
		int raidResponseSamples;
		int raidResponseSuccesses;

		// Durable peak unit count per owner, for casualty tracking across blackboard rebuilds.
		readonly Dictionary<string, int> peakForceUnits = [];

		// Durable event-transition state, so a signal wakes the commander only once per change.
		bool lastSuperweaponReady;
		int lastSpecialAssetCount;

		// Match-quality telemetry, sampled once per command.
		readonly CoalitionMatchMetrics matchMetrics = new();
		int lastMetricsSummaryTick = int.MinValue;
		int lastFloatingTick = int.MinValue;
		string lastProductionDirective;

		// Durable peak construction-yard count, for detecting new expansions (req 608).
		int peakConyardCount;
		readonly HashSet<CPos> knownConyardCells = [];
		bool expansionBaselineInitialized;
		CPos? recentExpansionCell;
		int recentExpansionTick = int.MinValue;
		int lastControllerReplanTick = int.MinValue;

		/// <summary>The current blackboard, for external consumers (LLM snapshot, tests).</summary>
		public CoalitionBlackboard Blackboard { get; private set; }

		/// <summary>The tick of the most recent coalition attack wave, for response-time measurement.</summary>
		public int LastWaveTick { get; private set; } = int.MinValue;

		/// <summary>
		/// Records an enemy reaction to a coalition attack: the delay between our wave launch and
		/// the enemy's first response, in seconds. Durable across blackboard rebuilds.
		/// </summary>
		public void RecordEnemyResponse(int currentTick)
		{
			if (LastWaveTick < 0)
				return;

			var delayTicks = currentTick - LastWaveTick;
			if (delayTicks < 0)
				return;

			responseTimeSum += delayTicks;
			responseTimeSamples++;
		}

		/// <summary>Marks the coalition attack wave launch tick, resetting the response timer.</summary>
		public void MarkWaveLaunch(int tick)
		{
			LastWaveTick = tick;
		}

		/// <summary>The tick of the most recent feint, for measuring whether it opened a window (req 627).</summary>
		public int LastFeintTick { get; private set; } = int.MinValue;

		/// <summary>Marks a feint launch tick (req 627).</summary>
		public void MarkFeintLaunch(int tick)
		{
			LastFeintTick = tick;
		}

		/// <summary>Records an MCV deployment/expansion for telemetry (req 608).</summary>
		public void RecordExpansion(int tick)
		{
			matchMetrics.RecordExpansion(tick);
			CoalitionTelemetry.Log(world, $"Expansion recorded at tick {tick}");
		}

		/// <summary>
		/// Schedules an early strategic review when a tactical controller cannot execute its mission.
		/// Requests are debounced so a persistent missing capability cannot cause a review storm.
		/// </summary>
		/// <summary>
		/// Debounce rule for controller-driven replanning. A controller can report the same inability
		/// every tick, so without this one blocked mission would re-plan the whole coalition
		/// continuously. Pure so the rule is testable without a World (reqs 548, 549).
		/// </summary>
		public static bool MayReplan(int currentTick, int lastReplanTick, int interval)
		{
			return lastReplanTick == int.MinValue || currentTick - lastReplanTick >= Math.Max(1, interval);
		}

		public void RequestReplan(string reason)
		{
			if (world == null || !MayReplan(world.WorldTick, lastControllerReplanTick, info.BlackboardInterval))
				return;

			lastControllerReplanTick = world.WorldTick;
			Blackboard?.AddEvent("controller_replan", null, reason);
			lastCommandTick = world.WorldTick - info.CommandInterval;
			CoalitionTelemetry.Log(world, $"Controller requested strategic replan: {reason}");
		}

		/// <summary>Records a wave-launch synchronization error for telemetry (req 612).</summary>
		public void RecordSyncError(int tick, int errorTicks)
		{
			matchMetrics.RecordSyncError(tick, errorTicks);
		}

		/// <summary>Records a retreat event for telemetry (req 614).</summary>
		public void RecordRetreat(int tick, int unitCount)
		{
			matchMetrics.RecordRetreat(tick, unitCount);
		}

		/// <summary>Records how many units survived the most recently started retreat.</summary>
		public void RecordRetreatOutcome(int survivingUnits)
		{
			matchMetrics.RecordRetreatOutcome(survivingUnits);
		}

		/// <summary>Records a recon mission outcome for telemetry (req 616).</summary>
		public void RecordReconMission(bool usefulIntel)
		{
			matchMetrics.RecordReconMission(usefulIntel);
		}

		/// <summary>Records a transport mission outcome for telemetry (req 617).</summary>
		public void RecordTransport(bool survived)
		{
			matchMetrics.RecordTransport(survived);
		}

		/// <summary>Records a counterattack and enemy destroyed for telemetry (req 620).</summary>
		public void RecordCounterattack(int enemyDestroyed)
		{
			matchMetrics.RecordCounterattack(enemyDestroyed);
		}

		/// <summary>Records a base-defense response time for telemetry (req 621).</summary>
		public void RecordBaseDefenseResponse(int threatTick, int responseTick)
		{
			matchMetrics.RecordBaseDefenseResponse(threatTick, responseTick);
		}

		/// <summary>Records an engagement and whether the coalition held local superiority (req 613).</summary>
		public void RecordEngagement(bool localSuperiority)
		{
			matchMetrics.RecordEngagement(localSuperiority);
		}

		/// <summary>Records a feint launch for telemetry (req 627).</summary>
		public void RecordFeintLaunch()
		{
			matchMetrics.RecordFeintLaunch();
		}

		/// <summary>Records that a feint opened a launch window for telemetry (req 627).</summary>
		public void RecordFeintOpenedWindow()
		{
			matchMetrics.RecordFeintOpenedWindow();
		}

		static readonly JsonSerializerOptions IntentOptions = new() { PropertyNameCaseInsensitive = true };

		/// <summary>True only when the resolved production priority actually changed.</summary>
		public static bool ProductionDirectiveChanged(string previous, string current)
		{
			return previous != current;
		}

		/// <summary>
		/// Authorizes a fair-fog field interception only for a material, currently observed force that
		/// the coalition can meet at parity. Unknown enemy strength never qualifies as an advantage.
		/// </summary>
		public static bool ShouldInterceptObservedForce(int observedMobile, float enemyToFriendlyRatio,
			int coalitionArmy, int coordinatedMinimum)
		{
			var materialContact = Math.Max(1, coordinatedMinimum / 4);
			var estimatedEnemyArmy = enemyToFriendlyRatio * coalitionArmy;
			return observedMobile > 0 && estimatedEnemyArmy >= materialContact && coalitionArmy >= materialContact
				&& enemyToFriendlyRatio > 0f && enemyToFriendlyRatio <= 1f;
		}

		/// <summary>Force multiple of the coordinated minimum required before advancing on an inferred objective.</summary>
		public const int AdvanceForceMultiple = 3;

		/// <summary>Tick at which the coalition first had an army and no located enemy base.</summary>
		int enemyBaseSearchStartTick = -1;
		bool lastReconInForce;

		/// <summary>
		/// Whether a coalition that cannot find the enemy should advance to make contact. Requires a
		/// force worth committing and a sustained failure to locate the enemy base, so this never
		/// pre-empts a deliberate assault or throws away an early army. Pure, so the rule is testable
		/// without a World.
		/// </summary>
		public static bool ShouldAdvanceToFindEnemy(int observedEnemyRegion, int coalitionArmy,
			int coordinatedMinimum, int currentTick, int searchStartTick, int commandInterval)
		{
			// An observed base means the normal assault gate applies; nothing to search for.
			if (observedEnemyRegion >= 0)
				return false;

			// Committing before the coalition fields a real force would feed units in piecemeal.
			// The multiple matters: advancing on an unconfirmed objective across a large map is far
			// more costly than defending, so it is only worth doing with an overwhelming force.
			if (coalitionArmy < coordinatedMinimum * AdvanceForceMultiple)
				return false;

			// Give reconnaissance a fair chance first: only advance once scouting has had time and
			// still failed. Ten command intervals is minutes of game time, not a hair trigger.
			if (searchStartTick < 0)
				return false;

			return currentTick - searchStartTick >= Math.Max(1, commandInterval) * 10;
		}

		/// <summary>Concentrates a contact interception midway toward home instead of charging the enemy front.</summary>
		public static CPos InterceptionCell(CPos contact, CPos home)
		{
			return contact + (home - contact) / 2;
		}

		/// <summary>
		/// True for actors that make up a player's economy: refineries, harvesters and silos. These
		/// are what "economic damage" is measured over (reqs 604, 605).
		/// </summary>
		static bool IsEconomicAsset(ActorInfo actorInfo)
		{
			return actorInfo.HasTraitInfo<RefineryInfo>()
				|| actorInfo.HasTraitInfo<HarvesterInfo>()
				|| actorInfo.HasTraitInfo<StoresResourcesInfo>();
		}

		/// <summary>Build cost of an actor, or 0 when it carries no value.</summary>
		static int CostOf(ActorInfo actorInfo)
		{
			return actorInfo.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0;
		}

		/// <summary>Build cost of an actor type name, resolved against the live ruleset.</summary>
		int CostOfType(string type)
		{
			return world.Map.Rules.Actors.TryGetValue(type, out var actorInfo) ? CostOf(actorInfo) : 0;
		}

		/// <summary>Scores the combat estimator against real engagement outcomes (req 159).</summary>
		public EngagementOutcomeLog EngagementLog { get; } = new();

		/// <summary>
		/// Predicts each offensive mission as it commits and resolves it when it concludes, so the
		/// estimator is measured per engagement rather than once per match. A mission is the right
		/// engagement unit here: it is the granularity the commander actually commits force at.
		/// </summary>
		void TrackEngagementOutcomes(float coalitionArmy, float enemyArmy)
		{
			var tick = world.WorldTick;
			foreach (var mission in missions.Missions)
			{
				if (!MissionManager.IsOffensive(mission.Type))
					continue;

				if (mission.Status == MissionStatus.Executing)
				{
					// The prediction is the estimate at the moment force was committed; re-predicting
					// an open engagement is ignored by the log, so hindsight cannot improve the score.
					var (winRatio, lossFraction) = CombatEstimator.Estimate(coalitionArmy, enemyArmy);
					EngagementLog.Predict(mission.Id, tick, winRatio, lossFraction, coalitionArmy);
				}
				else if (mission.Status is MissionStatus.Succeeded or MissionStatus.Failed or MissionStatus.Aborted)
				{
					// Actual loss fraction: what the committed force actually lost, taken from the
					// casualty tracking on the groups that carried the mission.
					var committed = Blackboard.Forces
						.Where(f => f.MissionId == mission.Id)
						.ToArray();
					var actualLoss = committed.Length == 0 ? 0f : committed.Average(f => f.CasualtyFraction);
					EngagementLog.Resolve(mission.Id, tick, mission.Status == MissionStatus.Succeeded, actualLoss);
				}
			}
		}

		/// <summary>
		/// The cell the influence map recommends assaulting: enemy value weighted by how weakly the
		/// ground is held. Null when nothing observed is worth attacking.
		/// </summary>
		CPos? InfluenceAssaultTarget()
		{
			var influence = Blackboard?.Influence;
			if (influence == null || Blackboard.EnemyIntel.Count == 0)
				return null;

			// Objective value per tile, from observed structures only.
			//
			// Mobile units are deliberately excluded. Counting them made the enemy ARMY the
			// objective, because a tile holding twenty tanks outscored a tile holding a refinery -
			// so the coalition chased the field army all match and never touched the base. Measured
			// over a full mirror: exchange 5.14 and zero enemy structures destroyed, which is
			// exactly how 38 of 58 matches ended in a time-limit draw.
			//
			// Killing units does not take ground; they are replaced. Only structures are objectives.
			var valueByTile = new Dictionary<(int, int), float>();
			foreach (var intel in Blackboard.EnemyIntel)
			{
				if (intel.Class != UnitClass.Structure)
					continue;

				var tile = (intel.LastSeenCell.X / InfluenceMap.TileSize, intel.LastSeenCell.Y / InfluenceMap.TileSize);
				var value = 1f + TargetEvaluator.EconomicValue(intel.Type) * 3f
					+ TargetEvaluator.ProductionValue(intel.Type) * 2f
					+ TargetEvaluator.TechnologyValue(intel.Type) * 1.5f;

				valueByTile[tile] = valueByTile.GetValueOrDefault(tile) + value * intel.Confidence;
			}

			if (valueByTile.Count == 0)
				return null;

			var best = influence.BestAssaultTile((x, y) => valueByTile.GetValueOrDefault((x, y)));
			return best == null ? null : influence.CellOf(best.Value.X, best.Value.Y);
		}

		/// <summary>
		/// The feint objective the influence map recommends: where the enemy is most invested and
		/// the coalition least, so the response is large and what is risked is small.
		/// </summary>
		CPos? InfluenceFeintTarget()
		{
			var influence = Blackboard?.Influence;
			var best = influence?.BestFeintTile();
			return best == null ? null : influence.CellOf(best.Value.X, best.Value.Y);
		}

		/// <summary>Cells the coalition must not lose: the main base plus every production facility.</summary>
		IEnumerable<CPos> ProtectedAssetCells()
		{
			yield return Blackboard.HomeCell;
			foreach (var facility in Blackboard.Facilities)
				yield return facility.Cell;
		}

		/// <summary>
		/// Decides whether an asset needs a dedicated relief mission (req 202). Pure so it can be
		/// tested without a World: an asset is in distress when the observed attackers near it
		/// outnumber the defenders already covering it.
		/// </summary>
		public static bool NeedsEmergencyRelief(int attackersNearAsset, int defendersNearAsset)
		{
			return attackersNearAsset > 0 && attackersNearAsset > defendersNearAsset;
		}

		/// <summary>
		/// The nearest coalition asset that is under attack and cannot hold with the forces already
		/// covering it, or null. Fair fog: only currently observed enemies count.
		/// </summary>
		CPos? AssetUnderImmediateThreat()
		{
			const int ThreatRadius = 12;
			var attackers = Blackboard.EnemyIntel
				.Where(i => i.Status == IntelStatus.Observed && i.Class != UnitClass.Structure)
				.ToArray();
			if (attackers.Length == 0)
				return null;

			CPos? worst = null;
			var worstDeficit = 0;
			foreach (var asset in ProtectedAssetCells())
			{
				var near = attackers.Count(i => (i.LastSeenCell - asset).LengthSquared <= ThreatRadius * ThreatRadius);
				if (near == 0)
					continue;

				var defenders = Blackboard.Forces.Sum(f =>
					(f.Center - asset).LengthSquared <= ThreatRadius * ThreatRadius ? f.TotalUnits : 0);
				if (!NeedsEmergencyRelief(near, defenders))
					continue;

				var deficit = near - defenders;
				if (deficit > worstDeficit)
				{
					worstDeficit = deficit;
					worst = asset;
				}
			}

			return worst;
		}

		/// <summary>
		/// An interception point for an observed mobile enemy force that is closing on a coalition
		/// asset but has not reached it yet (req 204), or null. Fair fog: observed contacts only.
		/// </summary>
		CPos? InboundEnemyForce()
		{
			const int ApproachRadius = 26;
			const int ArrivedRadius = 10;
			var movers = Blackboard.EnemyIntel
				.Where(i => i.Status == IntelStatus.Observed && i.Class != UnitClass.Structure)
				.ToArray();
			if (movers.Length == 0)
				return null;

			CPos? best = null;
			var bestDistance = long.MaxValue;
			foreach (var asset in ProtectedAssetCells())
			{
				foreach (var mover in movers)
				{
					var distance = (mover.LastSeenCell - asset).LengthSquared;

					// Already on top of the asset: that is base defense, not interception.
					if (distance <= ArrivedRadius * ArrivedRadius || distance > ApproachRadius * ApproachRadius)
						continue;

					if (distance < bestDistance)
					{
						bestDistance = distance;
						best = InterceptionCell(mover.LastSeenCell, asset);
					}
				}
			}

			return best;
		}

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
			planner = player.PlayerActor.TraitsImplementing<CommanderPlanBotModule>().FirstOrDefault(m => !m.IsTraitDisabled);

			var tick = world.WorldTick;
			if (tick - lastBlackboardTick >= info.BlackboardInterval)
			{
				lastBlackboardTick = tick;

				// Feed the durable intel tracker with everything the coalition can see this tick (or
				// everything, in omniscient mode) and age it into the honesty ladder. The aged list is
				// seeded into the fresh blackboard so last-known/inferred intel survives the rebuild.
				intelTracker ??= new CoalitionIntelTracker(info.SightingMemoryTicks, world.Timestep);
				var seedIntel = ObserveEnemies(tick);

				Blackboard = new CoalitionBlackboard(world, player, TeamPlayers(), Classify,
					info.WaterTerrainTypes, info.BigWaterMinimumCells, info.ValuableResourceTypes,
					info.ArtilleryTypes, info.SubmarineTypes, info.DetectionTypes,
					info.SupportPowerStructures, info.ProductionStructures,
					brain?.Info.TransportTypes, brain?.Info.ScoutUnitTypes?.ToFrozenSet(), info.AntiAirUnits, info.SpecialTypes,
					seedIntel, info.IsOmniscient)
				{
					// The deception record is durable across blackboard rebuilds: it lives on the mission
					// manager and is copied into every fresh model for the planner and the LLM snapshot.
					DeceptionAttempts = missions.DeceptionAttempts,
					DeceptionSuccesses = missions.DeceptionSuccesses,
					DeceptionEnemiesDrawn = missions.DeceptionEnemiesDrawn
				};
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

			if (tick - lastCommandTick >= info.CommandInterval && Blackboard != null)
			{
				lastCommandTick = tick;
				RunCommand();
			}
		}

		// State for detecting material events between reviews.
		readonly StrategicEventDetector eventDetector = new();

		/// <summary>Scores the opponent model's forecasts against later observation (req 622).</summary>
		public OpponentPredictionLog PredictionLog { get; } = new();

		/// <summary>
		/// Detects a material event worth an immediate strategic review, or null. Compares the current
		/// blackboard against the previous review: enemy base discovery, enemy composition changes,
		/// loss of allied production, loss of contact with the enemy, and discovery of a high-value
		/// enemy structure all wake the commander.
		/// </summary>
		string ReviewTrigger()
		{
			var enemyStructures = Blackboard.EnemyIntel.Count(i => i.Class == UnitClass.Structure);
			var ownStructures = world.Actors.Count(a =>
				a.IsInWorld && !a.IsDead && a.Owner == player && a.Info.HasTraitInfo<BuildingInfo>());
			var highValueSeen = Blackboard.EnemyIntel.Any(i =>
				TargetEvaluator.TechnologyValue(i.Type) > 0 || TargetEvaluator.EconomicValue(i.Type) > 0);

			var activeAttacks = missions.Missions.Count(m => MissionManager.IsOffensive(m.Type)
				&& m.Status == MissionStatus.Executing);
			var failedMissions = missions.Missions.Count(m => m.Status is MissionStatus.Failed or MissionStatus.Aborted);
			var completedMissions = missions.Missions.Count(m => m.Status == MissionStatus.Succeeded);
			var routeSignature = 17;
			foreach (var actor in world.Actors)
				foreach (var bridge in actor.TraitsImplementing<IBridgeSegment>())
					if (bridge.Valid)
						routeSignature = routeSignature * 31 + (int)bridge.DamageState;

			var detected = eventDetector.Detect(Blackboard.EnemyRegion, enemyStructures, ownStructures,
				Blackboard.EnemyIntel.Count, highValueSeen, activeAttacks, failedMissions,
				Blackboard.Transports.Count, completedMissions, routeSignature, Blackboard.CoalitionCash);
			if (detected != null)
				return detected;

			// A ready strategic superweapon wakes the commander to plan a support-power strike.
			if (Blackboard.HasReadySuperweapon && !lastSuperweaponReady)
			{
				lastSuperweaponReady = true;
				return "support power ready";
			}

			lastSuperweaponReady = Blackboard.HasReadySuperweapon;

			// A newly available special asset (Tanya, spy, engineer) wakes special-operations planning.
			var specialCount = Blackboard.SpecialAssets.Count;
			if (specialCount > lastSpecialAssetCount)
			{
				lastSpecialAssetCount = specialCount;
				return "special unit available";
			}

			lastSpecialAssetCount = specialCount;

			return null;
		}

		/// <summary>All allied players with an enabled bot, including this one.</summary>
		internal Player[] TeamPlayers()
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
			var coalitionArmy = Blackboard.CoalitionArmyStrength;
			var enemyArmy = Blackboard.EnemyArmyStrength;
			var ratio = coalitionArmy <= 0 ? 0 : enemyArmy / coalitionArmy;

			// Advance the mission lifecycle.
			missions.Update(Blackboard, coalitionArmy, enemyArmy);

			// Score the combat estimator per engagement (req 159): each offensive mission is one
			// engagement, predicted when it is committed and resolved when it concludes.
			TrackEngagementOutcomes(coalitionArmy, enemyArmy);

			// Select posture before scoring targets or creating missions so every decision in this
			// review uses one coherent policy rather than the previous review's stance.
			var enemyStaticDefense = Blackboard.EnemyRegion >= 0
				? Blackboard.Regions[Blackboard.EnemyRegion].Threats[(int)CoalitionCapability.StaticDefense]
				: 0f;
			var enemyEconomyStrong = Blackboard.EnemyIntel.Any(i =>
				i.Class == UnitClass.Structure && TargetEvaluator.EconomicValue(i.Type) > 0);
			var expansionOpportunity = Blackboard.MapAnalysis.ExpansionValue.Any(v => v >= 0.6f);
			var recentlyDefended = strategicPosture == StrategicPosture.Defensive && ratio < 1f;
			var casualtyFraction = Blackboard.Forces.Count == 0 ? 0f : Blackboard.Forces.Max(f => f.CasualtyFraction);
			var newPosture = PostureSelection.Select(ratio, enemyStaticDefense,
				(int)coalitionArmy, enemyEconomyStrong, expansionOpportunity, recentlyDefended, casualtyFraction);
			if (newPosture != strategicPosture)
			{
				strategicPosture = newPosture;
				CoalitionTelemetry.Log(world, $"Strategic posture: {strategicPosture.ToString().ToLowerInvariant()}");
			}

			var posturePolicy = PostureSelection.PolicyFor(strategicPosture);
			brain?.OverrideReserveFraction(posturePolicy.ReserveFraction);

			// Mission creation driven by the force balance, intel, and LLM intent. Attack unless the
			// enemy is clearly stronger (an even or slightly unfavorable fight is still worth taking
			// with better tactics and reserve commitment); defend only when clearly outnumbered.
			var (wantAttack, wantDefend, wantBuild) = CommandValidator.ResolveCommanderIntent(llmIntent?.Posture, ratio);

			// The searched plan, when one is committed, overrides the intent derived from the
			// instantaneous army ratio. That ratio is exactly the wrong thing to steer by: every
			// successful attack makes it worse before it makes it better, because units are lost to
			// the defences before the production behind them is destroyed. Deriving intent from it
			// each review meant recalling assaults at the moment they began to work, and no
			// threshold could have fixed that - the flaw was in re-deciding at all.
			//
			// A committed plan is not reconsidered here. It is reviewed against the abort conditions
			// it declared at launch, inside CommanderPlanBotModule, and a falling army ratio is
			// deliberately not among them.
			var plannedObjective = (CPos?)null;
			if (planner != null && planner.Driving)
			{
				var verb = planner.Verb;
				switch (verb)
				{
					case Commander.Model.MacroVerb.Attack:
					case Commander.Model.MacroVerb.Harass:
					case Commander.Model.MacroVerb.Feint:
						wantAttack = true;
						wantDefend = false;
						plannedObjective = planner.ObjectiveCell;
						break;

					case Commander.Model.MacroVerb.Defend:
					case Commander.Model.MacroVerb.Consolidate:
						wantAttack = false;
						wantDefend = true;
						break;

					default:
						// Expand, Tech and Produce leave the field alone and let the build logic run.
						wantAttack = false;
						wantBuild = true;
						break;
				}
			}

			// The offensive objective: the observed enemy base when one has been seen, otherwise the
			// region inferred from where enemy forces keep arriving from. Without the fallback a
			// coalition that never scouts a structure can never name an objective, so it spends the
			// entire match reacting - out-trading the enemy while never threatening it, which ends in
			// a time-limit draw at best.
			var offensiveRegion = Blackboard.EnemyRegion >= 0 ? Blackboard.EnemyRegion
				: info.AdvanceOnInferredBase ? Blackboard.InferredEnemyRegion : -1;

			// Reconnaissance in force. The deliberate-assault gate needs a 33% strength edge, but the
			// enemy estimate carries a fog floor proportional to the unexplored map, so an army that
			// never advances can never earn that edge - it assumes a large hidden enemy precisely
			// because it has not looked. That deadlock is why the coalition could out-trade an
			// opponent for a whole match and still never threaten it. A large force with no located
			// enemy therefore advances to find and fix, which is what converts the inference into an
			// observation and unlocks the deliberate assault.
			// Start the search clock once the coalition actually has a force to search with, and stop
			// it the moment the enemy base is observed so a later loss of contact restarts the timer
			// rather than instantly re-triggering an advance.
			if (Blackboard.EnemyRegion >= 0)
				enemyBaseSearchStartTick = -1;
			else if (enemyBaseSearchStartTick < 0 && coalitionArmy >= (brain?.Info.CoordinatedAttackMinimum ?? 24) * AdvanceForceMultiple)
				enemyBaseSearchStartTick = world.WorldTick;

			// Disabled by default: measured over a 12-match opponent matrix this advance did not
			// produce a single win and cost reconnaissance, because the army it commits is the same
			// army the scouting probes are drawn from. The capability is kept and tested because the
			// underlying problem it addresses is real - without it the coalition never names an
			// offensive objective at all - but shipping it on would be shipping a measured regression.
			var reconInForce = info.AdvanceOnInferredBase && ShouldAdvanceToFindEnemy(
				Blackboard.EnemyRegion, (int)coalitionArmy,
				brain?.Info.CoordinatedAttackMinimum ?? 24,
				world.WorldTick, enemyBaseSearchStartTick, info.CommandInterval);

			if (reconInForce && !lastReconInForce)
				CoalitionTelemetry.Log(world,
					$"Reconnaissance in force: army {(int)coalitionArmy} with no located enemy base, advancing to make contact");

			lastReconInForce = reconInForce;

			// A plan that names a place can act on it even when no enemy region has been identified
			// by the blackboard - the search reasons over the region graph and the belief state, and
			// has already decided the objective is worth taking.
			if (plannedObjective.HasValue && offensiveRegion < 0)
				offensiveRegion = Array.IndexOf(Blackboard.Regions, Blackboard.RegionOf(plannedObjective.Value));

			if ((wantAttack || reconInForce) && (offensiveRegion >= 0 || plannedObjective.HasValue))
			{
				// Main effort: concentrate the coalition on the single highest-value objective so
				// effort is not spread evenly across all fronts.
				var scored = BestScoredTarget();
				if (scored != mainEffort)
				{
					mainEffort = scored;
					Blackboard.AddEvent("main_effort", scored, scored != null ? "concentrate on highest-value objective" : "no main effort");
					CoalitionTelemetry.Log(world, scored != null ? $"Main effort set to {scored.Value}" : "Main effort cleared: no scored target");
				}

				// Where, not just what (handbook §15.1). The scored target says which objective is
				// worth taking; the influence map says where the enemy is thin enough that taking it
				// is possible. Aiming at the region centre sends the army at the strongest point of
				// the base, which is how an assault becomes a grind against the perimeter.
				var target = plannedObjective ?? InfluenceAssaultTarget() ?? scored
					?? (offensiveRegion >= 0 ? RegionCenter(offensiveRegion) : Blackboard.HomeCell);

				// A decisive edge turns the main effort into a breakthrough; a fair fight stays a
				// conventional attack. A heavily fortified enemy is besieged instead.
				var attackType = ratio < 0.5f ? MissionType.Breakthrough : MissionType.Attack;
				if (offensiveRegion >= 0
					&& Blackboard.Regions[offensiveRegion].Threats[(int)CoalitionCapability.StaticDefense] > 0.7f)
					attackType = MissionType.Siege;

				// An inferred objective is advanced on to find and fix the enemy, not besieged: there
				// is no confirmed fortification to reduce, and the advance is what turns the inference
				// into an observation.
				if (Blackboard.EnemyRegionIsInferred)
					attackType = MissionType.Attack;

				var objective = Blackboard.EnemyRegionIsInferred
					? "Advance on the inferred enemy base and make contact"
					: "Destroy enemy concentration";
				EnsureMission(attackType, 90, target, objective);
			}
			else
				mainEffort = null;

			// Fair-fog contact battle: locating an enemy base can take several minutes on a large map,
			// but an observed mobile army is a valid operational target. When the coalition can meet
			// that force at parity, authorize a centralized counterattack instead of making each local
			// role improvise or waiting until the force reaches the base.
			var observedMobile = Blackboard.EnemyIntel.Where(i =>
				i.Status == IntelStatus.Observed && i.Class != UnitClass.Structure).ToArray();
			if (Blackboard.EnemyRegion < 0 && ShouldInterceptObservedForce(
				observedMobile.Length, ratio, (int)coalitionArmy,
				brain?.Info.CoordinatedAttackMinimum ?? 24))
			{
				var contact = new CPos(
					(int)observedMobile.Average(i => i.LastSeenCell.X),
					(int)observedMobile.Average(i => i.LastSeenCell.Y));
				var intercept = InterceptionCell(contact, Blackboard.HomeCell);
				EnsureMission(MissionType.Counterattack, 85, intercept, "Intercept observed enemy field army");
			}

			// Exploitation (req 187): once a breakthrough has actually opened the breach, a separate
			// follow-on mission pushes through it. Keeping this distinct from the breach force is the
			// point of the doctrine - the breaching force consolidates, the exploitation force runs.
			var breached = missions.Missions.FirstOrDefault(m =>
				m.Type == MissionType.Breakthrough && m.Status == MissionStatus.Executing
				&& m.Phase is MissionPhase.Exploitation or MissionPhase.Consolidation);
			if (breached?.Target != null && !missions.Missions.Any(m => m.Type == MissionType.Exploitation
				&& m.Status is MissionStatus.Ready or MissionStatus.Executing))
				EnsureMission(MissionType.Exploitation, 88, breached.Target,
					"Exploit the breach before the enemy reconsolidates");
			else if (breached == null)
				CancelActiveMissions(MissionType.Exploitation, "breach closed or breakthrough concluded");

			// Emergency reinforcement (req 202): an allied asset under immediate attack that the local
			// garrison cannot hold gets a dedicated, highest-priority relief mission rather than
			// competing for the generic Defend directive.
			var distressCell = AssetUnderImmediateThreat();
			if (distressCell != null)
				EnsureMission(MissionType.EmergencyReinforcement, 95, distressCell,
					"Relieve the threatened asset");
			else
				CancelActiveMissions(MissionType.EmergencyReinforcement, "threat to the asset cleared");

			// Interception (req 204): a mobile enemy force observed closing on a coalition asset is cut
			// off en route instead of being met at the objective.
			var inbound = InboundEnemyForce();
			if (inbound != null)
				EnsureMission(MissionType.Interception, 86, inbound,
					"Cut off the inbound enemy force before it arrives");
			else
				CancelActiveMissions(MissionType.Interception, "no inbound enemy force observed");

			var urgentCounterattack = missions.Missions.Any(m => m.Type == MissionType.Counterattack
				&& m.Status is MissionStatus.Ready or MissionStatus.Executing);

			// Additional offensive missions driven by intel: raids on economy/production, air and
			// support-power strikes, chokepoint seizure, and a flank to divide the defense. A "build"
			// posture defers offensive raids while the coalition expands its economy.
			if (!wantBuild && posturePolicy.SecondaryOperationBudget >= 0.1f)
				CreateOffensiveMissions(ratio);

			if (wantDefend)
				EnsureMission(MissionType.Defend, 80, Blackboard.HomeCell, "Hold the base");
			else
				CancelActiveMissions(MissionType.Defend, "force balance recovered");

			// Reconnaissance: if the enemy position is unknown, probe the least-explored nearby region;
			// once it is known, run value-of-information-driven recon (deep, expansion search, defense probe).
			if (Blackboard.EnemyRegion < 0)
			{
				var reconTarget = LeastExploredRegionNear();
				if (reconTarget != null)
					EnsureMission(MissionType.Recon, 40, reconTarget, "Locate the enemy");
			}
			else
				CreateReconMissions();

			// Specialized defense: mobile interception, anti-air umbrella, naval screen, and economy escort.
			CreateDefensiveMissions();

			// Advanced mission types: harassment, expansion denial, naval blockade/strike, pincer,
			// delaying action, air/naval/route recon, and decoy transport. These extend the deterministic
			// commander with the remaining mission types that were previously enum-only or missing.
			// A "build" posture defers these proactive strikes while the coalition expands its economy.
			if (!wantBuild && !urgentCounterattack && posturePolicy.SecondaryOperationBudget >= 0.1f)
				CreateAdvancedMissions(ratio);

			// Deception: once an attack is staged, keep a feint active against another enemy-facing region.
			// Enemy models that over-respond to raids make feints more valuable, and the measured deception
			// record feeds back: feints that drew enemy responses are funded harder, while a string of
			// ignored feints (several attempts, zero responses) stops wasting forces on deception the
			// enemy does not honor.
			if (missions.Missions.Any(m => m.Type == MissionType.Attack && m.Status == MissionStatus.Executing)
				&& !missions.Missions.Any(m => m.Type == MissionType.Feint)
				&& !DeceptionSaturated())
			{
				// A feint into ground we already hold draws nothing; the influence map picks where
				// the enemy is invested and we are not (handbook §15.1).
				var feintTarget = InfluenceFeintTarget() ?? FeintRegionTarget();
				if (feintTarget != null)
					EnsureMission(MissionType.Feint, FeintPriority(), feintTarget, "Divert enemy attention");
			}

			// Bait: an over-responsive enemy is lured by a small exposed force into an ambush position
			// halfway to our base, where the main army waits to pounce.
			if (missions.Missions.Any(m => m.Type == MissionType.Attack && m.Status == MissionStatus.Executing)
				&& (Blackboard.Opponent.ShouldExploit(Blackboard.Opponent.MovesWholeArmyToDefend)
					|| Blackboard.Opponent.ShouldExploit(Blackboard.Opponent.RespondsStronglyToRaids))
				&& !missions.Missions.Any(m => m.Type == MissionType.Bait)
				&& !DeceptionSaturated())
			{
				var home = (CPos?)Blackboard.HomeCell;
				var enemy = RegionCenter(Blackboard.EnemyRegion);
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

			// LLM mission cancellation: cancel each active mission whose type matches a requested
			// cancellation, releasing its forces back to the pool for reassignment.
			if (llmIntent?.CancelMissions != null)
			{
				foreach (var cancelType in llmIntent.CancelMissions)
				{
					var type = ParseMissionType(cancelType);
					if (type == null)
						continue;
					foreach (var mission in missions.Missions.Where(m => m.Type == type.Value && m.Status is MissionStatus.Executing or MissionStatus.Ready).ToArray())
					{
						mission.Status = MissionStatus.Cancelled;
						mission.OutcomeReason = "LLM cancellation";
						arbiter.ReleaseMission(mission.Id);
						CoalitionTelemetry.Log(world, $"Mission {mission.Id} ({mission.Type}) cancelled by LLM directive");
					}
				}
			}

			// LLM reserve override: the commander can tighten or loosen the reserve fraction.
			if (llmIntent?.ReserveFraction > 0 && brain != null)
			{
				brain.OverrideReserveFraction(llmIntent.ReserveFraction);
				CoalitionTelemetry.Log(world, $"Reserve fraction overridden by LLM: 1/{llmIntent.ReserveFraction}");
			}

			// LLM mission modification: cancel existing missions of each requested type so the
			// deterministic commander recreates them on the next tick with updated parameters.
			if (llmIntent?.ModifyMissions != null)
			{
				foreach (var modifyType in llmIntent.ModifyMissions)
				{
					var type = ParseMissionType(modifyType);
					if (type == null)
						continue;
					foreach (var mission in missions.Missions.Where(m => m.Type == type.Value && m.Status is MissionStatus.Executing or MissionStatus.Ready).ToArray())
					{
						mission.Status = MissionStatus.Cancelled;
						mission.OutcomeReason = "LLM modification";
						arbiter.ReleaseMission(mission.Id);
						CoalitionTelemetry.Log(world, $"Mission {mission.Id} ({mission.Type}) cancelled by LLM modify directive");
					}
				}
			}

			// Assign forces to missions through the order arbiter: every active mission owns a force
			// at a priority, conflicting assignments are rejected with a machine-readable reason, and
			// completed or cancelled missions release their forces back to the pool.
			SyncForceAssignments();

			// LLM force directives: assign specific forces to missions (or release them back to the
			// pool). Resolved against the live force registry and mission manager, with rejections
			// logged for unknown references; the arbiter arbitrates conflicts.
			ApplyLlmForceDirectives();

			// Capability-driven production from observed enemy composition, plus any LLM production
			// boost (already validated and cleaned by ApplyLlmIntent).
			var produceJson = MergeProduce(BuildProduceJson(), llmIntent?.Produce);
			foreach (var capability in posturePolicy.ProductionCapabilities)
				if (!ProductionContract.IsSatisfied(capability, Blackboard.Forces))
					produceJson = MergeProduce(produceJson, ResolveCapabilityUnits(capability));

			foreach (var requirement in CurrentProductionRequirements())
				if (!ProductionContract.IsSatisfied(requirement, Blackboard.Forces))
					produceJson = MergeProduce(produceJson, ResolveCapabilityUnits(requirement));

			// LLM capability directive: translate the requested capability into the matching
			// counter-unit list and merge it into the production directive.
			if (llmIntent?.RequestCapability != null)
			{
				var capabilityUnits = ResolveCapabilityUnits(llmIntent.RequestCapability);
				if (capabilityUnits != null && capabilityUnits.Length > 0)
					produceJson = MergeProduce(produceJson, capabilityUnits);
			}

			// LLM production directive: the commander directly specifies which units to prioritize.
			if (llmIntent?.ProductionDirective != null && llmIntent.ProductionDirective.Length > 0)
				produceJson = MergeProduce(produceJson, llmIntent.ProductionDirective);

			if (ProductionDirectiveChanged(lastProductionDirective, produceJson))
			{
				lastProductionDirective = produceJson;
				CoalitionTelemetry.Log(world, $"Production priorities changed: {produceJson}");
			}

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
				attackTick = attack.Type == MissionType.Counterattack
					? world.WorldTick : attack.CreatedTick + 400 + attack.PlannedRegions.Length * 40;
				if (missions.Missions.Any(m => m.Type == MissionType.Feint) && missions.DeceptionSuccesses == 0)
					attackTick += 200;
			}

			var directiveJson = missions.BuildDirectiveJson(Blackboard, produceJson, llmIntent?.Retreat == true, rolesJson, forceJson, attackTick);

			// Posture controls expansion timing and combat risk; a validated LLM expansion choice can
			// override the posture for this review. Reserve commitment is explicit for all-in stances.
			var expansionPriority = llmIntent?.ExpansionPriority is 1 or -1
				? llmIntent.ExpansionPriority : posturePolicy.ExpansionPriority;
			var supportPowerTick = attackTick >= 0 ? Math.Max(world.WorldTick, attackTick - 40) : world.WorldTick;
			var commitReserve = posturePolicy.CommitReserve || attack?.Type == MissionType.Counterattack;
			var postureDirective = FormattableString.Invariant(
				$",\"expansionPriority\":{expansionPriority},\"acceptableLoss\":{posturePolicy.AcceptableLossFraction:0.00},\"commitReserve\":{commitReserve.ToString().ToLowerInvariant()},\"attackPhase\":\"{attack?.Phase.ToString().ToLowerInvariant() ?? "none"}\",\"supportPowerTick\":{supportPowerTick}");
			if (recentExpansionCell != null && world.WorldTick - recentExpansionTick <= 600)
				postureDirective += $",\"expansionGuard\":{{\"x\":{recentExpansionCell.Value.X},\"y\":{recentExpansionCell.Value.Y}}}";
			directiveJson = directiveJson.Insert(directiveJson.Length - 1, postureDirective);

			if (llmIntent != null)
				CoalitionTelemetry.Log(world,
					$"LLM intent applied: posture={llmIntent.Posture ?? "none"} missions={llmIntent.Missions?.Length ?? 0} produce={llmIntent.Produce?.Length ?? 0} retreat={llmIntent.Retreat} capability={llmIntent.RequestCapability ?? "none"} directive={llmIntent.ProductionDirective?.Length ?? 0} expansion={llmIntent.ExpansionPriority} modify={llmIntent.ModifyMissions?.Length ?? 0}");
			llmIntent = null;

			var strategy = directiveJson.Contains("\"strategy\":\"attack\"") ? "attack"
				: directiveJson.Contains("\"strategy\":\"defend\"") ? "defend" : "build";
			if (lastPosture != strategy)
			{
				lastPosture = strategy;
				Blackboard.AddEvent("posture_change", null, strategy);
				CoalitionTelemetry.Log(world, $"Posture {strategy}; coalition {Blackboard.CoalitionArmyStrength:0} vs enemy {Blackboard.EnemyArmyStrength:0}");
			}

			// Per-front postures (req 341): every region independently overrides the global posture
			// when its local control, pressure, or expansion opportunity warrants it.
			SetLocalPostures();

			brain?.ApplyTeamPlan(directiveJson);

			SampleMatchMetrics();
		}

		/// <summary>
		/// Sets per-region local postures from each theater's own control, pressure, and expansion
		/// value. Regions with no overriding local condition keep LocalPosture = None (use global).
		/// </summary>
		void SetLocalPostures()
		{
			for (var i = 0; i < Blackboard.Regions.Length; i++)
			{
				var region = Blackboard.Regions[i];
				var localPosture = PostureSelection.SelectLocal(region.FriendlyControl, region.EnemyPressure,
					Blackboard.MapAnalysis.ExpansionValue[i]);
				if (region.LocalPosture != localPosture)
				{
					region.LocalPosture = localPosture;
					if (localPosture != StrategicPosture.None)
						CoalitionTelemetry.Log(world,
							$"Local posture {localPosture.ToString().ToLowerInvariant()} for region {region.Index} " +
							$"(control {region.FriendlyControl:0.00}, pressure {region.EnemyPressure:0.00})");
				}
			}
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

			// Use the same strength the commander actually decides on (CoalitionArmyStrength, no health
			// discount) rather than ForcePower (health-discounted), so the predicted win ratio agrees
			// with the attack/defend/abort decisions.
			var friendlyValue = Blackboard.CoalitionArmyStrength;
			var enemyValue = Blackboard.EnemyArmyStrength;
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

			matchMetrics.Sample(friendlyValue, enemyValue, idle, cohesion, Blackboard.CoalitionCash);
			var productionIdle = Blackboard.Facilities.Count == 0 ? 1f
				: Blackboard.Facilities.Count(f => f.Current == null) * 1f / Blackboard.Facilities.Count;
			var reserveFraction = brain?.CurrentReserveFraction
				?? PostureSelection.PolicyFor(strategicPosture).ReserveFraction;
			matchMetrics.SampleOperations(productionIdle, reserveFraction <= 0 ? 0f : 1f / reserveFraction);

			// Economic damage tracking: sample both the refinery count and the credit value of the
			// standing economy for each side, so damage is reported in credits and not only in
			// buildings destroyed (reqs 604, 605).
			var friendlyEconomy = world.Actors.Where(a => !a.IsDead && a.IsInWorld
				&& teamIds.Contains(a.Owner.InternalName) && IsEconomicAsset(a.Info)).ToArray();
			var friendlyRefineries = friendlyEconomy.Count(a => a.Info.HasTraitInfo<RefineryInfo>());
			var friendlyEconomicValue = friendlyEconomy.Sum(a => CostOf(a.Info));

			// Fair fog: the enemy economy is only what the coalition can currently account for.
			var enemyEconomy = Blackboard.EnemyIntel
				.Where(i => i.Class == UnitClass.Structure && TargetEvaluator.EconomicValue(i.Type) > 0).ToArray();
			var enemyRefineries = enemyEconomy.Length;
			var enemyEconomicValue = enemyEconomy.Sum(i => CostOfType(i.Type) * Math.Max(1, i.ExpectedCount));

			matchMetrics.SampleEconomy(friendlyRefineries, enemyRefineries, friendlyEconomicValue, enemyEconomicValue);

			// Expansion detection (req 608): record the tick when a new construction yard appears.
			// Construction yards are identified by the BuildingInfo trait and being a "fact" type
			// (the standard RA construction yard). This is a heuristic but sufficient for telemetry.
			var conyardCells = world.Actors.Where(a => !a.IsDead && a.IsInWorld && teamIds.Contains(a.Owner.InternalName)
				&& a.Info.HasTraitInfo<BuildingInfo>() && a.Info.Name == "fact")
				.Select(a => a.Location).ToHashSet();
			if (!expansionBaselineInitialized)
			{
				expansionBaselineInitialized = true;
				knownConyardCells.UnionWith(conyardCells);
				peakConyardCount = conyardCells.Count;
			}
			else
			{
				var newCells = conyardCells.Where(c => !knownConyardCells.Contains(c)).ToArray();
				foreach (var cell in newCells)
				{
					matchMetrics.RecordExpansion(world.WorldTick);
					recentExpansionCell = cell;
					recentExpansionTick = world.WorldTick;
					Blackboard.AddEvent("expansion_built", cell, "protect new construction yard");
				}

				knownConyardCells.Clear();
				knownConyardCells.UnionWith(conyardCells);
				peakConyardCount = Math.Max(peakConyardCount, conyardCells.Count);
			}

			matchMetrics.RecordEstimate(CombatEstimator.Estimate(friendlyValue, enemyValue).WinRatio);

			// Excess resource floating: a growing, unspent cash pile means production is not keeping up.
			if (Blackboard.CoalitionCash > 12000 && (lastFloatingTick == int.MinValue || world.WorldTick - lastFloatingTick >= 6000))
			{
				lastFloatingTick = world.WorldTick;
				CoalitionTelemetry.Log(world, $"Excess cash floating: {Blackboard.CoalitionCash}");
			}

			if (lastMetricsSummaryTick == int.MinValue || world.WorldTick - lastMetricsSummaryTick >= 6000)
			{
				lastMetricsSummaryTick = world.WorldTick;
				CoalitionTelemetry.Log(world, matchMetrics.Summary());
				CoalitionTelemetry.Log(world, missions.MissionSummary());
			}

			if (world.IsGameOver)
			{
				var won = player.WinState == WinState.Won;
				matchMetrics.RecordResult(won, world.WorldTick);
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
		/// The fog-of-war gate for enemy observation. Fair fog (levels 0-1) only sees currently visible cells;
		/// level 2 additionally reveals enemy structures (but not mobile units); level 3 is omniscient.
		/// </summary>
		bool SeesEnemy(Actor a, IEnumerable<Player> team)
		{
			if (info.IsOmniscient)
				return true;
			if (team.Any(ally => ally.Shroud.IsVisible(a.CenterPosition)))
				return true;
			if (info.EffectiveIntelligence >= 2 && Classify(a) == UnitClass.Structure)
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
			foreach (var force in Blackboard.Forces)
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
			var total = Blackboard.EnemyIntel.Count;
			if (total == 0)
				return;

			var opponent = Blackboard.Opponent;
			opponent.ArmorBias = Blackboard.EnemyIntel.Count(i => i.Class == UnitClass.Armor) * 1f / total;
			opponent.AirBias = Blackboard.EnemyIntel.Count(i => i.Class == UnitClass.Air) * 1f / total;
			opponent.InfantryBias = Blackboard.EnemyIntel.Count(i => i.Class == UnitClass.Infantry) * 1f / total;
			opponent.NavalBias = Blackboard.EnemyIntel.Count(i => i.Class == UnitClass.Naval) * 1f / total;
			opponent.StaticDefenseBias = Blackboard.EnemyIntel.Count(i => i.Class == UnitClass.Structure) * 1f / total;

			// If many enemy sightings sit away from their base region, the enemy tends to commit its
			// whole army to defending - a signal that feints will draw forces away from the main push.
			opponent.MovesWholeArmyToDefend = Blackboard.EnemyRegion >= 0
				&& Blackboard.EnemyIntel.Count(i => Blackboard.RegionOf(i.LastSeenCell).Index != Blackboard.EnemyRegion) * 2 > total;

			// Preferred attack lane: the region most often hosting enemy sightings away from the base.
			// Tracks where the enemy tends to mass, so our defense can cover the likely axis.
			var laneCounts = new Dictionary<int, int>();
			foreach (var intel in Blackboard.EnemyIntel)
			{
				var region = Blackboard.RegionOf(intel.LastSeenCell).Index;
				laneCounts[region] = laneCounts.GetValueOrDefault(region) + 1;
			}

			var bestLane = -1;
			var bestLaneCount = 0;
			foreach (var kv in laneCounts)
				if (kv.Key != Blackboard.HomeRegion && kv.Value > bestLaneCount)
				{
					bestLane = kv.Key;
					bestLaneCount = kv.Value;
				}

			opponent.PreferredAttackLane = bestLane;

			// Playstyle from the scouted shape: an army that outnumbers its own structures is pressing
			// (rush), structures without a matching army are turtling.
			var structures = Blackboard.EnemyIntel.Count(i => i.Class == UnitClass.Structure);
			opponent.ExpansionCount = structures;
			var army = total - structures;
			opponent.Playstyle = OpponentModel.DerivePlaystyle(army, structures);

			// Predicted build from the most advanced scouted structure.
			var build = "unknown";
			foreach (var intel in Blackboard.EnemyIntel.Where(i => i.Class == UnitClass.Structure))
			{
				var direction = OpponentModel.DerivePredictedBuild(intel.Type);
				if (direction != null)
					build = direction;
			}

			opponent.PredictedBuild = build;

			// Profile confidence grows with observations: a single sighting is nearly useless,
			// dozens of sightings make the biases trustworthy.
			opponent.Confidence = Math.Clamp(total / 20f, 0f, 1f);

			// Score the model's own forecasts (req 622). Each review states what it expects; the next
			// review that actually observes the answer resolves it. A prediction is only recorded once
			// the profile carries some confidence, so the opening guess is not counted against it.
			if (opponent.Confidence > 0.2f)
			{
				PredictionLog.Predict("playstyle", opponent.Playstyle, world.WorldTick, opponent.Confidence);
				PredictionLog.Predict("build", opponent.PredictedBuild, world.WorldTick, opponent.Confidence);
				if (opponent.PreferredAttackLane >= 0)
					PredictionLog.Predict("attack_lane", opponent.PreferredAttackLane.ToString(
						System.Globalization.CultureInfo.InvariantCulture), world.WorldTick, opponent.Confidence);
			}

			// Resolution: a forecast is checked only against evidence strong enough to settle it, so a
			// thin snapshot never confirms or refutes the profile.
			if (total >= 8)
			{
				PredictionLog.Observe("playstyle", OpponentModel.DerivePlaystyle(army, structures));
				PredictionLog.Observe("build", build);
			}

			// The attack lane resolves against where the enemy actually massed its mobile forces.
			var mobileLane = laneCounts
				.Where(kv => kv.Key != Blackboard.HomeRegion)
				.OrderByDescending(kv => kv.Value)
				.ThenBy(kv => kv.Key)
				.Select(kv => (int?)kv.Key)
				.FirstOrDefault();
			if (mobileLane != null && total >= 8)
				PredictionLog.Observe("attack_lane",
					mobileLane.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));

			// Copy the durable learned values into the fresh model: response time from accumulated
			// wave-to-reaction delays, and raid sensitivity from how much enemy contact our raids
			// generate.
			if (responseTimeSamples > 0)
			{
				opponent.AverageResponseTime = responseTimeSum * world.Timestep / 1000f / responseTimeSamples;
				opponent.ResponseSamples = responseTimeSamples;
			}

			opponent.RespondsStronglyToRaids = raidContactTicks > 0;
			opponent.RaidResponseSamples = raidResponseSamples;
			opponent.RaidResponseRate = raidResponseSamples == 0 ? 0f : raidResponseSuccesses * 1f / raidResponseSamples;
			opponent.RespondsStronglyToFeints = missions.DeceptionAttempts >= 2
				&& Blackboard.DeceptionEffectiveness >= 0.5f;
			opponent.FeintResponseSamples = missions.DeceptionAttempts;
			opponent.FeintResponseRate = Blackboard.DeceptionEffectiveness;
			opponent.ExpansionSamples = matchMetrics.ExpansionTimings.Count;
			opponent.AverageExpansionTick = opponent.ExpansionSamples == 0 ? 0f
				: (float)matchMetrics.ExpansionTimings.Average();

			// Report the derived profile when it changes, so scripted-opponent validation and replays
			// can observe what the coalition believes about the enemy.
			var profile = $"{opponent.Playstyle}/{opponent.PredictedBuild}";
			if (profile != lastOpponentProfile)
			{
				lastOpponentProfile = profile;
				CoalitionTelemetry.Log(world,
					$"Opponent model: {opponent.Playstyle}, build={opponent.PredictedBuild}, confidence={opponent.Confidence:0.00}");
				CoalitionTelemetry.Log(world, PredictionLog.Summary());
				CoalitionTelemetry.Log(world, EngagementLog.Summary());
			}
		}

		/// <summary>
		/// Records enemy contact generated by our raids. Caller (the brain) reports how much enemy
		/// presence appeared near the raid target; sustained contact marks the enemy as raid-sensitive.
		/// </summary>
		public void RecordRaidContact(int enemyUnitsNearRaid)
		{
			raidResponseSamples++;
			if (enemyUnitsNearRaid >= 2)
			{
				raidContactTicks++;
				raidResponseSuccesses++;
			}
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
			var homeRegion = Blackboard.HomeRegion;
			foreach (var intel in Blackboard.EnemyIntel.Where(i => i.Class == UnitClass.Structure))
			{
				// Special insertions travel by stealth profile: the route cost already weights
				// vision exposure, detection, and chokepoint risk, so unreachable targets are
				// skipped rather than sending Tanya on a one-way trip into the sea.
				var targetRegion = Blackboard.RegionOf(intel.LastSeenCell).Index;
				var route = CoalitionRoutePlanner.FindRoute(
					Blackboard.MapAnalysis, Blackboard.ThreatField(), homeRegion, targetRegion,
					MovementClass.Ground, RouteWeights.Stealth());
				if (!route.Found)
					continue;

				// Consequence-aware scoring: the target's strategic value minus the approach risk.
				// The SPECIALOPS_RISK_THRESHOLD env var (req 725) sets the maximum acceptable risk
				// for self-play parameter sweeps. Targets above this threshold are skipped.
				var specialopsThreshold = float.MaxValue;
				var envThreshold = Environment.GetEnvironmentVariable("SPECIALOPS_RISK_THRESHOLD");
				if (float.TryParse(envThreshold, out var parsedThreshold))
					specialopsThreshold = parsedThreshold;

				var value = TargetEvaluator.EconomicValue(intel.Type)
					+ TargetEvaluator.ProductionValue(intel.Type)
					+ TargetEvaluator.TechnologyValue(intel.Type);
				var region = Blackboard.Regions[targetRegion];
				var risk = region.Threats[(int)CoalitionCapability.StaticDefense]
					+ region.Threats[(int)CoalitionCapability.VisionExposure]
					+ route.Cost * 0.5f;

				// Skip targets above the risk threshold (req 725).
				if (!WithinSpecialOpsRisk(risk, specialopsThreshold))
					continue;

				var score = value * 2f - risk;

				if (score > bestScore)
				{
					bestScore = score;
					best = intel.LastSeenCell;
				}
			}

			return best;
		}

		/// <summary>Pure special-operations risk gate used by configurable tuning sweeps.</summary>
		public static bool WithinSpecialOpsRisk(float risk, float maximumRisk)
		{
			return risk <= maximumRisk;
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
			var homeRegion = Blackboard.HomeRegion;
			var weights = PostureSelection.TargetWeightsFor(strategicPosture);
			foreach (var intel in Blackboard.EnemyIntel.Where(i => i.Class == UnitClass.Structure))
			{
				var targetRegion = Blackboard.RegionOf(intel.LastSeenCell).Index;
				var route = CoalitionRoutePlanner.FindRoute(
					Blackboard.MapAnalysis, Blackboard.ThreatField(), homeRegion, targetRegion,
					MovementClass.Ground, RouteWeights.Assault());
				if (!route.Found)
					continue;

				var type = intel.Type;
				var (economy, production, technology) = TargetEvaluator.Classify(type);
				var uncertainty = intel.Confidence < 0.5f ? 1f : 0.3f;
				var reinforcementRisk = Blackboard.Regions[targetRegion].Threats[(int)CoalitionCapability.Reinforcement];
				var counterattackRisk = Blackboard.Regions[targetRegion].Threats[(int)CoalitionCapability.GroundAntiArmor];

				var breakdown = TargetEvaluator.Score(
					type, economy, production, technology, targetRegion, route.Cost,
					friendlyLossRisk: 0.2f, enemyReinforcementRisk: reinforcementRisk,
					enemyCounterattackRisk: counterattackRisk, uncertainty: uncertainty,
					Blackboard.MapAnalysis, MovementClass.Ground, weights);

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
				case "pincer":
					return MissionType.Pincer;
				case "exploitation":
				case "exploit":
					return MissionType.Exploitation;
				case "emergencyreinforcement":
				case "emergency_reinforcement":
				case "reinforce":
					return MissionType.EmergencyReinforcement;
				case "interception":
				case "intercept":
					return MissionType.Interception;
				case "navalblockade":
				case "naval_blockade":
					return MissionType.NavalBlockade;
				case "fakebuildup":
				case "fake_buildup":
					return MissionType.FakeBuildup;
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

			var created = missions.CreateMission(type, priority, target, objective, createdTick: world.WorldTick);
			Blackboard.AddEvent("mission_created", target, $"{type}:{objective}");
			CoalitionTelemetry.Log(world, $"Mission {created.Id} ({type}) created: {objective}");
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
			var profile = ProductionContract.Aggregate(Blackboard.Regions);
			var contracts = new (CoalitionCapability Capability, string[] CounterUnits)[]
			{
				(CoalitionCapability.AntiAir, info.AntiAirUnits.ToArray()),
				(CoalitionCapability.GroundAntiArmor, info.AntiArmorUnits.ToArray()),
				(CoalitionCapability.GroundAntiInfantry, info.AntiInfantryUnits.ToArray()),
				(CoalitionCapability.StaticDefense, info.ArtilleryTypes.ToArray()),
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

			var units = ProductionContract.Resolve(profile, contracts, t => fielded.GetValueOrDefault(t), Blackboard.HasBigWater,
				ProductionContract.CapabilityWeightScale);

			// Recon requirement: with the enemy position unknown, produce scouts to locate them.
			if (Blackboard.EnemyRegion < 0 && brain?.Info.ScoutUnitTypes is { Length: > 0 } scoutTypes)
			{
				var fieldedScouts = scoutTypes.Sum(t => fielded.GetValueOrDefault(t));
				if (fieldedScouts < brain.Info.ScoutSquadSize)
					units = units == null ? scoutTypes : scoutTypes.Concat(units).Distinct().ToArray();
			}

			if (units == null || units.Length == 0)
				return null;

			return "[\"" + string.Join("\",\"", units) + "\"]";
		}

		/// <summary>
		/// Merges the deterministic production list with the LLM's requested production boosts. The
		/// LLM units are appended (deduplicated, case-insensitive) so they enter the brain's pick
		/// order ahead of the deterministic contract; invalid unit names are filtered by the brain
		/// when it matches them against buildable items.
		/// </summary>
		static string MergeProduce(string produceJson, string[] llmProduce)
		{
			if (llmProduce == null || llmProduce.Length == 0)
				return produceJson;

			string[] existing = null;
			if (!string.IsNullOrEmpty(produceJson))
				existing = JsonSerializer.Deserialize<string[]>(produceJson, IntentOptions);

			var merged = CommandValidator.MergeProduce(existing, llmProduce);
			return merged.Count == 0 ? null : "[\"" + string.Join("\",\"", merged) + "\"]";
		}

		/// <summary>
		/// Collects the set of unit names this bot can currently build, used to validate the
		/// LLM production-directive override against the live ruleset.
		/// </summary>
		HashSet<string> BuildableUnitNames()
		{
			var names = new HashSet<string>();
			foreach (var queue in player.PlayerActor.TraitsImplementing<ProductionQueue>())
				foreach (var item in queue.BuildableItems())
					names.Add(item.Name);
			return names;
		}

		/// <summary>
		/// Resolves an LLM capability directive string into the matching configured counter-unit
		/// list. Returns null for an unknown capability (already rejected by the validator).
		/// </summary>
		string[] ResolveCapabilityUnits(string capability)
		{
			return capability?.ToLowerInvariant().Trim() switch
			{
				"anti_air" => info.AntiAirUnits.ToArray(),
				"anti_armor" => info.AntiArmorUnits.ToArray(),
				"anti_infantry" => info.AntiInfantryUnits.ToArray(),
				"artillery" => info.ArtilleryTypes.ToArray(),
				"naval" => info.NavalPriority.ToArray(),
				"recon" => brain?.Info.ScoutUnitTypes?.ToArray(),
				"mobility" => info.ArmyPriority.ToArray(),
				"fast_raiding" => brain?.Info.ScoutUnitTypes?.ToArray(),
				"air_superiority" => brain?.Info.AirUnitTypes?.ToArray(),
				"transport" => brain?.Info.TransportTypes?.ToArray(),
				"special_operations" => info.SpecialTypes.ToArray(),

				// Before enemy composition is known, base defense means a balanced field army. Observed
				// capability threats are merged separately by BuildProduceJson and add exact counters.
				"base_defense" => info.ArmyPriority.ToArray(),
				_ => null
			};
		}

		/// <summary>Current operation-driven capability requirements, independent of unit names.</summary>
		string[] CurrentProductionRequirements()
		{
			var active = missions.Missions.Where(m => m.Status is MissionStatus.Ready or MissionStatus.Executing).ToArray();
			return ProductionContract.DetermineRequirements(
				Blackboard.EnemyRegion < 0 || active.Any(m => MissionManager.IsRecon(m.Type)),
				Blackboard.EnemyIntel.Any(i => i.Class == UnitClass.Air),
				active.Any(m => m.PlannedRegions.Length >= 3),
				active.Any(m => m.Type is MissionType.Raid or MissionType.Harassment
					or MissionType.EconomyRaid or MissionType.ProductionRaid),
				active.Any(m => m.Type is MissionType.Transport or MissionType.DecoyTransport),
				active.Any(m => m.Type == MissionType.SpecialOps),
				active.Any(m => m.Type is MissionType.NavalStrike or MissionType.NavalBlockade
					or MissionType.NavalScreen or MissionType.NavalRecon),
				Blackboard.HasBigWater);
		}

		/// <summary>
		/// Assigns this bot a non-overlapping coalition specialization: main, naval, expansion, or escort.
		/// </summary>
		string AssignRole()
		{
			var mine = Blackboard.Forces.FirstOrDefault(f => f.Owner == player.InternalName);
			if (mine == null || Blackboard.Forces.Count == 0)
				return null;

			var cash = TeamPlayers().ToDictionary(p => p.InternalName,
				p => p.PlayerActor.TraitOrDefault<PlayerResources>()?.GetCashAndResources() ?? 0);
			var roles = CoalitionForceRegistry.AssignRoles(Blackboard.Forces, cash, Blackboard.HasBigWater);
			var role = roles.GetValueOrDefault(mine.Owner, "escort");

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

			var ordered = Blackboard.Forces.OrderByDescending(f => f.TotalUnits).ToArray();
			var main = ordered.FirstOrDefault();
			var secondary = ordered.Skip(1).FirstOrDefault();
			var transportOwner = Blackboard.Transports.FirstOrDefault()?.Owner
				?? Blackboard.SpecialAssets.FirstOrDefault()?.Owner;

			foreach (var mission in missions.Missions.Where(m => m.Status == MissionStatus.Executing || m.Status == MissionStatus.Ready))
			{
				var force = mission.Type switch
				{
					MissionType.Attack or MissionType.Raid or MissionType.Counterattack or MissionType.Breakthrough
						or MissionType.Siege or MissionType.ChokepointSeizure => main,
					MissionType.Harassment or MissionType.EconomyRaid or MissionType.ProductionRaid
						or MissionType.ExpansionDenial or MissionType.Flank or MissionType.Pincer
						or MissionType.Feint or MissionType.Bait or MissionType.FakeBuildup => secondary ?? main,
					MissionType.Transport or MissionType.SpecialOps or MissionType.DecoyTransport
						=> Blackboard.Forces.FirstOrDefault(f => f.Owner == transportOwner) ?? main,
					MissionType.Recon or MissionType.DeepRecon or MissionType.AirRecon or MissionType.NavalRecon
						or MissionType.RouteRecon or MissionType.ExpansionSearch or MissionType.DefenseProbe => secondary ?? main,
					MissionType.NavalBlockade => Blackboard.Forces.FirstOrDefault(f => f.Counts[(int)UnitClass.Naval] > 0) ?? main,
					MissionType.Defend or MissionType.MobileDefense or MissionType.AntiAirUmbrella or MissionType.NavalScreen
						or MissionType.DelayingAction or MissionType.Evacuation or MissionType.Escort => main,
					_ => null
				};

				if (force == null)
					continue;

				foreach (var rejection in arbiter.Assign(mission.Id, RoleOf(mission.Type), PriorityOf(mission.Type), force.Owner))
					CoalitionTelemetry.Log(world, $"Order arbiter: {rejection}");

				mission.AssignedForces = arbiter.ForcesOf(mission.Id).ToList();
				if (MissionManager.IsDeception(mission.Type))
					mission.FriendlyValueCommitted = force.TotalUnits;
				EnrichMission(mission);
			}

			// Copy ownership back onto the fresh force groups.
			foreach (var force in Blackboard.Forces)
			{
				force.MissionId = arbiter.MissionOf(force.Owner);
				force.Role = arbiter.RoleOf(force.Owner);
			}
		}

		/// <summary>
		/// Applies the LLM's assign_force / release_force directives. Each assignment is resolved to a
		/// live force (by player id) and mission (by id or type name), then committed through the order
		/// arbiter at special-mission priority; conflicts are rejected with a machine-readable reason.
		/// Unknown or blank references are logged and skipped.
		/// </summary>
		void ApplyLlmForceDirectives()
		{
			if (llmIntent?.ReleaseForce != null)
			{
				foreach (var forceId in llmIntent.ReleaseForce)
				{
					var force = ResolveForce(forceId);
					if (force == null)
					{
						CoalitionTelemetry.Log(world, $"Force directive: REJECTED_UNKNOWN_FORCE \"{forceId}\"");
						continue;
					}

					arbiter.ReleaseForce(force.Owner);
					CoalitionTelemetry.Log(world, $"Force {force.Owner} released by LLM directive");
				}
			}

			if (llmIntent?.AssignForce != null)
			{
				foreach (var assignment in llmIntent.AssignForce)
				{
					if (assignment == null)
						continue;

					var force = ResolveForce(assignment.ForceId);
					if (force == null)
					{
						CoalitionTelemetry.Log(world, $"Force directive: REJECTED_UNKNOWN_FORCE \"{assignment.ForceId}\"");
						continue;
					}

					var mission = ResolveMission(assignment.MissionId);
					if (mission == null)
					{
						CoalitionTelemetry.Log(world, $"Force directive: REJECTED_UNKNOWN_MISSION \"{assignment.MissionId}\"");
						continue;
					}

					foreach (var rejection in arbiter.Assign(mission.Id, RoleOf(mission.Type), ArbiterPriority.SpecialMission, force.Owner))
						CoalitionTelemetry.Log(world, $"Order arbiter: {rejection}");

					mission.AssignedForces = arbiter.ForcesOf(mission.Id).ToList();
				}
			}

			// Copy the resulting ownership back onto the blackboard force groups so the next command
			// sees the LLM-directed assignments.
			foreach (var force in Blackboard.Forces)
			{
				force.MissionId = arbiter.MissionOf(force.Owner);
				force.Role = arbiter.RoleOf(force.Owner);
			}
		}

		/// <summary>Resolves an LLM force reference to a live force group. Accepts the player id with an
		/// optional "FORCE_" prefix, case-insensitively.</summary>
		ForceGroup ResolveForce(string forceId)
		{
			if (string.IsNullOrWhiteSpace(forceId))
				return null;
			var id = forceId.Trim();
			if (id.StartsWith("FORCE_", StringComparison.OrdinalIgnoreCase))
				id = id["FORCE_".Length..];
			return Blackboard.Forces.FirstOrDefault(f => string.Equals(f.Owner, id, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>Resolves an LLM mission reference to a live, active mission. Accepts either the
		/// mission id (e.g. "OP-3") or a mission type name (e.g. "attack"). Only missions that are
		/// ready or executing are assignable.</summary>
		CoalitionMission ResolveMission(string missionId)
		{
			if (string.IsNullOrWhiteSpace(missionId))
				return null;
			var id = missionId.Trim();
			var mission = missions.Missions.FirstOrDefault(m =>
				m.Status is MissionStatus.Executing or MissionStatus.Ready &&
				string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
			if (mission != null)
				return mission;
			var type = ParseMissionType(id);
			if (type == null)
				return null;
			return missions.Missions.FirstOrDefault(m =>
				m.Type == type.Value && m.Status is MissionStatus.Executing or MissionStatus.Ready);
		}

		/// <summary>Fills the mission's staging region and planned route from the blackboard's map analysis.</summary>
		void EnrichMission(CoalitionMission mission)
		{
			if (mission.Target != null && Blackboard.HomeRegion >= 0)
			{
				var targetRegion = Blackboard.RegionOf(mission.Target.Value).Index;
				var route = CoalitionRoutePlanner.FindRoute(Blackboard.MapAnalysis, Blackboard.ThreatField(),
					Blackboard.HomeRegion, targetRegion, MovementClass.Ground, RouteWeights.Assault());
				mission.PlannedRegions = route.Found ? route.Regions : [];
			}

			var best = Blackboard.HomeRegion;
			var bestRally = float.MinValue;
			foreach (var region in Blackboard.Regions)
			{
				if (!ReachableFromHome(region.Index))
					continue;
				if (Blackboard.MapAnalysis.RallyValue[region.Index] > bestRally)
				{
					bestRally = Blackboard.MapAnalysis.RallyValue[region.Index];
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
					or MissionType.ExpansionDenial or MissionType.Flank or MissionType.Pincer => ArbiterPriority.ActiveCombat,
				MissionType.AirStrike or MissionType.NavalStrike or MissionType.NavalBlockade or MissionType.SupportPowerStrike => ArbiterPriority.SpecialMission,
				MissionType.Defend or MissionType.MobileDefense or MissionType.AntiAirUmbrella or MissionType.NavalScreen
					or MissionType.DelayingAction or MissionType.Evacuation or MissionType.Escort => ArbiterPriority.Defense,
				MissionType.Recon or MissionType.Feint or MissionType.Bait or MissionType.DeepRecon or MissionType.AirRecon
					or MissionType.NavalRecon or MissionType.RouteRecon or MissionType.ExpansionSearch or MissionType.DefenseProbe
					or MissionType.Demonstration or MissionType.DecoyTransport or MissionType.FakeBuildup => ArbiterPriority.Recon,
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
					or MissionType.ExpansionDenial or MissionType.Flank or MissionType.Pincer => "flank",
				MissionType.AirStrike => "air",
				MissionType.NavalStrike or MissionType.NavalBlockade => "naval",
				MissionType.SupportPowerStrike => "support",
				MissionType.Feint or MissionType.Demonstration or MissionType.FakeBuildup => "feint",
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
			foreach (var force in Blackboard.Forces)
				for (var c = 0; c < 4; c++)
					counts[c] += force.Counts[c];

			var air = counts[(int)UnitClass.Air];
			var naval = counts[(int)UnitClass.Naval];
			var land = counts[(int)UnitClass.Infantry] + counts[(int)UnitClass.Armor];

			// Coalition reserve: the sum of each force's held-back reserve (the army the coalition
			// keeps uncommitted), so the brain and the LLM see how much is in reserve.
			var reserveFraction = Math.Max(1, brain?.Info.ScaledReserveFraction() ?? 4);
			var reserve = Blackboard.Forces.Sum(f => f.TotalUnits / reserveFraction);

			// "water" tells the brain whether a big explored water body exists. Without it the mixed-arms
			// gate must not demand a naval arm, and naval production is skipped.
			return $"{{\"army\":{air + naval + land},\"air\":{air},\"naval\":{naval}," +
				$"\"land\":{land},\"reserve\":{reserve}," +
				$"\"water\":{(Blackboard.HasBigWater ? "true" : "false")}}}";
		}

		/// <summary>
		/// Snapshots the live blackboard into the plain-data tool context consumed by the LLM tool
		/// API. Called on the game thread; the HTTP tool server only reads the snapshot, so tool calls
		/// never race the game loop.
		/// </summary>
		public ToolContext BuildToolContext()
		{
			if (Blackboard == null)
				return null;

			var members = TeamPlayers();
			return new ToolContext
			{
				Tick = Blackboard.Tick,
				Timestep = world.Timestep,
				Regions = Blackboard.Regions,
				Forces = Blackboard.Forces.ToArray(),
				SpecialAssets = Blackboard.SpecialAssets.ToArray(),
				Transports = Blackboard.Transports.ToArray(),
				Facilities = Blackboard.Facilities.ToArray(),
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
				EnemyIntel = Blackboard.EnemyIntel.ToArray(),
				Events = Blackboard.Events.ToArray(),
				Opponent = Blackboard.Opponent,
				CoalitionCash = Blackboard.CoalitionCash,
				MemberCash = members.ToDictionary(p => p.InternalName,
					p => p.PlayerActor.TraitOrDefault<PlayerResources>()?.GetCashAndResources() ?? 0),
				HomeRegion = Blackboard.HomeRegion,
				EnemyRegion = Blackboard.EnemyRegion,
				CoalitionArmyStrength = Blackboard.CoalitionArmyStrength,
				EnemyArmyStrength = Blackboard.EnemyArmyStrength,
				EnemyArmyCount = (int)Blackboard.EnemyArmyCount,
				DeceptionEffectiveness = Blackboard.DeceptionEffectiveness,
				DeceptionEnemiesDrawn = Blackboard.DeceptionEnemiesDrawn,
				PowerProvided = Blackboard.PowerProvided,
				PowerDrained = Blackboard.PowerDrained,
				RefineryCount = Blackboard.RefineryCount,
				HarvesterCount = Blackboard.HarvesterCount,
				ActiveHarvesterCount = Blackboard.ActiveHarvesterCount,
				ResourceCellsRemaining = Blackboard.ResourceCellsRemaining,
				MapAnalysis = Blackboard.MapAnalysis,
				ThreatField = Blackboard.ThreatField(),
				ProductionRequirements = CurrentProductionRequirements()
			};
		}

		/// <summary>Returns the least explored region that is reachable from the home region on the ground.</summary>
		CPos? LeastExploredRegionNear()
		{
			CoalitionRegion best = null;
			var bestCoverage = 1f;
			var bestDistance = -1;
			for (var i = 0; i < Blackboard.Regions.Length; i++)
			{
				// Skip regions the coalition cannot reach on the ground: reconning an island or a
				// sea body the army can never enter wastes scouts and produces unusable intel.
				if (i == Blackboard.HomeRegion || !ReachableFromHome(i))
					continue;

				var coverage = Blackboard.Regions[i].FriendlyControl;
				var center = RegionCenter(i);
				var distance = center == null ? -1 : (center.Value - Blackboard.HomeCell).LengthSquared;
				if (coverage < bestCoverage || (coverage == bestCoverage && distance > bestDistance))
				{
					best = Blackboard.Regions[i];
					bestCoverage = coverage;
					bestDistance = distance;
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
			var attackRegion = attack?.Target != null ? Blackboard.RegionOf(attack.Target.Value).Index : -1;
			for (var i = 0; i < Blackboard.Regions.Length; i++)
			{
				if (Blackboard.Regions[i].EnemyPressure <= 0)
					continue;

				if (attackRegion >= 0 && Blackboard.MapAnalysis.IsAdjacent(MovementClass.Ground, attackRegion, i))
					return RegionCenter(i);
			}

			// Fall back to any enemy-facing region distinct from the main target.
			for (var i = 0; i < Blackboard.Regions.Length; i++)
				if (Blackboard.Regions[i].EnemyPressure > 0 && i != attackRegion)
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
			var effectiveness = missions.DeceptionAttempts == 0 ? 0.5f : Blackboard.DeceptionEffectiveness;
			var basePriority = Blackboard.Opponent.ShouldExploit(Blackboard.Opponent.MovesWholeArmyToDefend) ? 75 : 60;
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
			if (Blackboard.EnemyRegion < 0)
				return;

			var structures = Blackboard.EnemyIntel.Where(i => i.Class == UnitClass.Structure).ToArray();
			if (structures.Length == 0)
				return;

			var economy = structures.Where(i => TargetEvaluator.EconomicValue(i.Type) > 0).ToArray();
			if (economy.Length > 0 && ratio < 1.2f && !missions.Missions.Any(m => m.Type == MissionType.EconomyRaid))
				EnsureMission(MissionType.EconomyRaid, 65, HighestScored(economy, i => TargetEvaluator.EconomicValue(i.Type)), "Raid enemy economy");

			var production = structures.Where(i => TargetEvaluator.ProductionValue(i.Type) > 0).ToArray();
			if (production.Length > 0 && ratio < 1.0f && !missions.Missions.Any(m => m.Type == MissionType.ProductionRaid))
				EnsureMission(MissionType.ProductionRaid, 65, HighestScored(production, i => TargetEvaluator.ProductionValue(i.Type)), "Raid enemy production");

			var enemyAA = Blackboard.Regions[Blackboard.EnemyRegion].Threats[(int)CoalitionCapability.AntiAir];
			var hasAir = Blackboard.Forces.Any(f => f.Counts[(int)UnitClass.Air] > 0);
			var highValue = BestScoredTarget();
			if (hasAir && enemyAA < 0.5f && highValue != null && !missions.Missions.Any(m => m.Type == MissionType.AirStrike))
				EnsureMission(MissionType.AirStrike, 70, highValue, "Air strike on high-value target");

			// Shaping: soften enemy air defenses before a staged ground assault, even when AA is strong.
			var stagedAttack = missions.Missions.Any(m => MissionManager.IsOffensive(m.Type) && m.Status == MissionStatus.Executing
				&& m.Type != MissionType.AirStrike && m.Type != MissionType.NavalStrike && m.Type != MissionType.SupportPowerStrike);
			if (hasAir && enemyAA >= 0.5f && stagedAttack && !missions.Missions.Any(m => m.Type == MissionType.AirStrike))
				EnsureMission(MissionType.AirStrike, 65, RegionCenter(Blackboard.EnemyRegion), "Soften enemy air defenses before the assault");

			if (Blackboard.HasReadySuperweapon && highValue != null && !missions.Missions.Any(m => m.Type == MissionType.SupportPowerStrike))
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
			if (Blackboard.EnemyRegion < 0)
				return null;

			for (var i = 0; i < Blackboard.Regions.Length; i++)
				if (Blackboard.MapAnalysis.IsChokepoint(MovementClass.Ground, Blackboard.EnemyRegion, i))
					return RegionCenter(i);

			return null;
		}

		CPos? FlankRegionTarget(CoalitionMission attack)
		{
			var attackRegion = attack.Target != null ? Blackboard.RegionOf(attack.Target.Value).Index : -1;
			for (var i = 0; i < Blackboard.Regions.Length; i++)
			{
				if (Blackboard.Regions[i].EnemyPressure <= 0 || i == attackRegion)
					continue;
				if (Blackboard.MapAnalysis.IsAdjacent(MovementClass.Ground, Blackboard.EnemyRegion, i))
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
			var away = Blackboard.HomeRegion < 0 ? null : Blackboard.EnemyIntel.FirstOrDefault(i =>
				i.Status == IntelStatus.Observed && i.Class != UnitClass.Structure
				&& Blackboard.RegionOf(i.LastSeenCell).Index != Blackboard.HomeRegion);
			if (away != null)
			{
				if (!missions.Missions.Any(m => m.Type == MissionType.MobileDefense))
					EnsureMission(MissionType.MobileDefense, 50, away.LastSeenCell, "Intercept enemy away from base");
			}
			else
				CancelActiveMissions(MissionType.MobileDefense, "mobile threat no longer observed");

			// Anti-air umbrella: enemy air presence demands AA concentration over the base.
			if (Blackboard.EnemyIntel.Any(i => i.Status == IntelStatus.Observed && i.Class == UnitClass.Air))
			{
				if (!missions.Missions.Any(m => m.Type == MissionType.AntiAirUmbrella))
					EnsureMission(MissionType.AntiAirUmbrella, 55, Blackboard.HomeCell, "Anti-air umbrella over the base");
			}
			else
				CancelActiveMissions(MissionType.AntiAirUmbrella, "air threat no longer observed");

			// Naval screen: enemy ships demand a coastal screen.
			if (Blackboard.EnemyIntel.Any(i => i.Status == IntelStatus.Observed && i.Class == UnitClass.Naval)
				&& Blackboard.HasBigWater)
			{
				if (!missions.Missions.Any(m => m.Type == MissionType.NavalScreen))
					EnsureMission(MissionType.NavalScreen, 55, Blackboard.HomeCell, "Naval screen");
			}
			else
				CancelActiveMissions(MissionType.NavalScreen, "naval threat no longer observed");

			// Economy escort: a raid-sensitive enemy threatens harvesters.
			if (Blackboard.Opponent.AttacksHarvesters && !missions.Missions.Any(m => m.Type == MissionType.Escort))
				EnsureMission(MissionType.Escort, 45, Blackboard.HomeCell, "Escort harvesters");
		}

		bool CancelActiveMissions(MissionType type, string reason)
		{
			var cancelled = false;
			foreach (var mission in missions.Missions.Where(m => m.Type == type
				&& m.Status is MissionStatus.Ready or MissionStatus.Executing).ToArray())
			{
				cancelled = true;
				mission.Status = MissionStatus.Cancelled;
				mission.OutcomeReason = reason;
				arbiter.ReleaseMission(mission.Id);
				CoalitionTelemetry.Log(world, $"Mission {mission.Id} ({type}) cancelled: {reason}");
			}

			return cancelled;
		}

		/// <summary>
		/// Creates the remaining mission types that extend the deterministic commander: harassment,
		/// expansion denial, naval blockade and naval strike, pincer envelopment, delaying action,
		/// air/naval/route reconnaissance, decoy transport, and fake buildup. Each is gated on its
		/// tactical precondition and deduplicated against the active mission set.
		/// </summary>
		void CreateAdvancedMissions(float ratio)
		{
			var hasAir = Blackboard.Forces.Any(f => f.Counts[(int)UnitClass.Air] > 0);
			var hasNaval = Blackboard.Forces.Any(f => f.Counts[(int)UnitClass.Naval] > 0);
			var enemyNavalKnown = Blackboard.EnemyIntel.Any(i => i.Class == UnitClass.Naval);
			var enemyStructures = Blackboard.EnemyIntel.Where(i => i.Class == UnitClass.Structure).ToArray();

			// Harassment: a small fast force raids enemy harvesters when the fight is not heavily
			// unfavorable and the enemy economy is known.
			if (ratio < 1.5f && Blackboard.EnemyRegion >= 0 && enemyStructures.Any(i => TargetEvaluator.EconomicValue(i.Type) > 0)
				&& !missions.Missions.Any(m => m.Type == MissionType.Harassment))
			{
				var economyTarget = HighestScored(
					enemyStructures.Where(i => TargetEvaluator.EconomicValue(i.Type) > 0).ToArray(),
					i => TargetEvaluator.EconomicValue(i.Type));
				if (economyTarget != null)
					EnsureMission(MissionType.Harassment, 45, economyTarget, "Raid enemy harvesters");
			}

			// Expansion denial: enemy structures sit in a region that is not their base region.
			if (Blackboard.EnemyRegion >= 0 && !missions.Missions.Any(m => m.Type == MissionType.ExpansionDenial))
			{
				var expansion = enemyStructures.FirstOrDefault(i => Blackboard.RegionOf(i.LastSeenCell).Index != Blackboard.EnemyRegion);
				if (expansion != null)
					EnsureMission(MissionType.ExpansionDenial, 50, expansion.LastSeenCell, "Deny enemy expansion");
			}

			// Naval blockade: patrol the enemy coast to deny enemy naval movement when big water exists
			// and the enemy has ships.
			if (Blackboard.HasBigWater && enemyNavalKnown && !missions.Missions.Any(m => m.Type == MissionType.NavalBlockade))
			{
				var coast = EnemyCoastRegion();
				if (coast != null)
					EnsureMission(MissionType.NavalBlockade, 60, coast, "Blockade the enemy coast");
			}

			// Naval strike: when the coalition has ships and big water, strike alongside the air strike.
			if (Blackboard.HasBigWater && hasNaval && Blackboard.EnemyRegion >= 0
				&& !missions.Missions.Any(m => m.Type == MissionType.NavalStrike))
			{
				var highValue = BestScoredTarget();
				EnsureMission(MissionType.NavalStrike, 65, highValue ?? RegionCenter(Blackboard.EnemyRegion), "Naval strike on high-value target");
			}

			// Pincer: a strong coalition (ratio < 0.8f) with a staged attack sends a second flanking
			// force converging on the same target from the opposite side.
			var stagedAttack = missions.Missions.FirstOrDefault(m => MissionManager.IsOffensive(m.Type)
				&& m.Type != MissionType.AirStrike && m.Type != MissionType.NavalStrike
				&& m.Type != MissionType.SupportPowerStrike && m.Type != MissionType.Pincer
				&& m.Status == MissionStatus.Executing && m.Target != null);
			if (ratio < 0.8f && stagedAttack != null && !missions.Missions.Any(m => m.Type == MissionType.Pincer))
			{
				var pincerTarget = PincerRegionTarget(stagedAttack);
				if (pincerTarget != null)
					EnsureMission(MissionType.Pincer, 85, pincerTarget, "Double envelopment from the opposite flank");
			}

			// Delaying action: when defending and outnumbered, a small force slows the enemy advance.
			if (ratio > 2.0f && !missions.Missions.Any(m => m.Type == MissionType.DelayingAction))
			{
				var away = Blackboard.EnemyIntel.FirstOrDefault(i => i.Class != UnitClass.Structure
					&& Blackboard.RegionOf(i.LastSeenCell).Index != Blackboard.HomeRegion);
				EnsureMission(MissionType.DelayingAction, 75, away?.LastSeenCell ?? Blackboard.HomeCell, "Delay the enemy advance");
			}

			// Air recon: the enemy position is unknown and the coalition has air scouts.
			if (Blackboard.EnemyRegion < 0 && hasAir && !missions.Missions.Any(m => m.Type == MissionType.AirRecon))
			{
				var reconTarget = LeastExploredRegionNear();
				if (reconTarget != null)
					EnsureMission(MissionType.AirRecon, 38, reconTarget, "Aerial reconnaissance to locate the enemy");
			}

			// Naval recon: big water with unknown enemy naval presence.
			if (Blackboard.HasBigWater && !enemyNavalKnown && !missions.Missions.Any(m => m.Type == MissionType.NavalRecon))
			{
				var reconTarget = LeastExploredRegionNear();
				if (reconTarget != null)
					EnsureMission(MissionType.NavalRecon, 38, reconTarget, "Naval reconnaissance to find the enemy fleet");
			}

			// Route recon: an attack is staged and the planned route passes through unexplored regions.
			if (stagedAttack != null && stagedAttack.PlannedRegions.Length > 0
				&& !missions.Missions.Any(m => m.Type == MissionType.RouteRecon))
			{
				var unexplored = stagedAttack.PlannedRegions.FirstOrDefault(r => r >= 0 && Blackboard.Regions[r].FriendlyControl < 0.1f);
				if (unexplored >= 0)
					EnsureMission(MissionType.RouteRecon, 42, RegionCenter(unexplored), "Scout the route to the target");
			}

			// Decoy transport: a special-operations mission is active and a transport is available;
			// send an empty transport to a fake landing zone to draw enemy attention.
			if (missions.Missions.Any(m => m.Type == MissionType.SpecialOps && m.Status == MissionStatus.Executing)
				&& Blackboard.Transports.Count > 0 && !missions.Missions.Any(m => m.Type == MissionType.DecoyTransport))
			{
				var decoyTarget = DecoyLandingZone();
				if (decoyTarget != null)
					EnsureMission(MissionType.DecoyTransport, 65, decoyTarget, "Feign a naval insertion to draw enemy attention");
			}

			// Fake buildup: a deception that shows force at a location to make the enemy think a
			// buildup is happening there, pinning reserves while the real attack goes in elsewhere.
			if (stagedAttack != null && !missions.Missions.Any(m => m.Type == MissionType.FakeBuildup)
				&& !DeceptionSaturated())
			{
				var fakeTarget = FeintRegionTarget();
				if (fakeTarget != null)
					EnsureMission(MissionType.FakeBuildup, 48, fakeTarget, "Feign a force buildup to pin enemy reserves");
			}
		}

		/// <summary>
		/// Picks a water region naval-adjacent to the enemy base region for a blockade patrol, falling
		/// back to the enemy region center when no distinct coastal region is found.
		/// </summary>
		CPos? EnemyCoastRegion()
		{
			if (Blackboard.EnemyRegion < 0)
				return RegionCenter(Blackboard.EnemyRegion);

			for (var i = 0; i < Blackboard.Regions.Length; i++)
			{
				if (i == Blackboard.EnemyRegion)
					continue;
				if (Blackboard.MapAnalysis.IsAdjacent(MovementClass.Naval, Blackboard.EnemyRegion, i))
					return RegionCenter(i);
			}

			return RegionCenter(Blackboard.EnemyRegion);
		}

		/// <summary>
		/// Picks a second flanking axis for a pincer: an enemy-pressured region adjacent to the enemy
		/// base that is distinct from the main attack's region and from any existing flank mission's
		/// target, so the two forces converge from opposite sides.
		/// </summary>
		CPos? PincerRegionTarget(CoalitionMission attack)
		{
			var attackRegion = attack.Target != null ? Blackboard.RegionOf(attack.Target.Value).Index : -1;
			var flank = missions.Missions.FirstOrDefault(m => m.Type == MissionType.Flank && m.Target != null);
			var flankRegion = flank?.Target != null ? Blackboard.RegionOf(flank.Target.Value).Index : -1;
			for (var i = 0; i < Blackboard.Regions.Length; i++)
			{
				if (Blackboard.Regions[i].EnemyPressure <= 0 || i == attackRegion || i == flankRegion)
					continue;
				if (Blackboard.MapAnalysis.IsAdjacent(MovementClass.Ground, Blackboard.EnemyRegion, i))
					return RegionCenter(i);
			}

			return null;
		}

		/// <summary>
		/// Picks a fake landing zone for a decoy transport: a coastal region near the enemy that is
		/// distinct from the active special-operations target, so the decoy draws attention away from
		/// the real insertion.
		/// </summary>
		CPos? DecoyLandingZone()
		{
			var specialOps = missions.Missions.FirstOrDefault(m => m.Type == MissionType.SpecialOps && m.Target != null);
			var specialOpsRegion = specialOps?.Target != null ? Blackboard.RegionOf(specialOps.Target.Value).Index : -1;
			if (Blackboard.EnemyRegion >= 0)
			{
				for (var i = 0; i < Blackboard.Regions.Length; i++)
				{
					if (i == specialOpsRegion || i == Blackboard.EnemyRegion)
						continue;
					if (Blackboard.MapAnalysis.IsAdjacent(MovementClass.Ground, Blackboard.EnemyRegion, i)
						|| Blackboard.MapAnalysis.IsAdjacent(MovementClass.Naval, Blackboard.EnemyRegion, i))
						return RegionCenter(i);
				}
			}

			return RegionCenter(Blackboard.EnemyRegion);
		}

		/// <summary>
		/// Creates value-of-information-driven reconnaissance once the enemy is located: deep recon of
		/// the enemy rear, expansion search toward resource-rich unexplored ground, and defense probing
		/// of the enemy's perimeter.
		/// </summary>
		void CreateReconMissions()
		{
			var existing = missions.Missions.Any(m => MissionManager.IsRecon(m.Type));
			if (existing || Blackboard.EnemyRegion < 0)
				return;

			// Deep recon: the least-explored region adjacent to the enemy base (their rear).
			var deep = BestReconRegion(r => Blackboard.MapAnalysis.IsAdjacent(MovementClass.Ground, r, Blackboard.EnemyRegion) ? 1f : 0f);
			if (deep != null)
				EnsureMission(MissionType.DeepRecon, 40, RegionCenter(deep.Value), "Deep reconnaissance of the enemy rear");

			// Expansion search: the least-explored region with the highest expansion value.
			if (!missions.Missions.Any(m => m.Type == MissionType.ExpansionSearch))
			{
				var expansion = BestReconRegion(r => -Blackboard.MapAnalysis.ExpansionValue[r]);
				if (expansion != null)
					EnsureMission(MissionType.ExpansionSearch, 35, RegionCenter(expansion.Value), "Search for an expansion site");
			}

			// Defense probe: a lightly-observed enemy region with high static-defense threat.
			if (!missions.Missions.Any(m => m.Type == MissionType.DefenseProbe))
			{
				var probe = BestReconRegion(r => -Blackboard.Regions[r].Threats[(int)CoalitionCapability.StaticDefense]);
				if (probe != null)
					EnsureMission(MissionType.DefenseProbe, 35, RegionCenter(probe.Value), "Probe enemy defenses");
			}
		}

		/// <summary>Returns the best unexplored, ground-reachable region by a value selector.</summary>
		int? BestReconRegion(Func<int, float> value)
		{
			int? best = null;
			var bestValue = float.MinValue;
			for (var i = 0; i < Blackboard.Regions.Length; i++)
			{
				if (Blackboard.Regions[i].FriendlyControl > 0.1f || !ReachableFromHome(i))
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
			if (regionIndex < 0 || Blackboard.HomeRegion < 0)
				return false;

			return Blackboard.MapAnalysis.ComponentOf(MovementClass.Ground, regionIndex)
				== Blackboard.MapAnalysis.ComponentOf(MovementClass.Ground, Blackboard.HomeRegion);
		}

		CPos? RegionCenter(int regionIndex)
		{
			if (regionIndex < 0 || regionIndex >= Blackboard.Regions.Length)
				return null;
			var bounds = Blackboard.Regions[regionIndex].Bounds;
			return new CPos((bounds.Left + bounds.Right) / 2, (bounds.Top + bounds.Bottom) / 2);
		}

		/// <summary>Routes a raw LLM intent reply (command.intent.v1 subset) into the next command.</summary>
		public void ApplyLlmIntent(string intentJson)
		{
			if (Blackboard is null || string.IsNullOrEmpty(intentJson))
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

				// Validate the new intent fields: unknown capabilities, blank production directive
				// entries, and out-of-range expansion priorities are rejected and cleared.
				var capabilityRejection = CommandValidator.ValidateCapability(intent.RequestCapability);
				if (capabilityRejection != null)
				{
					CoalitionTelemetry.Log(world, $"Command validator: {capabilityRejection}");
					intent.RequestCapability = null;
				}

				var directiveRejections = CommandValidator.ValidateProduce(intent.ProductionDirective);
				foreach (var (_, reason) in directiveRejections)
					CoalitionTelemetry.Log(world, $"Command validator: {reason}");

				if (directiveRejections.Count > 0)
					intent.ProductionDirective = intent.ProductionDirective.Where((_, i) => !directiveRejections.Any(r => r.Index == i)).ToArray();

				var expansionRejection = CommandValidator.ValidateExpansionPriority(intent.ExpansionPriority);
				if (expansionRejection != null)
				{
					CoalitionTelemetry.Log(world, $"Command validator: {expansionRejection}");
					intent.ExpansionPriority = 0;
				}

				var reserveRejection = CommandValidator.ValidateReserveFraction(intent.ReserveFraction);
				reserveRejection ??= CommandValidator.ValidateReserveJustification(
					intent.ReserveFraction, intent.ReserveJustification);
				if (reserveRejection != null)
				{
					CoalitionTelemetry.Log(world, $"Command validator: {reserveRejection}");
					intent.ReserveFraction = 0;
					intent.ReserveJustification = null;
				}

				// Production-directive unit names are checked against the local buildable-item set so an
				// unknown unit is rejected engine-side instead of silently dropped downstream.
				var unitRejections = CommandValidator.ValidateUnitNames(
					intent.ProductionDirective, BuildableUnitNames(), "production_directive");
				foreach (var (_, reason) in unitRejections)
					CoalitionTelemetry.Log(world, $"Command validator: {reason}");

				if (unitRejections.Count > 0)
					intent.ProductionDirective = intent.ProductionDirective.Where((_, i) => !unitRejections.Any(r => r.Index == i)).ToArray();

				llmIntent = intent;
			}
			catch
			{
				// Invalid intent is ignored; the deterministic commander remains authoritative.
			}
		}
	}
}
