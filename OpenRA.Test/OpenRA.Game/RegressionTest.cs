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
	/// Pins defects that were actually found and fixed, so they cannot silently return (reqs 709,
	/// 710, 712). Each case names the real failure it reproduces rather than restating a feature
	/// contract - a regression suite is only useful if every entry once failed.
	/// </summary>
	[TestFixture]
	sealed class RegressionTest
	{
		[TestCase(TestName = "Regression: economic damage is credited by value, not by refinery count.")]
		public void EconomicDamageCountsValueNotBuildings()
		{
			// Economic damage was reported purely as refineries lost, which scored losing a silo the
			// same as losing a refinery and reported zero for any economy destroyed that was not a
			// refinery at all.
			var metrics = new CoalitionMatchMetrics();
			metrics.SampleEconomy(2, 2, friendlyEconomicValue: 10000, enemyEconomicValue: 10000);
			metrics.SampleEconomy(2, 2, friendlyEconomicValue: 10000, enemyEconomicValue: 6000);

			Assert.That(metrics.EnemyEconomicDamage, Is.EqualTo(4000),
				"Destroyed enemy economy must be measured in credits.");
			Assert.That(metrics.EnemyRefineryLosses, Is.Zero,
				"No refinery was lost, so the building count must stay at zero while value damage is recorded.");
			Assert.That(metrics.FriendlyEconomicDamage, Is.Zero);
		}

		[TestCase(TestName = "Regression: rebuilding does not erase already-recorded economic damage.")]
		public void RebuildingDoesNotEraseDamage()
		{
			// A peak that tracked the current value in both directions would let an opponent's
			// rebuild silently cancel the damage the coalition had already done.
			var metrics = new CoalitionMatchMetrics();
			metrics.SampleEconomy(2, 2, enemyEconomicValue: 10000);
			metrics.SampleEconomy(2, 2, enemyEconomicValue: 4000);
			metrics.SampleEconomy(2, 2, enemyEconomicValue: 10000);

			Assert.That(metrics.EnemyEconomicDamage, Is.EqualTo(6000),
				"Damage already inflicted must survive the enemy rebuilding.");
		}

		[TestCase(TestName = "Regression: a settled engagement prediction is never rewritten by later evidence.")]
		public void SettledPredictionsAreImmutable()
		{
			// Scoring that allowed re-resolution would let a later review launder a bad prediction
			// into a good one, making the estimator look calibrated when it was not.
			var log = new EngagementOutcomeLog();
			log.Predict("OP-1", 100, 0.95f, 0.1f, 500f);
			log.Resolve("OP-1", 200, won: false, actualLossFraction: 0.9f);
			log.Resolve("OP-1", 300, won: true, actualLossFraction: 0.1f);

			Assert.That(log.Engagements[0].Won, Is.False);
			Assert.That(log.BrierScore, Is.EqualTo(0.9025f).Within(0.0001f));
		}

		[TestCase(TestName = "Regression: an unresolved prediction is not scored as a correct one.")]
		public void UnresolvedIsNotCredited()
		{
			// Reporting 1.0 for "nothing measured yet" would have made an untested estimator and a
			// perfect one indistinguishable in telemetry.
			Assert.That(new EngagementOutcomeLog().BrierScore, Is.Null);
			Assert.That(new OpponentPredictionLog().Accuracy, Is.Null);
		}

		[TestCase(TestName = "Regression: fair fog keeps the honesty ladder distinct from observation.")]
		public void InferredTargetsAreNotObservations()
		{
			// The offensive fallback names an objective inferred from public map data. That must stay
			// clearly separated from an observed enemy structure, or a guess would be reported as a
			// sighting and the fog guarantee would be meaningless.
			var home = new CPos(10, 10);
			var inferred = CoalitionBlackboard.InferEnemyBaseCell(
				[home, new CPos(90, 90)], home, approach: null, isExplored: _ => false);

			Assert.That(inferred, Is.EqualTo(new CPos(90, 90)));
			Assert.That(CoalitionCommandCenterBotModule.ShouldAdvanceToFindEnemy(
				observedEnemyRegion: 3, coalitionArmy: 1000, coordinatedMinimum: 24,
				currentTick: 99999, searchStartTick: 0, commandInterval: 100), Is.False,
				"Once a real structure is observed the inference path must stop being used.");
		}

		[TestCase(TestName = "Regression: every mission type stays reachable through the validator.")]
		public void NoMissionTypeBecomesUnreachable()
		{
			// Adding a MissionType without registering its name left it permanently rejected as
			// unknown, so the mission existed in code but could never be requested.
			foreach (var type in System.Enum.GetValues<MissionType>())
				Assert.That(CommandValidator.KnownMissionTypes, Does.Contain(type.ToString().ToLowerInvariant()),
					$"MissionType.{type} is unreachable: the validator would reject it.");
		}

		[TestCase(TestName = "Regression: every RA support power keeps a tactical role.")]
		public void NoSupportPowerBecomesUnusable()
		{
			// Chronosphere, Advanced Chronoshift and Iron Curtain were classified Unsupported, so the
			// bot silently never fired three of RA's six support powers.
			foreach (var power in new[]
			{
				"SovietSpyPlane", "SovietParatroopers", "UkraineParabombs", "NukePowerInfoOrder",
				"Chronoshift", "AdvancedChronoshift", "GrantExternalConditionPowerInfoOrder"
			})
				Assert.That(SupportPowerPolicy.Classify(power), Is.Not.EqualTo(SupportPowerRole.Unsupported),
					$"RA support power \"{power}\" would never be fired.");
		}
	}
}
