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
using System.Linq;

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	/// <summary>
	/// <para>Scores the opponent model's own forecasts against what the coalition later observes (req 622).</para>
	/// <para>
	/// This is deliberately separate from the combat estimator's predicted win ratio: that measures
	/// whether a fight was judged correctly, this measures whether the opponent profile - playstyle,
	/// preferred attack lane, tech direction, reaction to harassment - actually predicted the enemy's
	/// behaviour. A model that is confidently wrong is worse than one that admits uncertainty, so an
	/// unresolved prediction is never counted as correct.
	/// </para>
	/// </summary>
	public sealed class OpponentPredictionLog
	{
		/// <summary>What the model claimed, and what the coalition later saw.</summary>
		public sealed class Prediction
		{
			public readonly string Kind;
			public readonly string Predicted;
			public readonly int MadeAtTick;
			public readonly float Confidence;
			public string Observed;
			public bool Resolved;

			public Prediction(string kind, string predicted, int madeAtTick, float confidence)
			{
				Kind = kind;
				Predicted = predicted;
				MadeAtTick = madeAtTick;
				Confidence = confidence;
			}

			public bool Correct => Resolved
				&& string.Equals(Predicted, Observed, StringComparison.OrdinalIgnoreCase);
		}

		readonly List<Prediction> predictions = [];

		public IReadOnlyList<Prediction> Predictions => predictions;

		/// <summary>Predictions that have since been checked against an observation.</summary>
		public int ResolvedCount => predictions.Count(p => p.Resolved);

		/// <summary>Resolved predictions that matched the observation.</summary>
		public int CorrectCount => predictions.Count(p => p.Correct);

		/// <summary>Predictions still waiting for an observation to confirm or refute them.</summary>
		public int PendingCount => predictions.Count(p => !p.Resolved);

		/// <summary>
		/// Fraction of resolved predictions that were correct, or null while nothing has resolved yet.
		/// Null rather than 0 or 1: "no evidence" is not "perfect" and not "always wrong".
		/// </summary>
		public float? Accuracy => ResolvedCount == 0 ? null : (float)CorrectCount / ResolvedCount;

		/// <summary>
		/// Confidence calibration: the mean confidence attached to correct predictions minus the mean
		/// attached to incorrect ones. Positive means the model is more confident when it is right,
		/// which is the property that makes <see cref="OpponentModel.ShouldExploit"/> safe to act on.
		/// Null until both a correct and an incorrect prediction exist to compare.
		/// </summary>
		public float? Calibration
		{
			get
			{
				var correct = predictions.Where(p => p.Correct).ToArray();
				var wrong = predictions.Where(p => p.Resolved && !p.Correct).ToArray();
				if (correct.Length == 0 || wrong.Length == 0)
					return null;

				return correct.Average(p => p.Confidence) - wrong.Average(p => p.Confidence);
			}
		}

		/// <summary>
		/// Records a forecast. Re-stating the same still-open prediction is a no-op, so a model that
		/// repeats itself every review does not inflate its own sample size.
		/// </summary>
		public void Predict(string kind, string predicted, int tick, float confidence)
		{
			if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(predicted))
				return;

			var open = predictions.LastOrDefault(p => p.Kind == kind && !p.Resolved);
			if (open != null && string.Equals(open.Predicted, predicted, StringComparison.OrdinalIgnoreCase))
				return;

			predictions.Add(new Prediction(kind, predicted, tick, Math.Clamp(confidence, 0f, 1f)));
		}

		/// <summary>
		/// Resolves every open prediction of this kind against what was actually observed. Only open
		/// predictions resolve, so a past forecast is never retroactively rewritten by later evidence.
		/// </summary>
		public void Observe(string kind, string observed)
		{
			if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(observed))
				return;

			foreach (var prediction in predictions.Where(p => p.Kind == kind && !p.Resolved))
			{
				prediction.Observed = observed;
				prediction.Resolved = true;
			}
		}

		/// <summary>Per-kind accuracy, so a model good at tech direction but bad at lanes is visible as such.</summary>
		public IReadOnlyDictionary<string, float> AccuracyByKind()
		{
			return predictions
				.Where(p => p.Resolved)
				.GroupBy(p => p.Kind, StringComparer.Ordinal)
				.ToDictionary(g => g.Key, g => g.Count(p => p.Correct) * 1f / g.Count(), StringComparer.Ordinal);
		}

		/// <summary>One-line telemetry summary of opponent-model prediction accuracy (req 622).</summary>
		public string Summary()
		{
			if (ResolvedCount == 0)
				return $"Opponent prediction accuracy: no resolved predictions ({PendingCount} pending)";

			var byKind = string.Join(", ", AccuracyByKind()
				.OrderBy(kv => kv.Key, StringComparer.Ordinal)
				.Select(kv => $"{kv.Key} {kv.Value * 100:0}%"));
			var calibration = Calibration == null ? "n/a" : $"{Calibration.Value:+0.00;-0.00}";
			return $"Opponent prediction accuracy: {Accuracy * 100:0}% ({CorrectCount}/{ResolvedCount} correct, " +
				$"{PendingCount} pending; calibration {calibration}; {byKind})";
		}
	}
}
