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

namespace OpenRA.Mods.Common.Commander.Model
{
	/// <summary>
	/// <para>
	/// Turns a state into the handful of numbers the win-probability model is fitted on.
	/// </para>
	/// <para>
	/// <b>Every feature here must be computable from what the commander actually knows, and must
	/// actually vary.</b> That sounds obvious and the first version of this file failed both halves
	/// of it. Measured across 16,000 logged states, <c>CashAdvantage</c> was constant at exactly
	/// 1.000 - the enemy's cash is never observable, so comparing to it produced a second bias term
	/// - and <c>TechAdvantage</c> was constant at 0.000 because nothing ever populated it. Four
	/// more sat above 0.87 on average for the same underlying reason: an "advantage" that compares a
	/// fully-known own quantity against a fog-limited enemy one is not an advantage, it is a
	/// measurement of how much of the enemy you happen to be looking at. Six of nine features were
	/// noise, and the fitted weights were correspondingly meaningless.
	/// </para>
	/// <para>
	/// So the set is split by what is knowable. Own-side features are absolute quantities squashed
	/// into 0..1 by <see cref="Saturate"/>, which keeps them scale-free without pretending to a
	/// comparison that cannot be made. The one genuine comparison - own army against enemy army
	/// actually seen - is kept and named honestly. Phase 5's belief state is what will make further
	/// enemy-side comparisons meaningful; until it exists, inventing them would be worse than going
	/// without.
	/// </para>
	/// </summary>
	public static class StateFeatures
	{
		/// <summary>The feature vector's layout. The last entry is the constant term.</summary>
		public enum Feature
		{
			/// <summary>Own army against the enemy army currently observed. Honest about being partial.</summary>
			ArmyVsSeenEnemy,

			/// <summary>How large our army is in absolute terms, saturating.</summary>
			ArmyScale,

			/// <summary>How strong our economy is, saturating.</summary>
			EconomyScale,

			/// <summary>How many harvesters we are running, saturating.</summary>
			HarvesterScale,

			/// <summary>Our base relative to the most of it we have ever had: the losing signal.</summary>
			BaseIntact,

			/// <summary>Mean control across regions.</summary>
			MapControl,

			/// <summary>Fraction of regions where both sides are present.</summary>
			ContestedFraction,

			/// <summary>Fraction of the map seen recently: what reconnaissance actually buys.</summary>
			ExploredFraction,

			Bias,
		}

		public const int Count = (int)Feature.Bias + 1;

		/// <summary>Regions unseen for longer than this count as unexplored.</summary>
		public const int StaleVisibilityTicks = 25 * 60;

		/// <summary>
		/// Signed advantage in the range -1 to +1: +1 is total dominance, 0 is parity, -1 is
		/// having nothing while the opponent has everything.
		/// </summary>
		public static float Advantage(float mine, float theirs)
		{
			var total = mine + theirs;
			if (total <= 0f)
				return 0f;

			return Math.Clamp((mine - theirs) / total, -1f, 1f);
		}

		/// <summary>
		/// Squashes an absolute quantity into 0..1 against a reference value, at which it reads 0.5.
		/// Keeps a raw count usable as a feature without the weight having to change as the match
		/// grows, and without the false precision of a ratio against something unobservable.
		/// </summary>
		public static float Saturate(float value, float reference)
		{
			if (value <= 0f || reference <= 0f)
				return 0f;

			return value / (value + reference);
		}

		/// <summary>Extracts the feature vector. Allocation-free variant for the search's hot path.</summary>
		public static void Extract(AbstractState state, ForwardModel model, Span<float> features)
		{
			ArgumentNullException.ThrowIfNull(state);
			ArgumentNullException.ThrowIfNull(model);

			if (features.Length < Count)
				throw new ArgumentException($"Need {Count} slots.", nameof(features));

			var self = state.Self;
			var enemy = state.Enemy;
			var ownArmy = self.ArmyValue();

			features[(int)Feature.ArmyVsSeenEnemy] = Advantage(ownArmy, enemy.ArmyValue());
			features[(int)Feature.ArmyScale] = Saturate(ownArmy, 5000f);
			features[(int)Feature.EconomyScale] = Saturate(model.IncomePerSecond(self), 150f);
			features[(int)Feature.HarvesterScale] = Saturate(self.Harvesters, 8f);

			// The one thing that unambiguously means losing, and it needs no knowledge of the enemy
			// at all: our own base, measured against the most of it we have ever had.
			features[(int)Feature.BaseIntact] = self.PeakBaseIntegrity <= 0f
				? 1f
				: Math.Clamp(self.BaseIntegrity / self.PeakBaseIntegrity, 0f, 1f);

			var controlSum = 0f;
			var contested = 0;
			var explored = 0;
			for (var region = 0; region < state.RegionCount; region++)
			{
				controlSum += state.Control[region];
				if (self.ArmyValueIn(region) > 0f && enemy.ArmyValueIn(region) > 0f)
					contested++;

				if (state.VisibilityAge[region] <= StaleVisibilityTicks)
					explored++;
			}

			features[(int)Feature.MapControl] = state.RegionCount == 0
				? 0f
				: Math.Clamp(controlSum / state.RegionCount, -1f, 1f);

			features[(int)Feature.ContestedFraction] = state.RegionCount == 0
				? 0f
				: contested / (float)state.RegionCount;

			// What reconnaissance actually buys, stated as a number the evaluator can weigh against
			// the cost of the scouts that produced it.
			features[(int)Feature.ExploredFraction] = state.RegionCount == 0
				? 0f
				: explored / (float)state.RegionCount;

			// The constant term, which lets the model express a baseline win rate rather than being
			// forced through the origin.
			features[(int)Feature.Bias] = 1f;
		}

		/// <summary>Convenience overload for logging and tests.</summary>
		public static float[] Extract(AbstractState state, ForwardModel model)
		{
			var features = new float[Count];
			Extract(state, model, features);
			return features;
		}

		public static string NameOf(int index) =>
			index >= 0 && index < Count ? ((Feature)index).ToString() : "?";
	}
}
