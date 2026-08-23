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
	/// Covers per-engagement combat-estimator accuracy (req 159), replacing the previous match-level
	/// win-ratio correlation with proper scoring of individual engagement predictions.
	/// </summary>
	[TestFixture]
	sealed class EngagementAccuracyTest
	{
		[TestCase(TestName = "Accuracy is unknown until an engagement resolves.")]
		public void UnresolvedIsUnknown()
		{
			var log = new EngagementOutcomeLog();
			Assert.That(log.BrierScore, Is.Null);
			Assert.That(log.DirectionalAccuracy, Is.Null);
			Assert.That(log.Bias, Is.Null);

			log.Predict("m1", 100, 0.8f, 0.2f, 500f);
			Assert.That(log.PendingCount, Is.EqualTo(1));
			Assert.That(log.BrierScore, Is.Null, "A committed but unresolved fight scores nothing yet.");
			Assert.That(log.Summary(), Does.Contain("no resolved engagements"));
		}

		[TestCase(TestName = "A perfect prediction scores a zero Brier term.")]
		public void PerfectPredictionScoresZero()
		{
			var log = new EngagementOutcomeLog();
			log.Predict("m1", 100, 1.0f, 0.3f, 500f);
			log.Resolve("m1", 400, true, 0.3f);

			Assert.That(log.BrierScore, Is.EqualTo(0f).Within(0.0001f));
			Assert.That(log.LossPredictionError, Is.EqualTo(0f).Within(0.0001f));
			Assert.That(log.DirectionalAccuracy, Is.EqualTo(1f));
		}

		[TestCase(TestName = "A confident wrong call is penalised more than an uncertain one.")]
		public void ConfidenceIsPenalisedWhenWrong()
		{
			// This is the property that makes the Brier score the right measure: an estimator that
			// says "certain win" and loses is far worse than one that said "coin flip" and lost.
			var confident = new EngagementOutcomeLog();
			confident.Predict("m1", 100, 1.0f, 0.1f, 500f);
			confident.Resolve("m1", 400, false, 0.9f);

			var hedged = new EngagementOutcomeLog();
			hedged.Predict("m1", 100, 0.5f, 0.1f, 500f);
			hedged.Resolve("m1", 400, false, 0.9f);

			Assert.That(confident.BrierScore, Is.GreaterThan(hedged.BrierScore));
			Assert.That(confident.BrierScore, Is.EqualTo(1f).Within(0.0001f));
			Assert.That(hedged.BrierScore, Is.EqualTo(0.25f).Within(0.0001f));
		}

		[TestCase(TestName = "Bias exposes a systematically optimistic estimator.")]
		public void OptimismIsDetected()
		{
			// The failure mode that matters: an estimator that keeps predicting wins and keeps losing
			// feeds forces into fights the coalition cannot win.
			var log = new EngagementOutcomeLog();
			for (var i = 0; i < 4; i++)
			{
				log.Predict($"m{i}", 100 * i, 0.9f, 0.2f, 500f);
				log.Resolve($"m{i}", 100 * i + 50, false, 0.8f);
			}

			Assert.That(log.Bias, Is.EqualTo(0.9f).Within(0.0001f));
			Assert.That(log.Summary(), Does.Contain("optimistic"));
			Assert.That(log.DirectionalAccuracy, Is.EqualTo(0f));
		}

		[TestCase(TestName = "A calibrated estimator reports near-zero bias.")]
		public void CalibratedEstimatorIsReportedAsSuch()
		{
			var log = new EngagementOutcomeLog();
			log.Predict("m1", 100, 1f, 0.2f, 500f);
			log.Resolve("m1", 200, true, 0.2f);
			log.Predict("m2", 300, 0f, 0.8f, 500f);
			log.Resolve("m2", 400, false, 0.8f);

			Assert.That(log.Bias, Is.EqualTo(0f).Within(0.0001f));
			Assert.That(log.Summary(), Does.Contain("calibrated"));
		}

		[TestCase(TestName = "Re-predicting an open engagement keeps the committed call, not hindsight.")]
		public void HindsightCannotImproveTheScore()
		{
			var log = new EngagementOutcomeLog();
			log.Predict("m1", 100, 0.9f, 0.1f, 500f);

			// Later reviews see the fight going badly and would lower the estimate; the score must
			// still reflect the prediction that force was actually committed on.
			log.Predict("m1", 200, 0.1f, 0.9f, 500f);
			log.Resolve("m1", 300, false, 0.9f);

			Assert.That(log.ResolvedCount, Is.EqualTo(1));
			Assert.That(log.Engagements[0].PredictedWinRatio, Is.EqualTo(0.9f).Within(0.0001f));
			Assert.That(log.BrierScore, Is.EqualTo(0.81f).Within(0.0001f));
		}

		[TestCase(TestName = "Resolving an unknown or already-resolved engagement is a no-op.")]
		public void ResolveIsSafe()
		{
			var log = new EngagementOutcomeLog();
			log.Resolve("nonexistent", 100, true, 0f);
			Assert.That(log.ResolvedCount, Is.Zero);

			log.Predict("m1", 100, 0.7f, 0.2f, 500f);
			log.Resolve("m1", 200, true, 0.2f);
			log.Resolve("m1", 300, false, 0.9f);

			Assert.That(log.ResolvedCount, Is.EqualTo(1));
			Assert.That(log.Engagements[0].Won, Is.True, "A settled engagement must not be rewritten.");
		}

		[TestCase(TestName = "Predictions are clamped into a valid probability range.")]
		public void PredictionsAreClamped()
		{
			var log = new EngagementOutcomeLog();
			var engagement = log.Predict("m1", 100, 5f, -2f, -10f);
			Assert.That(engagement.PredictedWinRatio, Is.EqualTo(1f));
			Assert.That(engagement.PredictedLossFraction, Is.EqualTo(0f));
			Assert.That(engagement.CommittedPower, Is.EqualTo(0f));
		}
	}
}
