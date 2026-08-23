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

using NUnit.Framework;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

namespace OpenRA.Test
{
	/// <summary>
	/// What the army is made of (handbook §7.3). Each production queue independently picking its
	/// best buildable unit floods infantry: the default base has six infantry buildings against four
	/// war factories, and infantry cost a seventh of a tank. Measured waves were 43 infantry to 6
	/// tanks with the armour count falling over the match.
	/// </summary>
	[TestFixture]
	sealed class ArmyCompositionTest
	{
		[TestCase(TestName = "A screen is built before there is armour to screen.")]
		public void FloorComesFirst()
		{
			Assert.That(ArmyComposition.ShouldProduceInfantry(infantry: 0, armor: 0), Is.True);
			Assert.That(ArmyComposition.ShouldProduceInfantry(
				ArmyComposition.MinimumInfantry - 1, armor: 0), Is.True,
				"An armour-only force with no screen is its own failure mode.");
		}

		[TestCase(TestName = "Past the ratio the barracks idle so the credits buy armour instead.")]
		public void ScreenIsCapped()
		{
			// 43 infantry to 6 tanks was the measured failure; the ratio stops it at 9.
			Assert.That(ArmyComposition.ShouldProduceInfantry(infantry: 43, armor: 6), Is.False);
			Assert.That(ArmyComposition.ShouldProduceInfantry(infantry: 8, armor: 10), Is.True,
				"Below the ratio the screen is still short.");
			Assert.That(ArmyComposition.ShouldProduceInfantry(infantry: 15, armor: 10), Is.False);
		}

		[TestCase(TestName = "A badly skewed army is reported, not just quietly fielded.")]
		public void ImbalanceIsVisible()
		{
			Assert.That(ArmyComposition.IsInfantryHeavy(infantry: 43, armor: 6), Is.True);
			Assert.That(ArmyComposition.IsInfantryHeavy(infantry: 15, armor: 10), Is.False);
			Assert.That(ArmyComposition.IsInfantryHeavy(infantry: 3, armor: 0), Is.False,
				"An opening with no armour yet is not an imbalance.");
		}

		[TestCase(TestName = "Artillery scales with armour, because it exists to escort the column.")]
		public void ArtilleryFollowsArmour()
		{
			Assert.That(ArmyComposition.ShouldProduceArtillery(artillery: 0, armor: 10), Is.True);
			Assert.That(ArmyComposition.ShouldProduceArtillery(artillery: 2, armor: 10), Is.False);
			Assert.That(ArmyComposition.ShouldProduceArtillery(artillery: 0, armor: 0), Is.False,
				"Artillery with nothing to screen it is artillery about to be lost.");
		}

		[TestCase(TestName = "Anti-air is a token escort until enemy air is actually seen.")]
		public void AntiAirRespondsToEvidence()
		{
			var speculative = ArmyComposition.ShouldProduceAntiAir(antiAir: 2, armor: 10, enemyAirSeen: false);
			var confirmed = ArmyComposition.ShouldProduceAntiAir(antiAir: 2, armor: 10, enemyAirSeen: true);

			Assert.That(speculative, Is.False, "Before air is seen the escort requirement is a guess.");
			Assert.That(confirmed, Is.True, "After it is seen the requirement is real.");
			Assert.That(ArmyComposition.ShouldProduceAntiAir(0, 0, true), Is.False);
		}

		[TestCase(TestName = "Support requirements round up, so a small column still gets one.")]
		public void SupportRoundsUp()
		{
			Assert.That(ArmyComposition.DesiredSupport(armor: 2, perArmor: 0.2f), Is.EqualTo(1));
			Assert.That(ArmyComposition.DesiredSupport(armor: 0, perArmor: 0.2f), Is.Zero);
			Assert.That(ArmyComposition.DesiredSupport(armor: 20, perArmor: 0.2f), Is.EqualTo(4));
		}
	}
}
