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
	/// Covers opponent-model prediction accuracy (req 622) - distinct from the combat estimator's
	/// predicted win ratio, which measures whether a fight was judged correctly rather than whether
	/// the opponent profile predicted the enemy's behaviour.
	/// </summary>
	[TestFixture]
	sealed class OpponentPredictionTest
	{
		[TestCase(TestName = "Accuracy is unknown until a prediction resolves, not perfect.")]
		public void UnresolvedIsUnknown()
		{
			var log = new OpponentPredictionLog();
			Assert.That(log.Accuracy, Is.Null, "No evidence must not read as a perfect score.");

			log.Predict("playstyle", "rush", 100, 0.8f);
			Assert.That(log.Accuracy, Is.Null);
			Assert.That(log.PendingCount, Is.EqualTo(1));
			Assert.That(log.Summary(), Does.Contain("no resolved predictions"));
		}

		[TestCase(TestName = "A correct prediction scores, a wrong one does not.")]
		public void CorrectAndIncorrect()
		{
			var log = new OpponentPredictionLog();
			log.Predict("playstyle", "rush", 100, 0.8f);
			log.Observe("playstyle", "rush");
			Assert.That(log.Accuracy, Is.EqualTo(1f));

			log.Predict("build", "air", 200, 0.7f);
			log.Observe("build", "naval");
			Assert.That(log.Accuracy, Is.EqualTo(0.5f).Within(0.001f));
			Assert.That(log.CorrectCount, Is.EqualTo(1));
			Assert.That(log.ResolvedCount, Is.EqualTo(2));
		}

		[TestCase(TestName = "Repeating an open prediction does not inflate the sample size.")]
		public void RepeatedPredictionIsNotDoubleCounted()
		{
			// The commander re-states its profile every review; that must not count as new evidence.
			var log = new OpponentPredictionLog();
			for (var i = 0; i < 10; i++)
				log.Predict("playstyle", "turtle", 100 + i, 0.9f);

			log.Observe("playstyle", "turtle");
			Assert.That(log.ResolvedCount, Is.EqualTo(1),
				"Ten identical forecasts are one prediction, not ten correct ones.");
		}

		[TestCase(TestName = "A changed forecast opens a new prediction.")]
		public void ChangedForecastIsANewPrediction()
		{
			var log = new OpponentPredictionLog();
			log.Predict("playstyle", "rush", 100, 0.5f);
			log.Predict("playstyle", "turtle", 200, 0.7f);
			log.Observe("playstyle", "turtle");

			Assert.That(log.ResolvedCount, Is.EqualTo(2));
			Assert.That(log.CorrectCount, Is.EqualTo(1), "The superseded forecast still counts as wrong.");
		}

		[TestCase(TestName = "Resolution never rewrites an already-settled prediction.")]
		public void ResolutionIsFinal()
		{
			var log = new OpponentPredictionLog();
			log.Predict("build", "armor", 100, 0.8f);
			log.Observe("build", "armor");
			log.Observe("build", "naval");

			Assert.That(log.Accuracy, Is.EqualTo(1f),
				"Later evidence must not retroactively falsify a prediction that already resolved.");
		}

		[TestCase(TestName = "Calibration is positive when the model is more confident where it is right.")]
		public void CalibrationRewardsConfidenceWhenCorrect()
		{
			var log = new OpponentPredictionLog();
			log.Predict("playstyle", "rush", 100, 0.9f);
			log.Observe("playstyle", "rush");
			log.Predict("build", "air", 200, 0.3f);
			log.Observe("build", "naval");

			Assert.That(log.Calibration, Is.Not.Null);
			Assert.That(log.Calibration.Value, Is.GreaterThan(0f),
				"Confident when right, hesitant when wrong is what makes ShouldExploit safe.");
		}

		[TestCase(TestName = "Calibration is unknown until both a hit and a miss exist to compare.")]
		public void CalibrationNeedsBothOutcomes()
		{
			var log = new OpponentPredictionLog();
			log.Predict("playstyle", "rush", 100, 0.9f);
			log.Observe("playstyle", "rush");
			Assert.That(log.Calibration, Is.Null);
		}

		[TestCase(TestName = "Accuracy is reported per prediction kind.")]
		public void AccuracyByKind()
		{
			var log = new OpponentPredictionLog();
			log.Predict("playstyle", "rush", 100, 0.8f);
			log.Observe("playstyle", "rush");
			log.Predict("attack_lane", "3", 200, 0.8f);
			log.Observe("attack_lane", "7");

			var byKind = log.AccuracyByKind();
			Assert.That(byKind["playstyle"], Is.EqualTo(1f));
			Assert.That(byKind["attack_lane"], Is.EqualTo(0f));
			Assert.That(log.Summary(), Does.Contain("playstyle 100%"));
			Assert.That(log.Summary(), Does.Contain("attack_lane 0%"));
		}

		[TestCase(TestName = "Blank predictions and observations are ignored.")]
		public void BlankInputIsIgnored()
		{
			var log = new OpponentPredictionLog();
			log.Predict(null, "rush", 100, 0.8f);
			log.Predict("playstyle", null, 100, 0.8f);
			log.Observe("playstyle", null);
			Assert.That(log.Predictions, Is.Empty);
		}
	}
}
