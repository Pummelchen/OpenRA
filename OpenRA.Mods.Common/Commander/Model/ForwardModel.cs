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
using OpenRA.Mods.Common.Commander.Terrain;

namespace OpenRA.Mods.Common.Commander.Model
{
	/// <summary>What a side is trying to do over one step of the plan.</summary>
	public enum MacroVerb
	{
		/// <summary>Put money into a new resource base.</summary>
		Expand,

		/// <summary>Put money into the tech tree instead of into the field.</summary>
		Tech,

		/// <summary>Convert cash into army at home.</summary>
		Produce,

		/// <summary>Commit the field army against a region and take it.</summary>
		Attack,

		/// <summary>Show force somewhere it is not wanted, to move the defence off somewhere it is.</summary>
		Feint,

		/// <summary>Hold what is held.</summary>
		Defend,

		/// <summary>Send a small force against economy rather than against the army.</summary>
		Harass,

		/// <summary>Gather the army in one place, which is how the square law is turned into an advantage.</summary>
		Consolidate,
	}

	/// <summary>A single choice in a plan: a verb and where it applies.</summary>
	public readonly record struct MacroAction(MacroVerb Verb, int Region)
	{
		public override string ToString() => $"{Verb}@R{Region}";
	}

	/// <summary>
	/// <para>
	/// The abstract simulator: given a state and what both sides are trying to do, what does the
	/// world look like a few seconds later.
	/// </para>
	/// <para>
	/// This is the piece the previous commander did not have, and its absence is why that commander
	/// could not attack. Without a forward model, posture can only be a function of the present, and
	/// every attack looks like a mistake at the moment it starts costing units. With one, the search
	/// can see past the dip to the base falling on the other side of it.
	/// </para>
	/// <para>
	/// Everything here is calibrated from mod data rather than tuned: build times and costs come
	/// from the production queues, damage from the weapon tables, travel time from the region graph.
	/// The model is only allowed to be wrong in ways the game itself is wrong.
	/// </para>
	/// </summary>
	public sealed class ForwardModel
	{
		/// <summary>Economic and production constants, read from the ruleset rather than invented.</summary>
		public sealed class Parameters
		{
			/// <summary>
			/// Credits per second per harvester to assume before anything has been measured, and
			/// for a player whose earnings cannot be observed - which is to say, the enemy.
			/// </summary>
			public float FallbackIncomePerHarvester { get; set; } = 16f;

			/// <summary>
			/// How income scales with harvester count: 1 would be linear, 0 would be no gain at all.
			/// Measured over-forecasting of about a fifth at typical counts puts it near 0.6.
			/// </summary>
			public float HarvesterReturnsExponent { get; set; } = 0.6f;

			/// <summary>
			/// <para>
			/// How much of a measured army trend to carry forward. 1 extrapolates it fully, 0
			/// assumes the army simply stays where it is.
			/// </para>
			/// <para>
			/// This is deliberately well below 1, and the number was argued with rather than chosen.
			/// Measured over four matches on two maps, extrapolating the trend in full produced a
			/// <i>worse</i> thirty-second forecast than assuming no change at all - 23-36% error
			/// against 20-24%. Army value at this horizon behaves close to a random walk: production
			/// arrives in lumps and combat is bursty, so a trend measured over the last few seconds
			/// carries little information about the next thirty. Shrinking toward the no-change
			/// forecast is the standard remedy for exactly that situation, and keeps the drift term
			/// from injecting more variance than signal.
			/// </para>
			/// <para>
			/// Note that this term is common to every action the search compares, so it cancels in
			/// the comparison. What discriminates between plans is combat, movement and economy -
			/// which is why a modest background drift is acceptable where a wrong combat model
			/// would not be.
			/// </para>
			/// </summary>
			public float ArmyTrendConfidence { get; set; } = 0.35f;

			/// <summary>Cost of a harvester plus its share of the refinery that serves it.</summary>
			public float ExpansionCost { get; init; } = 1400f;

			/// <summary>Seconds before an expansion starts paying.</summary>
			public float ExpansionDelay { get; init; } = 30f;

			/// <summary>Credits to unlock the next tier of the tech tree.</summary>
			public float TechCost { get; init; } = 1500f;

			/// <summary>Cells crossed per second by a mixed force, paced by its slowest member.</summary>
			public float ForceSpeedCellsPerSecond { get; init; } = 2.5f;

			/// <summary>Fraction of the army a harassing detachment takes.</summary>
			public float HarassFraction { get; init; } = 0.15f;

			/// <summary>Fraction of the army a feint takes. Enough to be believed, not enough to be missed.</summary>
			public float FeintFraction { get; init; } = 0.2f;

			/// <summary>Static defences a defending side effectively adds, as a fraction of its army value.</summary>
			public float DefenceBonus { get; init; } = 0.25f;

			/// <summary>
			/// Credits of structure destroyed per second, per credit of unopposed attacking force.
			/// This is how the model represents winning at all, and its absence is why an earlier
			/// version of the commander refused to attack: with nothing to gain, the search was
			/// right to expand instead.
			/// </summary>
			public float DemolitionRate { get; init; } = 0.004f;

			public static readonly Parameters Default = new();
		}

		readonly RegionGraph graph;
		readonly RoleStats stats;
		readonly Parameters parameters;
		readonly int[,] travelSeconds;

		public ForwardModel(RegionGraph graph, RoleStats stats, Parameters parameters = null)
		{
			ArgumentNullException.ThrowIfNull(graph);
			ArgumentNullException.ThrowIfNull(stats);

			this.graph = graph;
			this.stats = stats;
			this.parameters = parameters ?? Parameters.Default;
			travelSeconds = BuildTravelTable(graph, this.parameters.ForceSpeedCellsPerSecond);
		}

		public RoleStats Stats => stats;

		/// <summary>Seconds for a force to move between two regions, or -1 if it cannot get there.</summary>
		public int TravelSeconds(int from, int to)
		{
			if (from < 0 || to < 0 || from >= graph.Regions.Length || to >= graph.Regions.Length)
				return -1;

			return travelSeconds[from, to];
		}

		/// <summary>
		/// Advances the world by <paramref name="seconds"/> with both sides acting. Returns a new
		/// state; the input is not modified, because the search needs to expand several actions from
		/// the same node.
		/// </summary>
		public AbstractState Step(AbstractState state, MacroAction selfAction, MacroAction enemyAction, float seconds)
		{
			ArgumentNullException.ThrowIfNull(state);
			if (seconds <= 0f)
				return state.Clone();

			var next = state.Clone();
			next.Tick = state.Tick + (int)(seconds * AbstractState.TicksPerSecond);

			StepEconomy(next.Self, selfAction, seconds);
			StepEconomy(next.Enemy, enemyAction, seconds);

			StepMovement(next.Self, selfAction, seconds);
			StepMovement(next.Enemy, enemyAction, seconds);

			StepCombat(next, selfAction, enemyAction, seconds);
			StepDemolition(next, seconds);
			StepControl(next, seconds);

			for (var r = 0; r < next.RegionCount; r++)
				next.VisibilityAge[r] += (int)(seconds * AbstractState.TicksPerSecond);

			return next;
		}

		/// <summary>
		/// <para>
		/// Income per second, anchored to what this player is measured to be earning, and scaled
		/// sub-linearly when a plan changes the harvester count.
		/// </para>
		/// <para>
		/// Three versions of this were measured and two were discarded. Capping income at a
		/// per-refinery throughput was wrong for this mod - a refinery unloads twenty bales at one
		/// tick each and is never the constraint. Deriving income from a per-harvester rate was
		/// wrong for a subtler reason: it over-forecast by about a fifth, consistently, because
		/// <b>income does not scale linearly with harvesters</b>. They queue for the same refinery
		/// and drive to the same ore, so the tenth harvester earns less than the first did. The
		/// exponent below is that diminishing return, and it is also the honest answer to "is
		/// another harvester worth 1,400 credits" - which a linear model would always say yes to.
		/// </para>
		/// </summary>
		public float IncomePerSecond(PlayerState player)
		{
			ArgumentNullException.ThrowIfNull(player);

			// Nowhere to unload is no income, whatever else is true.
			if (player.Refineries <= 0 || player.Harvesters <= 0)
				return 0f;

			// Before anything has been measured - or for the enemy, whose earnings cannot be seen -
			// fall back to a flat rate.
			if (player.ObservedIncomePerSecond <= 0f || player.ObservedHarvesters <= 0)
				return player.Harvesters * parameters.FallbackIncomePerHarvester;

			if (player.Harvesters == player.ObservedHarvesters)
				return player.ObservedIncomePerSecond;

			var ratio = player.Harvesters / (float)player.ObservedHarvesters;
			return player.ObservedIncomePerSecond * MathF.Pow(ratio, parameters.HarvesterReturnsExponent);
		}

		void StepEconomy(PlayerState player, MacroAction action, float seconds)
		{
			player.Cash += IncomePerSecond(player) * seconds;

			switch (action.Verb)
			{
				case MacroVerb.Expand:
				{
					var cost = parameters.ExpansionCost;
					if (player.Cash >= cost)
					{
						player.Cash -= cost;
						player.Harvesters++;

						// A harvester with nowhere to unload is a harvester that does not earn, so
						// the refinery comes with it rather than being assumed.
						if (player.Refineries == 0 || player.Harvesters > player.Refineries * 2)
							player.Refineries++;
					}

					break;
				}

				case MacroVerb.Tech:
				{
					if (player.Cash >= parameters.TechCost)
					{
						player.Cash -= parameters.TechCost;

						// Unlock the lowest tech bit not yet held.
						for (var bit = 0; bit < 64; bit++)
						{
							var mask = 1UL << bit;
							if ((player.TechBits & mask) == 0)
							{
								player.TechBits |= mask;
								break;
							}
						}
					}

					break;
				}

				default:
				{
					// Cash is drawn down at the rate this player actually spends it, and the army
					// grows at the rate it has actually been growing. Both are measured rather than
					// derived, for the same reason income is: a bot sitting on twenty thousand
					// credits has enormous queue capacity and is not using it, and a model fed
					// capacity forecasts an army that never arrives. Note also the coupling that
					// instant-build cheats exposed the hard way - production is bounded by cash, so
					// removing the time constraint makes the whole cost fall due at once rather
					// than making units free.
					var wanted = player.ProductionThroughput * seconds;
					var spent = Math.Min(player.Cash, wanted);
					if (spent > 0f)
						player.Cash -= spent;

					var growth = player.ArmyGrowthPerSecond * seconds * parameters.ArmyTrendConfidence;
					if (growth > 0f)
					{
						// The observed growth rate was achieved while the treasury could pay for it.
						// A plan that runs the money out cannot keep growing at the same rate, so
						// growth is scaled by the fraction of the intended spend that was actually
						// affordable. Without this, a hundred and fifty credits buys ten thousand
						// credits of army, which the search would happily plan around.
						var affordable = wanted > 0f
							? Math.Clamp(spent / wanted, 0f, 1f)
							: Math.Clamp(player.Cash / growth, 0f, 1f);

						growth *= affordable;
						if (wanted <= 0f)
							player.Cash -= growth;

						if (growth > 0f)
							player.AddForce(HomeRegion(player), CombatRole.Armor, growth);
					}
					else if (growth < 0f)
					{
						// Losses do not need to be paid for.
						ApplyLoss(player, -growth);
					}

					break;
				}
			}
		}

		/// <summary>Removes a credit value of army, spread proportionally across what is present.</summary>
		static void ApplyLoss(PlayerState player, float credits)
		{
			var total = player.ArmyValue();
			if (total <= 0f || credits <= 0f)
				return;

			var survivingFraction = Math.Max(0f, 1f - (credits / total));
			for (var region = 0; region < player.RegionCount; region++)
				Scale(player, region, survivingFraction);
		}

		/// <summary>
		/// Moves force toward the objective. Forces travel at the pace of their slowest member and
		/// arrive over time rather than teleporting, which is what makes a distant objective cost
		/// something to choose.
		/// </summary>
		void StepMovement(PlayerState player, MacroAction action, float seconds)
		{
			var target = action.Region;
			if (target < 0 || target >= player.RegionCount)
				return;

			float fraction;
			switch (action.Verb)
			{
				case MacroVerb.Attack:
				case MacroVerb.Consolidate:
					fraction = 1f;
					break;
				case MacroVerb.Harass:
					fraction = parameters.HarassFraction;
					break;
				case MacroVerb.Feint:
					fraction = parameters.FeintFraction;
					break;
				default:
					return;
			}

			for (var region = 0; region < player.RegionCount; region++)
			{
				if (region == target)
					continue;

				var travel = TravelSeconds(region, target);
				if (travel <= 0)
					continue;

				// Fraction of the journey covered in this step.
				var progress = Math.Clamp(seconds / travel, 0f, 1f) * fraction;
				if (progress <= 0f)
					continue;

				for (var role = 0; role < RoleStats.Roles; role++)
				{
					// Static defences do not march.
					if ((CombatRole)role == CombatRole.Defense)
						continue;

					var present = player.ForceValue(region, (CombatRole)role);
					if (present <= 0f)
						continue;

					var moving = present * progress;
					player.AddForce(region, (CombatRole)role, -moving);
					player.AddForce(target, (CombatRole)role, moving);
				}
			}
		}

		/// <summary>Resolves every region where both sides have force present.</summary>
		void StepCombat(AbstractState state, MacroAction selfAction, MacroAction enemyAction, float seconds)
		{

			for (var region = 0; region < state.RegionCount; region++)
			{
				var selfValue = state.Self.ArmyValueIn(region);
				var enemyValue = state.Enemy.ArmyValueIn(region);
				if (selfValue <= 0f || enemyValue <= 0f)
					continue;

				// Whoever is attacking here is the attacker; the other side gets the defender's
				// edge, which is what makes attacking into a prepared position cost more than
				// meeting the same force in the open.
				var selfAttacking = selfAction.Verb == MacroVerb.Attack && selfAction.Region == region;
				var enemyAttacking = enemyAction.Verb == MacroVerb.Attack && enemyAction.Region == region;

				var selfDefends = !selfAttacking && enemyAttacking;
				var enemyDefends = !enemyAttacking && selfAttacking;

				var selfForce = state.Self.ForcesIn(region).ToArray();
				var enemyForce = state.Enemy.ForcesIn(region).ToArray();

				if (selfDefends)
					selfForce[(int)CombatRole.Defense] += selfValue * parameters.DefenceBonus;

				if (enemyDefends)
					enemyForce[(int)CombatRole.Defense] += enemyValue * parameters.DefenceBonus;

				var outcome = CombatResolver.Resolve(selfForce, enemyForce, stats, seconds);

				Scale(state.Self, region, outcome.AttackerRemaining);
				Scale(state.Enemy, region, outcome.DefenderRemaining);
			}
		}

		/// <summary>
		/// <para>
		/// An army standing on the enemy's structures with nothing left to stop it destroys them.
		/// </para>
		/// <para>
		/// Without this the model has no representation of the game's objective: an assault could
		/// only ever cost units, so every plan that involved one scored worse than staying home, and
		/// the search obligingly stayed home. Demolition is gated on the defender having no mobile
		/// force left in the region - taking ground first and levelling it second, which is also the
		/// order it happens in.
		/// </para>
		/// </summary>
		void StepDemolition(AbstractState state, float seconds)
		{
			for (var region = 0; region < state.RegionCount; region++)
			{
				Demolish(state.Self, state.Enemy, region, seconds);
				Demolish(state.Enemy, state.Self, region, seconds);
			}
		}

		void Demolish(PlayerState attacker, PlayerState defender, int region, float seconds)
		{
			var structures = defender.StructuresIn(region);
			if (structures <= 0f)
				return;

			// Anything still shooting has to be dealt with first.
			if (defender.ArmyValueIn(region) > 0f)
				return;

			var force = attacker.ArmyValueIn(region);
			if (force <= 0f)
				return;

			var destroyed = Math.Min(structures, force * parameters.DemolitionRate * seconds);
			defender.AddStructures(region, -destroyed);
			defender.BaseIntegrity = Math.Max(0f, defender.BaseIntegrity - destroyed);
		}

		static void Scale(PlayerState player, int region, float fraction)
		{
			fraction = Math.Clamp(fraction, 0f, 1f);
			for (var role = 0; role < RoleStats.Roles; role++)
				player.SetForce(region, (CombatRole)role, player.ForceValue(region, (CombatRole)role) * fraction);
		}

		/// <summary>
		/// Control follows presence, but slowly: holding ground is a matter of staying there, and a
		/// region does not change hands because a scout drove through it.
		/// </summary>
		void StepControl(AbstractState state, float seconds)
		{
			const float SecondsToFullControl = 60f;
			var rate = Math.Clamp(seconds / SecondsToFullControl, 0f, 1f);

			for (var region = 0; region < state.RegionCount; region++)
			{
				var mine = state.Self.ArmyValueIn(region);
				var theirs = state.Enemy.ArmyValueIn(region);
				var total = mine + theirs;

				var target = total <= 0f ? state.Control[region] : (mine - theirs) / total;
				state.Control[region] += (target - state.Control[region]) * rate;
			}
		}

		/// <summary>The region holding the most of a player's force: where new production appears.</summary>
		static int HomeRegion(PlayerState player)
		{
			var best = 0;
			var bestValue = -1f;
			for (var region = 0; region < player.RegionCount; region++)
			{
				var value = player.ArmyValueIn(region);
				if (value > bestValue)
				{
					bestValue = value;
					best = region;
				}
			}

			return best;
		}

		/// <summary>
		/// All-pairs travel time over the region graph, by Floyd-Warshall. Computed once per map:
		/// forty regions is 64,000 operations, and the search then reads travel time as a lookup
		/// rather than paying for a path query per node.
		/// </summary>
		static int[,] BuildTravelTable(RegionGraph graph, float cellsPerSecond)
		{
			var n = graph.Regions.Length;
			var table = new int[n, n];
			const int Unreachable = -1;

			var work = new float[n, n];
			for (var a = 0; a < n; a++)
				for (var b = 0; b < n; b++)
					work[a, b] = a == b ? 0f : float.PositiveInfinity;

			var speed = Math.Max(0.1f, cellsPerSecond);
			foreach (var choke in graph.Chokepoints)
			{
				var a = graph.Regions[choke.RegionA];
				var b = graph.Regions[choke.RegionB];
				var dx = a.CentreX - b.CentreX;
				var dy = a.CentreY - b.CentreY;
				var cells = MathF.Sqrt((dx * dx) + (dy * dy));
				var cost = cells / speed;

				if (cost < work[choke.RegionA, choke.RegionB])
				{
					work[choke.RegionA, choke.RegionB] = cost;
					work[choke.RegionB, choke.RegionA] = cost;
				}
			}

			for (var k = 0; k < n; k++)
				for (var a = 0; a < n; a++)
					for (var b = 0; b < n; b++)
						if (work[a, k] + work[k, b] < work[a, b])
							work[a, b] = work[a, k] + work[k, b];

			for (var a = 0; a < n; a++)
				for (var b = 0; b < n; b++)
					table[a, b] = float.IsInfinity(work[a, b]) ? Unreachable : Math.Max(1, (int)work[a, b]);

			for (var a = 0; a < n; a++)
				table[a, a] = 0;

			return table;
		}
	}
}
