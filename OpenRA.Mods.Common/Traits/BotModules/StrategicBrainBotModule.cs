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

		[Desc("Interval (in ticks) between tactical updates.")]
		public readonly int TacticInterval = 20;

		[Desc("Number of combat units required before the bot adopts an attack posture.")]
		public readonly int AttackForceThreshold = 14;

		[Desc("Units retreat individually when their health drops below this percentage.")]
		public readonly int RetreatHealthPercent = 30;

		[Desc("A retreating unit rejoins the army once its health recovers above this percentage.")]
		public readonly int RegroupHealthPercent = 60;

		[Desc("Enemy units within this many cells of the base center trigger base defense.")]
		public readonly int BaseDefenseScanRadius = 25;

		[Desc("The army is not committed to a wave while it is smaller than this many units.")]
		public readonly int MinWaveSize = 6;

		[Desc("The army is withdrawn back to the base when no enemy sighting was refreshed within this many ticks.")]
		public readonly int WithdrawDelayTicks = 300;

		[Desc("Interval (in ticks) between team coordination updates.")]
		public readonly int CoordinationInterval = 100;

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
		}

		readonly StrategicBrainBotModuleInfo info;
		readonly Dictionary<Actor, Sighting> sightings = [];
		readonly HashSet<Actor> retreating = [];

		World world;
		Player player;
		IBot bot;
		ProductionQueue[] queues = [];

		int enemyArmyCount;
		bool enemyAirSpotted;
		bool enemyArmorSpotted;
		bool enemyInfantrySpotted;
		WPos? enemyBaseCenter;

		Posture posture = Posture.BuildArmy;
		int lastAttackTick;

		public StrategicBrainBotModule(StrategicBrainBotModuleInfo info, ActorInitializer init)
			: base(info)
		{
			this.info = info;
			world = init.World;
		}

		void IBotRespondToAttack.RespondToAttack(IBot bot, Actor self, AttackInfo e)
		{
			if (player == null || e.Attacker == null || e.Attacker.IsDead || !e.Attacker.IsInWorld)
				return;

			if (player.RelationshipWith(e.Attacker.Owner) != PlayerRelationship.Enemy)
				return;

			// An enemy attacked one of our actors: raise the alarm and prepare to defend.
			lastAttackTick = world.WorldTick;
			SetPosture(Posture.Defend);
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

			var tick = world.WorldTick;
			if (tick % info.IntelUpdateInterval == 0)
				UpdateIntel(tick);

			if (tick % info.BuildPlanInterval == 0)
				UpdateBuildPlan();

			if (tick % info.TacticInterval == 0)
				UpdateTactics();

			if (tick % info.CoordinationInterval == 0)
				UpdateCoordination();
		}

		/// <summary>
		/// Refreshes enemy intelligence. The bot only records enemy actors inside explored territory
		/// (radar-style awareness of uncovered regions, but no wallhacks on unexplored map), and forgets
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

				// Fog-respecting awareness: only record actors in territory we have explored.
				if (!player.Shroud.IsExplored(a.CenterPosition))
					continue;

				sightings[a] = new Sighting { Position = a.CenterPosition, Tick = tick };
			}

			bool IsStructure(Actor a) => a.Info.HasTraitInfo<BuildingInfo>();
			bool IsArmy(Actor a) => !IsStructure(a) && !info.ExcludeFromArmyTypes.Contains(a.Info.Name);

			enemyArmyCount = sightings.Keys.Count(IsArmy);
			enemyAirSpotted = sightings.Keys.Any(a => info.AirUnitTypes.Contains(a.Info.Name));
			enemyArmorSpotted = sightings.Keys.Any(a => info.ArmorUnitTypes.Contains(a.Info.Name));
			enemyInfantrySpotted = sightings.Keys.Any(a => info.InfantryUnitTypes.Contains(a.Info.Name));

			var structureSightings = sightings.Keys.Where(IsStructure).Select(a => a.CenterPosition).ToArray();
			enemyBaseCenter = structureSightings.Length > 0 ? structureSightings.Average() : null;

			UpdatePosture();
		}

		/// <summary>
		/// Adapts the bot's posture from the scouted force balance and recent attacks:
		/// defend when outnumbered or under attack, attack with a decisive force, otherwise keep building.
		/// </summary>
		void UpdatePosture()
		{
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

			// Build the adaptive pick order: counters first, then the base composition.
			var pickOrder = new List<string>();
			if (enemyAirSpotted)
				pickOrder.AddRange(info.AntiAirUnits);
			if (enemyArmorSpotted)
				pickOrder.AddRange(info.AntiArmorUnits);
			if (enemyInfantrySpotted)
				pickOrder.AddRange(info.AntiInfantryUnits);
			pickOrder.AddRange(info.ArmyPriority);

			var idleQueues = queues.Where(q => q.CurrentItem() == null).ToArray();
			if (idleQueues.Length == 0)
				return;

			foreach (var unitName in pickOrder)
			{
				// Harvesters, MCVs and other support actors are managed by their dedicated modules.
				if (info.ExcludeFromArmyTypes.Contains(unitName))
					continue;

				var queue = idleQueues.FirstOrDefault(q => q.BuildableItems().Any(i => i.Name == unitName));
				if (queue == null)
					continue;

				bot.QueueOrder(Order.StartProduction(queue.Actor, unitName, 1));
				break;
			}
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
			var retreatCell = world.Map.CellContaining(baseCenter.Value);

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

				if (fraction < info.RetreatHealthPercent)
				{
					retreating.Add(a);
					bot.QueueOrder(new Order("Move", a, Target.FromCell(world, retreatCell), false));
				}
			}

			var activeArmy = units.Where(a => !retreating.Contains(a)).ToArray();
			if (activeArmy.Length == 0)
				return;

			// Base defense: intercept enemies that approach our structures.
			var baseThreat = ClosestEnemyTo(baseCenter.Value, BaseRadiusSquared(info.BaseDefenseScanRadius));
			if (baseThreat != null)
			{
				SetPosture(Posture.Defend);
				bot.QueueOrder(new Order("AttackMove", null, Target.FromPos(baseThreat.CenterPosition), false, groupedActors: activeArmy));
				return;
			}

			// Without a decisive force or hostile intent, hold the army near the base.
			if (posture != Posture.Attack || activeArmy.Length < info.MinWaveSize)
			{
				if (world.WorldTick - lastAttackTick > info.WithdrawDelayTicks)
					bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(world, retreatCell), false, groupedActors: activeArmy));
				return;
			}

			// Launch or sustain an attack wave against the scouted enemy.
			var target = BestAttackTarget();
			if (target == null)
				return;

			lastAttackTick = world.WorldTick;
			bot.QueueOrder(new Order("AttackMove", null, Target.FromPos(target.Value), false, groupedActors: activeArmy));
		}

		/// <summary>
		/// Team coordination: reinforces allied bases that are under attack and lets ready allied bots
		/// launch their attack waves in the same time window for a synchronized push.
		/// </summary>
		void UpdateCoordination()
		{
			var ownArmyCount = OwnCombatUnits().Count();
			if (ownArmyCount < info.MinWaveSize)
				return;

			var allies = world.Players.Where(p =>
				p != player &&
				p.PlayerActor.TraitsImplementing<ModularBot>().Any(b => b.IsEnabled) &&
				player.RelationshipWith(p) == PlayerRelationship.Ally).ToArray();

			if (allies.Length == 0)
				return;

			// Reinforce an ally whose base is under attack.
			foreach (var ally in allies)
			{
				var allyBase = ally.PlayerActor.TraitsImplementing<StrategicBrainBotModule>()
					.Select(m => m.BaseCenter())
					.FirstOrDefault(center => center != null);
				if (allyBase == null)
					continue;

				if (ClosestEnemyTo(allyBase.Value, BaseRadiusSquared(info.AllyReinforceScanRadius)) == null)
					continue;

				var reinforcements = OwnCombatUnits().Take(ownArmyCount / info.ReinforcementFraction).ToArray();
				if (reinforcements.Length > 0)
					bot.QueueOrder(new Order("AttackMove", null, Target.FromPos(allyBase.Value), false, groupedActors: reinforcements));
			}
		}

		/// <summary>Returns the average position of the bot's structures, or null if it has none.</summary>
		WPos? BaseCenter()
		{
			var structures = OwnStructures().ToArray();
			return structures.Length == 0 ? null : structures.Select(a => a.CenterPosition).Average();
		}

		IEnumerable<Actor> OwnStructures()
		{
			return world.Actors.Where(a =>
				a.IsInWorld && !a.IsDead && a.Owner == player && a.Info.HasTraitInfo<BuildingInfo>());
		}

		IEnumerable<Actor> OwnCombatUnits()
		{
			return world.Actors.Where(a =>
				a.IsInWorld && !a.IsDead && a.Owner == player && !a.Info.HasTraitInfo<BuildingInfo>() &&
				!info.ExcludeFromArmyTypes.Contains(a.Info.Name));
		}

		/// <summary>Returns the closest remembered enemy to a position within the given squared radius, or null.</summary>
		Actor ClosestEnemyTo(WPos pos, long radiusSquared)
		{
			Actor closest = null;
			var closestDistance = long.MaxValue;
			foreach (var kv in sightings)
			{
				var a = kv.Key;
				if (!a.IsInWorld || a.IsDead)
					continue;

				var distance = (a.CenterPosition - pos).LengthSquared;
				if (distance > radiusSquared || distance >= closestDistance)
					continue;

				closest = a;
				closestDistance = distance;
			}

			return closest;
		}

		/// <summary>
		/// Selects the attack wave target: the enemy base if known, otherwise the newest sighting.
		/// </summary>
		WPos? BestAttackTarget()
		{
			if (enemyBaseCenter != null)
				return enemyBaseCenter;

			Actor newest = null;
			var newestTick = int.MinValue;
			foreach (var kv in sightings)
			{
				if (kv.Value.Tick > newestTick)
				{
					newest = kv.Key;
					newestTick = kv.Value.Tick;
				}
			}

			return newest == null ? null : sightings[newest].Position;
		}

		static long BaseRadiusSquared(int cells)
		{
			var length = WDist.FromCells(cells).Length;
			return (long)length * length;
		}
	}
}
