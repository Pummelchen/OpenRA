#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of the
 * License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using NUnit.Framework;
using OpenRA.Mods.Common.Commander.Model;

namespace OpenRA.Test
{
	/// <summary>
	/// The combat half of the forward model: Lanchester's square law applied after reduction through
	/// the counter matrix. The reduction is the part that matters - the law assumes a homogeneous
	/// force, and applying it to raw counts is the classic way to be confidently wrong.
	/// </summary>
	[TestFixture]
	sealed class CombatResolverTest
	{
		const int R = RoleStats.Roles;

		/// <summary>Every role hits every other equally: the homogeneous case the raw law assumes.</summary>
		static RoleStats Uniform(float damage = 1f, float hitPoints = 1f)
		{
			var d = new float[R * R];
			Array.Fill(d, damage);
			var h = new float[R];
			Array.Fill(h, hitPoints);
			return new RoleStats(d, h);
		}

		static float[] Force(CombatRole role, float credits)
		{
			var f = new float[R];
			f[(int)role] = credits;
			return f;
		}

		[TestCase(TestName = "Equal forces annihilate each other.")]
		public void EqualForcesAnnihilate()
		{
			var stats = Uniform();
			var a = Force(CombatRole.Armor, 1000f);
			var b = Force(CombatRole.Armor, 1000f);

			var outcome = CombatResolver.Resolve(a, b, stats, 600f);

			Assert.That(outcome.AttackerRemaining, Is.EqualTo(outcome.DefenderRemaining).Within(0.01f),
				"A symmetric fight has a symmetric result.");
			Assert.That(outcome.AttackerRemaining, Is.LessThan(0.05f),
				"Given long enough, an even fight consumes both sides.");
		}

		[TestCase(TestName = "The square law rewards concentration super-linearly.")]
		public void ConcentrationIsSuperLinear()
		{
			var stats = Uniform();

			// Twice the force is four times the power, so it should win while keeping roughly
			// sqrt(1 - 1/4) = 0.866 of itself. A linear model would predict 0.5.
			var outcome = CombatResolver.Resolve(Force(CombatRole.Armor, 2000f), Force(CombatRole.Armor, 1000f),
				stats, 600f);

			Assert.That(outcome.AttackerWon, Is.True);
			Assert.That(outcome.DefenderRemaining, Is.EqualTo(0f).Within(0.001f));
			Assert.That(outcome.AttackerRemaining, Is.EqualTo(0.866f).Within(0.02f),
				"Survivors follow sqrt(1 - weaker/stronger); this is the whole reason to mass.");
			Assert.That(outcome.IsDecisive, Is.True);
		}

		[TestCase(TestName = "A narrow win is nearly as expensive as a loss.")]
		public void NarrowWinsAreExpensive()
		{
			var stats = Uniform();
			var outcome = CombatResolver.Resolve(Force(CombatRole.Armor, 1100f), Force(CombatRole.Armor, 1000f),
				stats, 600f);

			Assert.That(outcome.AttackerWon, Is.True);
			Assert.That(outcome.IsDecisive, Is.False,
				"Winning with a fifth of the force left is a trade, not a victory worth planning for.");
			Assert.That(outcome.AttackerRemaining, Is.LessThan(0.5f));
		}

		[TestCase(TestName = "Damage is counted only against what the enemy is actually made of.")]
		public void ReductionRespectsComposition()
		{
			// Anti-air that cannot touch ground, against a purely ground force.
			var damage = new float[R * R];
			damage[((int)CombatRole.AntiAir * R) + (int)CombatRole.Aircraft] = 5f;
			var hp = new float[R];
			Array.Fill(hp, 1f);
			var stats = new RoleStats(damage, hp);

			var rate = CombatResolver.DamageRate(Force(CombatRole.AntiAir, 1000f), Force(CombatRole.Armor, 1000f), stats);
			Assert.That(rate, Is.EqualTo(0f),
				"An anti-air battery adds nothing against tanks, however many credits it cost.");

			var versusAir = CombatResolver.DamageRate(Force(CombatRole.AntiAir, 1000f),
				Force(CombatRole.Aircraft, 1000f), stats);
			Assert.That(versusAir, Is.GreaterThan(0f));
		}

		[TestCase(TestName = "A mixed enemy dilutes a specialist's contribution proportionally.")]
		public void MixedCompositionDilutes()
		{
			var damage = new float[R * R];
			damage[((int)CombatRole.AntiAir * R) + (int)CombatRole.Aircraft] = 4f;
			var hp = new float[R];
			Array.Fill(hp, 1f);
			var stats = new RoleStats(damage, hp);

			var half = new float[R];
			half[(int)CombatRole.Aircraft] = 500f;
			half[(int)CombatRole.Armor] = 500f;

			var full = CombatResolver.DamageRate(Force(CombatRole.AntiAir, 100f), Force(CombatRole.Aircraft, 1000f), stats);
			var diluted = CombatResolver.DamageRate(Force(CombatRole.AntiAir, 100f), half, stats);

			Assert.That(diluted, Is.EqualTo(full / 2f).Within(0.01f),
				"Against a half-air force, anti-air is worth half as much - not all, and not nothing.");
		}

		[TestCase(TestName = "Forces that cannot hurt each other do not.")]
		public void MutuallyHarmlessForcesStandOff()
		{
			// Real durability, no way to hurt each other - not a degenerate zero-hit-point force.
			var hp = new float[R];
			Array.Fill(hp, 1f);
			var stats = new RoleStats(new float[R * R], hp);
			var outcome = CombatResolver.Resolve(Force(CombatRole.AntiAir, 500f), Force(CombatRole.Naval, 500f),
				stats, 120f);

			Assert.That(outcome.Resolved, Is.False);
			Assert.That(outcome.AttackerRemaining, Is.EqualTo(1f));
			Assert.That(outcome.DefenderRemaining, Is.EqualTo(1f),
				"Anti-air facing a submarine is a real situation, and must not divide by zero.");
		}

		[TestCase(TestName = "A force that cannot be shot back at takes no losses.")]
		public void OneSidedEngagement()
		{
			var damage = new float[R * R];
			for (var d = 0; d < R; d++)
				damage[((int)CombatRole.Artillery * R) + d] = 2f;

			var hp = new float[R];
			Array.Fill(hp, 1f);
			var stats = new RoleStats(damage, hp);

			var outcome = CombatResolver.Resolve(Force(CombatRole.Artillery, 100f), Force(CombatRole.Infantry, 100f),
				stats, 600f);

			Assert.That(outcome.AttackerRemaining, Is.EqualTo(1f));
			Assert.That(outcome.DefenderRemaining, Is.EqualTo(0f));
			Assert.That(outcome.Resolved, Is.True);
		}

		[TestCase(TestName = "A short engagement resolves partially, not instantly.")]
		public void TimeLimitedEngagement()
		{
			var stats = Uniform(damage: 0.001f);
			var brief = CombatResolver.Resolve(Force(CombatRole.Armor, 1000f), Force(CombatRole.Armor, 1000f),
				stats, 5f);

			Assert.That(brief.Resolved, Is.False, "Five seconds does not decide a battle of two thousand credits.");
			Assert.That(brief.AttackerRemaining, Is.LessThan(1f), "But it does cost something.");
			Assert.That(brief.AttackerRemaining, Is.GreaterThan(0.8f));

			// Longer must cost more: the model has to be monotone in time or the search could
            // conclude that waiting inside a battle is free.
			var longer = CombatResolver.Resolve(Force(CombatRole.Armor, 1000f), Force(CombatRole.Armor, 1000f),
				stats, 30f);
			Assert.That(longer.AttackerRemaining, Is.LessThan(brief.AttackerRemaining));
		}

		[TestCase(TestName = "Empty forces are handled without simulating anything.")]
		public void EmptyForces()
		{
			var stats = Uniform();
			var empty = new float[R];

			Assert.That(CombatResolver.Resolve(Force(CombatRole.Armor, 100f), empty, stats, 60f).AttackerRemaining,
				Is.EqualTo(1f));
			Assert.That(CombatResolver.Resolve(empty, Force(CombatRole.Armor, 100f), stats, 60f).DefenderRemaining,
				Is.EqualTo(1f));
			Assert.That(CombatResolver.Resolve(empty, empty, stats, 60f).Resolved, Is.True);
			Assert.That(CombatResolver.Resolve(Force(CombatRole.Armor, 100f), empty, stats, 0f).Resolved, Is.False,
				"A zero-length engagement decides nothing.");
		}

		[TestCase(TestName = "Required strength answers how much more is needed, not merely whether.")]
		public void RequiredStrength()
		{
			var stats = Uniform();
			var attacker = Force(CombatRole.Armor, 1000f);
			var defender = Force(CombatRole.Armor, 1000f);

			// To win while keeping 70% of the force, power must be 1/(1-0.49) ~ 1.96x, so force
			// must be sqrt(1.96) ~ 1.4x.
			var multiple = CombatResolver.RequiredStrengthMultiple(attacker, defender, stats, 0.7f);
			Assert.That(multiple, Is.EqualTo(1.4f).Within(0.05f));

			// And the answer must actually hold when simulated, or it is just arithmetic.
			var scaled = Force(CombatRole.Armor, 1000f * multiple);
			var outcome = CombatResolver.Resolve(scaled, defender, stats, 600f);
			Assert.That(outcome.AttackerRemaining, Is.EqualTo(0.7f).Within(0.03f),
				"The prediction must survive contact with the model that produced it.");

			Assert.That(CombatResolver.RequiredStrengthMultiple(attacker, new float[R], stats, 0.7f),
				Is.EqualTo(0f), "Nothing is needed to beat nothing.");
		}

		[TestCase(TestName = "Durability counts as much as damage.")]
		public void PowerIsDamageTimesDurability()
		{
			// Half the damage, twice the hit points: the same power, so an even fight.
			var damage = new float[R * R];
			var hp = new float[R];
			damage[((int)CombatRole.Infantry * R) + (int)CombatRole.Armor] = 2f;
			damage[((int)CombatRole.Infantry * R) + (int)CombatRole.Infantry] = 2f;
			damage[((int)CombatRole.Armor * R) + (int)CombatRole.Infantry] = 1f;
			damage[((int)CombatRole.Armor * R) + (int)CombatRole.Armor] = 1f;
			hp[(int)CombatRole.Infantry] = 1f;
			hp[(int)CombatRole.Armor] = 2f;
			var stats = new RoleStats(damage, hp);

			var infantry = Force(CombatRole.Infantry, 1000f);
			var armor = Force(CombatRole.Armor, 1000f);

			Assert.That(CombatResolver.Power(infantry, armor, stats),
				Is.EqualTo(CombatResolver.Power(armor, infantry, stats)).Within(1f),
				"Glass cannon and armoured brawler of equal power must be an even fight.");

			var outcome = CombatResolver.Resolve(infantry, armor, stats, 600f);
			Assert.That(outcome.AttackerRemaining, Is.EqualTo(outcome.DefenderRemaining).Within(0.02f));
		}
	}
}
