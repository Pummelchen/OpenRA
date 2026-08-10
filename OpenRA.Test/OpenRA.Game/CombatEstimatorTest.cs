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
	}
}
