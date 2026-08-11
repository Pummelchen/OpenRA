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
	sealed class TargetEvaluatorTest
	{
		[TestCase(TestName = "Economic value is nonzero for refineries and silos only.")]
		public void EconomicValue()
		{
			Assert.That(TargetEvaluator.EconomicValue("proc"), Is.EqualTo(10f));
			Assert.That(TargetEvaluator.EconomicValue("silo"), Is.EqualTo(6f));
			Assert.That(TargetEvaluator.EconomicValue("weap"), Is.EqualTo(0f));
			Assert.That(TargetEvaluator.EconomicValue("e1"), Is.EqualTo(0f));
		}

		[TestCase(TestName = "Production value is nonzero for factories and production structures.")]
		public void ProductionValue()
		{
			Assert.That(TargetEvaluator.ProductionValue("weap"), Is.EqualTo(10f));
			Assert.That(TargetEvaluator.ProductionValue("afld"), Is.EqualTo(10f));
			Assert.That(TargetEvaluator.ProductionValue("proc"), Is.EqualTo(0f));
		}

		[TestCase(TestName = "Technology value is nonzero for tech and superweapon structures.")]
		public void TechnologyValue()
		{
			Assert.That(TargetEvaluator.TechnologyValue("atek"), Is.EqualTo(10f));
			Assert.That(TargetEvaluator.TechnologyValue("pdox"), Is.EqualTo(10f));
			Assert.That(TargetEvaluator.TechnologyValue("weap"), Is.EqualTo(0f));
		}

		[TestCase(TestName = "An economy target scores higher under raiding weights.")]
		public void RaidingPrefersEconomy()
		{
			var balanced = TargetEvaluator.Score("proc", true, false, false, 0,
				routeCost: 1f, friendlyLossRisk: 0.2f, enemyReinforcementRisk: 0f,
				enemyCounterattackRisk: 0f, uncertainty: 0f, map: null, movementClass: MovementClass.Ground,
				TargetWeights.Balanced());
			var raiding = TargetEvaluator.Score("proc", true, false, false, 0,
				routeCost: 1f, friendlyLossRisk: 0.2f, enemyReinforcementRisk: 0f,
				enemyCounterattackRisk: 0f, uncertainty: 0f, map: null, movementClass: MovementClass.Ground,
				TargetWeights.Raiding());

			Assert.That(raiding.EconomicDamage, Is.GreaterThan(balanced.EconomicDamage));
			Assert.That(raiding.Total, Is.GreaterThan(balanced.Total));
		}

		[TestCase(TestName = "Reinforcement risk and travel cost reduce a target's score.")]
		public void RiskReducesScore()
		{
			var safe = TargetEvaluator.Score("weap", false, true, false, 0,
				routeCost: 1f, friendlyLossRisk: 0f, enemyReinforcementRisk: 0f,
				enemyCounterattackRisk: 0f, uncertainty: 0f, map: null, movementClass: MovementClass.Ground);
			var risky = TargetEvaluator.Score("weap", false, true, false, 0,
				routeCost: 5f, friendlyLossRisk: 1f, enemyReinforcementRisk: 1f,
				enemyCounterattackRisk: 1f, uncertainty: 1f, map: null, movementClass: MovementClass.Ground);

			Assert.That(risky.Total, Is.LessThan(safe.Total));
		}

		[TestCase(TestName = "Uncertainty is subtracted, so a last-known target is worth less.")]
		public void UncertaintyReducesScore()
		{
			var observed = TargetEvaluator.Score("proc", true, false, false, 0,
				routeCost: 0f, friendlyLossRisk: 0f, enemyReinforcementRisk: 0f,
				enemyCounterattackRisk: 0f, uncertainty: 0f, map: null, movementClass: MovementClass.Ground);
			var suspected = TargetEvaluator.Score("proc", true, false, false, 0,
				routeCost: 0f, friendlyLossRisk: 0f, enemyReinforcementRisk: 0f,
				enemyCounterattackRisk: 0f, uncertainty: 1f, map: null, movementClass: MovementClass.Ground);

			Assert.That(suspected.Total, Is.LessThan(observed.Total));
		}

		[TestCase(TestName = "Classification matches the value functions.")]
		public void Classify()
		{
			Assert.That(TargetEvaluator.Classify("proc"), Is.EqualTo((true, false, false)));
			Assert.That(TargetEvaluator.Classify("weap"), Is.EqualTo((false, true, false)));
			Assert.That(TargetEvaluator.Classify("atek"), Is.EqualTo((false, false, true)));
			Assert.That(TargetEvaluator.Classify("e1"), Is.EqualTo((false, false, false)));
		}
	}
}
