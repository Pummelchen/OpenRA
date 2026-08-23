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
using OpenRA.Mods.Common.Commander.Terrain;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Commander.Model
{
	/// <summary>
	/// <para>
	/// Reads a live world into an <see cref="AbstractState"/>. This is the boundary between the
	/// engine and the model layer: everything above it is pure arithmetic that can be tested without
	/// a game running.
	/// </para>
	/// <para>
	/// Own forces are read exactly - the commander has every right to know what it owns. Enemy
	/// forces are read only where they are currently visible, and phase 5 replaces the gap with a
	/// belief distribution. Nothing here consults an enemy actor the player cannot see.
	/// </para>
	/// </summary>
	public sealed class StateExtractor
	{
		readonly World world;
		readonly Map map;
		readonly RegionGraph graph;
		readonly Dictionary<string, CombatRole> roleCache = [];
		readonly Dictionary<string, int> costCache = [];
		float peakBaseIntegrity;

		public StateExtractor(World world, RegionGraph graph)
		{
			ArgumentNullException.ThrowIfNull(world);
			ArgumentNullException.ThrowIfNull(graph);

			this.world = world;
			this.graph = graph;
			map = world.Map;
		}

		public RegionGraph Graph => graph;

		/// <summary>
		/// Builds the state as <paramref name="self"/> is entitled to see it. Enemy detail is
		/// limited to what <paramref name="self"/> can currently observe, so the model never plans
		/// against information the player does not have.
		/// </summary>
		public AbstractState Extract(Player self, IEnumerable<Player> enemies)
		{
			ArgumentNullException.ThrowIfNull(self);
			ArgumentNullException.ThrowIfNull(enemies);

			var enemySet = enemies.ToArray();
			var state = new AbstractState(graph.Regions.Length) { Tick = world.WorldTick };

			ReadOwnEconomy(self, state.Self);

			// The enemy economy is *observed*, never read. Their cash, their queues and their total
			// harvester count are not things the player can see, and an earlier version of this
			// method read all three straight off their PlayerResources - which quietly turned every
			// prediction about them into cheating. What can be seen is counted in ReadForces below;
			// what cannot is left at zero until phase 5 supplies a belief distribution over it.
			state.Enemy.Cash = 0f;
			state.Enemy.ProductionThroughput = 0f;

			// ActorsHavingTrait rather than world.Actors: the player actor and the world actor are
			// owned actors too, and asking them where they are throws.
			foreach (var actor in world.ActorsHavingTrait<IOccupySpace>())
			{
				if (actor.Owner == null || actor.IsDead || !actor.IsInWorld)
					continue;

				var isSelf = actor.Owner == self || actor.Owner.IsAlliedWith(self);
				var isEnemy = Array.IndexOf(enemySet, actor.Owner) >= 0;
				if (!isSelf && !isEnemy)
					continue;

				// Fog is not negotiable: an enemy actor the player cannot see does not enter the
				// state at all. The honest gap is filled by inference, never by peeking.
				if (isEnemy && !self.Shroud.IsVisible(actor.Location))
					continue;

				if (!MapRegions.ToGrid(map, actor.Location, out var gx, out var gy))
					continue;

				var region = graph.RegionAt(gx, gy);
				if (region < 0)
					continue;

				var target = isSelf ? state.Self : state.Enemy;

				// Enemy economy is whatever has actually been seen, and nothing more.
				if (!isSelf)
				{
					if (actor.Info.HasTraitInfo<HarvesterInfo>())
						target.Harvesters++;

					if (actor.Info.HasTraitInfo<RefineryInfo>())
						target.Refineries++;
				}

				var cost = CostOf(actor.Info);
				if (cost <= 0)
					continue;

				var role = RoleOf(actor.Info);

				// Structures that do not shoot are not force; they are what losing looks like - and
				// they are recorded per region, so an assault on a place can destroy what is there.
				if (role == CombatRole.Defense && !IsArmed(actor.Info))
				{
					target.BaseIntegrity += cost;
					target.AddStructures(region, cost);
					continue;
				}

				// Scale by remaining health, so a burning tank is not counted as a fresh one.
				var health = actor.TraitOrDefault<IHealth>();
				var fraction = health == null || health.MaxHP <= 0
					? 1f
					: Math.Clamp(health.HP / (float)health.MaxHP, 0f, 1f);

				target.AddForce(region, role, cost * fraction);
			}

			// The peak is a property of the match, not of this instant, so it is remembered here
			// rather than recomputed - a base is only "damaged" relative to what it once was.
			peakBaseIntegrity = Math.Max(peakBaseIntegrity, state.Self.BaseIntegrity);
			state.Self.PeakBaseIntegrity = peakBaseIntegrity;
			state.Enemy.PeakBaseIntegrity = state.Enemy.BaseIntegrity;

			ReadVisibilityAndValue(self, state);
			ReadControl(state);
			return state;
		}

		/// <summary>
		/// The player's own economy, which it is entitled to know exactly.
		/// <see cref="ObservedSpendRate"/> must be supplied by the caller, which is the only party
		/// that can measure a rate - it needs two samples and this method sees one.
		/// </summary>
		void ReadOwnEconomy(Player player, PlayerState target)
		{
			var resources = player.PlayerActor.TraitOrDefault<PlayerResources>();
			target.Cash = resources?.GetCashAndResources() ?? 0;

			var harvesters = 0;
			var refineries = 0;
			foreach (var actor in world.ActorsHavingTrait<IOccupySpace>())
			{
				if (actor.Owner != player || actor.IsDead || !actor.IsInWorld)
					continue;

				if (actor.Info.HasTraitInfo<HarvesterInfo>())
					harvesters++;

				if (actor.Info.HasTraitInfo<RefineryInfo>())
					refineries++;
			}

			target.Harvesters = harvesters;
			target.Refineries = refineries;
			target.ProductionThroughput = ObservedSpendRate;
			target.ObservedIncomePerSecond = ObservedIncomePerSecond;
			target.ObservedHarvesters = harvesters;
			target.ArmyGrowthPerSecond = ObservedArmyGrowthPerSecond;
		}

		/// <summary>
		/// Credits per second the player has recently been converting into things, measured from
		/// <c>PlayerResources.Spent</c>. This is deliberately the observed rate rather than queue
		/// capacity: a bot sitting on twenty thousand credits has enormous capacity and is not using
		/// it, and a model fed capacity predicts an army that never arrives.
		/// </summary>
		public float ObservedSpendRate { get; set; }

		/// <summary>
		/// Credits per second the player is measured to be earning, from its own running total.
		/// Anchoring income to this rather than deriving it is what lets the forecast start from
		/// the truth instead of from a formula.
		/// </summary>
		public float ObservedIncomePerSecond { get; set; }

		/// <summary>
		/// Net credits per second the army has recently been gaining. Supplied by the caller,
		/// because measuring a rate needs two samples and this class sees one.
		/// </summary>
		public float ObservedArmyGrowthPerSecond { get; set; }

		/// <summary>
		/// Credits per second the player's queues can absorb. Build time in this engine is broadly
		/// proportional to cost, so cost-over-time is close to constant per queue and gives a stable
		/// figure without having to guess what will be built next.
		/// </summary>
		public float ProductionThroughput(Player player)
		{
			var total = 0f;
			foreach (var queue in player.PlayerActor.TraitsImplementing<ProductionQueue>())
			{
				if (!queue.Enabled)
					continue;

				var samples = 0;
				var rate = 0f;
				foreach (var item in queue.AllItems())
				{
					var buildable = item.TraitInfoOrDefault<BuildableInfo>();
					if (buildable == null)
						continue;

					var cost = queue.GetProductionCost(item);
					var ticks = queue.GetBuildTime(item, buildable);
					if (cost <= 0)
						continue;

					// FastBuild reports zero ticks. That does not make production infinite - the
					// whole cost simply falls due at once - so it is bounded by cash instead, which
					// the forward model already enforces.
					var seconds = Math.Max(1, ticks) / (float)AbstractState.TicksPerSecond;
					rate += cost / seconds;
					samples++;

					if (samples >= 8)
						break;
				}

				if (samples > 0)
					total += rate / samples;
			}

			return total;
		}

		void ReadVisibilityAndValue(Player self, AbstractState state)
		{
			var resourceLayer = world.WorldActor.TraitOrDefault<IResourceLayer>();

			for (var region = 0; region < graph.Regions.Length; region++)
			{
				var r = graph.Regions[region];
				var cell = MapRegions.ToCell(map, r.CentreX, r.CentreY);
				state.VisibilityAge[region] = self.Shroud.IsVisible(cell) ? 0 : int.MaxValue / 2;
			}

			if (resourceLayer == null)
				return;

			// Region value is what is worth holding: ore in the ground, counted where it lies.
			for (var y = 0; y < graph.Height; y++)
			{
				for (var x = 0; x < graph.Width; x++)
				{
					var region = graph.RegionAt(x, y);
					if (region < 0)
						continue;

					var cell = MapRegions.ToCell(map, x, y);
					if (!map.Contains(cell))
						continue;

					var content = resourceLayer.GetResource(cell);
					if (content.Type != null)
						state.Value[region] += content.Density;
				}
			}
		}

		static void ReadControl(AbstractState state)
		{
			for (var region = 0; region < state.RegionCount; region++)
			{
				var mine = state.Self.ArmyValueIn(region);
				var theirs = state.Enemy.ArmyValueIn(region);
				var total = mine + theirs;
				state.Control[region] = total <= 0f ? 0f : (mine - theirs) / total;
			}
		}

		CombatRole RoleOf(ActorInfo info)
		{
			if (roleCache.TryGetValue(info.Name, out var cached))
				return cached;

			var profile = CounterMatrix.Profile(info, world.Map.Rules);
			var traits = new RoleClassifier.Traits(
				IsAircraft: info.HasTraitInfo<AircraftInfo>(),
				IsBuilding: info.HasTraitInfo<BuildingInfo>(),
				IsMobile: info.HasTraitInfo<MobileInfo>() || info.HasTraitInfo<AircraftInfo>(),
				Armor: info.TraitInfoOrDefault<ArmorInfo>()?.Type ?? "None",
				RangeCells: profile?.RangeCells ?? 0,
				CanTargetAir: profile?.CanTargetAir ?? false,
				CanTargetGround: profile?.CanTargetGround ?? false,
				IsArmed: profile?.IsArmed ?? false);

			var role = RoleClassifier.Classify(traits);
			roleCache[info.Name] = role;
			return role;
		}

		static bool IsArmed(ActorInfo info) => info.HasTraitInfo<ArmamentInfo>();

		int CostOf(ActorInfo info)
		{
			if (costCache.TryGetValue(info.Name, out var cached))
				return cached;

			var cost = info.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0;
			costCache[info.Name] = cost;
			return cost;
		}

		/// <summary>
		/// Per-role damage and durability, aggregated from every buildable actor in the ruleset.
		/// Computed once: it is a property of the mod, not of the match.
		/// </summary>
		public RoleStats BuildRoleStats()
		{
			var entries = new List<(UnitProfile, CombatRole)>();

			foreach (var actorInfo in world.Map.Rules.Actors.Values)
			{
				if (actorInfo.Name.StartsWith('^'))
					continue;

				var cost = CostOf(actorInfo);
				if (cost <= 0)
					continue;

				var profile = CounterMatrix.Profile(actorInfo, world.Map.Rules);
				if (profile == null)
					continue;

				var role = RoleOf(actorInfo);
				var damage = new float[RoleStats.Roles];

				for (var d = 0; d < RoleStats.Roles; d++)
					damage[d] = DamageAgainstRole(profile, (CombatRole)d);

				entries.Add((new UnitProfile
				{
					Type = actorInfo.Name,
					Cost = cost,
					HitPoints = profile.HitPoints,
					DamageVersusRole = damage,
				}, role));
			}

			return RoleStats.FromProfiles(entries);
		}

		/// <summary>
		/// Damage against a role, taken from the armour class that role typically wears. This is
		/// where the per-type counter matrix is folded down to per-role, and it is deliberately the
		/// only place that approximation is made.
		/// </summary>
		static float DamageAgainstRole(UnitCombatProfile profile, CombatRole role)
		{
			// Aircraft can only be hit by something that can shoot up, whatever its damage table says.
			if (role == CombatRole.Aircraft && !profile.CanTargetAir)
				return 0f;

			if (role != CombatRole.Aircraft && !profile.CanTargetGround)
				return 0f;

			var armor = role switch
			{
				CombatRole.Infantry => "None",
				CombatRole.Armor => "Heavy",
				CombatRole.Artillery => "Light",
				CombatRole.AntiAir => "Light",
				CombatRole.Aircraft => "Light",
				CombatRole.Naval => "Ship",
				CombatRole.Defense => "Concrete",
				_ => "None",
			};

			return profile.DamageVersus(armor);
		}
	}
}
