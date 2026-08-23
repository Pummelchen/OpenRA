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

		[Desc("Cash below which production orders are withheld.")]
		public readonly int MinProductionCash = 400;

		[Desc("Preferred unit production order. Earlier entries are produced first when buildable.")]
		public readonly string[] ArmyPriority = [];

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
		readonly HashSet<Actor> missionScouts = [];
		readonly HashSet<CPos> attemptedScoutTargets = [];
		int scoutsDeployed;
		int missionScoutsDeployed;
		readonly HashSet<Actor> deceptionForce = [];
		readonly List<string> requestedProduction = [];
		readonly HashSet<uint> teamRetreatActorIds = [];
		bool teamRetreatActive;
		bool enemyBaseEverLocated;

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
				requestedProduction.Add(requestedActor);
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

			// Build the adaptive pick order: team produce boosts and role priorities first, then
			// counters for the scouted enemy composition, then the base army composition.
			var pickOrder = new List<string>();
			if (produceBoost != null)
				pickOrder.AddRange(produceBoost);
			if (teamRole == "naval" && coalitionHasWater)
				pickOrder.AddRange(Info.NavalPriority);
			if (enemyAirSpotted)
				pickOrder.AddRange(Info.AntiAirUnits);
			if (enemyArmorSpotted)
				pickOrder.AddRange(Info.AntiArmorUnits);
			if (enemyInfantrySpotted)
				pickOrder.AddRange(Info.AntiInfantryUnits);
			pickOrder.AddRange(Info.ArmyPriority);

			// Log production-priority changes so the build plan is auditable in telemetry.
			var pickOrderStr = string.Join(",", pickOrder.Take(8));
			if (pickOrderStr != lastPickOrder)
			{
				lastPickOrder = pickOrderStr;
				CoalitionTelemetry.Log(World, $"Production priorities: {pickOrderStr}");
			}

			// While defending, replace a queued unit only when it no longer appears among the most
			// urgent counters. Cancellation refunds its cost and prevents a stale long build from
			// blocking an immediately needed response.
			if (posture == Posture.Defend)
			{
				var priorities = pickOrder.Take(5).ToArray();
				foreach (var q in queues)
				{
					var current = q.CurrentItem();
					if (current != null && !priorities.Contains(current.Item))
						Bot.QueueOrder(Order.CancelProduction(q.Actor, current.Item, 1));
				}
			}

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

			// Honor engine module requests (harvester replacement, MCV expansion) before discretionary
			// army production. These requests use the same prerequisite/cash-valid production queues and
			// cannot create units directly.
			var availableQueues = idleQueues.ToList();
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

				var requestedCost = requestedInfo.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0;
				if (resources.GetCashAndResources() < requestedCost)
					continue;
				if (Info.ExpansionUnitTypes.Contains(requested)
					&& !MayInvestInPrerequisite(OwnCombatUnits().Count(), Info.ExpansionArmyThreshold))
					continue;

				var queue = availableQueues.FirstOrDefault(q => q.BuildableItems().Any(i => i.Name == requested));
				if (queue == null)
					continue;

				Bot.QueueOrder(Order.StartProduction(queue.Actor, requested, 1));
				availableQueues.Remove(queue);
				CoalitionTelemetry.Log(World, $"Requested production ordered: {requested}");
			}

			// Produce on every remaining idle queue in parallel: each queue takes the highest-priority unit it can
			// build, so the air, naval, and land arms all get produced instead of the first pick
			// monopolizing production.
			var usedUnits = new HashSet<string>();
			foreach (var queue in availableQueues)
			{
				var unitName = pickOrder.FirstOrDefault(u =>
					!Info.ExcludeFromArmyTypes.Contains(u) && !usedUnits.Contains(u) && queue.BuildableItems().Any(i => i.Name == u));
				if (unitName == null)
					continue;

				usedUnits.Add(unitName);
				Bot.QueueOrder(Order.StartProduction(queue.Actor, unitName, 1));
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
					CoalitionTelemetry.Log(World, $"Reserve committed: coalition outnumbers the scouted enemy ({enemyArmyCount} vs {OwnCombatUnits().Count()})");
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
			var coordinatedMinimum = (int)Info.ScaleDifficulty(Info.CoordinatedAttackMinimum);
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

			scouts.RemoveWhere(a => !a.IsInWorld || a.IsDead || a.IsIdle);
			var active = scouts.Count;
			if (!ShouldScout(enemyBaseEverLocated, active, Info.ScoutSquadSize, scoutsDeployed))
				return;

			var baseCenter = BaseCenter();
			if (baseCenter == null)
				return;

			var toSend = Math.Min(Info.ScoutSendPerInterval, Info.ScoutSquadSize - active);
			if (toSend <= 0)
				return;

			var infantry = OwnCombatUnits().Where(a => Info.ScoutUnitTypes.Contains(a.Info.Name)
				&& !scouts.Contains(a)).ToArray();
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
				scoutsDeployed++;
				Bot.QueueOrder(new Order("Move", scout, Target.FromCell(World, targets[i]), false));
				CoalitionTelemetry.Log(World, $"Scout sent to {targets[i]} (shadow far from base)");
			}
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
			foreach (var cpos in spawnCandidates)
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
		public static CPos SpawnApproachCell(CPos spawn, CPos home, int offset)
		{
			var distance = Math.Max(0, offset);
			return new CPos(spawn.X + Math.Sign(home.X - spawn.X) * distance,
				spawn.Y + Math.Sign(home.Y - spawn.Y) * distance);
		}

		/// <summary>Recon is bounded and stops once the coalition has located an enemy base.</summary>
		public static bool ShouldScout(bool enemyBaseLocated, int activeScouts, int maximumScouts, int scoutsDeployed = 0)
		{
			return !enemyBaseLocated && maximumScouts > 0 && activeScouts < maximumScouts
				&& scoutsDeployed < maximumScouts;
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
