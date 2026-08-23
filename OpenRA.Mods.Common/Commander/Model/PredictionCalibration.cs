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

using System;
using System.Collections.Generic;

namespace OpenRA.Mods.Common.Commander.Model
{
	/// <summary>
	/// <para>
	/// Scores the forward model against what actually happened. A model nobody checks is a model
	/// that lies to the search, and the search has no way to notice.
	/// </para>
	/// <para>
	/// The method is the only honest one available: predict the state at a horizon, wait for the
	/// horizon to arrive, and compare. Errors are relative, because an army of 20,000 credits being
	/// 500 out is a good prediction and an army of 500 credits being 500 out is not.
	/// </para>
	/// </summary>
	public sealed class PredictionCalibration
	{
		/// <summary>One quantity worth being right about.</summary>
		public enum Metric
		{
			OwnArmyValue,
			OwnCash,
			OwnIncome,
			EnemyArmyValue,
		}

		/// <summary>A prediction awaiting the moment it can be judged.</summary>
		/// <remarks>
		/// <paramref name="EarnedAtPrediction"/> is carried so that actual income can be measured
		/// over exactly the span the prediction covers. Comparing a thirty-second forecast against a
		/// ten-second spot reading is comparing two different quantities: a harvester delivers a
		/// load every half minute or so, so a ten-second window contains zero, one or two
		/// deliveries and swings enormously for reasons that have nothing to do with the model.
		/// </remarks>
		readonly record struct Pending(int DueTick, float[] Predicted, float EarnedAtPrediction, float[] AtPrediction);

		readonly List<Pending> pending = [];
		readonly Dictionary<Metric, List<float>> errors = [];
		readonly Dictionary<Metric, List<float>> baselineErrors = [];

		public PredictionCalibration()
		{
			foreach (Metric metric in Enum.GetValues<Metric>())
			{
				errors[metric] = [];
				baselineErrors[metric] = [];
			}
		}

		/// <summary>Optional per-settlement trace, for diagnosing which way a metric is biased.</summary>
		public Action<string> Diagnostic { get; set; }

		public int Scored { get; private set; }
		public int Outstanding => pending.Count;

		/// <summary>
		/// <para>
		/// Reads the quantities being scored out of a state.
		/// </para>
		/// <para>
		/// <paramref name="incomePerSecond"/> must be the <i>measured</i> income when recording what
		/// actually happened, and the model's own figure when recording a prediction. An earlier
		/// version used the model's formula for both, which made the income row circular - it
		/// compared the formula against itself and could not have failed for the reason that
		/// mattered.
		/// </para>
		/// </summary>
		public static float[] Measure(AbstractState state, float incomePerSecond)
		{
			ArgumentNullException.ThrowIfNull(state);

			return
			[
				state.Self.ArmyValue(),
				state.Self.Cash,
				incomePerSecond,
				state.Enemy.ArmyValue(),
			];
		}

		/// <summary>
		/// Records a prediction to be judged at <paramref name="dueTick"/>, together with the state
		/// it was made from - which is the do-nothing baseline it will be scored against.
		/// </summary>
		public void Predict(int dueTick, float[] predicted, float earnedNow, float[] atPrediction)
		{
			ArgumentNullException.ThrowIfNull(predicted);
			ArgumentNullException.ThrowIfNull(atPrediction);
			pending.Add(new Pending(dueTick, predicted, earnedNow, atPrediction));
		}

		/// <summary>
		/// Judges every prediction that has come due. <paramref name="actualNow"/> is the state as
		/// it turned out, and <paramref name="earnedNow"/> lets actual income be averaged over
		/// exactly the span each prediction covered.
		/// </summary>
		public void Settle(int tick, float[] actualNow, float earnedNow, float secondsPerHorizon)
		{
			ArgumentNullException.ThrowIfNull(actualNow);

			for (var i = pending.Count - 1; i >= 0; i--)
			{
				if (pending[i].DueTick > tick)
					continue;

				var entry = pending[i];
				pending.RemoveAt(i);
				Scored++;

				// Actual income averaged over the horizon, which is the quantity the model forecast.
				var actual = (float[])actualNow.Clone();
				if (secondsPerHorizon > 0f && actual.Length > (int)Metric.OwnIncome)
					actual[(int)Metric.OwnIncome] =
						Math.Max(0f, earnedNow - entry.EarnedAtPrediction) / secondsPerHorizon;

				if (Diagnostic != null)
					Diagnostic($"predIncome={entry.Predicted[(int)Metric.OwnIncome]:F1} " +
						$"actIncome={actual[(int)Metric.OwnIncome]:F1} " +
						$"baseIncome={entry.AtPrediction[(int)Metric.OwnIncome]:F1} " +
						$"predArmy={entry.Predicted[0]:F0} actArmy={actual[0]:F0}");

				for (var m = 0; m < actual.Length && m < entry.Predicted.Length; m++)
				{
					var metric = (Metric)m;
					errors[metric].Add(RelativeError(entry.Predicted[m], actual[m]));

					// The baseline is "assume nothing changes". A forward model that cannot beat
					// standing still is not adding information, however plausible its arithmetic.
					if (m < entry.AtPrediction.Length)
						baselineErrors[metric].Add(RelativeError(entry.AtPrediction[m], actual[m]));
				}
			}
		}

		/// <summary>
		/// Error relative to the larger of the two magnitudes, so it stays bounded in 0..1 and does
		/// not explode when the actual value is near zero.
		/// </summary>
		public static float RelativeError(float predicted, float actual)
		{
			var scale = Math.Max(Math.Abs(predicted), Math.Abs(actual));
			if (scale <= 1f)
				return 0f;

			return Math.Abs(predicted - actual) / scale;
		}

		public float MeanError(Metric metric) => Mean(errors[metric]);

		public float MeanBaselineError(Metric metric) => Mean(baselineErrors[metric]);

		static float Mean(List<float> values)
		{
			if (values.Count == 0)
				return 0f;

			var total = 0f;
			foreach (var v in values)
				total += v;

			return total / values.Count;
		}

		/// <summary>A one-line report per metric: the model's error, the do-nothing error, and the verdict.</summary>
		public IEnumerable<string> Report(float threshold)
		{
			foreach (Metric metric in Enum.GetValues<Metric>())
			{
				var mine = MeanError(metric);
				var baseline = MeanBaselineError(metric);
				var verdict = mine <= threshold ? "PASS" : "FAIL";
				var beatsBaseline = mine <= baseline ? "beats" : "LOSES TO";

				yield return $"{metric,-16} error={mine,6:P1} baseline={baseline,6:P1} " +
					$"({beatsBaseline} do-nothing) n={errors[metric].Count} {verdict}";
			}
		}
	}
}
