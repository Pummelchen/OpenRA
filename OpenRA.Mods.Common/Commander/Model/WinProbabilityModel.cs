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
using System.Globalization;
using System.Linq;

namespace OpenRA.Mods.Common.Commander.Model
{
	/// <summary>
	/// <para>
	/// The evaluation function: given a state, how likely is this side to win it.
	/// </para>
	/// <para>
	/// Logistic regression over the features in <see cref="StateFeatures"/>, fitted on self-play
	/// outcomes. A few dozen weights - deterministic, microseconds to evaluate, no neural network
	/// and no inference server, so the commander is fully competent with the language model turned
	/// off. It is also readable: printing the weights tells you exactly what the commander believes
	/// wins games, which no learned black box would.
	/// </para>
	/// <para>
	/// Crucially it returns a <i>calibrated probability</i> rather than a score. That means the same
	/// Brier scoring already in this repository grades it directly, and that the search can compare
	/// a certain small gain against an uncertain large one instead of adding up arbitrary points.
	/// </para>
	/// </summary>
	public sealed class WinProbabilityModel
	{
		readonly float[] weights;

		public WinProbabilityModel(float[] weights)
		{
			ArgumentNullException.ThrowIfNull(weights);
			if (weights.Length != StateFeatures.Count)
				throw new ArgumentException($"Expected {StateFeatures.Count} weights.", nameof(weights));

			this.weights = weights;
		}

		public IReadOnlyList<float> Weights => weights;

		/// <summary>
		/// A deliberately modest starting model, used until self-play has produced enough games to
		/// fit one. The signs are the only claim it makes - more army, more economy and more ground
		/// are better - and the magnitudes are small so that a fitted model overrides it easily.
		/// </summary>
		public static WinProbabilityModel Default()
		{
			var weights = new float[StateFeatures.Count];
			weights[(int)StateFeatures.Feature.ArmyVsSeenEnemy] = 2.0f;
			weights[(int)StateFeatures.Feature.ArmyScale] = 1.0f;
			weights[(int)StateFeatures.Feature.EconomyScale] = 1.2f;
			weights[(int)StateFeatures.Feature.HarvesterScale] = 0.5f;
			weights[(int)StateFeatures.Feature.BaseIntact] = 1.5f;
			weights[(int)StateFeatures.Feature.MapControl] = 0.8f;
			weights[(int)StateFeatures.Feature.ContestedFraction] = 0f;
			weights[(int)StateFeatures.Feature.ExploredFraction] = 0.3f;

			// Negative: enemy structures still standing is the work not yet done. This is the term
			// that makes an assault worth planning, and its absence is why the search preferred to
			// expand indefinitely.
			weights[(int)StateFeatures.Feature.EnemyBaseRemaining] = -2.0f;

			// Chosen so a wholly average position - half the reference army, half the economy, an
			// intact base, nothing contested - reads near even rather than near certain.
			weights[(int)StateFeatures.Feature.Bias] = -1.25f;
			return new WinProbabilityModel(weights);
		}

		/// <summary>Probability of winning from this feature vector, between 0 and 1.</summary>
		public float Evaluate(ReadOnlySpan<float> features)
		{
			var z = 0f;
			for (var i = 0; i < weights.Length && i < features.Length; i++)
				z += weights[i] * features[i];

			return Sigmoid(z);
		}

		public float Evaluate(AbstractState state, ForwardModel model)
		{
			Span<float> features = stackalloc float[StateFeatures.Count];
			StateFeatures.Extract(state, model, features);
			return Evaluate(features);
		}

		public static float Sigmoid(float z)
		{
			// Guarded so a large margin saturates rather than overflowing to infinity.
			if (z >= 30f)
				return 1f;

			if (z <= -30f)
				return 0f;

			return 1f / (1f + MathF.Exp(-z));
		}

		/// <summary>Weights in a form a person can read, largest influence first.</summary>
		public IEnumerable<string> Describe()
		{
			return Enumerable.Range(0, weights.Length)
				.OrderByDescending(i => Math.Abs(weights[i]))
				.Select(i => $"{StateFeatures.NameOf(i),-24} {weights[i],8:F3}");
		}

		public string Serialise() =>
			string.Join(",", weights.Select(w => w.ToString("R", CultureInfo.InvariantCulture)));

		public static WinProbabilityModel Deserialise(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return Default();

			var parts = text.Split(',');
			if (parts.Length != StateFeatures.Count)
				return Default();

			var weights = new float[StateFeatures.Count];
			for (var i = 0; i < parts.Length; i++)
				if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out weights[i]))
					return Default();

			return new WinProbabilityModel(weights);
		}
	}
}
