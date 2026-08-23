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
	/// Predicts the outcome of an engagement between two mixed forces, by Lanchester's square law
	/// applied after reduction through the counter matrix.
	/// </para>
	/// <para>
	/// <b>Power = damage x durability.</b> Both scale with force size, so power scales with the
	/// square of it, which is the square law and the reason concentration wins: two equal forces
	/// combined are four times as powerful, not twice. The side with more power wins, and the
	/// fraction it keeps is <c>sqrt(1 - weaker/stronger)</c> - so a narrow win is nearly as
	/// expensive as a loss, and only a large advantage is cheap.
	/// </para>
	/// <para>
	/// The reduction is what makes any of this legitimate. Damage is evaluated against the
	/// composition actually present, so anti-air contributes nothing against tanks and artillery
	/// contributes nothing against aircraft, rather than both being counted as generic strength.
	/// </para>
	/// </summary>
	public static class CombatResolver
	{
		/// <summary>How an engagement of a given duration turned out.</summary>
		public readonly record struct Outcome(
			float AttackerRemaining,
			float DefenderRemaining,
			bool Resolved,
			float SecondsElapsed)
		{
			/// <summary>Fraction of the attacking force still alive, 0 to 1.</summary>
			public float AttackerLossFraction => 1f - AttackerRemaining;

			/// <summary>Fraction of the defending force still alive, 0 to 1.</summary>
			public float DefenderLossFraction => 1f - DefenderRemaining;

			/// <summary>True if one side was wiped out within the time allowed.</summary>
			public bool AttackerWon => Resolved && AttackerRemaining > DefenderRemaining;

			/// <summary>
			/// A win worth having: the field is taken and over half the force walks away from it.
			/// A commander that only ever wins narrowly is trading, not winning.
			/// </summary>
			public bool IsDecisive => AttackerWon && AttackerRemaining >= 0.5f;
		}

		/// <summary>
		/// Total damage per second one force deals to another, evaluated against the defender's
		/// actual composition. Forces are vectors of credits by role.
		/// </summary>
		public static float DamageRate(ReadOnlySpan<float> attacker, ReadOnlySpan<float> defender, RoleStats stats)
		{
			ArgumentNullException.ThrowIfNull(stats);

			var defenderTotal = 0f;
			for (var d = 0; d < RoleStats.Roles && d < defender.Length; d++)
				defenderTotal += Math.Max(0f, defender[d]);

			if (defenderTotal <= 0f)
				return 0f;

			var rate = 0f;
			for (var a = 0; a < RoleStats.Roles && a < attacker.Length; a++)
			{
				var credits = Math.Max(0f, attacker[a]);
				if (credits <= 0f)
					continue;

				// Weight by what the defender is actually made of: shooting at a force that is
				// nine-tenths aircraft, an artillery piece contributes a tenth of its ground damage,
				// not all of it.
				var weighted = 0f;
				for (var d = 0; d < RoleStats.Roles && d < defender.Length; d++)
				{
					var share = Math.Max(0f, defender[d]) / defenderTotal;
					if (share > 0f)
						weighted += share * stats.DamageVersus((CombatRole)a, (CombatRole)d);
				}

				rate += credits * weighted;
			}

			return rate;
		}

		/// <summary>Total hit points of a force, in the same credit terms.</summary>
		public static float HitPoints(ReadOnlySpan<float> force, RoleStats stats)
		{
			ArgumentNullException.ThrowIfNull(stats);

			var total = 0f;
			for (var r = 0; r < RoleStats.Roles && r < force.Length; r++)
				total += Math.Max(0f, force[r]) * stats.HitPoints((CombatRole)r);

			return total;
		}

		/// <summary>
		/// Lanchester power: damage times durability. Comparing two of these decides who wins before
		/// any integration is done, which is why the search can prune hopeless attacks cheaply.
		/// </summary>
		public static float Power(ReadOnlySpan<float> force, ReadOnlySpan<float> against, RoleStats stats)
		{
			return DamageRate(force, against, stats) * HitPoints(force, stats);
		}

		/// <summary>
		/// <para>
		/// Runs an engagement for at most <paramref name="seconds"/> and reports the surviving
		/// fraction of each side.
		/// </para>
		/// <para>
		/// Solved in closed form rather than stepped, so a two-minute lookahead costs the same as a
		/// two-second one. With <c>c = sqrt(kA kB)</c> the strengths follow
		/// <c>f(t) = cosh(ct) - (k/c) sinh(ct)</c>, which is exact for the continuous model.
		/// </para>
		/// </summary>
		public static Outcome Resolve(ReadOnlySpan<float> attacker, ReadOnlySpan<float> defender,
			RoleStats stats, float seconds)
		{
			ArgumentNullException.ThrowIfNull(stats);
			if (seconds <= 0f)
				return new Outcome(1f, 1f, false, 0f);

			var hpA = HitPoints(attacker, stats);
			var hpB = HitPoints(defender, stats);

			// An empty side is already resolved; nothing needs simulating.
			if (hpA <= 0f && hpB <= 0f)
				return new Outcome(0f, 0f, true, 0f);
			if (hpA <= 0f)
				return new Outcome(0f, 1f, true, 0f);
			if (hpB <= 0f)
				return new Outcome(1f, 0f, true, 0f);

			var damageA = DamageRate(attacker, defender, stats);
			var damageB = DamageRate(defender, attacker, stats);

			// Two forces that cannot hurt each other stand and look at one another. This is a real
			// case - anti-air against submarines - and must not divide by zero.
			if (damageA <= 0f && damageB <= 0f)
				return new Outcome(1f, 1f, false, seconds);

			// Rate at which each side's strength fraction is destroyed by the other, at full strength.
			var kA = damageA / hpB;   // how fast the attacker erases the defender
			var kB = damageB / hpA;   // and the reverse

			if (kA <= 0f)
				return ResolveOneSided(false, kB, seconds);
			if (kB <= 0f)
				return ResolveOneSided(true, kA, seconds);

			var c = MathF.Sqrt(kA * kB);
			var ct = c * seconds;

			// cosh and sinh overflow for large arguments, and a fight that long is over regardless.
			const float Saturated = 30f;
			if (ct > Saturated)
				ct = Saturated;

			var cosh = MathF.Cosh(ct);
			var sinh = MathF.Sinh(ct);

			var fA = cosh - (kB / c * sinh);
			var fB = cosh - (kA / c * sinh);

			// Whichever hits zero first ends the engagement; solve for that moment so the survivor's
			// strength is read at the right time rather than past it.
			if (fA <= 0f || fB <= 0f)
			{
				var attackerWins = kA > kB;
				var winnerK = attackerWins ? kA : kB;
				var loserK = attackerWins ? kB : kA;

				// atanh(c / winnerK): the instant the loser reaches zero.
				var ratio = c / winnerK;
				var elapsed = ratio >= 1f ? seconds : MathF.Atanh(ratio) / c;

				// Surviving fraction of the winner, from the square-law invariant.
				var survivor = MathF.Sqrt(Math.Max(0f, 1f - (loserK / winnerK)));

				return attackerWins
					? new Outcome(survivor, 0f, true, Math.Min(elapsed, seconds))
					: new Outcome(0f, survivor, true, Math.Min(elapsed, seconds));
			}

			return new Outcome(Math.Clamp(fA, 0f, 1f), Math.Clamp(fB, 0f, 1f), false, seconds);
		}

		/// <summary>One side cannot shoot back, so its strength decays linearly until it is gone.</summary>
		static Outcome ResolveOneSided(bool attackerShoots, float k, float seconds)
		{
			var destroyed = k * seconds;
			if (destroyed >= 1f)
				return attackerShoots
					? new Outcome(1f, 0f, true, 1f / k)
					: new Outcome(0f, 1f, true, 1f / k);

			return attackerShoots
				? new Outcome(1f, 1f - destroyed, false, seconds)
				: new Outcome(1f - destroyed, 1f, false, seconds);
		}

		/// <summary>
		/// The force needed to beat <paramref name="defender"/> while keeping
		/// <paramref name="survivingFraction"/> of itself, as a multiple of the attacker's current
		/// strength. This is what turns "am I winning" into "how much more do I need", which is the
		/// question a commander deciding whether to commit actually has.
		/// </summary>
		public static float RequiredStrengthMultiple(ReadOnlySpan<float> attacker, ReadOnlySpan<float> defender,
			RoleStats stats, float survivingFraction)
		{
			ArgumentNullException.ThrowIfNull(stats);

			var powerA = Power(attacker, defender, stats);
			var powerB = Power(defender, attacker, stats);
			if (powerB <= 0f)
				return 0f;

			if (powerA <= 0f)
				return float.PositiveInfinity;

			survivingFraction = Math.Clamp(survivingFraction, 0f, 0.999f);

			// Power scales with the square of force size, so the multiple is a square root: to keep
			// a given fraction the attacker needs powerA' = powerB / (1 - f^2).
			var requiredPower = powerB / (1f - (survivingFraction * survivingFraction));
			return MathF.Sqrt(requiredPower / powerA);
		}
	}
}
