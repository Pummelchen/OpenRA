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
using OpenRA.Mods.Common.Commander.Model;

namespace OpenRA.Mods.Common.Commander.Search
{
	/// <summary>
	/// <para>
	/// Produces the handful of candidate plans the search actually considers.
	/// </para>
	/// <para>
	/// This is what makes searching a real-time strategy game tractable at all. The raw action space
	/// is every unit taking every order every tick, which is not merely large but unsearchable. A
	/// puppet search instead enumerates the choices a competent script exposes - eight verbs, each
	/// pointed at the few regions where that verb makes any sense - and lets the execution layer
	/// carry them out. Branching drops from astronomical to roughly a dozen, which a two-minute
	/// lookahead can cover.
	/// </para>
	/// <para>
	/// Restricting regions per verb is not a shortcut, it is the point. There is no value in
	/// searching "attack the empty corner", and every node spent on it is a node not spent on the
	/// choice between besieging the enemy base and taking a third expansion.
	/// </para>
	/// </summary>
	public static class MacroActionGenerator
	{
		/// <summary>How many regions each targeting verb considers. Keeps branching near a dozen.</summary>
		public const int RegionsPerVerb = 3;

		/// <summary>
		/// Candidate actions for <paramref name="state"/>, in a deterministic order. The order is
		/// load-bearing: an MCTS expands untried actions in the order they are offered, so a
		/// non-deterministic generator would make the whole search irreproducible.
		/// </summary>
		public static List<MacroAction> Generate(AbstractState state, ForwardModel model)
		{
			ArgumentNullException.ThrowIfNull(state);
			ArgumentNullException.ThrowIfNull(model);

			var actions = new List<MacroAction>();
			if (state.RegionCount == 0)
				return actions;

			var home = HomeRegion(state.Self);

			// Economy and technology are not directed at a place; they happen at home.
			actions.Add(new MacroAction(MacroVerb.Produce, home));
			actions.Add(new MacroAction(MacroVerb.Tech, home));

			foreach (var region in TopRegions(state, ExpansionValue, RegionsPerVerb))
				actions.Add(new MacroAction(MacroVerb.Expand, region));

			// Offensive verbs point at where the enemy actually is or has something worth taking.
			var offensive = TopRegions(state, OffensiveValue, RegionsPerVerb);
			foreach (var region in offensive)
				actions.Add(new MacroAction(MacroVerb.Attack, region));

			// A feint goes somewhere the enemy cares about but the main effort is not, so it is
			// generated from the same ranking offset by one - the second-best target rather than the
			// best, which is exactly what makes it believable and cheap.
			for (var i = 1; i < offensive.Count; i++)
				actions.Add(new MacroAction(MacroVerb.Feint, offensive[i]));

			foreach (var region in TopRegions(state, HarassValue, RegionsPerVerb - 1))
				actions.Add(new MacroAction(MacroVerb.Harass, region));

			// Defensive verbs point at what is ours and worth keeping.
			foreach (var region in TopRegions(state, DefensiveValue, RegionsPerVerb - 1))
				actions.Add(new MacroAction(MacroVerb.Defend, region));

			actions.Add(new MacroAction(MacroVerb.Consolidate, home));

			return actions;
		}

		/// <summary>Where a player's force is heaviest: where production appears and defence rallies.</summary>
		public static int HomeRegion(PlayerState player)
		{
			ArgumentNullException.ThrowIfNull(player);

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
		/// Attacking is worth doing where the enemy has something that losing would hurt, discounted
		/// by what is defending it. Note it is <i>discounted</i>, not excluded: a defended base is
		/// still the thing worth taking, and a commander that only ever attacks undefended ground
		/// never wins a game.
		/// </summary>
		static float OffensiveValue(AbstractState state, int region)
		{
			var enemyForce = state.Enemy.ArmyValueIn(region);
			var enemyDefence = state.Enemy.ForceValue(region, CombatRole.Defense);
			var ownForce = state.Self.ArmyValueIn(region);

			// Somewhere the enemy holds and we do not is a target; somewhere neither of us is, is not.
			if (enemyForce <= 0f && state.Control[region] > -0.1f)
				return 0f;

			var prize = enemyForce + (state.Value[region] * 0.001f);
			return prize / (1f + (enemyDefence * 0.002f)) + (ownForce * 0.1f);
		}

		/// <summary>Raiding wants soft targets: enemy presence with little defending it.</summary>
		static float HarassValue(AbstractState state, int region)
		{
			var enemyForce = state.Enemy.ArmyValueIn(region);
			var enemyDefence = state.Enemy.ForceValue(region, CombatRole.Defense);
			if (enemyForce <= 0f)
				return 0f;

			// The inverse of the assault ranking: a raid goes where an assault would not.
			return enemyForce / (1f + (enemyDefence * 0.02f) + (enemyForce * 0.005f));
		}

		/// <summary>Defending is worth doing where we hold something and the enemy is near it.</summary>
		static float DefensiveValue(AbstractState state, int region)
		{
			var ownForce = state.Self.ArmyValueIn(region);
			if (ownForce <= 0f && state.Control[region] < 0.1f)
				return 0f;

			var threat = state.Enemy.ArmyValueIn(region);
			return ownForce + (threat * 2f) + (state.Value[region] * 0.001f);
		}

		/// <summary>Expanding wants ore we can reach and the enemy is not sitting on.</summary>
		static float ExpansionValue(AbstractState state, int region)
		{
			if (state.Value[region] <= 0f)
				return 0f;

			if (state.Enemy.ArmyValueIn(region) > state.Self.ArmyValueIn(region))
				return 0f;

			return state.Value[region] * (1f + state.Control[region]);
		}

		/// <summary>
		/// The best regions by a scoring function, ties broken by index so the result does not depend
		/// on sort stability.
		/// </summary>
		static List<int> TopRegions(AbstractState state, Func<AbstractState, int, float> score, int count)
		{
			var scored = new List<(int Region, float Score)>();
			for (var region = 0; region < state.RegionCount; region++)
			{
				var value = score(state, region);
				if (value > 0f)
					scored.Add((region, value));
			}

			scored.Sort((a, b) => a.Score != b.Score ? b.Score.CompareTo(a.Score) : a.Region.CompareTo(b.Region));

			var result = new List<int>();
			for (var i = 0; i < scored.Count && i < count; i++)
				result.Add(scored[i].Region);

			return result;
		}
	}
}
