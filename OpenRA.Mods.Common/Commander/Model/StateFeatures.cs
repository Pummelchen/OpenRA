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
	/// Every feature is a <i>relative advantage</i> in the range -1 to +1, never a raw quantity.
	/// That is deliberate. An absolute army value means nothing without knowing what the opponent
	/// has, it grows through the match so its weight would have to change with the clock, and it
	/// differs between mods and maps. A ratio is scale-free: +0.3 means the same thing at five
	/// minutes and at twenty, on a small map and a large one, which is what lets weights fitted on
	/// one set of games generalise to another.
	/// </para>
	/// </summary>
	public static class StateFeatures
	{
		/// <summary>The feature vector's layout. The last entry is the constant term.</summary>
		public enum Feature
		{
			ArmyAdvantage,
			IncomeAdvantage,
			HarvesterAdvantage,
			BaseIntegrityAdvantage,
			CashAdvantage,
			TechAdvantage,
			MapControl,
			ContestedFraction,
			Bias,
		}

		public const int Count = (int)Feature.Bias + 1;

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

		/// <summary>Extracts the feature vector. Allocation-free variant for the search's hot path.</summary>
		public static void Extract(AbstractState state, ForwardModel model, Span<float> features)
		{
			ArgumentNullException.ThrowIfNull(state);
			ArgumentNullException.ThrowIfNull(model);

			if (features.Length < Count)
				throw new ArgumentException($"Need {Count} slots.", nameof(features));

			var self = state.Self;
			var enemy = state.Enemy;

			features[(int)Feature.ArmyAdvantage] = Advantage(self.ArmyValue(), enemy.ArmyValue());
			features[(int)Feature.IncomeAdvantage] =
				Advantage(model.IncomePerSecond(self), model.IncomePerSecond(enemy));
			features[(int)Feature.HarvesterAdvantage] = Advantage(self.Harvesters, enemy.Harvesters);
			features[(int)Feature.BaseIntegrityAdvantage] = Advantage(self.BaseIntegrity, enemy.BaseIntegrity);
			features[(int)Feature.CashAdvantage] = Advantage(self.Cash, enemy.Cash);
			features[(int)Feature.TechAdvantage] =
				Advantage(System.Numerics.BitOperations.PopCount(self.TechBits),
					System.Numerics.BitOperations.PopCount(enemy.TechBits));

			// Map control, and how much of the map is being fought over. A commander that holds
			// everything uncontested is in a different position from one holding the same ground
			// with an army parked on the other side of it.
			var controlSum = 0f;
			var contested = 0;
			for (var region = 0; region < state.RegionCount; region++)
			{
				controlSum += state.Control[region];
				if (self.ArmyValueIn(region) > 0f && enemy.ArmyValueIn(region) > 0f)
					contested++;
			}

			features[(int)Feature.MapControl] = state.RegionCount == 0
				? 0f
				: Math.Clamp(controlSum / state.RegionCount, -1f, 1f);

			features[(int)Feature.ContestedFraction] = state.RegionCount == 0
				? 0f
				: contested / (float)state.RegionCount;

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
