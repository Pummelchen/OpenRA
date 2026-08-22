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
		public readonly FrozenSet<string> ArmyPriority = [];

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
		public readonly FrozenSet<string> ScoutUnitTypes = [];

		[Desc("How many scouts (total) are kept walking into unexplored territory.")]
		public readonly int ScoutSquadSize = 25;

		[Desc("Scouts are sent to unexplored cells at least this many cells from the base.")]
		public readonly int ScoutMinDistance = 40;

		[Desc("Interval (in ticks) between scout deployments.")]
		public readonly int ScoutInterval = 100;

		[Desc("How many new scouts are deployed per interval.")]
		public readonly int ScoutSendPerInterval = 3;

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

		[Desc("Number of combat units required before the bot adopts an attack posture.")]
		public readonly int AttackForceThreshold = 14;

		[Desc("Units retreat individually when their health drops below this percentage.")]
		public readonly int RetreatHealthPercent = 30;

		[Desc("A retreating unit rejoins the army once its health recovers above this percentage.")]
		public readonly int RegroupHealthPercent = 60;

		[Desc("Below this force cohesion the army holds position to regroup instead of launching a wave.")]
		public readonly float RegroupCohesionThreshold = 0.3f;

		[Desc("Enemy units within this many cells of the base center trigger base defense.")]
		public readonly int BaseDefenseScanRadius = 25;

		[Desc("The army is not committed to a wave while it is smaller than this many units.")]
		public readonly int MinWaveSize = 6;

		[Desc("The army is withdrawn back to the base when no enemy sighting was refreshed within this many ticks.")]
		public readonly int WithdrawDelayTicks = 300;

		[Desc("Interval (in ticks) between team coordination updates.")]
		public readonly int CoordinationInterval = 100;

		[Desc("How long (in ticks) after repelling a base attack the counterattack window stays open.")]
		public readonly int CounterDelayTicks = 400;

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

	public sealed class StrategicBrainBotModule : ConditionalTrait<StrategicBrainBotModuleInfo>, IBotTick, IBotRespondToAttack
	{
		enum Posture { BuildArmy, Attack, Defend }

		sealed class Sighting
		{
			public WPos Position;
			public int Tick;
			public string Type;
			public bool IsStructure;
		}

		readonly StrategicBrainBotModuleInfo info;
		// Enemy memory deliberately contains snapshots, never live actors. A live Actor can reveal
		// a mobile enemy's current position after it has moved back under fog.
		readonly Dictionary<uint, Sighting> sightings = [];
		readonly HashSet<Actor> retreating = [];
		readonly HashSet<Actor> claimedUnits = [];
		readonly HashSet<Actor> scouts = [];

		// Exposed to the tactical controllers.
		internal World World => world;
		internal Player Player => player;
		internal IBot Bot => bot;
		internal StrategicBrainBotModuleInfo Info => info;

		// Per-domain tactical controllers: each executes its own component of a mission (land/air/
		// naval waves, transports, special insertions) and claims its own units through the arbiter.
		GroundController ground;
		AirController air;
		NavalController naval;
		TransportController transport;
		SpecialOpsController specialOps;

		World world;
		Player player;
		IBot bot;
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

		// LLM reserve override: when >0, replaces the difficulty-scaled reserve fraction.
		int reserveFractionOverride;

		// Reserve manager: tracks reserve commitments and their reasons for telemetry (reqs 355-360).
		readonly ReserveManager reserveManager = new();

		// Coalition force summary, fed by the command center through the team plan.
		int coalitionArmy;
		int coalitionAir;
		int coalitionNaval;
		int coalitionLand;
		bool coalitionHasWater;
		int attackTick;

		// Team plan state, fed by the external model brain.
		string teamStrategy;
		string teamRole;
		CPos? attackTarget;
		CPos? feintTarget;
		CPos? reconTarget;
		CPos? baitTarget;
		CPos? counterTarget;
		CPos? transportTarget;
		CPos? strikeTarget;
		CPos? supportPowerTarget;
		string transportKind;
		string defenseKind;
		string[] produceBoost;
		bool teamRetreat;
		int feintTick;

		static readonly System.Text.Json.JsonSerializerOptions PlanOptions = new() { PropertyNameCaseInsensitive = true };

		public StrategicBrainBotModule(StrategicBrainBotModuleInfo info, ActorInitializer init)
			: base(info)
		{
			this.info = info;
			world = init.World;
			ground = new GroundController(this);
			air = new AirController(this);
			naval = new NavalController(this);
			transport = new TransportController(this);
			specialOps = new SpecialOpsController(this);
		}

		void IBotRespondToAttack.RespondToAttack(IBot bot, Actor self, AttackInfo e)
		{
			if (player == null || e.Attacker == null || e.Attacker.IsDead || !e.Attacker.IsInWorld)
				return;

			if (player.RelationshipWith(e.Attacker.Owner) != PlayerRelationship.Enemy)
				return;

			// An enemy attacked one of our actors: raise the alarm and prepare to defend, and record
			// how quickly the enemy reacted to our last wave (response-time sample for the model).
			lastAttackTick = world.WorldTick;
			SetPosture(Posture.Defend);

			var commander = player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);
			commander?.RecordEnemyResponse(world.WorldTick);
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (IsTraitDisabled)
				return;

			this.bot = bot;
			player = bot.Player;
			world = player.World;

			queues = player.PlayerActor.TraitsImplementing<ProductionQueue>()
				.Where(q => q.Enabled)
				.ToArray();

			// Optional economic bonus: a per-tick cash injection scaled by the difficulty axis.
			// 0 (the default) is a strictly fair game with no hidden income.
			var economicBonus = info.ResolvedDifficulty().EconomicBonus;
			if (economicBonus > 0 && world.WorldTick % 100 == 0)
			{
				var resources = player.PlayerActor.TraitOrDefault<PlayerResources>();
				resources?.GiveCash(50 * economicBonus);
			}

			var tick = world.WorldTick;

			// The order arbiter: each mission claims its units in priority order, so no unit receives
			// conflicting orders from several missions in the same tick.
			claimedUnits.Clear();

			if (tick % info.IntelUpdateInterval == 0)
				UpdateIntel(tick);

			if (tick % info.BuildPlanInterval == 0)
				UpdateBuildPlan();

			// Scouts are claimed before tactics so they are never pulled into waves or feints.
			if (tick % info.ScoutInterval == 0)
				UpdateScouting();

			if (tick % info.TacticInterval == 0)
				UpdateTactics();

			if (tick % info.CoordinationInterval == 0)
				UpdateCoordination();
		}

		/// <summary>
		/// Refreshes enemy intelligence. The bot records only currently visible enemy actors and forgets
		/// sightings after a limited time. The scouted force is classified to drive adaptive production
		/// and posture.
		/// </summary>
		void UpdateIntel(int tick)
		{
			var stale = sightings.Where(kv => tick - kv.Value.Tick > info.SightingMemoryTicks).Select(kv => kv.Key).ToArray();
			foreach (var key in stale)
				sightings.Remove(key);

			foreach (var a in world.Actors)
			{
				if (a.IsDead || !a.IsInWorld || a.Owner == player)
					continue;

				if (player.RelationshipWith(a.Owner) != PlayerRelationship.Enemy)
					continue;

				// Player actors have no position and cannot be sighted.
				if (a.OccupiesSpace == null)
					continue;

				// Fog-respecting awareness: explored terrain is not current observation.
				if (!player.Shroud.IsVisible(a.CenterPosition))
					continue;

				sightings[a.ActorID] = new Sighting
				{
					Position = a.CenterPosition,
					Tick = tick,
					Type = a.Info.Name,
					IsStructure = a.Info.HasTraitInfo<BuildingInfo>()
				};
			}

			bool IsArmy(Sighting s) => !s.IsStructure && !info.ExcludeFromArmyTypes.Contains(s.Type);

			enemyArmyCount = sightings.Values.Count(IsArmy);
			enemyAirSpotted = sightings.Values.Any(s => info.AirUnitTypes.Contains(s.Type));
			enemyArmorSpotted = sightings.Values.Any(s => info.ArmorUnitTypes.Contains(s.Type));
			enemyInfantrySpotted = sightings.Values.Any(s => info.InfantryUnitTypes.Contains(s.Type));

			var composition = $"armor={enemyArmorSpotted} air={enemyAirSpotted} infantry={enemyInfantrySpotted}";
			if (composition != lastComposition)
			{
				lastComposition = composition;
				CoalitionTelemetry.Log(world, $"Enemy composition: {composition} (army {enemyArmyCount})");
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
				if (teamRetreat || teamRole == "defend" || teamStrategy == "defend" || teamStrategy == "turtle")
					SetPosture(Posture.Defend);
				else if (teamRole == "main" || teamRole == "escort" || teamStrategy == "attack")
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

			var tick = world.WorldTick;
			if (tick - lastAttackTick < info.AttackPostureTicks / 2)
			{
				SetPosture(Posture.Defend);
				return;
			}

			if (enemyArmyCount > ownArmyCount * info.DefendArmyRatio)
			{
				SetPosture(Posture.Defend);
				return;
			}

			if (ownArmyCount >= info.AttackForceThreshold && enemyBaseCenter != null)
			{
				SetPosture(Posture.Attack);
				return;
			}

			SetPosture(Posture.BuildArmy);
		}

		void SetPosture(Posture newPosture)
		{
			posture = newPosture;
		}

		sealed class TeamPlan
		{
			public string Strategy { get; set; }
			public TeamTarget Attack { get; set; }
			public TeamTarget Feint { get; set; }
			public TeamTarget Recon { get; set; }
			public TeamTarget Bait { get; set; }
			public TeamTarget Counter { get; set; }
			public TeamTarget Transport { get; set; }
			public TeamTarget Strike { get; set; }
			public TeamTarget SupportPower { get; set; }
			public string TransportKind { get; set; }
			public Dictionary<string, string> Roles { get; set; }
			public string[] Produce { get; set; }
			public bool Retreat { get; set; }
			public TeamForce Force { get; set; }
			public int AttackTick { get; set; }
			public string DefenseKind { get; set; }
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

			teamStrategy = plan.Strategy;
			teamRetreat = plan.Retreat;
			produceBoost = plan.Produce;
			attackTarget = ClampCell(ToCell(plan.Attack));
			feintTarget = ClampCell(ToCell(plan.Feint));
			reconTarget = ClampCell(ToCell(plan.Recon));
			baitTarget = ClampCell(ToCell(plan.Bait));
			counterTarget = ClampCell(ToCell(plan.Counter));
			transportTarget = ClampCell(ToCell(plan.Transport));
			transportKind = plan.TransportKind;
			strikeTarget = ClampCell(ToCell(plan.Strike));
			supportPowerTarget = ClampCell(ToCell(plan.SupportPower));
			defenseKind = plan.DefenseKind;
			teamRole = plan.Roles != null && plan.Roles.TryGetValue(player.InternalName, out var role) ? role : null;
			attackTick = plan.AttackTick;
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
			return cell == null ? null : world.Map.Clamp(cell.Value);
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
			if (player == null)
				return;

			var resources = player.PlayerActor.TraitOrDefault<PlayerResources>();
			if (resources == null || resources.GetCashAndResources() < info.MinProductionCash)
				return;

			// Build the adaptive pick order: team produce boosts and role priorities first, then
			// counters for the scouted enemy composition, then the base army composition.
			var pickOrder = new List<string>();
			if (produceBoost != null)
				pickOrder.AddRange(produceBoost);
			if (teamRole == "naval" && coalitionHasWater)
				pickOrder.AddRange(info.NavalPriority);
			if (enemyAirSpotted)
				pickOrder.AddRange(info.AntiAirUnits);
			if (enemyArmorSpotted)
				pickOrder.AddRange(info.AntiArmorUnits);
			if (enemyInfantrySpotted)
				pickOrder.AddRange(info.AntiInfantryUnits);
			pickOrder.AddRange(info.ArmyPriority);

			// Log production-priority changes so the build plan is auditable in telemetry.
			var pickOrderStr = string.Join(",", pickOrder.Take(8));
			if (pickOrderStr != lastPickOrder)
			{
				lastPickOrder = pickOrderStr;
				CoalitionTelemetry.Log(world, $"Production priorities: {pickOrderStr}");
			}

			// Aggressive cancellation: while defending, cancel whatever is in production if it is not
			// one of the top-priority units, so the counter force comes out fast.
			if (posture == Posture.Defend)
			{
				var priorities = pickOrder.Take(5).ToArray();
				foreach (var q in queues)
				{
					var current = q.CurrentItem();
					if (current != null && !priorities.Contains(current.Item))
						bot.QueueOrder(Order.CancelProduction(q.Actor, current.Item, 1));
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
					foreach (var building in criticalBuildings)
					{
						var exists = world.Actors.Any(a => a.IsInWorld && !a.IsDead && a.Owner == player && a.Info.Name == building);
						var alreadyQueued = queues.Any(q => q.AllQueued().Any(i => i.Item == building));
						if (!exists && !alreadyQueued && buildingQueue.BuildableItems().Any(i => i.Name == building))
						{
							bot.QueueOrder(Order.StartProduction(buildingQueue.Actor, building, 1));
							CoalitionTelemetry.Log(world, $"Emergency replacement: {building} ordered");
							return;
						}
					}
				}
			}

			// Produce on every idle queue in parallel: each queue takes the highest-priority unit it can
			// build, so the air, naval, and land arms all get produced instead of the first pick
			// monopolizing production.
			var usedUnits = new HashSet<string>();
			foreach (var queue in idleQueues)
			{
				var unitName = pickOrder.FirstOrDefault(u =>
					!info.ExcludeFromArmyTypes.Contains(u) && !usedUnits.Contains(u) && queue.BuildableItems().Any(i => i.Name == u));
				if (unitName == null)
					continue;

				usedUnits.Add(unitName);
				bot.QueueOrder(Order.StartProduction(queue.Actor, unitName, 1));
			}

			// Order missing prerequisite buildings for the desired units that no queue can build yet.
			// The building is only ordered when this queue can build it right now (its own prerequisites
			// are met) and it is not already queued; otherwise the next pick gets a chance.
			foreach (var unitName in pickOrder)
			{
				if (info.ExcludeFromArmyTypes.Contains(unitName) || usedUnits.Contains(unitName))
					continue;

				var missing = MissingPrerequisiteBuilding(unitName);
				if (missing == null)
					continue;

				var buildingQueue = queues.FirstOrDefault(q => q.Info.Type == "Building");
				var alreadyQueued = queues.Any(q => q.AllQueued().Any(i => i.Item == missing));
				if (buildingQueue == null || alreadyQueued || !buildingQueue.BuildableItems().Any(i => i.Name == missing))
					continue;

				bot.QueueOrder(Order.StartProduction(buildingQueue.Actor, missing, 1));
				CoalitionTelemetry.Log(world, $"Prerequisite building ordered: {missing} (for {unitName})");
				break;
			}
		}

		/// <summary>Returns the first prerequisite building of a unit that the player has not yet built, or null.</summary>
		string MissingPrerequisiteBuilding(string unitName)
		{
			if (!world.Map.Rules.Actors.TryGetValue(unitName, out var unitInfo))
				return null;

			var buildable = unitInfo.TraitInfoOrDefault<BuildableInfo>();
			if (buildable == null)
				return null;

			foreach (var prerequisite in buildable.Prerequisites)
			{
				// Prerequisites may carry ! (invert) and ~ (hide) modifiers; faction and checkbox
				// prerequisites do not resolve to buildings and are skipped.
				var name = prerequisite.TrimStart('!', '~');
				if (!world.Map.Rules.Actors.TryGetValue(name, out var buildingInfo) || !buildingInfo.HasTraitInfo<BuildingInfo>())
					continue;

				var have = world.Actors.Any(a => !a.IsDead && a.IsInWorld && a.Owner == player && a.Info.Name == name);
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
			var retreatCell = RetreatCell(baseCenter.Value);

			// Micro-precision scales the retreat threshold: a precise bot pulls units earlier.
			var retreatThreshold = info.ResolvedDifficulty().RetreatHealthPercent();

			foreach (var a in units)
			{
				var health = a.TraitOrDefault<IHealth>();
				var fraction = health == null ? 100 : health.HP * 100 / health.MaxHP;

				if (retreating.Contains(a))
				{
					// A retreated unit rejoins the army once it has recovered or reached the base.
					var nearBase = (a.CenterPosition - baseCenter.Value).LengthSquared <= BaseRadiusSquared(10);
					if (fraction > info.RegroupHealthPercent || nearBase)
						retreating.Remove(a);
					else
					{
						bot.QueueOrder(new Order("Move", a, Target.FromCell(world, retreatCell), false));
						continue;
					}
				}

				if (fraction < retreatThreshold)
				{
					retreating.Add(a);
					bot.QueueOrder(new Order("Move", a, Target.FromCell(world, retreatCell), false));
				}
			}

			var activeArmy = units.Where(a => !retreating.Contains(a)).ToArray();
			if (activeArmy.Length == 0)
				return;

			// Strategic reserve: missions commit only the available army (everything minus the held-back
			// reserve), unless the reserve is committed for a decisive push. Zero scouted enemies means
			// unknown (fog), not weak - the reserve is only committed against a scouted, outnumbered enemy.
			reserveCommitted = enemyArmyCount > 0 && enemyArmyCount <= OwnCombatUnits().Count() * info.CommitReserveRatio;
			if (reserveCommitted != lastReserveCommitted)
			{
				lastReserveCommitted = reserveCommitted;
				if (reserveCommitted)
					CoalitionTelemetry.Log(world, $"Reserve committed: coalition outnumbers the scouted enemy ({enemyArmyCount} vs {OwnCombatUnits().Count()})");
			}

			var availableArmy = AvailableArmy(activeArmy);
			var reserveCount = activeArmy.Length - availableArmy.Length;

			// Base defense: intercept enemies approaching our structures. Defense is proportional to the
			// nearby threat (so a minor raid does not strip the whole army from its missions), and the
			// most valuable structure's vicinity is defended first.
			var defendedPos = MostValuableStructurePosition() ?? baseCenter.Value;
			var baseThreat = ClosestEnemyTo(defendedPos, BaseRadiusSquared(info.BaseDefenseScanRadius));
			if (baseThreat != null)
			{
				SetPosture(Posture.Defend);
				lastDefendTick = world.WorldTick;
				counterPos = world.Map.CellContaining(baseThreat.Value);
				enemyCountAtDefense = enemyArmyCount;

				var nearby = sightings.Values.Count(s =>
					(s.Position - defendedPos).LengthSquared <= BaseRadiusSquared(info.BaseDefenseScanRadius));
				var minCommitment = Math.Min(info.MinWaveSize, activeArmy.Length);
				var commitment = Math.Clamp(nearby * 3, minCommitment, activeArmy.Length);
				var defenders = Claim(activeArmy).Take(commitment).ToArray();
				if (defenders.Length > 0)
					bot.QueueOrder(new Order("AttackMove", null, Target.FromPos(baseThreat.Value), false, groupedActors: defenders));
				return;
			}

			// Anti-air umbrella: hold AA units over the base against enemy air.
			if (defenseKind == "aa" && enemyAirSpotted)
			{
				var aa = Claim(activeArmy.Where(a => info.AntiAirUnits.Contains(a.Info.Name))).ToArray();
				if (aa.Length > 0)
					bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(world, retreatCell), false, groupedActors: aa));
			}

			// Naval screen: hold ships near the coast against an enemy navy.
			if (defenseKind == "naval")
			{
				var ships = Claim(activeArmy.Where(a => info.NavalPriority.Contains(a.Info.Name))).ToArray();
				if (ships.Length > 0)
					bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(world, retreatCell), false, groupedActors: ships));
			}

			// Economy escort: keep a small guard on the harvesters and other economy units.
			if (defenseKind == "escort")
			{
				var harvester = world.Actors.FirstOrDefault(a => a.IsInWorld && !a.IsDead && a.Owner == player
					&& !a.Info.HasTraitInfo<BuildingInfo>() && info.ExcludeFromArmyTypes.Contains(a.Info.Name));
				if (harvester != null)
				{
					var guard = Claim(activeArmy).Take(3).ToArray();
					if (guard.Length > 0)
						bot.QueueOrder(new Order("AttackMove", null, Target.FromActor(harvester), false, groupedActors: guard));
				}
			}

			// Reserve edge behavior: the uncommitted reserve intercepts raids on non-base assets
			// (harvesters, refineries, expansions) and defends allied bases, without stripping the
			// available army that is staged for missions. Enhanced (req 355): commit the reserve to
			// intercept when the enemy is attacking, logging the commitment with a reason.
			var reserve = activeArmy.Where(a => !availableArmy.Contains(a)).ToArray();
			var raidThreat = ClosestEnemyTo(baseCenter.Value, BaseRadiusSquared(info.BaseDefenseScanRadius * 3));
			if (raidThreat != null && reserve.Length >= info.MinWaveSize / 2 && world.WorldTick - lastDefendTick > info.CounterDelayTicks)
			{
				SetPosture(Posture.Defend);
				var interceptors = Claim(reserve).ToArray();
				if (interceptors.Length > 0)
				{
					ReserveCounterattack(bot, interceptors, raidThreat.Value);
				}
			}

			// Reserve exploit breakthrough (req 357): when a Breakthrough mission reaches the
			// Exploitation phase, commit the reserve to push through the breach.
			var ccModule = player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);
			var sharedBB = ccModule?.Blackboard;
			if (attackTarget != null && reserve.Length >= info.MinWaveSize / 2)
			{
				var exploitReserve = Claim(reserve).ToArray();
				if (exploitReserve.Length > 0)
					ReserveExploitBreakthrough(bot, exploitReserve, attackTarget.Value);
			}

			// Reserve protect expansion (req 359): when a new expansion is detected, assign a small
			// reserve force to guard it.
			var expansionManager = player.PlayerActor.TraitsImplementing<McvExpansionManagerBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);
			if (expansionManager != null && reserve.Length >= info.MinWaveSize / 2)
			{
				var expansionGuard = Claim(reserve).Take(Math.Min(reserve.Length, info.MinWaveSize / 2)).ToArray();
				if (expansionGuard.Length > 0)
				{
					var expansionCenter = BaseCenter();
					var expansionCell = expansionCenter != null
						? world.Map.CellContaining(expansionCenter.Value)
						: player.HomeLocation;
					ReserveProtectExpansion(bot, expansionGuard, expansionCell);
				}
			}

			// Counterattack-after-defense: shortly after repelling an attack, strike back at the
			// attacker with the whole army - no coordinated gate, the enemy force is weakened.
			// Gate on the shared blackboard so only one bot fires the counterattack per window.
			if (world.WorldTick - lastDefendTick <= info.CounterDelayTicks && counterPos != null && activeArmy.Length >= info.MinWaveSize
				&& (sharedBB == null || world.WorldTick - sharedBB.LastCounterattackTick >= info.CounterDelayTicks / 2))
			{
				if (sharedBB != null)
					sharedBB.LastCounterattackTick = world.WorldTick;
				var counter = Claim(activeArmy).ToArray();
				if (counter.Length > 0)
				{
					bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(world, counterPos.Value), false, groupedActors: counter));
					var depleted = enemyCountAtDefense > 0 && enemyArmyCount < enemyCountAtDefense;
					CoalitionTelemetry.Log(world,
						$"Counterattack with {counter.Length} units after defense{(depleted ? " (enemy depleted)" : string.Empty)}");

					// Record counterattack and base defense response telemetry (reqs 620, 621).
					var ccModuleForCounter = player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>()
						.FirstOrDefault(m => !m.IsTraitDisabled);
					if (ccModuleForCounter != null)
					{
						var enemyDestroyed = depleted ? enemyCountAtDefense - enemyArmyCount : 0;
						ccModuleForCounter.RecordCounterattack(Math.Max(0, enemyDestroyed));
						ccModuleForCounter.RecordBaseDefenseResponse(lastDefendTick, world.WorldTick);
					}
				}

				return;
			}

			// Team-wide retreat: pull the whole army back to the base.
			if (teamRetreat)
			{
				var retreaters = Claim(activeArmy).ToArray();
				if (retreaters.Length > 0)
				{
					bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(world, retreatCell), false, groupedActors: retreaters));
					// Record retreat telemetry (req 614).
					var ccModuleForRetreat = player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>()
						.FirstOrDefault(m => !m.IsTraitDisabled);
					ccModuleForRetreat?.RecordRetreat(world.WorldTick, retreaters.Length);
				}

				return;
			}

			// Intercept a team-designated threat location (defend an allied base or a key position).
			if (counterTarget != null)
			{
				SetPosture(Posture.Defend);
				var interceptors = Claim(activeArmy).ToArray();
				if (interceptors.Length > 0)
					bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(world, counterTarget.Value), false, groupedActors: interceptors));
				return;
			}

			// Stealth/transport missions run independently of the main army.
			if (transportTarget != null && transportKind != null)
				ExecuteTransportMission();

			// Special operations: insert scarce assets at the designated target, on foot when no
			// transport is available. The asset is claimed so waves never take it.
			if (transportTarget != null && transportKind != null && specialOps != null)
				specialOps.Execute(transportTarget, transportKind, transportAvailable: world.Actors.Any(a =>
					a.IsInWorld && !a.IsDead && a.Owner == player && info.TransportTypes.Contains(a.Info.Name)));

			// Reconnaissance: probe the designated position with a small force to confirm what is there.
			if (reconTarget != null)
			{
				var recon = Claim(availableArmy).Take(3).ToArray();
				if (recon.Length > 0)
				{
					bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(world, reconTarget.Value), false, groupedActors: recon));
					CoalitionTelemetry.Log(world, $"Recon probe of {recon.Length} units to {reconTarget.Value}");
					// Record recon telemetry (req 616). Useful intel is assumed true when the probe survives.
					var ccModuleForRecon = player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>()
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
					bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(world, baitTarget.Value), false, groupedActors: bait));
					CoalitionTelemetry.Log(world, $"Bait placed: {bait.Length} units at {baitTarget.Value}");
				}
			}

			// Feint: divert a small fraction of the available army to a decoy position. Feint units are
			// claimed, so the main wave never orders the same units (the feint is no longer overwritten).
			if (feintTarget != null && availableArmy.Length > info.FeintFraction && world.WorldTick - feintTick > info.TacticInterval * 5)
			{
				feintTick = world.WorldTick;
				var feint = Claim(availableArmy).Take(availableArmy.Length / info.FeintFraction).ToArray();
				if (feint.Length > 0)
				{
					bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(world, feintTarget.Value), false, groupedActors: feint));
					CoalitionTelemetry.Log(world, $"Feint of {feint.Length} units to {feintTarget.Value}");

					// Record the feint launch (req 627) so the commander can later measure whether it
					// opened a launch window for the main wave.
					var ccModuleForFeint = player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>()
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
				var strikeUnits = Claim(activeArmy.Where(a =>
					info.AirUnitTypes.Contains(a.Info.Name) || info.NavalPriority.Contains(a.Info.Name))).ToArray();
				if (strikeUnits.Length > 0)
				{
					bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(world, strikeTarget.Value), false, groupedActors: strikeUnits));
					CoalitionTelemetry.Log(world, $"Strike of {strikeUnits.Length} units to {strikeTarget.Value}");
				}
			}

			// Support-power strike: fire the first ready superweapon at the designated target.
			if (supportPowerTarget != null)
			{
				FireSupportPower(supportPowerTarget.Value);
				supportPowerTarget = null;
			}

			// Cohesion: a scattered army regroups before launching, so it does not attack as isolated
			// units. The force's cohesion comes from the shared blackboard (spread around its center).
			var commanderCenter = player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);
			var ownCohesion = commanderCenter?.Blackboard?.Forces
				.FirstOrDefault(f => f.Owner == player.InternalName)?.Cohesion ?? 1f;
			if (ownCohesion < info.RegroupCohesionThreshold)
			{
				var regroupers = Claim(activeArmy).ToArray();
				if (regroupers.Length > 0)
				{
					bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(world, retreatCell), false, groupedActors: regroupers));
					CoalitionTelemetry.Log(world, $"Army regrouping: cohesion {ownCohesion:0.00} below {info.RegroupCohesionThreshold}");
				}

				return;
			}

			// Coordinated attack gate: waves only launch once the coalition fields a large, mixed
			// force. Land is the essential arm; air and naval are layered on when available. Requiring
			// air in particular blocked attacks on maps where air production is not prioritized, which
			// left the coalition sitting on defense to be worn down.
			var coordinatedMinimum = (int)info.ScaleDifficulty(info.CoordinatedAttackMinimum);
			var coordinated = coalitionArmy >= coordinatedMinimum
				&& (!info.CoordinatedAttackMixedArms || (coalitionLand > 0
					&& (!coalitionHasWater || coalitionNaval > 0)));
			if (!coordinated)
			{
				var gate = $"coalition {coalitionArmy}/{coordinatedMinimum} ready (air {coalitionAir}, naval {coalitionNaval}, land {coalitionLand}, water {(coalitionHasWater ? "yes" : "no")})";
				if (gate != lastCoordGate)
				{
					lastCoordGate = gate;
					CoalitionTelemetry.Log(world, $"Coordinated force: {gate}");
				}
			}

			// Without a decisive force or hostile intent, hold the available army near the base.
			// The attack tick is the coalition-wide launch window (time-on-target): every allied bot
			// launches in the same tick range so the waves arrive together.
			if (posture != Posture.Attack || availableArmy.Length < info.MinWaveSize || !coordinated || world.WorldTick < attackTick)
			{
				if (world.WorldTick - lastAttackTick > info.WithdrawDelayTicks)
				{
					var holders = Claim(availableArmy).ToArray();
					if (holders.Length > 0)
						bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(world, retreatCell), false, groupedActors: holders));
				}

				return;
			}

			// Launch or sustain an attack wave from the available army: the team-designated target takes
			// priority over the locally scouted enemy, so all allied bots push the same position together.
			// Each domain controller executes its own component (land/air/naval) so the wave is
			// coordinated without a single blob order and each domain can later refine its behavior.
			var target = attackTarget != null ? world.Map.CenterOfCell(attackTarget.Value) : BestAttackTarget();
			if (target == null)
				return;

			lastAttackTick = world.WorldTick;
			var priorClaims = claimedUnits.Count;
			ground?.Attack(availableArmy, target.Value);
			air?.Attack(availableArmy, target.Value);
			naval?.Attack(availableArmy, target.Value);

			// Mark the wave launch so the opponent model can measure the enemy's response time,
			// and record how much enemy contact this raid generated (raid-sensitivity signal).
			var commander = player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);
			if (commander != null)
			{
				commander.MarkWaveLaunch(world.WorldTick);

				// Record whether this engagement is fought with local numerical superiority (req 613),
				// and whether a recent feint opened this launch window (req 627).
				var bb = commander.Blackboard;
				if (bb != null)
					commander.RecordEngagement(bb.CoalitionArmyStrength >= bb.EnemyArmyStrength * 1.5f);
				if (commander.LastFeintTick >= 0 && world.WorldTick - commander.LastFeintTick <= info.TacticInterval * 5)
					commander.RecordFeintOpenedWindow();
				if (attackTarget != null)
				{
					var raidCell = attackTarget.Value;
					var team = commander.TeamPlayers();
					var enemiesNearRaid = world.Actors.Count(a =>
						a.IsInWorld && !a.IsDead && a.Owner != player && a.OccupiesSpace != null
						&& player.RelationshipWith(a.Owner) == PlayerRelationship.Enemy
						&& team.Any(ally => ally.Shroud.IsVisible(a.CenterPosition))
						&& (a.CenterPosition - world.Map.CenterOfCell(raidCell)).LengthSquared <= BaseRadiusSquared(20));
					commander.RecordRaidContact(enemiesNearRaid);
				}
			}

			// Count only the units the domain controllers claimed for this wave (prior claims from
			// recon/bait/feint are excluded).
			var wave = claimedUnits.Skip(priorClaims).ToArray();
			if (wave.Length >= info.MinWaveSize)
			{
				var waveAir = wave.Count(a => info.AirUnitTypes.Contains(a.Info.Name));
				var waveNaval = wave.Count(a => info.NavalPriority.Contains(a.Info.Name));
				var waveAA = wave.Count(a => info.AntiAirUnits.Contains(a.Info.Name));
				var waveArtillery = wave.Count(a => a.Info.Name is "v2rl" or "arty");
				var waveLand = wave.Length - waveAir - waveNaval;
				CoalitionTelemetry.Log(world,
					$"Wave of {wave.Length} units launched (reserve {reserveCount} held back) at ToT {attackTick} " +
					$"(sync error {world.WorldTick - attackTick}t) " +
					$"[{waveLand} land ({waveArtillery} artillery, {waveAA} aa), {waveAir} air, {waveNaval} naval]");
			}
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
			var reserveFraction = reserveFractionOverride > 0 ? reserveFractionOverride : info.ScaledReserveFraction();
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
			if (reserve.Length < info.MinWaveSize / 2)
				return;

			reserveManager.Commit(world.WorldTick, reserve.Length, "counterattack interception", world, info.MinWaveSize);
			bot.QueueOrder(new Order("AttackMove", null, Target.FromPos(raidThreat), false, groupedActors: reserve));
		}

		/// <summary>
		/// Directs the reserve to reinforce a failing front (req 356). When an ally is under attack,
		/// send the reserve (not the available army) to reinforce their position.
		/// </summary>
		void ReserveReinforceFront(IBot bot, Actor[] reserve, CPos allyUnderAttack)
		{
			if (reserve.Length < info.MinWaveSize / 2)
				return;

			reserveManager.Commit(world.WorldTick, reserve.Length, "reinforce failing front", world, info.MinWaveSize);
			bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(world, allyUnderAttack), false, groupedActors: reserve));
		}

		/// <summary>
		/// Directs the reserve to exploit a breakthrough (req 357). When a Breakthrough mission
		/// reaches the Exploitation phase, commit the reserve to push through the breach.
		/// </summary>
		void ReserveExploitBreakthrough(IBot bot, Actor[] reserve, CPos breakthroughTarget)
		{
			if (reserve.Length < info.MinWaveSize / 2)
				return;

			reserveManager.Commit(world.WorldTick, reserve.Length, "exploit breakthrough", world, info.MinWaveSize);
			bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(world, breakthroughTarget), false, groupedActors: reserve));
		}

		/// <summary>
		/// Directs a small reserve force to protect a new expansion (req 359). Called when a new
		/// expansion is detected via the MCV expansion manager.
		/// </summary>
		void ReserveProtectExpansion(IBot bot, Actor[] reserve, CPos expansionLocation)
		{
			var guardForce = reserve.Take(Math.Min(reserve.Length, info.MinWaveSize / 2)).ToArray();
			if (guardForce.Length == 0)
				return;

			reserveManager.Commit(world.WorldTick, guardForce.Length, "protect expansion", world, info.MinWaveSize);
			bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(world, expansionLocation), false, groupedActors: guardForce));
		}

		/// <summary>
		/// Suicide scouts: cheap infantry walk alone into different unexplored regions far from the
		/// base. Losing them is cheap, and each one uncovers a corridor of the map for the coalition.
		/// </summary>
		void UpdateScouting()
		{
			if (info.ScoutUnitTypes.Count == 0)
				return;

			var active = scouts.Count(a => a.IsInWorld && !a.IsDead);
			if (active >= info.ScoutSquadSize)
				return;

			var baseCenter = BaseCenter();
			if (baseCenter == null)
				return;

			var toSend = Math.Min(info.ScoutSendPerInterval, info.ScoutSquadSize - active);
			if (toSend <= 0)
				return;

			var infantry = OwnCombatUnits().Where(a => info.ScoutUnitTypes.Contains(a.Info.Name)).ToArray();
			if (infantry.Length == 0)
				return;

			var targets = ScoutTargets(baseCenter.Value, toSend);
			for (var i = 0; i < Math.Min(toSend, Math.Min(infantry.Length, targets.Length)); i++)
			{
				var scout = infantry[i];
				if (!claimedUnits.Add(scout))
					continue;

				scouts.Add(scout);
				bot.QueueOrder(new Order("Move", scout, Target.FromCell(world, targets[i]), false));
				CoalitionTelemetry.Log(world, $"Scout sent to {targets[i]} (shadow far from base)");
			}
		}

		/// <summary>Picks unexplored cells at least ScoutMinDistance from the base, spread across the map.</summary>
		CPos[] ScoutTargets(WPos baseCenter, int count)
		{
			var targets = new List<CPos>();
			var minDistanceSq = (long)WDist.FromCells(info.ScoutMinDistance).Length;
			minDistanceSq *= minDistanceSq;
			var stride = Math.Max(4, world.Map.MapSize.Width / 16);

			var index = 0;
			foreach (var cpos in world.Map.AllCells)
			{
				if (++index % stride != 0)
					continue;

				if (player.Shroud.IsExplored(cpos))
					continue;

				if ((world.Map.CenterOfCell(cpos) - baseCenter).LengthSquared < minDistanceSq)
					continue;

				targets.Add(cpos);
				if (targets.Count >= count)
					break;
			}

			return targets.ToArray();
		}

		/// <summary>
		/// Executes a transport mission through the transport controller's state machine. The
		/// controller claims the payload so the main army does not order it elsewhere during the
		/// insertion, and clears the target when the mission completes or aborts.
		/// </summary>
		void ExecuteTransportMission()
		{
			var active = transport.Execute(transportTarget, transportKind, world.WorldTick);
			if (!active)
			{
				var ccModuleForTransport = player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>()
					.FirstOrDefault(m => !m.IsTraitDisabled);

				if (transport.Aborted)
				{
					CoalitionTelemetry.Log(world, "Transport mission aborted during transit");
					// Record transport telemetry (req 617): aborted = not survived.
					ccModuleForTransport?.RecordTransport(false);
				}
				else
				{
					var transportActor = world.Actors.FirstOrDefault(a => a.IsInWorld && !a.IsDead && a.Owner == player
						&& info.TransportTypes.Contains(a.Info.Name));
					var health = transportActor?.TraitOrDefault<IHealth>();
					var percent = health == null ? 100 : health.HP * 100 / health.MaxHP;
					CoalitionTelemetry.Log(world, $"Transport mission completed; transport survived at {percent}% health");
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
			if (army.Length < info.MinWaveSize)
				return;

			var allies = world.Players.Where(p =>
				p != player &&
				p.PlayerActor.TraitsImplementing<ModularBot>().Any(b => b.IsEnabled) &&
				player.RelationshipWith(p) == PlayerRelationship.Ally).ToArray();

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

				if (ClosestEnemyTo(allyBase.Value, BaseRadiusSquared(info.AllyReinforceScanRadius)) == null)
					continue;

				// Per-ally cooldown: only one bot reinforces a given ally per coordination interval,
				// so the whole coalition doesn't send duplicate waves to the same defender.
				var lastSent = lastReinforceTick.GetValueOrDefault(ally.InternalName);
				if (world.WorldTick - lastSent < info.CoordinationInterval)
					continue;

				var reinforcements = Claim(available).Take(Math.Max(1, available.Length / info.ReinforcementFraction)).ToArray();
				if (reinforcements.Length > 0)
				{
					lastReinforceTick[ally.InternalName] = world.WorldTick;
					bot.QueueOrder(new Order("AttackMove", null, Target.FromPos(allyBase.Value), false, groupedActors: reinforcements));
				}

				// Reserve reinforce failing front (req 356): when an ally is under attack, also send
				// the reserve (not just the available army) to reinforce their position.
				var reserveForFront = army.Where(a => !available.Contains(a) && claimedUnits.Add(a)).ToArray();
				if (reserveForFront.Length >= info.MinWaveSize / 2)
					ReserveReinforceFront(bot, reserveForFront, world.Map.CellContaining(allyBase.Value));
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
			var commander = player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);
			var blackboard = commander?.Blackboard;
			var baseCenter = BaseCenter();
			if (blackboard == null || baseCenter == null)
				return [];

			var from = blackboard.RegionOf(world.Map.CellContaining(baseCenter.Value)).Index;
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
			return world.Actors.Where(a =>
				a.IsInWorld && !a.IsDead && a.Owner == player && a.Info.HasTraitInfo<BuildingInfo>());
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
			return world.Actors.Where(a =>
				a.IsInWorld && !a.IsDead && a.Owner == player && a.OccupiesSpace != null && !a.Info.HasTraitInfo<BuildingInfo>() &&
				!info.ExcludeFromArmyTypes.Contains(a.Info.Name));
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
		/// The planned retreat fallback: the center of the safest ground-reachable region (lowest total
		/// threat), so retreats route away from danger instead of blindly running to the base. Falls
		/// back to the base center when the coalition blackboard is unavailable.
		/// </summary>
		CPos RetreatCell(WPos baseCenter)
		{
			var commander = player.PlayerActor.TraitsImplementing<CoalitionCommandCenterBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);
			var blackboard = commander?.Blackboard;
			if (blackboard == null || blackboard.HomeRegion < 0)
				return world.Map.CellContaining(baseCenter);

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
		void FireSupportPower(CPos target)
		{
			var manager = player.PlayerActor.TraitOrDefault<SupportPowerManager>();
			if (manager == null)
				return;

			// Friendly-fire avoidance: superweapons have a blast radius, so a target crowded with
			// friendly units is withheld rather than risking them.
			var targetPos = world.Map.CenterOfCell(target);
			var friendlyNear = world.Actors.Count(a => a.IsInWorld && !a.IsDead && a.Owner == player && a.OccupiesSpace != null
				&& (a.CenterPosition - targetPos).LengthSquared <= BaseRadiusSquared(SupportPowerFriendlyFireRadius));
			if (ShouldWithholdSupportPower(friendlyNear))
			{
				CoalitionTelemetry.Log(world, $"Support power withheld: {friendlyNear} friendly units near target {target}");
				return;
			}

			foreach (var kv in manager.Powers)
			{
				if (!kv.Value.Ready)
					continue;

				bot.QueueOrder(new Order(kv.Key, manager.Self, Target.FromCell(world, target), false));
				CoalitionTelemetry.Log(world, $"Support power {kv.Key} fired at {target}");
				return;
			}
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
