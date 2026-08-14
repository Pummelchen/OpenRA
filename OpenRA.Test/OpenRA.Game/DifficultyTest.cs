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

using NUnit.Framework;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class DifficultyTest
	{
		[TestCase(TestName = "The scalar convenience sets every axis together.")]
		public void ScalarSetsAll()
		{
			var difficulty = CoalitionDifficulty.FromScalar(2);
			Assert.That(difficulty.CommandQuality, Is.EqualTo(2));
			Assert.That(difficulty.ReactionSpeed, Is.EqualTo(2));
			Assert.That(difficulty.MicroPrecision, Is.EqualTo(2));
			Assert.That(difficulty.CoordinationStrength, Is.EqualTo(2));
			Assert.That(difficulty.EconomicBonus, Is.EqualTo(0), "The scalar must not grant an economic bonus.");
			Assert.That(difficulty.Intelligence, Is.EqualTo(0), "The scalar must not grant an intelligence advantage.");
		}

		[TestCase(TestName = "Intelligence is an independent axis: fair fog by default, omniscient only at the top.")]
		public void IntelligenceAxis()
		{
			var fair = new CoalitionDifficulty { Intelligence = 0 };
			var revealed = new CoalitionDifficulty { Intelligence = 2 };
			var omniscient = new CoalitionDifficulty { Intelligence = 3 };

			Assert.That(fair.IsOmniscient, Is.False, "Fair fog must be the default.");
			Assert.That(revealed.IsOmniscient, Is.False, "Revealed structures is still not omniscient.");
			Assert.That(omniscient.IsOmniscient, Is.True);

			// The fair-but-brutal profile keeps intelligence at fair fog, like the economic bonus.
			var brutal = new CoalitionDifficulty
			{
				CommandQuality = 3,
				CoordinationStrength = 3,
				ReactionSpeed = 3,
				MicroPrecision = 3,
				EconomicBonus = 0,
				Intelligence = 0
			};
			Assert.That(brutal.IsOmniscient, Is.False, "Extreme command does not imply omniscience.");
		}

		[TestCase(TestName = "The scalar is clamped to 0..3.")]
		public void ScalarClamped()
		{
			Assert.That(CoalitionDifficulty.FromScalar(-5).CommandQuality, Is.EqualTo(0));
			Assert.That(CoalitionDifficulty.FromScalar(9).CommandQuality, Is.EqualTo(3));
		}

		[TestCase(TestName = "Command quality scales thresholds: easier is easier to commit.")]
		public void CommandScaling()
		{
			var easy = CoalitionDifficulty.FromScalar(0);
			var supreme = CoalitionDifficulty.FromScalar(3);

			Assert.That(easy.Scale(100f), Is.EqualTo(150f).Within(0.001f), "Easy raises the threshold.");
			Assert.That(supreme.Scale(100f), Is.EqualTo(75f).Within(0.001f), "Supreme lowers it.");
		}

		[TestCase(TestName = "Coordination strength tightens the reserve.")]
		public void ReserveTightening()
		{
			Assert.That(CoalitionDifficulty.FromScalar(0).ScaledReserveFraction(), Is.EqualTo(8));
			Assert.That(CoalitionDifficulty.FromScalar(3).ScaledReserveFraction(), Is.EqualTo(3));
		}

		[TestCase(TestName = "Micro precision pulls units earlier (higher threshold).")]
		public void MicroPrecision()
		{
			var precise = CoalitionDifficulty.FromScalar(3);
			var sloppy = CoalitionDifficulty.FromScalar(0);

			Assert.That(precise.RetreatHealthPercent(), Is.EqualTo(30));
			Assert.That(sloppy.RetreatHealthPercent(), Is.EqualTo(45));
		}

		[TestCase(TestName = "Reaction speed scales the response delay.")]
		public void ReactionScaling()
		{
			var slow = CoalitionDifficulty.FromScalar(0);
			var fast = CoalitionDifficulty.FromScalar(3);

			Assert.That(slow.ReactionMultiplier(), Is.EqualTo(1.5f).Within(0.001f));
			Assert.That(fast.ReactionMultiplier(), Is.EqualTo(0.75f).Within(0.001f));
		}

		[TestCase(TestName = "Independent axes can be set individually for a fair-but-brutal profile.")]
		public void IndependentAxes()
		{
			var difficulty = new CoalitionDifficulty
			{
				CommandQuality = 3,
				CoordinationStrength = 3,
				ReactionSpeed = 3,
				MicroPrecision = 3,
				EconomicBonus = 0
			};

			Assert.That(difficulty.Scale(100f), Is.EqualTo(75f).Within(0.001f));
			Assert.That(difficulty.ScaledReserveFraction(), Is.EqualTo(3));
			Assert.That(difficulty.EconomicBonus, Is.EqualTo(0), "Fair mode has zero economic cheating.");
		}
	}
}
