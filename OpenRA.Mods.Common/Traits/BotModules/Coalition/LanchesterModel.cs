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

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	/// <summary>
	/// <para>
	/// Combat outcome prediction by Lanchester's square law (handbook §15.2).
	/// </para>
	/// <para>
	/// The commander previously committed on a strength ratio, which is a linear model and gets the
	/// most important question wrong. Under the square law fighting power scales with the *square* of
	/// numbers, so two waves of ten do not equal one wave of twenty - they lose to it badly. That is
	/// the arithmetic behind "arrive concentrated", and a linear ratio cannot express it: it rates
	/// both forces identically at 20 units.
	/// </para>
	/// <para>
	/// The model also answers a question a ratio cannot: how much force is *left* after winning.
	/// An attack that wins with nothing remaining has taken no ground, because there is nothing to
	/// hold it with.
	/// </para>
	/// </summary>
	public static class LanchesterModel
	{
		/// <summary>Predicted outcome of an engagement.</summary>
		public readonly record struct Outcome(bool AttackerWins, float SurvivingStrength, float LossFraction)
		{
			/// <summary>
			/// True when the winner has enough left to do something with the victory. Half the force
			/// is the line: an attack that loses more than that cannot hold the ground it just took,
			/// so it has bought a favourable exchange and no territory - which is precisely the
			/// failure mode this whole model exists to stop the commander repeating.
			/// </summary>
			public bool IsDecisive => AttackerWins && LossFraction < 0.5f;
		}

		/// <summary>
		/// Fighting power under the square law: effectiveness times the square of numbers. Strength
		/// is expressed as an aggregate combat value rather than a unit count, so mixed forces
		/// compare correctly.
		/// </summary>
		public static float Power(float strength, float effectiveness = 1f)
		{
			var s = Math.Max(0f, strength);
			return Math.Max(0f, effectiveness) * s * s;
		}

		/// <summary>
		/// Predicts an engagement. <paramref name="attackerEffectiveness"/> and
		/// <paramref name="defenderEffectiveness"/> carry everything that is not numbers - the
		/// counter matrix, terrain, static defence - so the square law applies to strength alone.
		/// </summary>
		public static Outcome Predict(float attackerStrength, float defenderStrength,
			float attackerEffectiveness = 1f, float defenderEffectiveness = 1f)
		{
			var attackerPower = Power(attackerStrength, attackerEffectiveness);
			var defenderPower = Power(defenderStrength, defenderEffectiveness);

			if (attackerPower <= 0f)
				return new Outcome(false, 0f, 1f);

			if (defenderPower <= 0f)
				return new Outcome(true, attackerStrength, 0f);

			if (attackerPower <= defenderPower)
			{
				// The attacker is destroyed; the defender keeps what the square law leaves it.
				var defenderSurvivors = MathF.Sqrt((defenderPower - attackerPower)
					/ Math.Max(0.0001f, defenderEffectiveness));
				return new Outcome(false, defenderSurvivors, 1f);
			}

			// Survivors follow from the Lanchester invariant: the winner keeps the square root of
			// the difference in power, not the difference in numbers.
			var survivors = MathF.Sqrt((attackerPower - defenderPower)
				/ Math.Max(0.0001f, attackerEffectiveness));

			var lossFraction = attackerStrength <= 0f ? 1f
				: Math.Clamp(1f - survivors / attackerStrength, 0f, 1f);

			return new Outcome(true, survivors, lossFraction);
		}

		/// <summary>
		/// Strength needed to win with a given fraction of the force surviving. This is what the
		/// commander should mass to before committing, rather than a fixed unit count that is
		/// meaningless against an unknown defender.
		/// </summary>
		public static float RequiredStrength(float defenderStrength, float desiredSurvivingFraction = 0.5f,
			float attackerEffectiveness = 1f, float defenderEffectiveness = 1f)
		{
			if (defenderStrength <= 0f)
				return 0f;

			var defenderPower = Power(defenderStrength, defenderEffectiveness);
			var survivingFraction = Math.Clamp(desiredSurvivingFraction, 0f, 0.99f);

			// Solve attackerPower - defenderPower = effectiveness * (fraction * N)^2 for N.
			var denominator = Math.Max(0.0001f, attackerEffectiveness * (1f - survivingFraction * survivingFraction));
			return MathF.Sqrt(defenderPower / denominator);
		}

		/// <summary>
		/// <para>
		/// Whether splitting a force between two objectives is better than concentrating it.
		/// </para>
		/// <para>
		/// Under the square law splitting is almost always wrong against a single defender: two
		/// halves each field a quarter of the concentrated power. It is correct only when the halves
		/// fight genuinely separate battles, so this returns true only when both fragments still win
		/// their own engagement decisively.
		/// </para>
		/// </summary>
		public static bool ShouldSplit(float strength, float defenderA, float defenderB)
		{
			if (defenderA <= 0f || defenderB <= 0f)
				return false;

			var half = strength / 2f;
			return Predict(half, defenderA).IsDecisive && Predict(half, defenderB).IsDecisive;
		}

		/// <summary>
		/// Concentration advantage: how much more power a single force has than the same strength
		/// split in two. Always 2 under the square law, and stated explicitly because it is the
		/// reason waves must not trickle.
		/// </summary>
		public static float ConcentrationAdvantage(float strength)
		{
			if (strength <= 0f)
				return 1f;

			var concentrated = Power(strength);
			var split = 2f * Power(strength / 2f);
			return split <= 0f ? 1f : concentrated / split;
		}
	}
}
