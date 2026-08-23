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
	/// Fits <see cref="WinProbabilityModel"/> weights to labelled self-play states by gradient
	/// descent on the log loss.
	/// </para>
	/// <para>
	/// Batch gradient descent rather than anything cleverer, for one reason: it is deterministic.
	/// The same games in the same order always produce the same weights, so a commander can be
	/// reproduced from its training data, a regression can be bisected, and a benchmark result
	/// means something. Stochastic methods converge faster and would cost all of that.
	/// </para>
	/// <para>
	/// The L2 penalty is not decoration either. Self-play states are enormously correlated - a
	/// thousand samples from one game are nearly one sample - so an unregularised fit will happily
	/// put a weight of forty on whichever feature happened to separate the games it saw.
	/// </para>
	/// </summary>
	public static class LogisticFit
	{
		/// <summary>One observed state and how the game it came from turned out.</summary>
		public readonly record struct Sample(float[] Features, bool Won);

		/// <summary>How the fit went, so a bad one can be recognised rather than shipped.</summary>
		public sealed class Result
		{
			public WinProbabilityModel Model { get; init; }

			/// <summary>Mean log loss on the training data. Lower is better; 0.693 is a coin flip.</summary>
			public float LogLoss { get; init; }

			/// <summary>Brier score: mean squared error of the probabilities. 0.25 is a coin flip.</summary>
			public float BrierScore { get; init; }

			/// <summary>Fraction of samples whose outcome the model would have called correctly.</summary>
			public float Accuracy { get; init; }

			public int Samples { get; init; }
			public int Iterations { get; init; }
		}

		/// <summary>
		/// Fits weights. <paramref name="learningRate"/> and <paramref name="iterations"/> are
		/// deliberately fixed rather than adaptive, so the result depends only on the data.
		/// </summary>
		public static Result Fit(IReadOnlyList<Sample> samples, float learningRate = 0.5f,
			int iterations = 2000, float l2 = 0.01f)
		{
			ArgumentNullException.ThrowIfNull(samples);

			var n = StateFeatures.Count;
			var weights = new float[n];

			if (samples.Count == 0)
				return Score(WinProbabilityModel.Default(), samples, 0);

			var gradient = new float[n];

			for (var iteration = 0; iteration < iterations; iteration++)
			{
				Array.Clear(gradient);

				foreach (var sample in samples)
				{
					if (sample.Features == null || sample.Features.Length < n)
						continue;

					var z = 0f;
					for (var i = 0; i < n; i++)
						z += weights[i] * sample.Features[i];

					// The gradient of log loss with respect to z is simply (prediction - label),
					// which is what makes logistic regression cheap enough to refit every batch.
					var error = WinProbabilityModel.Sigmoid(z) - (sample.Won ? 1f : 0f);
					for (var i = 0; i < n; i++)
						gradient[i] += error * sample.Features[i];
				}

				var scale = learningRate / samples.Count;
				for (var i = 0; i < n; i++)
				{
					// The bias is not penalised: shrinking it toward zero would force the model to
					// believe every game is even, which is a claim about the data rather than a
					// guard against overfitting it.
					var penalty = i == (int)StateFeatures.Feature.Bias ? 0f : l2 * weights[i];
					weights[i] -= scale * (gradient[i] + penalty);
				}
			}

			return Score(new WinProbabilityModel(weights), samples, iterations);
		}

		/// <summary>Grades a model against samples, including the Brier score the repo already uses elsewhere.</summary>
		public static Result Score(WinProbabilityModel model, IReadOnlyList<Sample> samples, int iterations)
		{
			ArgumentNullException.ThrowIfNull(model);
			ArgumentNullException.ThrowIfNull(samples);

			if (samples.Count == 0)
				return new Result { Model = model, LogLoss = 0f, BrierScore = 0f, Accuracy = 0f, Samples = 0, Iterations = iterations };

			var logLoss = 0f;
			var brier = 0f;
			var correct = 0;

			foreach (var sample in samples)
			{
				var p = model.Evaluate(sample.Features);
				var label = sample.Won ? 1f : 0f;

				// Clamped away from the asymptotes, where log loss is infinite and one confident
				// mistake would dominate every other sample in the set.
				var clamped = Math.Clamp(p, 1e-6f, 1f - 1e-6f);
				logLoss += -((label * MathF.Log(clamped)) + ((1f - label) * MathF.Log(1f - clamped)));

				var residual = p - label;
				brier += residual * residual;

				if ((p >= 0.5f) == sample.Won)
					correct++;
			}

			return new Result
			{
				Model = model,
				LogLoss = logLoss / samples.Count,
				BrierScore = brier / samples.Count,
				Accuracy = correct / (float)samples.Count,
				Samples = samples.Count,
				Iterations = iterations,
			};
		}
	}
}
