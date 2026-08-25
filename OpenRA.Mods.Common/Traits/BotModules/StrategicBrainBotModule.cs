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
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Commander.Model;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Strategic brain for the ultra-hard AI bot. Provides radar-respecting enemy intelligence, adaptive build plans, " +
		"attack/withdraw wave tactics, friendly-bot coordination, and posture adaptation. All decisions are deterministic " +
		"so bot orders remain replay and multiplayer safe.")]
	[TraitLocation(SystemActors.Player)]
	public sealed class StrategicBrainBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Interval (in ticks) between enemy intelligence refreshes.")]
		public readonly int IntelUpdateInterval = 25;

		[Desc("How long (in ticks) a sighting of an enemy actor is remembered after it leaves explored territory.")]
		public readonly int SightingMemoryTicks = 600;

		[Desc("Interval (in ticks) between production plan updates.")]
		public readonly int BuildPlanInterval = 40;

		[Desc("Banked cash above which production queues may duplicate unit types rather than each",
			"taking a different one. The uniqueness rule diversifies a wave, which is the right",
			"concern when money is tight and the wrong one when most of what has been earned is",
			"sitting unspent.")]
		public readonly int SpendFreelyCashThreshold = 4000;

		[Desc("Own army value below which, when outweighed by what has been seen, the commander",
			"builds the cheapest effective units rather than the heaviest. A unit that arrives",
			"after the base has fallen is worth nothing.")]
		public readonly int EmergencyArmyValue = 8000;

		[Desc("Ticks during which the commander builds the cheapest effective units rather than the",
			"heaviest, provided its army is still small. Heavy units are the right answer when there",
			"is time to build them; a 2,000-credit tank that takes most of a minute is the wrong",
			"opening against anybody who opens fast.")]
		public readonly int EarlyGameTicks = 9000;

		[Desc("Compute what to build from the mod's own damage, cost and health tables rather than",
			"from the hand-ordered lists below. The lists cannot express degree - a mammoth counters",
			"armour, but how much better than a heavy tank, per credit? - and go stale silently when",
			"the mod changes.")]
		public readonly bool ComputeProductionValue = false;

		[Desc("Cash below which production orders are withheld.")]
		public readonly int MinProductionCash = 400;

		[Desc("Preferred unit production order. Earlier entries are produced first when buildable.")]
		public readonly string[] ArmyPriority = [];

		[Desc("Units built when the enemy is seen to hold static defence. Artillery outranges a",
			"fortification, which is the only cheap way through a defended perimeter - and is",
			"exactly the wrong thing to lead with in a field battle, which is why this list is",
			"separate from AntiArmorUnits rather than merged into it.")]
		public readonly FrozenSet<string> AntiDefenceUnits =
			new HashSet<string> { "v2rl", "arty", "qtnk", "dtrk" }.ToFrozenSet();

		[Desc("Enemy structures that count as fortifications for the purpose of the list above.")]
		public readonly FrozenSet<string> EnemyDefenceTypes =
			new HashSet<string> { "pbox", "hbox", "gun", "ftur", "tsla", "agun", "sam", "gap" }.ToFrozenSet();

		[Desc("Units that are prioritized when enemy air units are spotted.")]
		public readonly FrozenSet<string> AntiAirUnits = [];

		[Desc("Units that are prioritized when enemy armored units are spotted.")]
		public readonly FrozenSet<string> AntiArmorUnits = [];

		[Desc("Units that are prioritized when enemy infantry is spotted.")]
		public readonly FrozenSet<string> AntiInfantryUnits = [];

		[Desc("Enemy actor types that are classified as air units.")]
		public readonly FrozenSet<string> AirUnitTypes = [];

		[Desc("Enemy actor types that are classified as armored units.")]
		public readonly FrozenSet<string> ArmorUnitTypes = [];

		[Desc("Enemy actor types that are classified as infantry.")]
		public readonly FrozenSet<string> InfantryUnitTypes = [];

		[Desc("How many items may already be queued on a production queue before an engine-module",
			"request (harvester replacement, MCV expansion) will still be added to it. These requests",
			"are economic rather than discretionary, so they do not wait for a gap in army production.")]
		public readonly int RequestedProductionMaximumBacklog = 2;

		[Desc("Own actor types that are never produced or used for combat by this module (harvesters, MCVs, civilians...).")]
		public readonly FrozenSet<string> ExcludeFromArmyTypes = [];

		[Desc("Units preferred when this bot is assigned the naval role by the team plan.")]
		public readonly FrozenSet<string> NavalPriority = [];

		[Desc("Actor types that can carry out transport missions (e.g. lst, heli).")]
		public readonly FrozenSet<string> TransportTypes = [];

		[Desc("Actor types that are loaded into transports for stealth missions.")]
		public readonly FrozenSet<string> TransportPayloadTypes = [];

		[Desc("Scarce special-operation assets (Tanya, spies, engineers...) that are never pulled " +
			"into waves and are inserted at designated rear-area targets.")]
		public readonly FrozenSet<string> SpecialTypes = [];

		[Desc("The fraction of the army (1/N) that is diverted to a feint attack.")]
		public readonly int FeintFraction = 6;

		[Desc("The fraction of the army (1/N) that is held back as an uncommitted strategic reserve.")]
		public readonly int ReserveFraction = 4;

		[Desc("The reserve is committed when the scouted enemy army is at most this ratio of ours (e.g. 0.6 = go all-in once we outnumber the enemy 10:6).")]
		public readonly float CommitReserveRatio = 0.6f;

		[Desc("Attack waves only launch when the coalition force reaches at least this many units.")]
		public readonly int CoordinatedAttackMinimum = 50;

		[Desc("Attack waves require the coalition to field all three arms: air, naval, and land.")]
		public readonly bool CoordinatedAttackMixedArms = true;

		[Desc("Cheap infantry types sent to walk alone into unexplored territory (suicide scouts).")]
		public readonly string[] ScoutUnitTypes = [];

		[Desc("How many scouts (total) are kept walking into unexplored territory.")]
		public readonly int ScoutSquadSize = 25;

		[Desc("Scouts are sent to unexplored cells at least this many cells from the base.")]
		public readonly int ScoutMinDistance = 40;

		[Desc("Interval (in ticks) between scout deployments.")]
		public readonly int ScoutInterval = 100;

		[Desc("Total scouts that may be dispatched over a whole match while the enemy base is still",
			"unlocated. Separate from ScoutSquadSize, which caps how many are out at once: scouts",
			"probing a defended base usually die, so a lifetime budget equal to the concurrent cap",
			"stops the search after a handful of losses and leaves the coalition blind for the match.",
			"0 reuses ScoutSquadSize as the budget, which caps the search at four probes and leaves",
			"the coalition blind for the match. Reconnaissance is the precondition for naming an",
			"offensive objective at all (COMMANDER_HANDBOOK.md section 6), so it is enabled here and",
			"the offensive doctrine that consumes it is measured with it.")]
		public readonly int ScoutLifetimeBudget = 40;

		[Desc("Consecutive scouting decisions that may reveal no new ground before the search is",
			"abandoned as futile. Guards against pouring production into probes that cannot reach",
			"the enemy at all, which is the normal case on water maps.")]
		public readonly int BarrenScoutCycles = 25;

		[Desc("Radius (in cells) around the objective in which the assault looks for structures to",
			"attack. An assault that attack-moves to a cell engages the first thing it meets, which",
			"on a defended base is the perimeter defence; naming a structure target instead is what",
			"turns a raid into a siege. See COMMANDER_HANDBOOK.md section 7.")]
		public readonly int SiegeScanRadius = 14;

		[Desc("Unit produced specifically for reconnaissance, ahead of using line troops as scouts.",
			"A dog costs 200 against rifle infantry's 100 but moves at 100 against their 54 - close",
			"to double - so per cell revealed it is the cheaper scout, and it arrives sooner, which",
			"matters more than price when the entire point is early information. Requires a kennel.",
			"Empty disables dedicated scout production. See COMMANDER_HANDBOOK.md section 6.")]
		public readonly string DedicatedScoutType = "dog";

		[Desc("How many dedicated scouts to keep available while the enemy base is still unlocated.")]
		public readonly int DedicatedScoutReserve = 3;

		[Desc("Promote artillery and anti-air to the front of the build order when the army has less",
			"than the doctrine ratio. Artillery out-ranges base defence, which is the cheap way",
			"through a defended perimeter - but promoting it costs armour, so whether it pays is a",
			"measurement rather than an assumption. See COMMANDER_HANDBOOK.md section 7.3.")]
		public readonly bool PromoteSupportUnits = true;

		[Desc("How many new scouts are deployed per interval.")]
		public readonly int ScoutSendPerInterval = 3;

		[Desc("Distance in cells from a public spawn center used as the scout approach point.")]
		public readonly int ScoutSpawnApproachOffset = 3;

		[Desc("Minimum combat-unit count before production may spend on technology prerequisites.")]
		public readonly int PrerequisiteArmyThreshold = 10;

		[Desc("Production-request unit types that create strategic expansions.")]
		public readonly FrozenSet<string> ExpansionUnitTypes = new HashSet<string> { "mcv" }.ToFrozenSet();

		[Desc("Minimum combat-unit count before production may honor an expansion-unit request.")]
		public readonly int ExpansionArmyThreshold = 100;

		[Desc("Difficulty 0-3 (easy, normal, hard, impossible): scales the coordinated-attack threshold, " +
			"the reserve, and how aggressively the bot commits. Convenience knob that sets all " +
			"independent axes together; set the per-axis fields below to override individually.")]
		public readonly int Difficulty = 3;

		[Desc("Command quality 0-3: how demanding the coordinated-attack threshold is.")]
		public readonly int CommandQuality = -1;

		[Desc("Reaction speed 0-3: how slowly the bot reacts to battlefield changes.")]
		public readonly int ReactionSpeed = -1;

		[Desc("Economic bonus 0-3: fractional cash injection; 0 is a strictly fair game.")]
		public readonly int EconomicBonus = 0;

		[Desc("Micro precision 0-3: how early the bot pulls damaged units back.")]
		public readonly int MicroPrecision = -1;

		[Desc("Coordination strength 0-3: how tightly the reserve and feints are managed.")]
		public readonly int CoordinationStrength = -1;

		/// <summary>The resolved independent difficulty axes, honoring per-axis overrides.</summary>
		public CoalitionDifficulty ResolvedDifficulty()
		{
			var scalar = CoalitionDifficulty.FromScalar(Difficulty);
			return new CoalitionDifficulty
			{
				CommandQuality = CommandQuality >= 0 ? CommandQuality : scalar.CommandQuality,
				ReactionSpeed = ReactionSpeed >= 0 ? ReactionSpeed : scalar.ReactionSpeed,
				EconomicBonus = EconomicBonus,
				MicroPrecision = MicroPrecision >= 0 ? MicroPrecision : scalar.MicroPrecision,
				CoordinationStrength = CoordinationStrength >= 0 ? CoordinationStrength : scalar.CoordinationStrength
			};
		}

		/// <summary>Scales a base value by command quality: 1.5x at easy down to 0.75x at supreme.</summary>
		public float ScaleDifficulty(float baseValue)
		{
			return ResolvedDifficulty().Scale(baseValue);
		}

		/// <summary>The reserve fraction gets tighter with coordination strength.</summary>
		public int ScaledReserveFraction()
		{
			return ResolvedDifficulty().ScaledReserveFraction();
		}

		[Desc("Interval (in ticks) between tactical updates.")]
		public readonly int TacticInterval = 20;

		[Desc("Radius in cells in which the tactical executor acquires currently visible local contacts.")]
		public readonly int TacticalEngagementScanRadius = 16;

		[Desc("Interval in ticks after which a non-combat movement directive may be refreshed to recover idle or stuck units.")]
		public readonly int TacticalOrderRefreshTicks = 75;

		[Desc("Radius in cells around an air objective used to detect visible anti-air coverage.")]
		public readonly int TacticalAirDangerRadius = 8;

		[Desc("Number of combat units required before the bot adopts an attack posture.")]
		public readonly int AttackForceThreshold = 14;

		[Desc("Units retreat individually when their health drops below this percentage.")]
		public readonly int RetreatHealthPercent = 30;

		[Desc("A retreating unit rejoins the army once its health recovers above this percentage.")]
		public readonly int RegroupHealthPercent = 60;

		[Desc("Below this force cohesion the army holds position to regroup instead of launching a wave.")]
		public readonly float RegroupCohesionThreshold = 0.3f;

		[Desc("Maximum lead in cells before assault units pause for slower support.")]
		public readonly int FormationMaxLeadCells = 15;

		[Desc("Distance in cells artillery remains behind the screening-force center.")]
		public readonly int ArtilleryScreenOffsetCells = 4;

		[Desc("Enemy units within this many cells of the base center trigger base defense.")]
		public readonly int BaseDefenseScanRadius = 25;

		[Desc("Currently visible enemies within this many cells of any economic or production asset trigger interception.")]
		public readonly int AssetDefenseScanRadius = 18;

		[Desc("Maximum defenders committed per observed attacker inside the close asset-defense perimeter.")]
		public readonly int DefenseUnitsPerAttacker = 6;

		[Desc("The army is not committed to a wave while it is smaller than this many units.")]
		public readonly int MinWaveSize = 6;

		[Desc("The army is withdrawn back to the base when no enemy sighting was refreshed within this many ticks.")]
		public readonly int WithdrawDelayTicks = 300;

		[Desc("Interval (in ticks) between team coordination updates.")]
		public readonly int CoordinationInterval = 100;

		[Desc("How long (in ticks) after repelling a base attack the counterattack window stays open.")]
		public readonly int CounterDelayTicks = 400;

		[Desc("Distance in cells projected beyond an observed attacker when estimating its approach corridor for a counterattack.")]
		public readonly int CounterPursuitCells = 30;

		[Desc("Allied bases are reinforced when enemies are spotted within this many cells.")]
		public readonly int AllyReinforceScanRadius = 40;

		[Desc("The fraction of the army (1/N) that is sent to reinforce an allied base under attack.")]
		public readonly int ReinforcementFraction = 3;

		[Desc("Switch to a defensive posture when the scouted enemy army outnumbers ours by this ratio.")]
		public readonly float DefendArmyRatio = 1.5f;

		[Desc("How long (in ticks) the bot stays in attack posture after launching a wave.")]
		public readonly int AttackPostureTicks = 300;

		public override object Create(ActorInitializer init) { return new StrategicBrainBotModule(this, init); }
	}

	public sealed class StrategicBrainBotModule : ConditionalTrait<StrategicBrainBotModuleInfo>, IBotTick,
		IBotRespondToAttack, IBotRequestUnitProduction
	{
		enum Posture { BuildArmy, Attack, Defend }

		sealed class Sighting
		{
			public WPos Position;
			public int Tick;
			public string Type;
			public bool IsStructure;
		}

		// Enemy memory deliberately contains snapshots, never live actors. A live Actor can reveal
		// a mobile enemy's current position after it has moved back under fog.
		readonly Dictionary<uint, Sighting> sightings = [];
		readonly HashSet<Actor> retreating = [];
		readonly HashSet<Actor> completedUnserviceableRetreats = [];
		readonly HashSet<Actor> claimedUnits = [];
		readonly HashSet<Actor> scouts = [];

		/// <summary>Which cell each dispatched scout was sent to, so a failed probe can be retried.</summary>
		readonly Dictionary<Actor, CPos> scoutAssignments = [];
		readonly HashSet<Actor> missionScouts = [];
		readonly HashSet<CPos> attemptedScoutTargets = [];
		int scoutsDeployed;
		int missionScoutsDeployed;
		readonly HashSet<Actor> deceptionForce = [];
		readonly List<string> requestedProduction = [];

		/// <summary>What the commander is holding cash for, if anything. Null when free to spend.</summary>
		string savingFor;
		string lastScoutType;
		readonly HashSet<uint> teamRetreatActorIds = [];
		bool teamRetreatActive;
		bool enemyBaseEverLocated;

		/// <summary>Map cells revealed as of the last scouting decision, and how many decisions in a row have added none.</summary>
		int lastRevealedCells;
		int barrenScoutCycles;

		// Exposed to the tactical controllers.
		internal World World { get; private set; }
		internal Player Player { get; private set; }
		internal IBot Bot { get; private set; }
		internal new StrategicBrainBotModuleInfo Info { get; }
		internal int CurrentReserveFraction => reserveFractionOverride > 0
			? reserveFractionOverride : Info.ScaledReserveFraction();

		/// <summary>Forwards a controller inability to the strategic commander for a debounced review.</summary>
		internal void RequestStrategicReplan(string reason)
		{
			var commander = Player?.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);
			commander?.RequestReplan(reason);
		}

		// Per-domain tactical controllers: each executes its own component of a mission (land/air/
		// naval waves, transports, special insertions) and claims its own units through the arbiter.
		readonly GroundController ground;
		readonly AirController air;
		readonly NavalController naval;
		readonly TransportController transport;
		readonly SpecialOpsController specialOps;
		ProductionQueue[] queues = [];

		int enemyArmyCount;
		bool enemyAirSpotted;

		/// <summary>Whether the enemy has been seen to hold fortifications worth a siege train.</summary>
		bool enemyDefenceSpotted;
		bool enemyArmorSpotted;
		bool enemyInfantrySpotted;
		string lastComposition;
		string lastPickOrder;
		WPos? enemyBaseCenter;

		Posture posture = Posture.BuildArmy;
		int lastAttackTick;
		int lastWaveTick;
		int lastDefendTick;
		CPos? counterPos;
		int enemyCountAtDefense;
		bool reserveCommitted;
		bool lastReserveCommitted;
		string lastCoordGate;

		// Per-ally reinforcement cooldown: prevents every allied bot from sending reinforcements
		// to the same ally in the same interval. Keyed by ally InternalName, value is the tick of
		// the last reinforcement sent to that ally.
		readonly Dictionary<string, int> lastReinforceTick = [];

		// Commander reserve policy: when >0, replaces the difficulty-scaled reserve fraction.
		int reserveFractionOverride;
		float acceptableLossFraction;
		bool teamCommitReserve;

		// Reserve manager: tracks reserve commitments and their reasons for telemetry (reqs 355-360).
		readonly ReserveManager reserveManager = new();

		// Coalition force summary, fed by the command center through the team plan.
		int coalitionArmy;
		int coalitionAir;
		int coalitionNaval;
		int coalitionLand;
		bool coalitionHasWater;
		int attackTick;
		int supportPowerTick;

		// Team plan state, fed by the external model brain.
		string teamStrategy;
		string teamRole;
		CPos? attackTarget;
		CPos? pincerTarget;
		CPos? feintTarget;
		CPos? reconTarget;
		CPos? issuedReconTarget;
		CPos? baitTarget;
		CPos? counterTarget;
		CPos? transportTarget;
		CPos? strikeTarget;
		CPos? supportPowerTarget;
		CPos? expansionGuardTarget;
		string transportKind;
		string strikeKind;
		string defenseKind;
		string deceptionKind;
		string attackPhase;
		string[] produceBoost;
		bool teamRetreat;
		int feintTick;
		int lastExpansionGuardTick = int.MinValue;

		static readonly System.Text.Json.JsonSerializerOptions PlanOptions = new() { PropertyNameCaseInsensitive = true };

		public StrategicBrainBotModule(StrategicBrainBotModuleInfo info, ActorInitializer init)
			: base(info)
		{
			Info = info;
			World = init.World;
			ground = new GroundController(this);
			air = new AirController(this);
			naval = new NavalController(this);
			transport = new TransportController(this);
			specialOps = new SpecialOpsController(this);
		}

		void IBotRespondToAttack.RespondToAttack(IBot bot, Actor self, AttackInfo e)
		{
			if (Player == null || e.Attacker == null || e.Attacker.IsDead || !e.Attacker.IsInWorld)
				return;

			if (Player.RelationshipWith(e.Attacker.Owner) != PlayerRelationship.Enemy)
				return;

			// An enemy attacked one of our actors: raise the alarm and prepare to defend, and record
			// how quickly the enemy reacted to our last wave (response-time sample for the model).
			lastAttackTick = World.WorldTick;
			SetPosture(Posture.Defend);

			var commander = Player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);
			commander?.RecordEnemyResponse(World.WorldTick);
		}

		void IBotRequestUnitProduction.RequestUnitProduction(IBot bot, string requestedActor)
		{
			if (!string.IsNullOrWhiteSpace(requestedActor))
			{
				requestedProduction.Add(requestedActor);
			}
		}

		int IBotRequestUnitProduction.RequestedProductionCount(IBot bot, string requestedActor)
		{
			return requestedProduction.Count(name => name == requestedActor);
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (IsTraitDisabled)
				return;

			Bot = bot;
			Player = bot.Player;
			World = Player.World;

			queues = Player.PlayerActor.TraitsImplementing<ProductionQueue>()
				.Where(q => q.Enabled)
				.ToArray();

			// Optional economic bonus: a per-tick cash injection scaled by the difficulty axis.
			// 0 (the default) is a strictly fair game with no hidden income.
			var economicBonus = Info.ResolvedDifficulty().EconomicBonus;
			if (economicBonus > 0 && World.WorldTick % 100 == 0)
			{
				var resources = Player.PlayerActor.TraitOrDefault<PlayerResources>();
				resources?.GiveCash(50 * economicBonus);
			}

			var tick = World.WorldTick;

			// The order arbiter: each mission claims its units in priority order, so no unit receives
			// conflicting orders from several missions in the same tick.
			claimedUnits.Clear();

			if (tick % Info.IntelUpdateInterval == 0)
				UpdateIntel(tick);

			if (tick % Info.BuildPlanInterval == 0)
				UpdateBuildPlan();

			// Scouts are claimed before tactics so they are never pulled into waves or feints.
			if (tick % Info.ScoutInterval == 0)
				UpdateScouting();

			if (tick % Info.TacticInterval == 0)
				UpdateTactics();

			if (tick % Info.CoordinationInterval == 0)
				UpdateCoordination();
		}

		/// <summary>
		/// Refreshes enemy intelligence. The bot records only currently visible enemy actors and forgets
		/// sightings after a limited time. The scouted force is classified to drive adaptive production
		/// and posture.
		/// </summary>
		void UpdateIntel(int tick)
		{
			var stale = sightings.Where(kv => tick - kv.Value.Tick > Info.SightingMemoryTicks).Select(kv => kv.Key).ToArray();
			foreach (var key in stale)
				sightings.Remove(key);

			foreach (var a in World.Actors)
			{
				if (a.IsDead || !a.IsInWorld || a.Owner == Player)
					continue;

				if (Player.RelationshipWith(a.Owner) != PlayerRelationship.Enemy)
					continue;

				// Player actors have no position and cannot be sighted.
				if (a.OccupiesSpace == null)
					continue;

				// Fog-respecting awareness: explored terrain is not current observation.
				if (!Player.Shroud.IsVisible(a.CenterPosition))
					continue;

				sightings[a.ActorID] = new Sighting
				{
					Position = a.CenterPosition,
					Tick = tick,
					Type = a.Info.Name,
					IsStructure = a.Info.HasTraitInfo<BuildingInfo>()
				};
			}

			bool IsArmy(Sighting s) => !s.IsStructure && !Info.ExcludeFromArmyTypes.Contains(s.Type);

			enemyArmyCount = sightings.Values.Count(IsArmy);
			enemyAirSpotted = sightings.Values.Any(s => Info.AirUnitTypes.Contains(s.Type));
			enemyDefenceSpotted = sightings.Values.Any(s => Info.EnemyDefenceTypes.Contains(s.Type));
			enemyArmorSpotted = sightings.Values.Any(s => Info.ArmorUnitTypes.Contains(s.Type));
			enemyInfantrySpotted = sightings.Values.Any(s => Info.InfantryUnitTypes.Contains(s.Type));
			enemyBaseEverLocated |= sightings.Values.Any(s => s.IsStructure);

			var composition = $"armor={enemyArmorSpotted} air={enemyAirSpotted} infantry={enemyInfantrySpotted}";
			if (composition != lastComposition)
			{
				lastComposition = composition;
				CoalitionTelemetry.Log(World, $"Enemy composition: {composition} (army {enemyArmyCount})");
			}

			var structureSightings = sightings.Values.Where(s => s.IsStructure).Select(s => s.Position).ToArray();
			enemyBaseCenter = structureSightings.Length > 0 ? structureSightings.Average() : null;

			UpdatePosture();
		}

		/// <summary>
		/// Adapts the bot's posture from the scouted force balance and recent attacks:
		/// defend when outnumbered or under attack, attack with a decisive force, otherwise keep building.
		/// </summary>
		void UpdatePosture()
		{
			// The team plan overrides the local posture: roles and strategy take precedence over the
			// locally scouted force balance.
			if (teamStrategy != null || teamRole != null || teamRetreat)
			{
				var teamPosture = ResolveTeamStrategy(teamRetreat, teamRole, teamStrategy);
				if (teamPosture == "defend")
					SetPosture(Posture.Defend);
				else if (teamPosture == "attack")
					SetPosture(Posture.Attack);
				else
					SetPosture(Posture.BuildArmy);
				return;
			}

			var ownArmyCount = OwnCombatUnits().Count();
			if (ownArmyCount == 0)
			{
				SetPosture(Posture.BuildArmy);
				return;
			}

			var tick = World.WorldTick;
			if (tick - lastAttackTick < Info.AttackPostureTicks / 2)
			{
				SetPosture(Posture.Defend);
				return;
			}

			if (enemyArmyCount > ownArmyCount * Info.DefendArmyRatio)
			{
				SetPosture(Posture.Defend);
				return;
			}

			if (ownArmyCount >= Info.AttackForceThreshold && enemyBaseCenter != null)
			{
				SetPosture(Posture.Attack);
				return;
			}

			SetPosture(Posture.BuildArmy);
		}

		/// <summary>
		/// Resolves the coalition plan without allowing an operational role to invent an attack.
		/// Roles specialize force ownership; only the shared strategy authorizes offensive posture.
		/// </summary>
		public static string ResolveTeamStrategy(bool retreat, string role, string strategy)
		{
			if (retreat || role == "defend" || strategy is "defend" or "turtle")
				return "defend";
			return strategy == "attack" ? "attack" : "build";
		}

		void SetPosture(Posture newPosture)
		{
			posture = newPosture;
		}

		sealed class TeamPlan
		{
			public string Strategy { get; set; }
			public TeamTarget Attack { get; set; }
			public TeamTarget Pincer { get; set; }
			public TeamTarget Feint { get; set; }
			public TeamTarget Recon { get; set; }
			public TeamTarget Bait { get; set; }
			public TeamTarget Counter { get; set; }
			public TeamTarget Transport { get; set; }
			public TeamTarget Strike { get; set; }
			public TeamTarget SupportPower { get; set; }
			public string TransportKind { get; set; }
			public string StrikeKind { get; set; }
			public Dictionary<string, string> Roles { get; set; }
			public string[] Produce { get; set; }
			public bool Retreat { get; set; }
			public TeamForce Force { get; set; }
			public int AttackTick { get; set; }
			public int SupportPowerTick { get; set; }
			public string DefenseKind { get; set; }
			public string DeceptionKind { get; set; }
			public Dictionary<string, string[]> Assignments { get; set; }
			public string AttackPhase { get; set; }
			public TeamTarget ExpansionGuard { get; set; }
			public int ExpansionPriority { get; set; }
			public float AcceptableLoss { get; set; }
			public bool CommitReserve { get; set; }
		}

		sealed class TeamForce
		{
			public int Army { get; set; }
			public int Air { get; set; }
			public int Naval { get; set; }
			public int Land { get; set; }

			/// <summary>True when the coalition has explored a water body big enough for a navy.</summary>
			public bool Water { get; set; }
		}

		sealed class TeamTarget
		{
			public int X { get; set; }
			public int Y { get; set; }
		}

		/// <summary>
		/// Consumes the team plan from the external model brain and stores this bot's share. Called on
		/// the game thread whenever a plan response arrives.
		/// </summary>
		public void ApplyTeamPlan(string planJson)
		{
			TeamPlan plan;
			try
			{
				plan = System.Text.Json.JsonSerializer.Deserialize<TeamPlan>(planJson, PlanOptions);
			}
			catch
			{
				return;
			}

			if (plan == null)
				return;

			bool Assigned(string key) => CoalitionOrderArbiter.IsAssigned(plan.Assignments, key, Player.InternalName);

			teamStrategy = Assigned("attack") ? plan.Strategy : Assigned("counter") ? "defend" : "build";
			teamRetreat = plan.Retreat;
			produceBoost = plan.Produce;
			attackTarget = Assigned("attack") ? ClampCell(ToCell(plan.Attack)) : null;
			pincerTarget = Assigned("pincer") ? ClampCell(ToCell(plan.Pincer)) : null;
			feintTarget = Assigned("feint") ? ClampCell(ToCell(plan.Feint)) : null;
			var nextReconTarget = Assigned("recon") ? ClampCell(ToCell(plan.Recon)) : null;
			if (nextReconTarget != reconTarget)
				issuedReconTarget = null;
			reconTarget = nextReconTarget;
			baitTarget = Assigned("bait") ? ClampCell(ToCell(plan.Bait)) : null;
			counterTarget = Assigned("counter") ? ClampCell(ToCell(plan.Counter)) : null;
			transportTarget = Assigned("transport") ? ClampCell(ToCell(plan.Transport)) : null;
			transportKind = plan.TransportKind;
			strikeKind = plan.StrikeKind;
			strikeTarget = Assigned("strike") ? ClampCell(ToCell(plan.Strike)) : null;
			supportPowerTarget = Assigned("supportPower") ? ClampCell(ToCell(plan.SupportPower)) : null;
			defenseKind = plan.DefenseKind;
			deceptionKind = plan.DeceptionKind;
			attackPhase = plan.AttackPhase;
			expansionGuardTarget = ClampCell(ToCell(plan.ExpansionGuard));
			acceptableLossFraction = Math.Clamp(plan.AcceptableLoss, 0f, 1f);
			teamCommitReserve = plan.CommitReserve;
			teamRole = plan.Roles != null && plan.Roles.TryGetValue(Player.InternalName, out var role) ? role : null;
			attackTick = plan.AttackTick;
			supportPowerTick = plan.SupportPowerTick;
			foreach (var expansion in Player.PlayerActor.TraitsImplementing<McvExpansionManagerBotModule>())
				expansion.SetStrategicPriority(teamRole == "expansion" ? 1 : plan.ExpansionPriority);
			if (plan.Force != null)
			{
				coalitionArmy = plan.Force.Army;
				coalitionAir = plan.Force.Air;
				coalitionNaval = plan.Force.Naval;
				coalitionLand = plan.Force.Land;
				coalitionHasWater = plan.Force.Water;
			}
		}

		CPos? ClampCell(CPos? cell)
		{
			return cell == null ? null : World.Map.Clamp(cell.Value);
		}

		static CPos? ToCell(TeamTarget target)
		{
			return target == null ? null : new CPos(target.X, target.Y);
		}

		/// <summary>
		/// Adaptive production: the unit pick order is the counter list of whatever enemy composition
		/// has been scouted, followed by the configured army priority. Production is only ordered on
		/// idle queues once the cash floor is satisfied.
		/// </summary>
		void UpdateBuildPlan()
		{
			if (Player == null)
				return;

			var resources = Player.PlayerActor.TraitOrDefault<PlayerResources>();
			if (resources == null || resources.GetCashAndResources() < Info.MinProductionCash)
				return;

			// Computed rather than listed, when enabled. The lists below are rules in the plainest
			// sense - somebody's opinion about what beats what, fixed when it was written - and this
			// project has had to measure and reverse half of those opinions. The engine already
			// holds the real answer in its damage, cost and health tables, so the ranking is
			// arithmetic over what the enemy is actually fielding.
			if (Info.ComputeProductionValue)
			{
				var computed = ComputedPickOrder();
				if (computed.Count > 0)
				{
					var computedStr = string.Join(",", computed.Take(8));
					if (computedStr != lastPickOrder)
					{
						lastPickOrder = computedStr;
						CoalitionTelemetry.Log(World, $"Production priorities (computed): {computedStr}");
					}

					ProduceFrom(computed);
					return;
				}
			}

			// Build the adaptive pick order: team produce boosts and role priorities first, then
			// counters for the scouted enemy composition, then the base army composition.
			var pickOrder = new List<string>();
			if (produceBoost != null)
				pickOrder.AddRange(produceBoost);
			if (teamRole == "naval" && coalitionHasWater)
				pickOrder.AddRange(Info.NavalPriority);
			// Counters are ordered by which threat actually dominates, not by a fixed hierarchy.
			// Only the first buildable entry per queue is ever picked, so whatever leads this list
			// is what gets built - and a fixed order meant a turtle holding fifteen fortifications
			// and two helipads was answered with anti-air, because air came first by convention,
			// leaving the siege train fifth and unreachable.
			var fortifications = sightings.Values.Count(v => Info.EnemyDefenceTypes.Contains(v.Type));
			var aircraft = sightings.Values.Count(v => Info.AirUnitTypes.Contains(v.Type));
			var besiege = enemyDefenceSpotted && fortifications > aircraft;

			if (besiege)
				pickOrder.AddRange(Info.AntiDefenceUnits);

			if (enemyAirSpotted)
				pickOrder.AddRange(Info.AntiAirUnits);
			// Fortifications need a different answer from tanks, and until now there was no branch
			// for them at all - the order adapted to air, armour and infantry and was silent about
			// static defence. Measured, leading the anti-armour list with artillery took the turtle
			// matchup from 0.42 to 0.60 and cost the rush matchup 1.78 to 1.11, because artillery
			// answers a fortification and loses a field battle. Making it conditional is the point:
			// build the siege train when there is something to besiege.
			if (enemyDefenceSpotted && !besiege)
				pickOrder.AddRange(Info.AntiDefenceUnits);

			if (enemyArmorSpotted)
				pickOrder.AddRange(Info.AntiArmorUnits);
			if (enemyInfantrySpotted)
				pickOrder.AddRange(Info.AntiInfantryUnits);
			// Heavy units are the right answer when there is time to build them and money to spare,
			// and the wrong one when a rush is already inbound. Measured against the naval bot -
			// which opens fast - the commander died at 17,000 ticks holding 137,760 credits, 87% of
			// everything it had earned, because the first thing in its list cost 2,000 and took the
			// best part of a minute while the enemy was already at the door.
			//
			// When the army is outweighed by what has actually been seen, the list is reordered
			// cheapest-first: three tanks now beat one tank later, and a unit that arrives after the
			// base has fallen is worth nothing at all.
			var seenEnemyValue = sightings.Values
				.Where(v => !v.IsStructure)
				.Sum(v => World.Map.Rules.Actors.TryGetValue(v.Type, out var ai)
					? ai.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0
					: 0);

			var ownValue = OwnCombatUnits().Sum(a => a.Info.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0);

			// Two ways to know the army is needed sooner than the heavy list can deliver it. The
			// first is seeing more enemy than we have, which is the honest signal and fires rarely -
			// reconnaissance is this commander's weakest sense, and a rule that depends on it does
			// nothing most of the time. The second needs no intelligence at all: early in a match,
			// nobody has a heavy army yet, and the side whose first units arrive first owns the map
			// until the other catches up.
			var pressed = ownValue < Info.EmergencyArmyValue
				&& (seenEnemyValue > ownValue || World.WorldTick < Info.EarlyGameTicks);

			if (pressed)
			{
				pickOrder.AddRange(Info.ArmyPriority
					.OrderBy(u => World.Map.Rules.Actors.TryGetValue(u, out var ai)
						? ai.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? int.MaxValue
						: int.MaxValue));
			}
			else
				pickOrder.AddRange(Info.ArmyPriority);

			// Log production-priority changes so the build plan is auditable in telemetry.
			var pickOrderStr = string.Join(",", pickOrder.Take(8));
			if (pickOrderStr != lastPickOrder)
			{
				lastPickOrder = pickOrderStr;
				CoalitionTelemetry.Log(World, $"Production priorities: {pickOrderStr}");
			}

			// While defending, replace a queued UNIT only when it no longer appears among the most
			// urgent counters. Cancellation refunds its cost and prevents a stale long build from
			// blocking an immediately needed response.
			//
			// Structures are exempt, and the omission of that exemption was a serious bug. This
			// swept every enabled queue and cancelled whatever it found that was not among the top
			// five UNITS - a list that never contains a refinery, a power plant or a war factory,
			// because those are not units. So for as long as the commander was defending, which is
			// most of a hard match, it cancelled its own base builder's work on the tick after the
			// base builder ordered it. Instrumented on a water map: the base builder decided to
			// build a shipyard three times, placement never failed, and no shipyard ever existed.
			// The same applied to everything else it tried to construct.
			if (posture == Posture.Defend)
			{
				var priorities = pickOrder.Take(5).ToArray();
				foreach (var q in queues)
				{
					if (q.Info.Type is "Building" or "Defense")
						continue;

					var current = q.CurrentItem();
					if (current != null && !priorities.Contains(current.Item))
						Bot.QueueOrder(Order.CancelProduction(q.Actor, current.Item, 1));
				}
			}

			// Economy first, and before the gate below. A harvester is the input to everything else
			// the commander does; a tank is one of the outputs.
			var servedQueues = ServeRequestedProduction(resources);

			// Saving for something the economy asked for. Discretionary production waits.
			if (savingFor != null)
				return;

			var idleQueues = queues.Where(q => q.CurrentItem() == null).ToArray();
			if (idleQueues.Length == 0)
				return;

			// Emergency replacement: when defending, rebuild lost production infrastructure before
			// spending on units, so the coalition can resume producing counters from its own factory.
			if (posture == Posture.Defend)
			{
				var buildingQueue = queues.FirstOrDefault(q => q.Info.Type == "Building" && q.CurrentItem() == null);
				if (buildingQueue != null)
				{
					var criticalBuildings = new[] { "weap", "barr", "tent", "proc", "powr", "apwr" };
					var replacement = ProductionContract.SelectEmergencyReplacement(true, criticalBuildings,
						building => World.Actors.Any(a => a.IsInWorld && !a.IsDead && a.Owner == Player && a.Info.Name == building),
						building => queues.Any(q => q.AllQueued().Any(i => i.Item == building)),
						building => buildingQueue.BuildableItems().Any(i => i.Name == building));
					if (replacement != null)
					{
						Bot.QueueOrder(Order.StartProduction(buildingQueue.Actor, replacement, 1));
						CoalitionTelemetry.Log(World, $"Emergency replacement: {replacement} ordered");
						return;
					}
				}
			}

			// Dedicated scout production. Dogs are excluded from ExcludeFromArmyTypes on purpose -
			// they are poor line troops and must not be swept into combat waves - which also means
			// the ordinary pick order will never build one. Reconnaissance is the precondition for
			// naming an objective at all, so it gets its own request rather than competing with the
			// army for a queue slot.
			if (Info.DedicatedScoutReserve > 0 && !enemyBaseEverLocated)
			{
				// Derived from what this faction can actually build right now, not named in config.
				// The dog is the obvious scout and its kennel is Soviet-only, so an Allied commander
				// told to scout with dogs simply never scouts.
				var candidates = queues
					.SelectMany(q => q.BuildableItems())
					.Distinct()
					.Select(ScoutSelection.Candidate)
					.Where(c => c != null)
					.Select(c => c.Value);

				var scoutType = ScoutSelection.Preferred(Info.ScoutUnitTypes, candidates);

				// The best scout overall may not be buildable yet. A dog needs a kennel, which the
				// base builder deprioritises into never - so if the better scout is one prerequisite
				// away, order that building rather than settling for the fallback all match.
				var aspirational = Info.ScoutUnitTypes.FirstOrDefault() ?? ScoutSelection.Best(Info.ScoutUnitTypes
					.Select(t => World.Map.Rules.Actors.TryGetValue(t, out var actorInfo) ? actorInfo : null)
					.Where(i => i != null)
					.Select(ScoutSelection.Candidate)
					.Where(c => c != null)
					.Select(c => c.Value));

				if (!string.IsNullOrEmpty(aspirational) && aspirational != scoutType)
				{
					var missing = MissingPrerequisiteBuilding(aspirational);
					var buildingQueue = queues.FirstOrDefault(q => q.Info.Type == "Building");
					if (missing != null && buildingQueue != null
						&& !queues.Any(q => q.AllQueued().Any(i => i.Item == missing))
						&& buildingQueue.BuildableItems().Any(i => i.Name == missing))
					{
						Bot.QueueOrder(Order.StartProduction(buildingQueue.Actor, missing, 1));
						CoalitionTelemetry.Log(World, $"Reconnaissance: ordering {missing} to unlock {aspirational}");
					}
				}

				if (!string.IsNullOrEmpty(scoutType))
				{
					var available = World.Actors.Count(a => a.IsInWorld && !a.IsDead
						&& a.Owner == Player && a.Info.Name == scoutType);
					var pending = requestedProduction.Count(n => n == scoutType);

					if (available + pending < Info.DedicatedScoutReserve)
					{
						requestedProduction.Add(scoutType);
						if (scoutType != lastScoutType)
						{
							lastScoutType = scoutType;
							CoalitionTelemetry.Log(World, $"Reconnaissance: producing {scoutType} as scout");
						}
					}
				}
			}

			// Produce on every remaining idle queue in parallel: each queue takes the highest-priority unit it can
			// build, so the air, naval, and land arms all get produced instead of the first pick
			// monopolizing production.
			// Composition gate (handbook §7.3). Each queue independently picking its best buildable
			// unit floods infantry: the default base has more barracks than war factories, and
			// infantry are a seventh the price and far quicker. Measured waves were 43 infantry to
			// 6 tanks with armour falling over the match, which loses to any tank army. Infantry are
			// a screen, so once the screen is full the barracks idle and the credits go to armour.
			var ownUnits = OwnCombatUnits().ToArray();
			var ownInfantry = ownUnits.Count(a => Info.InfantryUnitTypes.Contains(a.Info.Name));
			var ownArmor = ownUnits.Count(a => Info.ArmorUnitTypes.Contains(a.Info.Name));
			var screenFull = !ArmyComposition.ShouldProduceInfantry(ownInfantry, ownArmor);

			if (ArmyComposition.IsInfantryHeavy(ownInfantry, ownArmor) && World.WorldTick % 500 == 0)
				CoalitionTelemetry.Log(World,
					$"Composition: {ownInfantry} infantry to {ownArmor} armor - screen is oversized, holding barracks");

			// Support promotion. Artillery and anti-air sit late in the priority list, so the vehicle
			// queue takes a tank every cycle and the coalition fields neither - measured waves had
			// zero of both. Artillery out-ranges base defence, which is the only cheap way through a
			// defended perimeter, and a column with no anti-air is free kills. When either is below
			// the ratio the doctrine wants, it goes to the front of the order.
			var ownArtillery = ownUnits.Count(a => ArtilleryTypes.Contains(a.Info.Name));

			// Count anything the config calls anti-air, including v2rl - which is both artillery and
			// AA in this mod. Excluding it left the counter permanently unsatisfied, so the vehicle
			// queue promoted V2s ahead of tanks every cycle and the army became fragile Light-armour
			// launchers instead of tanks. Measured: a decisive loss at 14650/37000.
			var ownAntiAir = ownUnits.Count(a => Info.AntiAirUnits.Contains(a.Info.Name));

			var supportFirst = new List<string>();
			if (Info.PromoteSupportUnits && ArmyComposition.ShouldProduceArtillery(ownArtillery, ownArmor))
				supportFirst.AddRange(Info.ArmyPriority.Where(ArtilleryTypes.Contains));

			// Anti-air is promoted once enemy air exists - or once the opponent model expects it.
			// Waiting for the first aircraft means waiting until it is overhead, and anti-air takes
			// time to build; an airfield sighted at four minutes says what is coming at six. The
			// model has to be both confident and pointing at air before it counts, so a nearly
			// uniform posterior changes nothing.
			planner ??= Player.PlayerActor.TraitsImplementing<BotModules.Coalition.CommanderPlanBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);

			var expectAir = enemyAirSpotted || (planner?.ExpectsEnemyAir ?? false);

			if (Info.PromoteSupportUnits && expectAir
				&& ArmyComposition.ShouldProduceAntiAir(ownAntiAir, ownArmor, true))
				supportFirst.AddRange(Info.AntiAirUnits.Where(u => !Info.InfantryUnitTypes.Contains(u)));

			if (supportFirst.Count > 0)
				pickOrder = supportFirst.Concat(pickOrder).ToList();

			// Cash pressure. The uniqueness rule below exists to diversify a wave, and that is the
			// right worry when money is tight. Measured, this commander earned 289,500 credits and
			// banked 213,413 of them - three quarters of everything it made - because one tank per
			// war factory per cycle cannot absorb the income of a base this size. Above the
			// threshold the queues may duplicate, so eight factories build eight tanks rather than
			// one tank and seven progressively worse things.
			var bankedCash = Player.PlayerActor.TraitOrDefault<PlayerResources>()?.GetCashAndResources() ?? 0;
			var spendFreely = bankedCash >= Info.SpendFreelyCashThreshold;

			// A queue that has just been given a harvester is not free for a tank as well: the order
			// is queued but CurrentItem does not update until it is processed.
			var availableQueues = idleQueues.Where(q => !servedQueues.Contains(q.Actor.ActorID)).ToList();

			var usedUnits = new HashSet<string>();
			foreach (var queue in availableQueues)
			{
				// An infantry queue with a full screen produces nothing rather than spending credits
				// that armour would convert into fighting power.
				if (screenFull && queue.Info.Type == "Infantry")
					continue;

				var unitName = pickOrder.FirstOrDefault(u =>
					!Info.ExcludeFromArmyTypes.Contains(u)
					&& (spendFreely || !usedUnits.Contains(u))
					&& queue.BuildableItems().Any(i => i.Name == u));
				if (unitName == null)
				{
					// An idle air or naval queue that can build nothing the plan wants is worth
					// recording: it distinguishes "the arm was never raised" from "the arm was never
					// buildable", which are very different diagnoses.
					if (queue.Info.Type is "Aircraft" or "Ship")
						CoalitionTelemetry.Log(World,
							$"Arm production: {queue.Info.Type} idle, nothing in the pick order it can build "
							+ $"(buildable: {string.Join(",", queue.BuildableItems().Select(i => i.Name))})");
					continue;
				}

				usedUnits.Add(unitName);
				Bot.QueueOrder(Order.StartProduction(queue.Actor, unitName, 1));
				if (queue.Info.Type is "Aircraft" or "Ship")
					CoalitionTelemetry.Log(World, $"Arm production: {queue.Info.Type} queued {unitName}");
			}

			// Order missing prerequisite buildings for the desired units that no queue can build yet.
			// The building is only ordered when this queue can build it right now (its own prerequisites
			// are met) and it is not already queued; otherwise the next pick gets a chance. Establish a
			// minimum field army first so early tech spending cannot leave the base open to a rush.
			if (!MayInvestInPrerequisite(OwnCombatUnits().Count(), Info.PrerequisiteArmyThreshold))
				return;

			foreach (var unitName in pickOrder)
			{
				if (Info.ExcludeFromArmyTypes.Contains(unitName) || usedUnits.Contains(unitName))
					continue;

				var missing = MissingPrerequisiteBuilding(unitName);
				if (missing == null)
					continue;

				var buildingQueue = queues.FirstOrDefault(q => q.Info.Type == "Building");
				var alreadyQueued = queues.Any(q => q.AllQueued().Any(i => i.Item == missing));
				if (buildingQueue == null || alreadyQueued || !buildingQueue.BuildableItems().Any(i => i.Name == missing))
					continue;

				Bot.QueueOrder(Order.StartProduction(buildingQueue.Actor, missing, 1));
				CoalitionTelemetry.Log(World, $"Prerequisite building ordered: {missing} (for {unitName})");
				break;
			}
		}

		/// <summary>True when the field army is large enough to divert production cash into technology.</summary>
		public static bool MayInvestInPrerequisite(int combatUnits, int minimumArmy)
		{
			return minimumArmy <= 0 || combatUnits >= minimumArmy;
		}

		/// <summary>
		/// What to build, computed from the mod's tables against the enemy actually observed.
		/// Returns an empty list when nothing is buildable, so the caller falls back to the lists.
		/// </summary>
		List<string> ComputedPickOrder()
		{
			var queues = Player.PlayerActor.TraitsImplementing<ProductionQueue>()
				.Where(q => q.Enabled && q.Info.Type != "Building" && q.Info.Type != "Defense")
				.ToArray();

			if (queues.Length == 0)
				return [];

			// Everything any army queue can build right now, deduplicated.
			var candidates = new Dictionary<string, UnitCombatProfile>(StringComparer.Ordinal);
			foreach (var queue in queues)
			{
				foreach (var item in queue.BuildableItems())
				{
					if (Info.ExcludeFromArmyTypes.Contains(item.Name) || candidates.ContainsKey(item.Name))
						continue;

					var profile = CounterMatrix.Profile(item, World.Map.Rules);
					if (profile != null && profile.IsArmed && profile.Cost > 0)
						candidates[item.Name] = profile;
				}
			}

			if (candidates.Count == 0)
				return [];

			// The enemy as observed, weighted by credits rather than headcount: one mammoth is a
			// bigger problem than one rifleman.
			var seen = sightings.Values
				.Where(v => !v.IsStructure)
				.Select(v => World.Map.Rules.Actors.TryGetValue(v.Type, out var ai)
					? (v.Type, ai.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0,
						ai.TraitInfoOrDefault<ArmorInfo>()?.Type ?? "None")
					: (v.Type, 0, "None"))
				.Where(t => t.Item2 > 0);

			var composition = ProductionValuation.CompositionOf(seen);

			// How badly the army is needed now. Early, and while outweighed, a unit that arrives
			// late is worth nothing however efficient it is on paper.
			var ownValue = OwnCombatUnits().Sum(a => a.Info.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0);
			var urgency = ownValue >= Info.EmergencyArmyValue
				? 0f
				: 1f - (ownValue / (float)Math.Max(1, Info.EmergencyArmyValue));

			// Measured combat performance is recorded, reported to the chief, and deliberately NOT
			// fed into this ranking. Three ways of doing so were implemented and measured, and all
			// three made the commander worse:
			//
			//   survival time per type                     worse; the longest-lived unit is usually
			//                                              the one that never fights
			//   credits destroyed per credit lost          0.88 -> 0.74; structurally flatters cheap
			//                                              units and tipped the army to infantry,
			//                                              which loses to any tank army here
			//   the same, normalised within unit class     0.88 -> 0.62
			//
			// The third was meant to remove the cost bias and made things worse still, which points
			// at the mechanism rather than the metric: the sample is produced by the very policy
			// being updated. Whatever the commander happens to build early gets the kills and the
			// losses, the ranking amplifies it, and the composition locks in regardless of merit.
			// That is a feedback loop wearing the costume of learning, and fixing it needs
			// deliberate exploration or off-policy correction rather than a better statistic.
			//
			// The data is not wasted: it is what the chief reads to tell demolition from attrition,
			// and it is the honest answer to "what trades well here" for a human looking at the log.
			return ProductionValuation.Rank(candidates.Values, composition, urgency)
				.Where(v => v.Score > 0f)
				.Select(v => v.Unit)
				.ToList();
		}

		/// <summary>Queues the best buildable item into each idle army queue.</summary>
		void ProduceFrom(List<string> order)
		{
			var used = new HashSet<string>(StringComparer.Ordinal);
			foreach (var queue in Player.PlayerActor.TraitsImplementing<ProductionQueue>()
				.Where(q => q.Enabled && q.CurrentItem() == null))
			{
				if (queue.Info.Type is "Building" or "Defense")
					continue;

				var pick = order.FirstOrDefault(u =>
					!used.Contains(u) && queue.BuildableItems().Any(i => i.Name == u));

				if (pick == null)
				{
					// An idle air or naval queue that can build nothing the valuation wants is worth
					// recording: it distinguishes "the arm was never raised" from "the arm was never
					// buildable", which are very different diagnoses. The list-based path logged
					// this and the computed path must too - a replacement that is less observable
					// than what it replaces trades one blind spot for another.
					if (queue.Info.Type is "Aircraft" or "Ship")
						CoalitionTelemetry.Log(World,
							$"Arm production: {queue.Info.Type} idle, nothing in the valuation it can build "
							+ $"(buildable: {string.Join(",", queue.BuildableItems().Select(i => i.Name))})");

					continue;
				}

				used.Add(pick);
				Bot.QueueOrder(Order.StartProduction(queue.Actor, pick, 1));

				if (queue.Info.Type is "Aircraft" or "Ship")
					CoalitionTelemetry.Log(World, $"Arm production: {queue.Info.Type} queued {pick}");
			}
		}

		/// <summary>Returns the first prerequisite building of a unit that the player has not yet built, or null.</summary>
		string MissingPrerequisiteBuilding(string unitName)
		{
			if (!World.Map.Rules.Actors.TryGetValue(unitName, out var unitInfo))
				return null;

			var buildable = unitInfo.TraitInfoOrDefault<BuildableInfo>();
			if (buildable == null)
				return null;

			foreach (var prerequisite in buildable.Prerequisites)
			{
				// Prerequisites may carry ! (invert) and ~ (hide) modifiers; faction and checkbox
				// prerequisites do not resolve to buildings and are skipped.
				var name = prerequisite.TrimStart('!', '~');
				if (!World.Map.Rules.Actors.TryGetValue(name, out var buildingInfo) || !buildingInfo.HasTraitInfo<BuildingInfo>())
					continue;

				var have = World.Actors.Any(a => !a.IsDead && a.IsInWorld && a.Owner == Player && a.Info.Name == name);
				if (!have)
					return name;
			}

			return null;
		}

		/// <summary>
		/// Wave tactics: damaged units retreat to the base to preserve force strength, the army defends
		/// the base against nearby threats, and launches attack waves at the scouted enemy. When no
		/// enemy has been seen for a while the army withdraws and regroups.
		/// </summary>
		void UpdateTactics()
		{
			var baseCenter = BaseCenter();
			if (baseCenter == null)
				return;

			var units = OwnCombatUnits().ToList();
			retreating.RemoveWhere(a => !a.IsInWorld || a.IsDead);
			completedUnserviceableRetreats.RemoveWhere(a => !a.IsInWorld || a.IsDead);
			var retreatCell = RetreatCell(baseCenter.Value);
			var holdCell = World.Map.CellContaining(MostValuableStructurePosition() ?? baseCenter.Value);

			// Micro-precision scales the retreat threshold: a precise bot pulls units earlier.
			var retreatThreshold = acceptableLossFraction > 0f
				? (int)Math.Clamp(50f - acceptableLossFraction * 40f, 10f, 50f)
				: Math.Min(Info.RetreatHealthPercent, Info.ResolvedDifficulty().RetreatHealthPercent());

			foreach (var a in units)
			{
				var health = a.TraitOrDefault<IHealth>();
				var fraction = health == null ? 100 : health.HP * 100 / health.MaxHP;
				if (fraction > Info.RegroupHealthPercent)
					completedUnserviceableRetreats.Remove(a);

				if (retreating.Contains(a))
				{
					// A retreated unit repairs when a compatible service facility exists. Actors that
					// cannot be repaired (most infantry) rejoin after reaching safety instead of being
					// trapped forever in a low-health retreat loop.
					var nearBase = (a.CenterPosition - baseCenter.Value).LengthSquared <= BaseRadiusSquared(10);
					if (fraction > Info.RegroupHealthPercent)
					{
						retreating.Remove(a);
						continue;
					}

					var repairable = a.TraitOrDefault<Repairable>();
					var repairBuilding = repairable?.FindRepairBuilding(a);
					if (repairBuilding != null)
					{
						var resupplying = !a.IsIdle && a.CurrentActivity.ActivitiesImplementing<Resupply>().Any();
						if (!resupplying)
							Bot.QueueOrder(new Order("Repair", a, Target.FromActor(repairBuilding), false));
						continue;
					}

					if (nearBase)
					{
						retreating.Remove(a);
						completedUnserviceableRetreats.Add(a);
						continue;
					}

					Bot.QueueOrder(new Order("Move", a, Target.FromCell(World, holdCell), false));
					continue;
				}

				if (fraction < retreatThreshold && !completedUnserviceableRetreats.Contains(a))
				{
					retreating.Add(a);
					var repairBuilding = a.TraitOrDefault<Repairable>()?.FindRepairBuilding(a);
					if (repairBuilding != null)
						Bot.QueueOrder(new Order("Repair", a, Target.FromActor(repairBuilding), false));
					else
						Bot.QueueOrder(new Order("Move", a, Target.FromCell(World, holdCell), false));
				}
			}

			var activeArmy = units.Where(a => !retreating.Contains(a)).ToArray();
			if (activeArmy.Length == 0)
				return;

			// Strategic reserve: missions commit only the available army (everything minus the held-back
			// reserve), unless the reserve is committed for a decisive push. Zero scouted enemies means
			// unknown (fog), not weak - the reserve is only committed against a scouted, outnumbered enemy.
			var mapCells = World.Map.MapSize.Width * World.Map.MapSize.Height;
			var exploredFraction = mapCells > 0 ? Player.Shroud.RevealedCells * 1f / mapCells : 0f;
			reserveCommitted = teamCommitReserve || MayCommitObservedAdvantage(enemyBaseEverLocated,
				exploredFraction, enemyArmyCount, OwnCombatUnits().Count(), Info.CommitReserveRatio);
			if (reserveCommitted != lastReserveCommitted)
			{
				lastReserveCommitted = reserveCommitted;
				if (reserveCommitted)
					CoalitionTelemetry.Log(World,
						$"Reserve committed: {OwnCombatUnits().Count()} of ours against {enemyArmyCount} scouted enemies");
			}

			var availableArmy = AvailableArmy(activeArmy);
			var reserveCount = activeArmy.Length - availableArmy.Length;

			// Base defense: intercept enemies approaching our structures. Defense is proportional to the
			// nearby threat (so a minor raid does not strip the whole army from its missions), and the
			// most valuable structure's vicinity is defended first.
			var defendedPos = MostValuableStructurePosition() ?? baseCenter.Value;
			var assetThreat = ClosestVisibleThreatToAsset(Info.AssetDefenseScanRadius);
			if (assetThreat != null)
				defendedPos = assetThreat.Value.Asset;
			var baseThreat = assetThreat?.Threat
				?? ClosestEnemyTo(defendedPos, BaseRadiusSquared(Info.BaseDefenseScanRadius));
			if (baseThreat != null)
			{
				SetPosture(Posture.Defend);
				lastDefendTick = World.WorldTick;

				// The counterattack objective is the best observed estimate of where the attackers
				// originated: a currently/previously observed enemy base center when available, otherwise
				// the contact cell. No hidden actor position is consulted.
				var counterOrigin = enemyBaseCenter ?? TacticalFormation.ProjectBeyondContact(baseThreat.Value,
					baseCenter.Value, Info.CounterPursuitCells * 1024);
				counterPos = World.Map.Clamp(World.Map.CellContaining(counterOrigin));
				enemyCountAtDefense = enemyArmyCount;

				var nearby = sightings.Values.Count(s =>
					(s.Position - defendedPos).LengthSquared <= BaseRadiusSquared(Info.BaseDefenseScanRadius));
				var commitment = TacticalEngagement.DefenseCommitment(nearby, activeArmy.Length,
					Info.MinWaveSize, Info.DefenseUnitsPerAttacker);
				var defenders = activeArmy.OrderBy(a => (a.CenterPosition - baseThreat.Value).LengthSquared)
					.Take(commitment).ToArray();
				if (defenders.Length > 0)
					ExecuteTacticalForce(defenders, baseThreat.Value);

				return;
			}

			// Anti-air umbrella: hold AA units over the base against enemy air.
			if (defenseKind == "aa" && enemyAirSpotted)
			{
				var aa = Claim(activeArmy.Where(a => Info.AntiAirUnits.Contains(a.Info.Name))).ToArray();
				if (aa.Length > 0)
					Bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(World, holdCell), false, groupedActors: aa));
			}

			// Naval screen: hold ships near the coast against an enemy navy.
			if (defenseKind == "naval")
			{
				var ships = Claim(activeArmy.Where(a => Info.NavalPriority.Contains(a.Info.Name))).ToArray();
				if (ships.Length > 0)
					Bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(World, holdCell), false, groupedActors: ships));
			}

			// Economy escort: keep a small guard on the harvesters and other economy units.
			if (defenseKind == "escort")
			{
				var harvester = World.Actors.FirstOrDefault(a => a.IsInWorld && !a.IsDead && a.Owner == Player
					&& !a.Info.HasTraitInfo<BuildingInfo>() && Info.ExcludeFromArmyTypes.Contains(a.Info.Name));
				if (harvester != null)
				{
					var guard = Claim(activeArmy).Take(3).ToArray();
					if (guard.Length > 0)
						Bot.QueueOrder(new Order("AttackMove", null, Target.FromActor(harvester), false, groupedActors: guard));
				}
			}

			// Reserve edge behavior: the uncommitted reserve intercepts raids on non-base assets
			// (harvesters, refineries, expansions) and defends allied bases, without stripping the
			// available army that is staged for missions. Enhanced (req 355): commit the reserve to
			// intercept when the enemy is attacking, logging the commitment with a reason.
			var reserve = activeArmy.Where(a => !availableArmy.Contains(a)).ToArray();
			var raidThreat = ClosestEnemyTo(baseCenter.Value, BaseRadiusSquared(Info.BaseDefenseScanRadius * 3));
			if (raidThreat != null && reserve.Length >= Info.MinWaveSize / 2 && World.WorldTick - lastDefendTick > Info.CounterDelayTicks)
			{
				SetPosture(Posture.Defend);
				var interceptors = Claim(reserve).ToArray();
				if (interceptors.Length > 0)
				{
					ReserveCounterattack(Bot, interceptors, raidThreat.Value);
					lastDefendTick = World.WorldTick;
				}
			}

			// Reserve exploit breakthrough (req 357): when a Breakthrough mission reaches the
			// Exploitation phase, commit the reserve to push through the breach.
			var ccModule = Player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);
			var sharedBB = ccModule?.Blackboard;
			if (attackTarget != null && ReserveManager.ShouldExploit(attackPhase)
				&& reserve.Length >= Info.MinWaveSize / 2)
			{
				var exploitReserve = Claim(reserve).ToArray();
				if (exploitReserve.Length > 0)
					ReserveExploitBreakthrough(Bot, exploitReserve, attackTarget.Value);
			}

			// Reserve protect expansion (req 359): when a new expansion is detected, assign a small
			// reserve force to guard it.
			if (expansionGuardTarget != null && reserve.Length >= Info.MinWaveSize / 2
				&& World.WorldTick - lastExpansionGuardTick >= Info.CoordinationInterval)
			{
				var expansionGuard = Claim(reserve).Take(Math.Min(reserve.Length, Info.MinWaveSize / 2)).ToArray();
				if (expansionGuard.Length > 0)
				{
					ReserveProtectExpansion(Bot, expansionGuard, expansionGuardTarget.Value);
					lastExpansionGuardTick = World.WorldTick;
				}
			}

			// Counterattack-after-defense: shortly after repelling an attack, strike back at the
			// attacker with the whole army - no coordinated gate, the enemy force is weakened.
			// Gate on the shared blackboard so only one bot fires the counterattack per window.
			var enemyNearOrigin = counterPos == null ? 0 : sightings.Values.Count(s => !s.IsStructure
				&& (s.Position - World.Map.CenterOfCell(counterPos.Value)).LengthSquared <= BaseRadiusSquared(15));
			var productionAtOrigin = counterPos != null && sightings.Values.Any(s => s.IsStructure
				&& s.Type is "weap" or "afld" or "hpad" or "barr" or "tent" or "spen" or "syrd" or "fact"
				&& (s.Position - World.Map.CenterOfCell(counterPos.Value)).LengthSquared <= BaseRadiusSquared(15));
			var counterDecision = CounterattackAssessment.Evaluate(activeArmy.Length, enemyCountAtDefense,
				enemyArmyCount, enemyNearOrigin, productionAtOrigin, Info.MinWaveSize);
			if (World.WorldTick - lastDefendTick <= Info.CounterDelayTicks && counterPos != null && counterDecision.ShouldLaunch
				&& (sharedBB == null || World.WorldTick - sharedBB.LastCounterattackTick >= Info.CounterDelayTicks / 2))
			{
				if (sharedBB != null)
					sharedBB.LastCounterattackTick = World.WorldTick;
				var counter = activeArmy.ToArray();
				if (counter.Length > 0)
				{
					ExecuteTacticalForce(counter, World.Map.CenterOfCell(counterPos.Value));
					CoalitionTelemetry.Log(World,
						$"Counterattack with {counter.Length} units toward estimated origin {counterPos.Value}: {counterDecision.Reason}");

					// Record counterattack and base defense response telemetry (reqs 620, 621).
					var ccModuleForCounter = Player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>()
						.FirstOrDefault(m => !m.IsTraitDisabled);
					if (ccModuleForCounter != null)
					{
						var enemyDestroyed = counterDecision.EnemyDepleted ? enemyCountAtDefense - enemyArmyCount : 0;
						ccModuleForCounter.RecordCounterattack(Math.Max(0, enemyDestroyed));
						ccModuleForCounter.RecordBaseDefenseResponse(lastDefendTick, World.WorldTick);
					}
				}

				return;
			}

			if (!teamRetreat && teamRetreatActive)
			{
				var survivors = World.Actors.Count(a => a.IsInWorld && !a.IsDead
					&& teamRetreatActorIds.Contains(a.ActorID));
				var ccModuleForRetreat = Player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>()
					.FirstOrDefault(m => !m.IsTraitDisabled);
				ccModuleForRetreat?.RecordRetreatOutcome(survivors);
				teamRetreatActorIds.Clear();
				teamRetreatActive = false;
			}

			// Team-wide retreat: pull the whole army back to the base. Issue and record the order once;
			// subsequent tactical ticks preserve the active withdrawal without inflating telemetry.
			if (teamRetreat)
			{
				if (!teamRetreatActive)
				{
					var retreaters = Claim(activeArmy).ToArray();
					if (retreaters.Length > 0)
					{
						Bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(World, retreatCell), false, groupedActors: retreaters));
						teamRetreatActorIds.UnionWith(retreaters.Select(a => a.ActorID));
						teamRetreatActive = true;
						var ccModuleForRetreat = Player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>()
							.FirstOrDefault(m => !m.IsTraitDisabled);
						ccModuleForRetreat?.RecordRetreat(World.WorldTick, retreaters.Length);
					}
				}

				return;
			}

			// Intercept a team-designated threat location (defend an allied base or a key position).
			if (counterTarget != null)
			{
				SetPosture(Posture.Defend);
				var interceptors = activeArmy.ToArray();
				if (interceptors.Length > 0)
					ExecuteTacticalForce(interceptors, World.Map.CenterOfCell(counterTarget.Value));
				return;
			}

			// Stealth/transport missions run independently of the main army.
			if (transportTarget != null && transportKind != null)
				ExecuteTransportMission();

			// Special operations: insert scarce assets at the designated target, on foot when no
			// transport is available. The asset is claimed so waves never take it.
			if (transportTarget != null && transportKind != null && specialOps != null)
				specialOps.Execute(transportTarget, transportKind, transportAvailable: World.Actors.Any(a =>
					a.IsInWorld && !a.IsDead && a.Owner == Player && Info.TransportTypes.Contains(a.Info.Name)));

			// Reconnaissance: probe the designated position with a small force to confirm what is there.
			if (reconTarget != null && issuedReconTarget != reconTarget)
			{
				missionScouts.RemoveWhere(a => !a.IsInWorld || a.IsDead);
				var recon = Claim(missionScouts).ToList();
				var needed = Math.Min(3 - recon.Count, 3 - missionScoutsDeployed);
				if (needed > 0)
				{
					var reinforcements = Claim(availableArmy.Where(a => !missionScouts.Contains(a))).Take(needed).ToArray();
					missionScouts.UnionWith(reinforcements);
					missionScoutsDeployed += reinforcements.Length;
					recon.AddRange(reinforcements);
				}

				if (recon.Count > 0)
				{
					Bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(World, reconTarget.Value), false, groupedActors: recon.ToArray()));
					issuedReconTarget = reconTarget;
					CoalitionTelemetry.Log(World, $"Recon probe of {recon.Count} units to {reconTarget.Value}");

					// Record recon telemetry (req 616). Useful intel is assumed true when the probe survives.
					var ccModuleForRecon = Player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>()
						.FirstOrDefault(m => !m.IsTraitDisabled);
					ccModuleForRecon?.RecordReconMission(true);
				}
			}

			// Bait: a small exposed force draws an over-responsive enemy; the counterattack that follows
			// their push (after our defense) turns into the ambush.
			if (baitTarget != null)
			{
				var bait = Claim(availableArmy).Take(3).ToArray();
				if (bait.Length > 0)
				{
					Bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(World, baitTarget.Value), false, groupedActors: bait));
					CoalitionTelemetry.Log(World, $"Bait placed: {bait.Length} units at {baitTarget.Value}");
				}
			}

			// Deception forces have a strict exposure window and withdraw early once their purpose is
			// served or their health falls below the regroup threshold. Fake buildups move to a safe
			// forward staging point and never issue an attack order.
			deceptionForce.RemoveWhere(a => !a.IsInWorld || a.IsDead);
			var deceptionDamaged = deceptionForce.Any(a =>
			{
				var health = a.TraitOrDefault<IHealth>();
				return health != null && health.HP * 100 / health.MaxHP < Info.RegroupHealthPercent;
			});
			if (deceptionForce.Count > 0
				&& (deceptionDamaged || World.WorldTick - feintTick >= Info.TacticInterval * 2))
			{
				var withdrawing = Claim(deceptionForce).ToArray();
				if (withdrawing.Length > 0)
					Bot.QueueOrder(new Order("Move", null, Target.FromCell(World, retreatCell), false,
						groupedActors: withdrawing));
				deceptionForce.Clear();
				CoalitionTelemetry.Log(World, $"Deception force withdrew early ({(deceptionDamaged ? "loss limit" : "purpose complete")})");
			}

			var feintCommitment = FeintCommitment(availableArmy.Length, Info.FeintFraction);
			if (feintTarget != null && deceptionForce.Count == 0 && feintCommitment > 0
				&& World.WorldTick - feintTick > Info.TacticInterval * 5)
			{
				feintTick = World.WorldTick;
				var feint = Claim(availableArmy).Take(feintCommitment).ToArray();
				if (feint.Length > 0)
				{
					deceptionForce.UnionWith(feint);
					var destination = feintTarget.Value;
					if (deceptionKind == "fakebuildup")
					{
						var baseCell = World.Map.CellContaining(baseCenter.Value);
						destination = baseCell + (destination - baseCell) / 2;
					}

					var order = deceptionKind == "fakebuildup" ? "Move" : "AttackMove";
					Bot.QueueOrder(new Order(order, null, Target.FromCell(World, destination), false, groupedActors: feint));
					CoalitionTelemetry.Log(World, $"{deceptionKind ?? "feint"} of {feint.Length} units to {destination}");

					// Record the feint launch (req 627) so the commander can later measure whether it
					// opened a launch window for the main wave.
					var ccModuleForFeint = Player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>()
						.FirstOrDefault(m => !m.IsTraitDisabled);
					if (ccModuleForFeint != null)
					{
						ccModuleForFeint.RecordFeintLaunch();
						ccModuleForFeint.MarkFeintLaunch(feintTick);
					}
				}
			}

			// Air/naval strike: send only that domain at a high-value target, exempt from the ground gate.
			if (strikeTarget != null)
			{
				var strikeUnits = Claim(activeArmy.Where(a => strikeKind == "air"
					? Info.AirUnitTypes.Contains(a.Info.Name)
					: strikeKind == "naval" ? Info.NavalPriority.Contains(a.Info.Name)
					: Info.AirUnitTypes.Contains(a.Info.Name) || Info.NavalPriority.Contains(a.Info.Name))).ToArray();
				if (strikeUnits.Length > 0)
				{
					Bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(World, strikeTarget.Value), false, groupedActors: strikeUnits));
					CoalitionTelemetry.Log(World, $"Strike of {strikeUnits.Length} units to {strikeTarget.Value}");
				}
			}

			// Support-power strike: fire the first ready superweapon at the designated target.
			if (supportPowerTarget != null && World.WorldTick >= supportPowerTick && FireSupportPower(supportPowerTarget.Value))
			{
				supportPowerTarget = null;
			}

			// Cohesion: a scattered army regroups before launching, so it does not attack as isolated
			// units. The force's cohesion comes from the shared blackboard (spread around its center).
			var commanderCenter = Player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);
			var ownCohesion = commanderCenter?.Blackboard?.Forces
				.FirstOrDefault(f => f.Owner == Player.InternalName)?.Cohesion ?? 1f;
			if (ownCohesion < Info.RegroupCohesionThreshold)
			{
				var regroupers = Claim(activeArmy).ToArray();
				if (regroupers.Length > 0)
				{
					Bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(World, holdCell), false, groupedActors: regroupers));
					CoalitionTelemetry.Log(World, $"Army regrouping: cohesion {ownCohesion:0.00} below {Info.RegroupCohesionThreshold}");
				}

				return;
			}

			// Coordinated attack gate: waves only launch once the coalition fields a large, mixed
			// force. Land is the essential arm; air and naval are layered on when available. Requiring
			// air in particular blocked attacks on maps where air production is not prioritized, which
			// left the coalition sitting on defense to be worn down.
			// How much force this attack actually needs, rather than a fixed number that is
			// meaningless against an unknown defender (handbook §15.2). Under the square law the
			// requirement scales with the defender, and asking for enough to win with half the force
			// surviving is what separates taking ground from trading evenly on it.
			var configuredMinimum = (int)Info.ScaleDifficulty(Info.CoordinatedAttackMinimum);
			var observedDefence = ObservedDefenceStrength();
			var coordinatedMinimum = observedDefence > 0f
				? Math.Max(configuredMinimum, (int)Math.Ceiling(
					LanchesterModel.RequiredStrength(observedDefence, desiredSurvivingFraction: 0.5f)))
				: configuredMinimum;

			var coordinated = coalitionArmy >= coordinatedMinimum
				&& (!Info.CoordinatedAttackMixedArms || (coalitionLand > 0
					&& (!coalitionHasWater || coalitionNaval > 0)));
			if (!coordinated)
			{
				var gate = $"coalition {coalitionArmy}/{coordinatedMinimum} ready " +
					$"(air {coalitionAir}, naval {coalitionNaval}, land {coalitionLand}, " +
					$"water {(coalitionHasWater ? "yes" : "no")})";
				if (gate != lastCoordGate)
				{
					lastCoordGate = gate;
					CoalitionTelemetry.Log(World, $"Coordinated force: {gate}");
				}
			}

			// Without a decisive force or hostile intent, hold the available army near the base.
			// The attack tick is the coalition-wide launch window (time-on-target): every allied bot
			// launches in the same tick range so the waves arrive together.
			if (posture != Posture.Attack || availableArmy.Length < Info.MinWaveSize || !coordinated || World.WorldTick < attackTick)
			{
				if (World.WorldTick - lastAttackTick > Info.WithdrawDelayTicks)
				{
					var holders = Claim(availableArmy).ToArray();
					if (holders.Length > 0)
						Bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(World, holdCell), false, groupedActors: holders));
				}

				return;
			}

			// Launch or sustain an attack wave from the available army: the team-designated target takes
			// priority over the locally scouted enemy, so all allied bots push the same position together.
			// Each domain controller executes its own component (land/air/naval) so the wave is
			// coordinated without a single blob order and each domain can later refine its behavior.
			var target = attackTarget != null ? World.Map.CenterOfCell(attackTarget.Value) : BestAttackTarget();
			if (target == null)
				return;
			if (!MayIssueWave(World.WorldTick, lastWaveTick, Info.AttackPostureTicks))
				return;

			lastAttackTick = World.WorldTick;
			lastWaveTick = World.WorldTick;
			var priorClaims = claimedUnits.Count;
			if (pincerTarget != null)
			{
				var secondAxis = Claim(availableArmy.Where(a => !Info.AirUnitTypes.Contains(a.Info.Name)
					&& !Info.NavalPriority.Contains(a.Info.Name)))
					.Take(Math.Max(1, availableArmy.Length / 3)).ToArray();
				if (secondAxis.Length > 0)
				{
					Bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(World, pincerTarget.Value), false,
						groupedActors: secondAxis));
					CoalitionTelemetry.Log(World, $"Pincer second axis of {secondAxis.Length} units to {pincerTarget.Value}");
				}
			}

			ExecuteTacticalForce(availableArmy, target.Value);

			// Mark the wave launch so the opponent model can measure the enemy's response time,
			// and record how much enemy contact this raid generated (raid-sensitivity signal).
			var commander = Player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);
			if (commander != null)
			{
				commander.MarkWaveLaunch(World.WorldTick);

				// Record whether this engagement is fought with local numerical superiority (req 613),
				// and whether a recent feint opened this launch window (req 627).
				var bb = commander.Blackboard;
				if (bb != null)
					commander.RecordEngagement(bb.CoalitionArmyStrength >= bb.EnemyArmyStrength * 1.5f);
				if (commander.LastFeintTick >= 0 && World.WorldTick - commander.LastFeintTick <= Info.TacticInterval * 5)
					commander.RecordFeintOpenedWindow();
				if (attackTarget != null)
				{
					var raidCell = attackTarget.Value;
					var team = commander.TeamPlayers();
					var enemiesNearRaid = World.Actors.Count(a =>
						a.IsInWorld && !a.IsDead && a.Owner != Player && a.OccupiesSpace != null
						&& Player.RelationshipWith(a.Owner) == PlayerRelationship.Enemy
						&& team.Any(ally => ally.Shroud.IsVisible(a.CenterPosition))
						&& (a.CenterPosition - World.Map.CenterOfCell(raidCell)).LengthSquared <= BaseRadiusSquared(20));
					commander.RecordRaidContact(enemiesNearRaid);
				}
			}

			// Count only the units the domain controllers claimed for this wave (prior claims from
			// recon/bait/feint are excluded).
			var wave = claimedUnits.Skip(priorClaims).ToArray();
			if (wave.Length >= Info.MinWaveSize)
			{
				var composition = ComposeWave(wave);
				CoalitionTelemetry.Log(World,
					$"Wave of {wave.Length} units launched (reserve {reserveCount} held back) at ToT {attackTick} " +
					$"(sync error {World.WorldTick - attackTick}t) {composition}");

				// Name the combined-arms properties the wave actually has, so the doctrine rules are
				// observable rather than implied by a unit count.
				CoalitionTelemetry.Log(World,
					$"Wave doctrine: combined={composition.IsCombinedArms} armor+infantry={composition.ArmorHasInfantrySupport} " +
					$"artillery-screened={composition.ArtilleryHasScreen} aa-escort={composition.GroundHasAntiAirEscort} " +
					$"air-support={composition.GroundHasAirSupport} naval-support={composition.GroundHasNavalSupport} " +
					$"special-support={composition.GroundHasSpecialSupport} mass-air={composition.IsMassAirAttack}");
			}
		}

		/// <summary>Classifies a launched wave into its combined-arms composition (reqs 198, 228-233).</summary>
		WaveComposition ComposeWave(Actor[] wave)
		{
			var air = wave.Count(a => Info.AirUnitTypes.Contains(a.Info.Name));
			var naval = wave.Count(a => Info.NavalPriority.Contains(a.Info.Name));
			var artillery = wave.Count(a => ArtilleryTypes.Contains(a.Info.Name));
			var antiAir = wave.Count(a => !Info.AirUnitTypes.Contains(a.Info.Name)
				&& !Info.NavalPriority.Contains(a.Info.Name)
				&& !ArtilleryTypes.Contains(a.Info.Name)
				&& Info.AntiAirUnits.Contains(a.Info.Name));
			var special = wave.Count(a => Info.SpecialTypes.Contains(a.Info.Name));
			var infantry = wave.Count(a => Info.InfantryUnitTypes.Contains(a.Info.Name)
				&& !Info.SpecialTypes.Contains(a.Info.Name)
				&& !Info.AntiAirUnits.Contains(a.Info.Name));
			var armor = wave.Length - air - naval - artillery - antiAir - special - infantry;

			return new WaveComposition(armor, infantry, artillery, antiAir, air, naval, special);
		}

		static readonly System.Collections.Generic.HashSet<string> ArtilleryTypes = ["v2rl", "arty"];

		/// <summary>
		/// Aggregate strength of the enemy the coalition can currently see, in the same units as its
		/// own army count. Fair fog: only observed intel contributes, so an unscouted enemy reads as
		/// weak - which is why reconnaissance gates the whole offensive doctrine.
		/// </summary>
		float ObservedDefenceStrength()
		{
			var commander = Player?.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);

			var blackboard = commander?.Blackboard;
			if (blackboard == null)
				return 0f;

			return blackboard.EnemyIntel
				.Where(i => i.Class != UnitClass.Structure)
				.Sum(CombatEstimator.IntelPower);
		}

		/// <summary>Returns the configured feint commitment, or zero when the force/config is unsafe.</summary>
		public static int FeintCommitment(int availableUnits, int fraction)
		{
			return fraction > 0 && availableUnits > fraction ? Math.Max(1, availableUnits / fraction) : 0;
		}

		/// <summary>Debounces a persistent attack plan while still allowing its first wave immediately.</summary>
		public static bool MayIssueWave(int currentTick, int lastWaveTick, int interval)
		{
			return lastWaveTick <= 0 || currentTick - lastWaveTick >= Math.Max(1, interval);
		}

		/// <summary>Executes one objective through weapon-domain controllers without cross-domain order conflicts.</summary>
		void ExecuteTacticalForce(Actor[] force, WPos target)
		{
			if (force.Any(a => !Info.AirUnitTypes.Contains(a.Info.Name) && !Info.NavalPriority.Contains(a.Info.Name)))
				ground?.Attack(force, target);
			if (force.Any(a => Info.AirUnitTypes.Contains(a.Info.Name)))
				air?.Attack(force, target);
			if (coalitionHasWater && force.Any(a => Info.NavalPriority.Contains(a.Info.Name)))
				naval?.Attack(force, target);
		}

		/// <summary>Claims units for a mission: returns the unclaimed subset and marks them as ordered this tick.</summary>
		internal IEnumerable<Actor> Claim(IEnumerable<Actor> units)
		{
			return units.Where(claimedUnits.Add).ToArray();
		}

		/// <summary>Returns the available army: everything except the held-back strategic reserve, unless it is committed.</summary>
		Actor[] AvailableArmy(IEnumerable<Actor> army)
		{
			var list = army as Actor[] ?? army.ToArray();
			var reserveFraction = reserveFractionOverride > 0 ? reserveFractionOverride : Info.ScaledReserveFraction();
			if (reserveCommitted || list.Length < reserveFraction)
				return list;

			return list.Take(list.Length - list.Length / reserveFraction).ToArray();
		}

		/// <summary>Overrides the reserve fraction from an LLM directive (0 = revert to difficulty-scaled default).</summary>
		internal void OverrideReserveFraction(int fraction)
		{
			reserveFractionOverride = Math.Clamp(fraction, 0, 10);
		}

		/// <summary>
		/// Directs the reserve to stop counterattacks by intercepting enemy attackers (req 355).
		/// Called when the enemy is attacking and the reserve can intercept.
		/// </summary>
		void ReserveCounterattack(IBot bot, Actor[] reserve, WPos raidThreat)
		{
			if (reserve.Length < Info.MinWaveSize / 2)
				return;

			reserveManager.Commit(World.WorldTick, reserve.Length, "counterattack interception", World, Info.MinWaveSize);
			bot.QueueOrder(new Order("AttackMove", null, Target.FromPos(raidThreat), false, groupedActors: reserve));
		}

		/// <summary>
		/// Directs the reserve to reinforce a failing front (req 356). When an ally is under attack,
		/// send the reserve (not the available army) to reinforce their position.
		/// </summary>
		void ReserveReinforceFront(IBot bot, Actor[] reserve, CPos allyUnderAttack)
		{
			if (reserve.Length < Info.MinWaveSize / 2)
				return;

			reserveManager.Commit(World.WorldTick, reserve.Length, "reinforce failing front", World, Info.MinWaveSize);
			bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(World, allyUnderAttack), false, groupedActors: reserve));
		}

		/// <summary>
		/// Directs the reserve to exploit a breakthrough (req 357). When a Breakthrough mission
		/// reaches the Exploitation phase, commit the reserve to push through the breach.
		/// </summary>
		void ReserveExploitBreakthrough(IBot bot, Actor[] reserve, CPos breakthroughTarget)
		{
			if (reserve.Length < Info.MinWaveSize / 2)
				return;

			reserveManager.Commit(World.WorldTick, reserve.Length, "exploit breakthrough", World, Info.MinWaveSize);
			bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(World, breakthroughTarget), false, groupedActors: reserve));
		}

		/// <summary>
		/// Directs a small reserve force to protect a new expansion (req 359). Called when a new
		/// expansion is detected via the MCV expansion manager.
		/// </summary>
		void ReserveProtectExpansion(IBot bot, Actor[] reserve, CPos expansionLocation)
		{
			var guardForce = reserve.Take(Math.Min(reserve.Length, Info.MinWaveSize / 2)).ToArray();
			if (guardForce.Length == 0)
				return;

			reserveManager.Commit(World.WorldTick, guardForce.Length, "protect expansion", World, Info.MinWaveSize);
			bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(World, expansionLocation), false, groupedActors: guardForce));
		}

		/// <summary>
		/// Recon scouts: a bounded number of cheap infantry walk into different unexplored regions far
		/// from the base. Completed or dead scouts leave the active set so reconnaissance cannot consume
		/// an ever-growing share of the field army.
		/// </summary>
		void UpdateScouting()
		{
			if (Info.ScoutUnitTypes.Length == 0)
				return;

			// A scout target was previously retired the moment a scout was *dispatched* toward it. If
			// that scout died on the way - the normal outcome when probing a defended base - the cell
			// stayed unexplored and permanently excluded, so the coalition stopped scouting after a
			// handful of probes and never located the enemy base for the rest of the match. A target
			// is only genuinely finished when it has actually been explored; a lost scout means the
			// information was never obtained, so the target goes back into the pool.
			foreach (var lost in scouts.Where(a => !a.IsInWorld || a.IsDead).ToArray())
			{
				if (scoutAssignments.TryGetValue(lost, out var target))
				{
					if (!Player.Shroud.IsExplored(target))
						attemptedScoutTargets.Remove(target);

					scoutAssignments.Remove(lost);
				}
			}

			foreach (var finished in scouts.Where(a => a.IsInWorld && !a.IsDead && a.IsIdle).ToArray())
				scoutAssignments.Remove(finished);

			scouts.RemoveWhere(a => !a.IsInWorld || a.IsDead || a.IsIdle);
			var active = scouts.Count;
			// Is reconnaissance still buying anything? Ground revealed is the only honest measure of
			// that: a probe that dies having shown us nothing new has cost a unit and returned
			// nothing, and enough of those in a row means there is nothing further to reach.
			var revealed = Player.Shroud.RevealedCells;
			if (revealed > lastRevealedCells)
			{
				lastRevealedCells = revealed;
				barrenScoutCycles = 0;
			}
			else
				barrenScoutCycles++;

			var productive = barrenScoutCycles < Info.BarrenScoutCycles;

			if (!ShouldScout(enemyBaseEverLocated, active, Info.ScoutSquadSize, scoutsDeployed,
				Info.ScoutLifetimeBudget, productive))
				return;

			var baseCenter = BaseCenter();
			if (baseCenter == null)
				return;

			var toSend = Math.Min(Info.ScoutSendPerInterval, Info.ScoutSquadSize - active);
			if (toSend <= 0)
				return;

			// Draw from every owned unit rather than the combat pool: the dedicated scout is
			// deliberately excluded from the army, so looking only at combat units would never find
			// one. Ordered by the configured preference so dogs go first while any remain.
			var infantry = World.Actors
				.Where(a => a.IsInWorld && !a.IsDead && a.Owner == Player
					&& Info.ScoutUnitTypes.Contains(a.Info.Name) && !scouts.Contains(a)
					&& a.TraitOrDefault<Mobile>() != null)
				.OrderBy(a => Array.IndexOf(Info.ScoutUnitTypes, a.Info.Name))
				.ThenBy(a => a.ActorID)
				.ToArray();
			if (infantry.Length == 0)
				return;

			var targets = ScoutTargets(baseCenter.Value, toSend, infantry[0]);
			for (var i = 0; i < Math.Min(toSend, Math.Min(infantry.Length, targets.Length)); i++)
			{
				var scout = infantry[i];
				if (!claimedUnits.Add(scout))
					continue;

				scouts.Add(scout);
				attemptedScoutTargets.Add(targets[i]);
				scoutAssignments[scout] = targets[i];
				scoutsDeployed++;
				Bot.QueueOrder(new Order("Move", scout, Target.FromCell(World, targets[i]), false));
				CoalitionTelemetry.Log(World, $"Scout sent to {targets[i]} (shadow far from base)");
			}
		}


		/// <summary>
		/// <para>
		/// Serves production the engine's own modules asked for - harvester replacement and MCV
		/// expansion - before any discretionary army production is considered.
		/// </para>
		/// <para>
		/// This runs BEFORE the idle-queue check, and that is the entire point. The check returns out
		/// of the whole production routine when every queue is busy, which in this commander is
		/// essentially always: there is always another tank worth making. The economy's requests
		/// therefore sat behind a gate that never opened.
		/// </para>
		/// <para>
		/// What made it permanent rather than merely slow: the harvester module will not re-request
		/// while one request is still outstanding, so the single request it makes on the first tick -
		/// before any war factory exists to build it - was never fulfilled, never cleared, and
		/// blocked every later request for the rest of the match. Measured over a full fair-economy
		/// game, exactly zero harvesters were ever produced; the fleet consisted solely of the free
		/// harvester that arrives with each refinery, giving precisely one harvester per refinery
		/// while the scripted opponents ran ten to fifteen and out-earned this commander two to one.
		/// </para>
		/// </summary>
		/// <returns>Actor ids of the queues that were given something, so army production skips them.</returns>
		HashSet<uint> ServeRequestedProduction(PlayerResources resources)
		{
			var served = new HashSet<uint>();
			savingFor = null;
			var availableQueues = queues.Where(q => q.CurrentItem() == null).ToList();

			foreach (var requested in requestedProduction.ToArray())
			{
				// Keep a newly issued request pending until the next bot tick makes the production
				// order visible. This closes the same-tick request/order race with harvester and MCV
				// managers and prevents duplicate requests on parallel factories.
				if (queues.Any(q => q.AllQueued().Any(i => i.Item == requested)))
				{
					requestedProduction.Remove(requested);
					continue;
				}

				if (!World.Map.Rules.Actors.TryGetValue(requested, out var requestedInfo))
				{
					requestedProduction.Remove(requested);
					continue;
				}

				// Enough to START it, not enough to buy it outright. An OpenRA production queue
				// draws cash progressively as the item builds - the base builder queues a whole
				// structure on five hundred credits for exactly this reason - so demanding the full
				// price in hand before queueing tests something the economy never has to satisfy.
				//
				// That single wrong test cost the commander its entire economy. A harvester prices
				// at 1100; army production empties the account every cycle; so the balance never
				// once reached 1100, the request made on the first tick was never fulfilled, and
				// because the harvester module will not re-request while one is outstanding, no
				// harvester was ever built for the whole match. The fleet consisted solely of the
				// free harvester granted with each refinery - exactly one per refinery, two of them
				// - against opponents running ten to fifteen, who out-earned us better than two to
				// one and won.
				var requestedCost = requestedInfo.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0;
				if (resources.GetCashAndResources() < Math.Min(requestedCost, Info.MinProductionCash))
				{
					// Cannot afford it yet, so stop spending on anything discretionary until we can.
					//
					// Without this the commander never buys a harvester at all, and the mechanism is
					// a straightforward death spiral rather than a preference: a harvester costs
					// 1100, army production drains the account to nothing every cycle, so the
					// balance never once reaches 1100 and the request stands pending for the whole
					// match. Measured in a fair-economy game, cash sat between 1 and 300 credits
					// while the fleet stayed at two harvesters and the opponent, running ten to
					// fifteen, out-earned us better than two to one.
					//
					// A harvester is the input to army production. Buying tanks in front of it is
					// eating the seed corn.
					savingFor = requested;
					return served;
				}
				if (Info.ExpansionUnitTypes.Contains(requested)
					&& !MayInvestInPrerequisite(OwnCombatUnits().Count(), Info.ExpansionArmyThreshold))
					continue;

				var queue = availableQueues.FirstOrDefault(q => q.BuildableItems().Any(i => i.Name == requested));

				// These requests are meant to come BEFORE discretionary army production, and
				// restricting them to an idle queue defeats exactly that. Only the vehicle queue can
				// build a harvester or an MCV, and in this commander that queue is essentially never
				// idle - it always has another tank to make. Measured over a whole fair-economy
				// match, the harvester fleet therefore never grew past the free harvester that
				// arrives with each refinery: exactly one harvester per refinery, five against two,
				// while every scripted opponent ran ten to fifteen and out-earned us more than two
				// to one.
				//
				// A harvester queued behind one tank is worth vastly more than a harvester that is
				// never built. The backlog cap keeps it from being buried behind a whole wave.
				queue ??= queues.FirstOrDefault(q =>
					q.BuildableItems().Any(i => i.Name == requested)
					&& q.AllQueued().Count() <= Info.RequestedProductionMaximumBacklog);

				if (queue == null)
					continue;

				Bot.QueueOrder(Order.StartProduction(queue.Actor, requested, 1));
				availableQueues.Remove(queue);
				served.Add(queue.Actor.ActorID);
				CoalitionTelemetry.Log(World, $"Requested production ordered: {requested}");
			}

			return served;
		}

		/// <summary>Picks unexplored cells at least ScoutMinDistance from the base, spread across the map.</summary>
		CPos[] ScoutTargets(WPos baseCenter, int count, Actor scout)
		{
			var mobile = scout.TraitOrDefault<Mobile>();
			if (mobile == null)
				return [];

			var minDistanceSq = (long)WDist.FromCells(Info.ScoutMinDistance).Length;
			minDistanceSq *= minDistanceSq;
			var stride = Math.Max(4, World.Map.MapSize.Width / 16);
			var targets = new List<CPos>();
			var baseCell = World.Map.CellContaining(baseCenter);

			// Starting locations are public map metadata, not hidden player state. Check the most
			// distant viable candidates first so bounded reconnaissance reaches likely enemy bases
			// instead of spending all of its probes on arbitrary map corners. Aim at a fixed approach
			// cell rather than the spawn center, which is normally occupied by a hidden construction
			// yard and must not affect fair-fog target selection.
			var spawnCandidates = World.Map.ActorDefinitions
				.Where(n => n.Value.Value == "mpspawn")
				.Select(n => new ActorReference(n.Key, n.Value).GetValue<LocationInit, CPos>())
				.Select(spawn => SpawnApproachCell(spawn, baseCell, Info.ScoutSpawnApproachOffset))
				.Where(cpos => !Player.Shroud.IsExplored(cpos)
					&& !attemptedScoutTargets.Contains(cpos)
					&& (World.Map.CenterOfCell(cpos) - baseCenter).LengthSquared >= minDistanceSq
					&& mobile.CanEnterCell(cpos, scout, BlockedByActor.Immovable))
				.OrderByDescending(cpos => (World.Map.CenterOfCell(cpos) - baseCenter).LengthSquared)
				.ThenBy(cpos => cpos.Y)
				.ThenBy(cpos => cpos.X);

			// Spawn approaches first - they are where a base most likely is - then a radial sweep to
			// the map edge for everything else. A scout aimed at a nearby point stops there; one
			// aimed at the far edge walks the whole way and reveals the entire line, so the same
			// 100-credit rifleman buys several times the map knowledge. Bearings are interleaved so
			// early losses still leave the sweep spread around the compass.
			var radial = RadialScoutPattern.UnexploredSweep(
				World.Map.CellContaining(baseCenter),
				World.Map.MapSize.Width, World.Map.MapSize.Height,
				isExplored: c => Player.Shroud.IsExplored(c) || attemptedScoutTargets.Contains(c),
				isReachable: c => mobile.CanEnterCell(c, scout, BlockedByActor.Immovable));

			// Order of evidence, strongest first.
			//
			// The belief field used to pre-empt both of the sweeps below: it returned a single cell
			// and this method returned immediately with it. That was wrong twice over. It capped
			// every dispatch at one scout no matter how many were asked for, and it let a diffuse
			// "somewhere stale" score outrank a starting location, which is the one place on the map
			// an enemy base is actually known to be likely. Measured on shattered-mountain, the
			// enemy sat at (18,18) and our base at (111,78); scouts were dispatched to (116,126) and
			// (108,126) - the near corner - and across a whole match not one of the opponent's
			// thirty-four buildings was ever so much as explored, let alone seen.
			//
			// Spawn approaches are ordered by DESCENDING distance from our own base, so the far
			// corner is probed first rather than last. The belief field follows: it earns its place
			// once the obvious candidates are exhausted, since it is the only source that learns.
			var believed = BeliefScoutTargets(scout, mobile, count);

			foreach (var cpos in spawnCandidates.Concat(believed).Concat(radial))
			{
				if (!mobile.PathFinder.PathExistsForLocomotor(mobile.Locomotor, scout.Location, cpos))
					continue;

				targets.Add(cpos);
				if (targets.Count >= count)
					return targets.ToArray();
			}

			var index = 0;
			var candidates = World.Map.AllCells.Where(cpos =>
				++index % stride == 0
					&& !Player.Shroud.IsExplored(cpos)
					&& !attemptedScoutTargets.Contains(cpos)
					&& !targets.Contains(cpos)
					&& (World.Map.CenterOfCell(cpos) - baseCenter).LengthSquared >= minDistanceSq
					&& mobile.CanEnterCell(cpos, scout, BlockedByActor.Immovable))

				// Each deployment maximizes separation from prior targets. This produces distinct
				// reconnaissance axes instead of feeding every scout into the same far corner.
				.OrderByDescending(cpos => ScoutSeparationScore(cpos, attemptedScoutTargets,
					World.Map.CellContaining(baseCenter)))
				.ThenByDescending(cpos => (World.Map.CenterOfCell(cpos) - baseCenter).LengthSquared)
				.ThenBy(cpos => cpos.Y)
				.ThenBy(cpos => cpos.X);

			foreach (var cpos in candidates)
			{
				if (!mobile.PathFinder.PathExistsForLocomotor(mobile.Locomotor, scout.Location, cpos))
					continue;

				targets.Add(cpos);
				if (targets.Count >= count)
					break;
			}

			return targets.ToArray();
		}

		/// <summary>Squared distance to the nearest prior target, or the base for the first scout.</summary>
		public static int ScoutSeparationScore(CPos candidate, IReadOnlyCollection<CPos> attemptedTargets, CPos baseCell)
		{
			return attemptedTargets.Count == 0
				? (candidate - baseCell).LengthSquared
				: attemptedTargets.Min(target => (candidate - target).LengthSquared);
		}

		/// <summary>Returns a deterministic home-facing approach cell outside a normally occupied spawn center.</summary>
		/// <summary>
		/// The cells the commander's belief state most wants looked at, reachable and not already
		/// probed, best first. A sequence rather than a single cell: every scout already dispatched
		/// has put its destination in attemptedScoutTargets, so one answer is unusable after the
		/// first probe.
		/// </summary>
		IEnumerable<CPos> BeliefScoutTargets(Actor scout, Mobile mobile, int count)
		{
			planner ??= Player.PlayerActor.TraitsImplementing<BotModules.Coalition.CommanderPlanBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);

			if (planner == null)
				yield break;

			foreach (var cell in planner.ScoutTargets(Math.Max(count, 12)))
			{
				if (attemptedScoutTargets.Contains(cell))
					continue;

				if (!mobile.CanEnterCell(cell, scout, BlockedByActor.Immovable))
					continue;

				yield return cell;
			}
		}

		BotModules.Coalition.CommanderPlanBotModule planner;

		public static CPos SpawnApproachCell(CPos spawn, CPos home, int offset)
		{
			var distance = Math.Max(0, offset);
			return new CPos(spawn.X + Math.Sign(home.X - spawn.X) * distance,
				spawn.Y + Math.Sign(home.Y - spawn.Y) * distance);
		}

		/// <summary>
		/// <para>Recon is bounded and stops once the coalition has located an enemy base.</para>
		/// <para>
		/// The concurrent cap and the lifetime budget are separate numbers, and conflating them was a
		/// serious bug: <c>scoutsDeployed</c> counts every scout ever dispatched, so comparing it to
		/// the squad size meant the coalition stopped scouting permanently after four probes - dead
		/// or alive, successful or not. Scouts probing a defended base usually die, so the enemy base
		/// was never located, no offensive objective could be named, and the coalition spent the rest
		/// of the match reacting. The concurrent cap still stops reconnaissance eating the field army;
		/// the lifetime budget is what decides whether the search can continue at all.
		/// </para>
		/// </summary>
		public static bool ShouldScout(bool enemyBaseLocated, int activeScouts, int maximumScouts,
			int scoutsDeployed = 0, int lifetimeBudget = 0, bool searchIsProductive = true)
		{
			if (enemyBaseLocated || maximumScouts <= 0 || activeScouts >= maximumScouts)
				return false;

			// A search that has stopped revealing ground has run out of ground it can reach, and no
			// number of further probes will change that. This is the case a pure scout-count budget
			// cannot distinguish: on land maps a larger allowance is a decisive gain - against the
			// turtle bot the commander went from destroying nothing and losing twenty-five buildings
			// to destroying sixteen and losing four - while on water maps the same allowance was a
			// rout, thirty-three structures destroyed falling to two, because production is diverted
			// to reconnaissance for as long as the enemy is unlocated and a land scout cannot cross
			// water however long it is given. Whether the last several probes revealed anything new
			// separates the two directly, where counting corpses does not.
			if (!searchIsProductive)
				return false;

			// A zero or unset budget keeps the historical behaviour for callers that do not set one.
			//
			// The budget is a real bound and has to stay one. Removing it entirely was measured: on
			// land maps it was a clear gain - against the turtle bot the commander went from losing
			// twenty-five buildings and destroying none to destroying ten and losing none - but on
			// water maps it was a rout, from thirty-one structures destroyed to zero, because
			// production is diverted to scouts for as long as the enemy is unlocated and a search
			// that cannot reach the enemy never ends. What the budget must not do is expire in the
			// opening minutes of a long match, which at forty scouts it did: probes went out every
			// four seconds from 232s and the allowance was gone by 388s, leaving eight hundred
			// seconds in which the commander could not look for an opponent it had not yet found.
			var budget = lifetimeBudget > 0 ? lifetimeBudget : maximumScouts;
			return scoutsDeployed < budget;
		}

		/// <summary>Observed force advantage is trusted for an all-in only after broad reconnaissance.</summary>
		public static bool MayCommitObservedAdvantage(bool enemyBaseLocated, float exploredFraction,
			int enemyArmy, int ownArmy, float commitRatio)
		{
			return enemyBaseLocated && exploredFraction >= 0.7f && enemyArmy > 0
				&& enemyArmy <= ownArmy * commitRatio;
		}

		/// <summary>
		/// Executes a transport mission through the transport controller's state machine. The
		/// controller claims the payload so the main army does not order it elsewhere during the
		/// insertion, and clears the target when the mission completes or aborts.
		/// </summary>
		void ExecuteTransportMission()
		{
			var active = transport.Execute(transportTarget, transportKind, World.WorldTick);
			if (!active)
			{
				var ccModuleForTransport = Player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>()
					.FirstOrDefault(m => !m.IsTraitDisabled);

				if (transport.Aborted)
				{
					CoalitionTelemetry.Log(World, "Transport mission aborted during transit");

					// Record transport telemetry (req 617): aborted = not survived.
					ccModuleForTransport?.RecordTransport(false);
				}
				else
				{
					var transportActor = World.Actors.FirstOrDefault(a => a.IsInWorld && !a.IsDead && a.Owner == Player
						&& Info.TransportTypes.Contains(a.Info.Name));
					var health = transportActor?.TraitOrDefault<IHealth>();
					var percent = health == null ? 100 : health.HP * 100 / health.MaxHP;
					CoalitionTelemetry.Log(World, $"Transport mission completed; transport survived at {percent}% health");

					// Record transport telemetry (req 617): survived.
					ccModuleForTransport?.RecordTransport(true);
				}

				transportTarget = null;
				transportKind = null;
			}
		}

		/// <summary>
		/// Team coordination: reinforces allied bases that are under attack and lets ready allied bots
		/// launch their attack waves in the same time window for a synchronized push.
		/// </summary>
		void UpdateCoordination()
		{
			var army = OwnCombatUnits().Where(a => !retreating.Contains(a)).ToArray();
			if (army.Length < Info.MinWaveSize)
				return;

			var allies = World.Players.Where(p =>
				p != Player &&
				p.PlayerActor.TraitsImplementing<ModularBot>().Any(b => b.IsEnabled) &&
				Player.RelationshipWith(p) == PlayerRelationship.Ally).ToArray();

			if (allies.Length == 0)
				return;

			// Reinforce an ally whose base is under attack. Only unclaimed, non-reserve units are sent,
			// so reinforcement never strips units the arbiter committed to a wave, feint, or defense.
			var available = AvailableArmy(army);
			foreach (var ally in allies)
			{
				var allyBase = ally.PlayerActor.TraitsImplementing<StrategicBrainBotModule>()
					.Select(m => m.BaseCenter())
					.FirstOrDefault(center => center != null);
				if (allyBase == null)
					continue;

				if (ClosestEnemyTo(allyBase.Value, BaseRadiusSquared(Info.AllyReinforceScanRadius)) == null)
					continue;

				// Per-ally cooldown: only one bot reinforces a given ally per coordination interval,
				// so the whole coalition doesn't send duplicate waves to the same defender.
				var lastSent = lastReinforceTick.GetValueOrDefault(ally.InternalName);
				if (World.WorldTick - lastSent < Info.CoordinationInterval)
					continue;

				var reinforcements = Claim(available).Take(Math.Max(1, available.Length / Info.ReinforcementFraction)).ToArray();
				if (reinforcements.Length > 0)
				{
					lastReinforceTick[ally.InternalName] = World.WorldTick;
					Bot.QueueOrder(new Order("AttackMove", null, Target.FromPos(allyBase.Value), false, groupedActors: reinforcements));
				}

				// Reserve reinforce failing front (req 356): when an ally is under attack, also send
				// the reserve (not just the available army) to reinforce their position.
				var reserveForFront = army.Where(a => !available.Contains(a) && claimedUnits.Add(a)).ToArray();
				if (reserveForFront.Length >= Info.MinWaveSize / 2)
					ReserveReinforceFront(Bot, reserveForFront, World.Map.CellContaining(allyBase.Value));
			}
		}

		/// <summary>Returns the average position of the bot's structures, or null if it has none.</summary>
		internal WPos? BaseCenter()
		{
			var structures = OwnStructures().ToArray();
			return structures.Length == 0 ? null : structures.Select(a => a.CenterPosition).Average();
		}

		/// <summary>
		/// Plans the threat-weighted route a transport should follow to its target: the intermediate
		/// region centers along the stealth route, so the transport avoids AA, detection, and exposed
		/// ground instead of flying/driving straight through. Empty when no route or blackboard exists.
		/// </summary>
		public CPos[] PlanTransportRoute(CPos target)
		{
			var commander = Player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);
			var blackboard = commander?.Blackboard;
			var baseCenter = BaseCenter();
			if (blackboard == null || baseCenter == null)
				return [];

			var from = blackboard.RegionOf(World.Map.CellContaining(baseCenter.Value)).Index;
			var to = blackboard.RegionOf(target).Index;
			var route = CoalitionRoutePlanner.FindRoute(blackboard.MapAnalysis, blackboard.ThreatField(),
				from, to, MovementClass.Ground, RouteWeights.Stealth());
			if (!route.Found || route.Regions.Length <= 1)
				return [];

			return route.Regions.Skip(1).Select(r =>
			{
				var bounds = blackboard.Regions[r].Bounds;
				return new CPos((bounds.Left + bounds.Right) / 2, (bounds.Top + bounds.Bottom) / 2);
			}).ToArray();
		}

		IEnumerable<Actor> OwnStructures()
		{
			return World.Actors.Where(a =>
				a.IsInWorld && !a.IsDead && a.Owner == Player && a.Info.HasTraitInfo<BuildingInfo>());
		}

		/// <summary>The position of the most strategically valuable own structure, defended first.</summary>
		WPos? MostValuableStructurePosition()
		{
			Actor best = null;
			var bestValue = float.MinValue;
			foreach (var a in OwnStructures())
			{
				var value = TargetEvaluator.EconomicValue(a.Info.Name)
					+ TargetEvaluator.ProductionValue(a.Info.Name)
					+ TargetEvaluator.TechnologyValue(a.Info.Name);
				if (value > bestValue)
				{
					bestValue = value;
					best = a;
				}
			}

			return best?.CenterPosition;
		}

		IEnumerable<Actor> OwnCombatUnits()
		{
			return World.Actors.Where(a =>
				a.IsInWorld && !a.IsDead && a.Owner == Player && a.OccupiesSpace != null && !a.Info.HasTraitInfo<BuildingInfo>() &&
				!Info.ExcludeFromArmyTypes.Contains(a.Info.Name));
		}

		/// <summary>Returns the closest remembered enemy position within the given squared radius, or null.</summary>
		WPos? ClosestEnemyTo(WPos pos, long radiusSquared)
		{
			WPos? closest = null;
			var closestDistance = long.MaxValue;
			foreach (var sighting in sightings.Values)
			{
				var distance = (sighting.Position - pos).LengthSquared;
				if (distance > radiusSquared || distance >= closestDistance)
					continue;

				closest = sighting.Position;
				closestDistance = distance;
			}

			return closest;
		}

		/// <summary>
		/// Finds a currently observable enemy threatening any own structure, harvester, or MCV.
		/// Unlike strategic sightings this intentionally ignores stale memory: tactical defense must
		/// not chase an actor after it disappears under fog.
		/// </summary>
		(WPos Threat, WPos Asset)? ClosestVisibleThreatToAsset(int radiusCells)
		{
			var assets = World.Actors.Where(a => a.IsInWorld && !a.IsDead && a.Owner == Player
				&& (a.Info.HasTraitInfo<BuildingInfo>() || a.Info.Name is "harv" or "mcv")).ToArray();
			if (assets.Length == 0)
				return null;

			var radiusSquared = BaseRadiusSquared(radiusCells);
			(WPos Threat, WPos Asset)? best = null;
			var bestDistance = long.MaxValue;
			foreach (var enemy in World.Actors)
			{
				if (!enemy.IsInWorld || enemy.IsDead || enemy.OccupiesSpace == null
					|| Player.RelationshipWith(enemy.Owner) != PlayerRelationship.Enemy
					|| !Player.Shroud.IsVisible(enemy.CenterPosition) || !enemy.CanBeViewedByPlayer(Player))
					continue;

				foreach (var asset in assets)
				{
					var distance = (enemy.CenterPosition - asset.CenterPosition).LengthSquared;
					if (distance > radiusSquared || distance >= bestDistance)
						continue;

					best = (enemy.CenterPosition, asset.CenterPosition);
					bestDistance = distance;
				}
			}

			return best;
		}

		/// <summary>
		/// The planned retreat fallback: the center of the safest ground-reachable region (lowest total
		/// threat), so retreats route away from danger instead of blindly running to the base. Falls
		/// back to the base center when the coalition blackboard is unavailable.
		/// </summary>
		CPos RetreatCell(WPos baseCenter)
		{
			var commander = Player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);
			var blackboard = commander?.Blackboard;
			if (blackboard == null || blackboard.HomeRegion < 0)
				return World.Map.CellContaining(baseCenter);

			var home = blackboard.HomeRegion;
			var best = home;
			var bestThreat = float.MaxValue;
			foreach (var region in blackboard.Regions)
			{
				if (blackboard.MapAnalysis.ComponentOf(MovementClass.Ground, region.Index)
					!= blackboard.MapAnalysis.ComponentOf(MovementClass.Ground, home))
					continue;

				var threat = region.Threats.Sum();
				if (threat < bestThreat)
				{
					bestThreat = threat;
					best = region.Index;
				}
			}

			var bounds = blackboard.Regions[best].Bounds;
			return new CPos((bounds.Left + bounds.Right) / 2, (bounds.Top + bounds.Bottom) / 2);
		}

		/// <summary>
		/// Fires the first ready support power at the target. The power has its own cooldown, so an
		/// unready power is simply skipped; the commander re-requests the strike each review.
		/// </summary>
		bool FireSupportPower(CPos target)
		{
			var manager = Player.PlayerActor.TraitOrDefault<SupportPowerManager>();
			if (manager == null)
				return false;

			// Friendly-fire avoidance: superweapons have a blast radius, so a target crowded with
			// friendly units is withheld rather than risking them.
			var targetPos = World.Map.CenterOfCell(target);
			var friendlyNear = World.Actors.Count(a => a.IsInWorld && !a.IsDead && a.Owner == Player && a.OccupiesSpace != null
				&& (a.CenterPosition - targetPos).LengthSquared <= BaseRadiusSquared(SupportPowerFriendlyFireRadius));
			var targetValue = sightings.Values.Where(s =>
				(s.Position - targetPos).LengthSquared <= BaseRadiusSquared(8))
				.Sum(s => s.IsStructure ? 1f + TargetEvaluator.EconomicValue(s.Type)
					+ TargetEvaluator.ProductionValue(s.Type) + TargetEvaluator.TechnologyValue(s.Type) : 1f);

			foreach (var kv in manager.Powers)
			{
				if (!kv.Value.Ready)
					continue;
				var role = SupportPowerPolicy.Classify(kv.Key);
				if (!SupportPowerPolicy.ShouldFire(role, targetValue, friendlyNear, shapingWindowOpen: true))
					continue;

				Bot.QueueOrder(new Order(kv.Key, manager.Self, Target.FromCell(World, target), false));
				CoalitionTelemetry.Log(World, $"Support power {kv.Key} ({role}) fired at {target} during shaping window");
				return true;
			}

			CoalitionTelemetry.Log(World,
				$"Support power withheld at {target}: no ready supported power met value/safety threshold (value {targetValue:0.0}, friendly {friendlyNear})");
			return false;
		}

		/// <summary>
		/// Selects the attack wave target: the enemy base if known, otherwise the newest sighting.
		/// </summary>
		WPos? BestAttackTarget()
		{
			if (enemyBaseCenter != null)
				return enemyBaseCenter;

			WPos? newest = null;
			var newestTick = int.MinValue;
			foreach (var kv in sightings)
			{
				if (kv.Value.Tick > newestTick)
				{
					newest = kv.Value.Position;
					newestTick = kv.Value.Tick;
				}
			}

			return newest;
		}

		static long BaseRadiusSquared(int cells)
		{
			var length = WDist.FromCells(cells).Length;
			return (long)length * length;
		}

		/// <summary>Blast radius (cells) used to judge whether a support-power target risks friendly units.</summary>
		public const int SupportPowerFriendlyFireRadius = 15;

		/// <summary>Friendly units within the blast radius at or above this count withhold the power.</summary>
		public const int SupportPowerFriendlyFireThreshold = 3;

		/// <summary>True when a support power should be withheld to avoid friendly fire.</summary>
		public static bool ShouldWithholdSupportPower(int friendlyUnitsNearTarget)
		{
			return friendlyUnitsNearTarget >= SupportPowerFriendlyFireThreshold;
		}
	}
}
