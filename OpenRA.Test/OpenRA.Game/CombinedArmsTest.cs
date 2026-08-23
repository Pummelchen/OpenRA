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
	/// Combined-arms composition of a launched wave (reqs 198, 228-233, 237, 350). These properties
	/// were previously computed only to format a telemetry string, so "armour is escorted by
	/// infantry" was something a wave happened to be rather than something anything asserted.
	/// </summary>
	[TestFixture]
	sealed class CombinedArmsTest
	{
		static WaveComposition Wave(int armor = 0, int infantry = 0, int artillery = 0,
			int antiAir = 0, int air = 0, int naval = 0, int special = 0)
		{
			return new WaveComposition(armor, infantry, artillery, antiAir, air, naval, special);
		}

		[TestCase(TestName = "Armour advancing with infantry is combined arms; armour alone is not (req 228).")]
		public void ArmorInfantryPairing()
		{
			Assert.That(Wave(armor: 6, infantry: 4).ArmorHasInfantrySupport, Is.True);
			Assert.That(Wave(armor: 10).ArmorHasInfantrySupport, Is.False);
			Assert.That(Wave(infantry: 10).ArmorHasInfantrySupport, Is.False,
				"Infantry with no armour is not armour-with-support.");
		}

		[TestCase(TestName = "Artillery is only supported when something can screen it (req 229).")]
		public void ArtilleryNeedsAScreen()
		{
			Assert.That(Wave(armor: 6, artillery: 2).ArtilleryHasScreen, Is.True);
			Assert.That(Wave(infantry: 6, artillery: 2).ArtilleryHasScreen, Is.True);
			Assert.That(Wave(artillery: 4).ArtilleryHasScreen, Is.False,
				"Unescorted artillery is the classic way to lose artillery.");
			Assert.That(Wave(artillery: 4, air: 4).ArtilleryHasScreen, Is.False,
				"Aircraft do not screen ground artillery from a ground assault.");
		}

		[TestCase(TestName = "Anti-air escorts count only when there is something to escort (req 230).")]
		public void AntiAirEscortsGround()
		{
			Assert.That(Wave(armor: 8, antiAir: 2).GroundHasAntiAirEscort, Is.True);
			Assert.That(Wave(antiAir: 4).GroundHasAntiAirEscort, Is.False,
				"A wave of only AA units is not an escorted ground force.");
			Assert.That(Wave(armor: 8).GroundHasAntiAirEscort, Is.False);
		}

		[TestCase(TestName = "Cross-domain support requires both domains present (reqs 231, 232, 233).")]
		public void CrossDomainSupport()
		{
			Assert.That(Wave(armor: 6, air: 3).GroundHasAirSupport, Is.True);
			Assert.That(Wave(air: 6).GroundHasAirSupport, Is.False);
			Assert.That(Wave(armor: 6, naval: 2).GroundHasNavalSupport, Is.True);
			Assert.That(Wave(naval: 6).GroundHasNavalSupport, Is.False);
			Assert.That(Wave(armor: 6, special: 1).GroundHasSpecialSupport, Is.True);
			Assert.That(Wave(special: 1).GroundHasSpecialSupport, Is.True,
				"A special asset is itself a ground unit, so it escorts and is escorted.");
		}

		[TestCase(TestName = "A mass air attack is a concentration, not aircraft trickling in (req 198).")]
		public void MassAirRequiresConcentration()
		{
			Assert.That(Wave(air: WaveComposition.MassAirMinimum).IsMassAirAttack, Is.True);
			Assert.That(Wave(air: WaveComposition.MassAirMinimum - 1).IsMassAirAttack, Is.False,
				"Aircraft sent one at a time are killed one at a time.");
		}

		[TestCase(TestName = "Arms represented distinguishes a combined operation from a blob.")]
		public void ArmsRepresented()
		{
			Assert.That(Wave(armor: 20).ArmsRepresented, Is.EqualTo(1));
			Assert.That(Wave(armor: 20).IsCombinedArms, Is.False, "Twenty tanks is one arm, not a combined operation.");

			var combined = Wave(armor: 8, infantry: 6, artillery: 2, air: 3, naval: 2);
			Assert.That(combined.ArmsRepresented, Is.EqualTo(5));
			Assert.That(combined.IsCombinedArms, Is.True);
		}

		[TestCase(TestName = "Land excludes air and naval, and totals add up.")]
		public void CountsAreConsistent()
		{
			var wave = Wave(armor: 8, infantry: 6, artillery: 2, antiAir: 2, air: 3, naval: 4, special: 1);
			Assert.That(wave.Land, Is.EqualTo(19));
			Assert.That(wave.Total, Is.EqualTo(26));
		}

		[TestCase(TestName = "Negative counts are clamped rather than corrupting the totals.")]
		public void NegativeCountsAreClamped()
		{
			var wave = new WaveComposition(-5, 3, -2, 0, 0, 0);
			Assert.That(wave.Armor, Is.Zero);
			Assert.That(wave.Artillery, Is.Zero);
			Assert.That(wave.Total, Is.EqualTo(3));
		}

		[TestCase(TestName = "A breach force is only split when both halves stay viable (reqs 237, 350).")]
		public void ExploitationForceSeparation()
		{
			// Splitting a small force produces two fragments that each lose; below the threshold,
			// committing everything to the breach is the correct call.
			Assert.That(WaveComposition.CanSeparateExploitationForce(committed: 20, reserve: 10, minimumViableForce: 10), Is.True);
			Assert.That(WaveComposition.CanSeparateExploitationForce(committed: 12, reserve: 10, minimumViableForce: 10), Is.False,
				"A breach force below twice the viable minimum cannot spare an exploitation echelon.");
			Assert.That(WaveComposition.CanSeparateExploitationForce(committed: 40, reserve: 4, minimumViableForce: 10), Is.False,
				"An exploitation force too small to exploit is not a second echelon.");
		}
	}
}
