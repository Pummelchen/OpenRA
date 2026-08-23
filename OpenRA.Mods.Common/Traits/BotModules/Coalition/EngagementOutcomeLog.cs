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
	/// <para>
	/// Per-engagement accuracy for <see cref="CombatEstimator"/> (req 159). The previous measure
	/// correlated the commander's last predicted win ratio with the match result, which is a
	/// match-level signal: a single number for a whole game says little about whether individual
	/// fights were judged correctly.
	/// </para>
	/// <para>
	/// This records each engagement's prediction when it is committed and its real outcome when it
	/// concludes, then scores the estimator with a Brier score (mean squared error of the predicted
	/// win probability) and the mean absolute error of the predicted loss fraction. Both are proper
	/// measures: a confident wrong call is penalised more than an uncertain one.
	/// </para>
	/// </summary>
	public sealed class EngagementOutcomeLog
	{
		public sealed class Engagement
		{
			public readonly string Id;
			public readonly int StartTick;
			public readonly float PredictedWinRatio;
			public readonly float PredictedLossFraction;
			public readonly float CommittedPower;

			public int EndTick;
			public bool Resolved;
			public bool Won;
			public float ActualLossFraction;

			public Engagement(string id, int startTick, float predictedWinRatio,
				float predictedLossFraction, float committedPower)
			{
				Id = id;
				StartTick = startTick;
				PredictedWinRatio = Math.Clamp(predictedWinRatio, 0f, 1f);
				PredictedLossFraction = Math.Clamp(predictedLossFraction, 0f, 1f);
				CommittedPower = Math.Max(0f, committedPower);
			}

			/// <summary>Squared error of the win prediction against the binary outcome.</summary>
			public float BrierTerm => (PredictedWinRatio - (Won ? 1f : 0f)) * (PredictedWinRatio - (Won ? 1f : 0f));

			/// <summary>Absolute error of the predicted loss fraction.</summary>
			public float LossError => Math.Abs(PredictedLossFraction - ActualLossFraction);

			/// <summary>True when the prediction picked the right side of even odds.</summary>
			public bool DirectionallyCorrect => Won == PredictedWinRatio >= 0.5f;
		}

		readonly List<Engagement> engagements = [];

		public IReadOnlyList<Engagement> Engagements => engagements;
		public int ResolvedCount => engagements.Count(e => e.Resolved);
		public int PendingCount => engagements.Count(e => !e.Resolved);

		/// <summary>
		/// Mean squared error of the predicted win probability, 0 (perfect) to 1 (confidently wrong).
		/// Null while nothing has resolved: no evidence is not a perfect score.
		/// </summary>
		public float? BrierScore
		{
			get
			{
				var resolved = engagements.Where(e => e.Resolved).ToArray();
				return resolved.Length == 0 ? null : resolved.Average(e => e.BrierTerm);
			}
		}

		/// <summary>Mean absolute error of the predicted loss fraction, or null while nothing has resolved.</summary>
		public float? LossPredictionError
		{
			get
			{
				var resolved = engagements.Where(e => e.Resolved).ToArray();
				return resolved.Length == 0 ? null : resolved.Average(e => e.LossError);
			}
		}

		/// <summary>Fraction of resolved engagements where the predicted favourite actually won.</summary>
		public float? DirectionalAccuracy
		{
			get
			{
				var resolved = engagements.Where(e => e.Resolved).ToArray();
				return resolved.Length == 0 ? null : resolved.Count(e => e.DirectionallyCorrect) * 1f / resolved.Length;
			}
		}

		/// <summary>
		/// Estimator bias: mean predicted win ratio minus the observed win rate. Positive means the
		/// estimator is systematically optimistic, which is the failure mode that feeds forces into
		/// fights the coalition cannot win.
		/// </summary>
		public float? Bias
		{
			get
			{
				var resolved = engagements.Where(e => e.Resolved).ToArray();
				if (resolved.Length == 0)
					return null;

				return resolved.Average(e => e.PredictedWinRatio) - resolved.Count(e => e.Won) * 1f / resolved.Length;
			}
		}

		/// <summary>Records a committed engagement and the prediction made for it.</summary>
		public Engagement Predict(string id, int tick, float winRatio, float lossFraction, float committedPower)
		{
			if (string.IsNullOrEmpty(id))
				return null;

			// Re-predicting a still-open engagement updates nothing: the committed call is the one
			// being scored, not whatever the estimator thought later with more information.
			var open = engagements.LastOrDefault(e => e.Id == id && !e.Resolved);
			if (open != null)
				return open;

			var engagement = new Engagement(id, tick, winRatio, lossFraction, committedPower);
			engagements.Add(engagement);
			return engagement;
		}

		/// <summary>Resolves the open engagement with the given id against its real outcome.</summary>
		public void Resolve(string id, int tick, bool won, float actualLossFraction)
		{
			var open = engagements.LastOrDefault(e => e.Id == id && !e.Resolved);
			if (open == null)
				return;

			open.EndTick = tick;
			open.Won = won;
			open.ActualLossFraction = Math.Clamp(actualLossFraction, 0f, 1f);
			open.Resolved = true;
		}

		/// <summary>One-line telemetry summary of per-engagement estimator accuracy (req 159).</summary>
		public string Summary()
		{
			if (ResolvedCount == 0)
				return $"Combat estimator accuracy: no resolved engagements ({PendingCount} pending)";

			var bias = Bias.Value;
			var leaning = Math.Abs(bias) < 0.05f ? "calibrated"
				: bias > 0 ? "optimistic" : "pessimistic";
			return $"Combat estimator accuracy: {ResolvedCount} engagements, " +
				$"brier {BrierScore:0.000}, direction {DirectionalAccuracy * 100:0}%, " +
				$"loss error {LossPredictionError:0.00}, bias {bias:+0.00;-0.00} ({leaning})";
		}
	}
}
