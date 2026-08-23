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
	/// Go/no-go arithmetic for scarce assets (reqs 286-301). These units are irreplaceable in
	/// practice, so the question is never "is there a target" but "does the expected gain exceed the
	/// risk of losing the asset".
	/// </summary>
	[TestFixture]
	sealed class SpecialOpsPlanTest
	{
		static SpecialOpsPlan Plan(float success, float value, float risk, float assetValue = 1000f,
			SpecialOpsObjective objective = SpecialOpsObjective.ProductionDenial)
		{
			return new SpecialOpsPlan(objective, success, value, risk, assetValue);
		}

		[TestCase(TestName = "A high-value target with a survivable approach is launched (reqs 295-297).")]
		public void FavourableOperationLaunches()
		{
			var plan = Plan(success: 0.8f, value: 5000f, risk: 0.3f);
			Assert.That(plan.ExpectedValue, Is.GreaterThan(0f));
			Assert.That(plan.ShouldLaunch(), Is.True);
		}

		[TestCase(TestName = "A rich prize behind a near-certain loss is refused, not gambled on.")]
		public void RichPrizeDoesNotJustifyCertainLoss()
		{
			// Expected value alone would say yes; the asset is not replaceable, so a coin-flip on it
			// is not a plan.
			var plan = Plan(success: 0.9f, value: 100000f, risk: 0.95f);
			Assert.That(plan.ExpectedValue, Is.GreaterThan(0f));
			Assert.That(plan.ShouldLaunch(), Is.False);
		}

		[TestCase(TestName = "A negative expected value is refused however safe the approach.")]
		public void NegativeExpectedValueIsRefused()
		{
			var plan = Plan(success: 0.2f, value: 500f, risk: 0.2f, assetValue: 2000f);
			Assert.That(plan.ExpectedValue, Is.LessThan(0f));
			Assert.That(plan.ShouldLaunch(), Is.False);
		}

		[TestCase(TestName = "A worthwhile objective behind a bad window is held, not abandoned (req 298).")]
		public void WaitsForABetterWindow()
		{
			var plan = Plan(success: 0.5f, value: 8000f, risk: 0.8f);
			Assert.That(plan.ShouldLaunch(), Is.False);
			Assert.That(plan.ShouldWaitForWindow(), Is.True,
				"The prize is worth taking; the current approach is not the way to take it.");

			var worthless = Plan(success: 0.5f, value: 100f, risk: 0.8f);
			Assert.That(worthless.ShouldWaitForWindow(), Is.False,
				"Nothing is gained by holding an asset for a target that is not worth it.");
		}

		[TestCase(TestName = "A detected asset aborts immediately (req 300).")]
		public void DetectionAborts()
		{
			Assert.That(SpecialOpsPlan.ShouldAbort(assetDetected: true, remainingSuccessProbability: 0.9f), Is.True,
				"Once detected, the approach the plan relied on no longer exists.");
			Assert.That(SpecialOpsPlan.ShouldAbort(assetDetected: false, remainingSuccessProbability: 0.1f), Is.True,
				"A mission that can no longer succeed only spends the asset.");
			Assert.That(SpecialOpsPlan.ShouldAbort(assetDetected: false, remainingSuccessProbability: 0.8f), Is.False);
		}

		[TestCase(TestName = "A surviving asset is extracted so it can be used again (req 301).")]
		public void SurvivorsAreExtracted()
		{
			Assert.That(SpecialOpsPlan.ShouldExtract(objectiveComplete: true, assetAlive: true, extractionRouteExists: true), Is.True);
			Assert.That(SpecialOpsPlan.ShouldExtract(objectiveComplete: true, assetAlive: true, extractionRouteExists: false), Is.False,
				"There is nothing to extract along.");
			Assert.That(SpecialOpsPlan.ShouldExtract(objectiveComplete: true, assetAlive: false, extractionRouteExists: true), Is.False);
		}

		[TestCase(TestName = "Objectives rank by strategic consequence, not by building cost (reqs 289-294).")]
		public void ConsequenceOrdering()
		{
			// Denying technology or production compounds over the rest of the match; destroying one
			// isolated building is a single event.
			Assert.That(SpecialOpsPlan.ConsequenceRank(SpecialOpsObjective.TechnologyDenial),
				Is.GreaterThan(SpecialOpsPlan.ConsequenceRank(SpecialOpsObjective.ProductionDenial)));
			Assert.That(SpecialOpsPlan.ConsequenceRank(SpecialOpsObjective.ProductionDenial),
				Is.GreaterThan(SpecialOpsPlan.ConsequenceRank(SpecialOpsObjective.SupportPowerDenial)));
			Assert.That(SpecialOpsPlan.ConsequenceRank(SpecialOpsObjective.SupportPowerDenial),
				Is.GreaterThan(SpecialOpsPlan.ConsequenceRank(SpecialOpsObjective.EconomicDenial)));
			Assert.That(SpecialOpsPlan.ConsequenceRank(SpecialOpsObjective.EconomicDenial),
				Is.GreaterThan(SpecialOpsPlan.ConsequenceRank(SpecialOpsObjective.IsolatedHighValue)));
			Assert.That(SpecialOpsPlan.ConsequenceRank(SpecialOpsObjective.None), Is.Zero);
		}

		[TestCase(TestName = "An operation with no objective is never launched.")]
		public void NoObjectiveNeverLaunches()
		{
			var plan = Plan(success: 1f, value: 9999f, risk: 0f, objective: SpecialOpsObjective.None);
			Assert.That(plan.ShouldLaunch(), Is.False);
			Assert.That(plan.ShouldWaitForWindow(), Is.False);
		}

		[TestCase(TestName = "Probabilities are clamped so a malformed estimate cannot skew the decision.")]
		public void ProbabilitiesAreClamped()
		{
			var plan = Plan(success: 5f, value: -100f, risk: -3f);
			Assert.That(plan.SuccessProbability, Is.EqualTo(1f));
			Assert.That(plan.StrategicValue, Is.Zero);
			Assert.That(plan.AssetLossRisk, Is.Zero);
		}
	}
}
