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

using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class CombatEstimatorTest
	{
		[TestCase(TestName = "Class weights rank armor above infantry and air/naval.")]
		public void ClassWeights()
		{
			Assert.That(CombatEstimator.ClassWeight(UnitClass.Infantry), Is.EqualTo(1f));
			Assert.That(CombatEstimator.ClassWeight(UnitClass.Armor), Is.EqualTo(3f));
			Assert.That(CombatEstimator.ClassWeight(UnitClass.Air), Is.EqualTo(2.5f));
			Assert.That(CombatEstimator.ClassWeight(UnitClass.Naval), Is.EqualTo(2f));
			Assert.That(CombatEstimator.ClassWeight(UnitClass.Structure), Is.EqualTo(2f));
			Assert.That(CombatEstimator.ClassWeight(UnitClass.Support), Is.EqualTo(0.5f));
		}

		[TestCase(TestName = "No enemies is a guaranteed win at zero cost.")]
		public void OverwhelmingAdvantage()
		{
			var (winRatio, loss) = CombatEstimator.Estimate(10f, 0f);
			Assert.That(winRatio, Is.EqualTo(1f));
			Assert.That(loss, Is.EqualTo(0f));
		}

		[TestCase(TestName = "No friends is a guaranteed loss.")]
		public void OverwhelmingDisadvantage()
		{
			var (winRatio, loss) = CombatEstimator.Estimate(0f, 10f);
			Assert.That(winRatio, Is.EqualTo(0f));
			Assert.That(loss, Is.EqualTo(1f));
		}

		[TestCase(TestName = "Even forces predict a draw with heavy expected losses.")]
		public void EvenForces()
		{
			var (winRatio, loss) = CombatEstimator.Estimate(10f, 10f);
			Assert.That(winRatio, Is.EqualTo(1f));
			Assert.That(loss, Is.EqualTo(0f));
		}

		[TestCase(TestName = "A 2:1 advantage predicts a win with moderate losses.")]
		public void TwoToOneAdvantage()
		{
			var (winRatio, loss) = CombatEstimator.Estimate(20f, 10f);
			Assert.That(winRatio, Is.EqualTo(2f));
			Assert.That(loss, Is.EqualTo(0.25f));
		}

		[TestCase(TestName = "A 1:2 disadvantage predicts heavy losses.")]
		public void TwoToOneDisadvantage()
		{
			var (winRatio, loss) = CombatEstimator.Estimate(10f, 20f);
			Assert.That(winRatio, Is.EqualTo(0.5f));
			Assert.That(loss, Is.EqualTo(0.5f));
		}

		[TestCase(TestName = "Matchups favor armor over infantry and punish rifles versus planes.")]
		public void MatchupFactors()
		{
			Assert.That(CombatEstimator.MatchupFactor(UnitClass.Armor, UnitClass.Infantry), Is.EqualTo(1.25f));
			Assert.That(CombatEstimator.MatchupFactor(UnitClass.Air, UnitClass.Naval), Is.EqualTo(1.2f));
			Assert.That(CombatEstimator.MatchupFactor(UnitClass.Infantry, UnitClass.Air), Is.EqualTo(0.5f));
			Assert.That(CombatEstimator.MatchupFactor(UnitClass.Naval, UnitClass.Armor), Is.EqualTo(0.5f));
			Assert.That(CombatEstimator.MatchupFactor(UnitClass.Infantry, UnitClass.Infantry), Is.EqualTo(1f));
		}

		[TestCase(TestName = "Matchup power scales each class against the enemy's dominant class.")]
		public void MatchupPower()
		{
			var friendly = new int[6];
			friendly[(int)UnitClass.Armor] = 10;
			var enemy = new int[6];
			enemy[(int)UnitClass.Infantry] = 10;

			// 10 armor × 3 weight × 1.25 (vs infantry) = 37.5.
			Assert.That(CombatEstimator.MatchupPower(friendly, enemy, 1f), Is.EqualTo(37.5f).Within(0.001f));
		}

		[TestCase(TestName = "Anti-air coverage suppresses air power linearly.")]
		public void SuppressAir()
		{
			Assert.That(CombatEstimator.SuppressAir(10f, 0f), Is.EqualTo(10f));
			Assert.That(CombatEstimator.SuppressAir(10f, 0.5f), Is.EqualTo(5f));
			Assert.That(CombatEstimator.SuppressAir(10f, 1f), Is.EqualTo(0f));
		}

		[TestCase(TestName = "Artillery contributes a pre-contact range advantage.")]
		public void RangeAdvantage()
		{
			Assert.That(CombatEstimator.RangeAdvantage(8f), Is.EqualTo(2f));
			Assert.That(CombatEstimator.RangeAdvantage(0f), Is.EqualTo(0f));
		}

		[TestCase(TestName = "Terrain factor penalizes attacking into hard, exposed ground.")]
		public void TerrainFactor()
		{
			Assert.That(CombatEstimator.TerrainFactor(0f, 0f), Is.EqualTo(1f));
			Assert.That(CombatEstimator.TerrainFactor(1f, 0f), Is.EqualTo(0.75f));
			Assert.That(CombatEstimator.TerrainFactor(0f, 1f), Is.EqualTo(0.9f));
		}

		[TestCase(TestName = "The composed estimate suppresses air and rewards artillery.")]
		public void ComposedEstimate()
		{
			// 10 friendly power (5 air) with 2 artillery, against 10 enemy power with no AA, on open ground.
			var (winRatio, _) = CombatEstimator.Estimate(10f, 10f, 5f, 0f, 2f, 0f, 0f, 0f, 0f, 0f);

			// Friendly = (10 - 5 air + 5 air + 0.5 artillery) = 10.5 vs enemy 10 -> slight edge.
			Assert.That(winRatio, Is.EqualTo(10.5f / 10f).Within(0.001f));
		}

		[TestCase(TestName = "Major risks flag enemy AA, enemy artillery, and missing air cover.")]
		public void MajorRisks()
		{
			var risks = CombatEstimator.MajorRisks(
				enemyAntiAir: 1f, friendlyAir: 1f, enemyArtillery: 1f, friendlyArtillery: 0f, enemyAir: 0f, friendlyAntiAir: 0f).ToArray();

			Assert.That(risks, Is.EquivalentTo(new[] { "enemy_anti_air", "enemy_artillery", "no_air_cover" }));
		}

		[TestCase(TestName = "Capability gaps request anti-air when the enemy fields planes we cannot answer.")]
		public void CapabilityGaps()
		{
			var gaps = CombatEstimator.CapabilityGaps(
				enemyAir: 1f, friendlyAntiAir: 0f, enemyArmor: 1f, friendlyArtillery: 0f, enemyAntiAir: 0f, friendlyAir: 0f).ToArray();

			Assert.That(gaps, Is.EquivalentTo(new[] { "anti_air", "anti_armor" }));
		}

		[TestCase(TestName = "Reinforcement advantage names the side expected to receive help.")]
		public void ReinforcementAdvantage()
		{
			Assert.That(CombatEstimator.ReinforcementAdvantage(0.8f, 0.2f), Is.EqualTo("friendly"));
			Assert.That(CombatEstimator.ReinforcementAdvantage(0.2f, 0.8f), Is.EqualTo("enemy"));
			Assert.That(CombatEstimator.ReinforcementAdvantage(0.4f, 0.4f), Is.EqualTo("even"));
		}

		[TestCase(TestName = "Representative RA compositions produce sensible relative outcomes.")]
		public void RepresentativeEngagements()
		{
			var armor = new int[6];
			armor[(int)UnitClass.Armor] = 10;
			var rifles = new int[6];
			rifles[(int)UnitClass.Infantry] = 10;

			// Armor dominates rifles.
			var armorVsRifles = CombatEstimator.Estimate(
				CombatEstimator.MatchupPower(armor, rifles, 1f), CombatEstimator.MatchupPower(rifles, armor, 1f),
				0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
			Assert.That(armorVsRifles.WinRatio, Is.GreaterThan(2f));

			// Even rifles against rifles are an even fight.
			var riflesVsRifles = CombatEstimator.Estimate(
				CombatEstimator.MatchupPower(rifles, rifles, 1f), CombatEstimator.MatchupPower(rifles, rifles, 1f),
				0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
			Assert.That(riflesVsRifles.WinRatio, Is.EqualTo(1f).Within(0.001f));

			// Air against no anti-air is strong; air against full anti-air is halved.
			var air = new int[6];
			air[(int)UnitClass.Air] = 5;
			var airVsRifles = CombatEstimator.Estimate(
				CombatEstimator.MatchupPower(air, rifles, 1f), CombatEstimator.MatchupPower(rifles, air, 1f),
				5f * CombatEstimator.ClassWeight(UnitClass.Air), 0f, 0f, 0f, 0f, 0f, 0f, 0f);
			var airVsCoveredRifles = CombatEstimator.Estimate(
				CombatEstimator.MatchupPower(air, rifles, 1f), CombatEstimator.MatchupPower(rifles, air, 1f),
				5f * CombatEstimator.ClassWeight(UnitClass.Air), 0f, 0f, 0f, 0f, 1f, 0f, 0f);

			Assert.That(airVsCoveredRifles.WinRatio, Is.LessThan(airVsRifles.WinRatio));
		}
	}
}
