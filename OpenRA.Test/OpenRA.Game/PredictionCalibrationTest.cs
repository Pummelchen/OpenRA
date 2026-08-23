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

using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Commander.Model;

namespace OpenRA.Test
{
	/// <summary>
	/// The scorekeeper for the forward model. Its own correctness matters as much as the model's: a
	/// scoreboard that flatters the thing it measures is worse than no scoreboard, because it
	/// converts an unknown into a false certainty.
	/// </summary>
	[TestFixture]
	sealed class PredictionCalibrationTest
	{
		[TestCase(TestName = "Relative error is bounded and scale-free.")]
		public void RelativeError()
		{
			// 500 out of 20,000 is a good prediction; 500 out of 500 is not. An absolute measure
			// could not tell them apart.
			Assert.That(PredictionCalibration.RelativeError(20500f, 20000f), Is.EqualTo(0.024f).Within(0.002f));
			Assert.That(PredictionCalibration.RelativeError(1000f, 500f), Is.EqualTo(0.5f).Within(0.01f));
			Assert.That(PredictionCalibration.RelativeError(100f, 100f), Is.EqualTo(0f));

			// Near zero the ratio would explode, so it is defined to be zero there instead.
			Assert.That(PredictionCalibration.RelativeError(0.5f, 0f), Is.EqualTo(0f));
			Assert.That(PredictionCalibration.RelativeError(1000f, 0f), Is.EqualTo(1f));
		}

		[TestCase(TestName = "Nothing is scored before its horizon arrives.")]
		public void NothingSettlesEarly()
		{
			var calibration = new PredictionCalibration();
			calibration.Predict(1000, [100f, 0f, 0f, 0f], 0f, [90f, 0f, 0f, 0f]);

			calibration.Settle(999, [200f, 0f, 0f, 0f], 0f, 30f);
			Assert.That(calibration.Scored, Is.EqualTo(0));
			Assert.That(calibration.Outstanding, Is.EqualTo(1));

			calibration.Settle(1000, [200f, 0f, 0f, 0f], 0f, 30f);
			Assert.That(calibration.Scored, Is.EqualTo(1));
			Assert.That(calibration.Outstanding, Is.EqualTo(0));
		}

		[TestCase(TestName = "The baseline is the state the prediction was made from.")]
		public void BaselineIsTheDoNothingForecast()
		{
			// A model that merely repeats the present must score exactly what the present scores.
			// This is the check that stops a vacuous model from appearing to work.
			var calibration = new PredictionCalibration();
			calibration.Predict(100, [1000f, 0f, 0f, 0f], 0f, [1000f, 0f, 0f, 0f]);
			calibration.Settle(100, [1200f, 0f, 0f, 0f], 0f, 30f);

			Assert.That(calibration.MeanError(PredictionCalibration.Metric.OwnArmyValue),
				Is.EqualTo(calibration.MeanBaselineError(PredictionCalibration.Metric.OwnArmyValue)));
		}

		[TestCase(TestName = "Actual income is averaged over the span the forecast covered.")]
		public void IncomeIsAveragedOverTheHorizon()
		{
			// Earnings rose by 9,000 over the thirty seconds forecast, which is 300 per second. A
			// spot reading taken at the settling instant could say anything, because deliveries
			// arrive in lumps.
			var calibration = new PredictionCalibration();
			calibration.Predict(750, [0f, 0f, 300f, 0f], earnedNow: 1000f, atPrediction: [0f, 0f, 999f, 0f]);
			calibration.Settle(750, [0f, 0f, 999f, 0f], earnedNow: 10000f, secondsPerHorizon: 30f);

			Assert.That(calibration.MeanError(PredictionCalibration.Metric.OwnIncome), Is.EqualTo(0f),
				"A forecast of 300/s against 9,000 credits earned in 30 s is exactly right.");
		}

		[TestCase(TestName = "The report states the verdict against the threshold.")]
		public void ReportStatesTheVerdict()
		{
			var calibration = new PredictionCalibration();
			calibration.Predict(10, [1000f, 0f, 0f, 0f], 0f, [1000f, 0f, 0f, 0f]);
			calibration.Settle(10, [1050f, 0f, 0f, 0f], 0f, 30f);

			var lines = calibration.Report(0.15f).ToArray();
			Assert.That(lines, Has.Length.EqualTo(4));
			Assert.That(lines[0], Does.Contain("PASS"));

			var strict = calibration.Report(0.01f).ToArray();
			Assert.That(strict[0], Does.Contain("FAIL"),
				"The same measurement against a tighter threshold must fail, or the threshold means nothing.");
		}
	}
}
