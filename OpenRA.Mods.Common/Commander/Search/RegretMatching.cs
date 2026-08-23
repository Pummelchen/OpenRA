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

namespace OpenRA.Mods.Common.Commander.Search
{
	/// <summary>
	/// <para>
	/// Chooses between strategies in proportions that cannot be punished.
	/// </para>
	/// <para>
	/// "Unbeatable" needs a precise definition before it can be engineered toward, and the honest one
	/// is narrower than the word suggests. Against a <i>fixed</i> opponent the best play is a best
	/// response - exploit their pattern for everything it is worth. Against an <i>adapting</i>
	/// opponent a best response is itself exploitable, because whatever you do consistently can be
	/// countered. What is achievable is <b>unexploitability</b>: in a zero-sum game a Nash
	/// equilibrium guarantees at least the game value against every opponent, including one that
	/// knows your strategy exactly. That is the strongest guarantee available.
	/// </para>
	/// <para>
	/// This replaces UCB1, which was the wrong tool for the job. UCB1 converges to the single best
	/// arm against a <i>stationary</i> environment; an opponent who notices you always siege will
	/// build against siege, and UCB1 - having converged - keeps playing the countered arm until
	/// enough losses accumulate to move the average.
	/// </para>
	/// <para>
	/// One thing to be precise about, because it is easy to overclaim: regret matching does not
	/// produce a mixed strategy against everything. Run against a <i>fixed</i> opponent it converges
	/// onto the pure best response, much as a bandit would - and that is correct, because
	/// best-responding to a fixed opponent is optimal against that opponent. The Nash guarantee
	/// comes specifically from the <see cref="AverageStrategy"/> learned in <i>self-play</i>, where
	/// both sides adapt. The unexploitable mixture and the best response are two different objects.
	/// </para>
	/// <para>
	/// Nash is a floor, not a ceiling: it guarantees you cannot be beaten badly, not that you beat a
	/// weak opponent quickly. So the intended use is to learn the mixture in self-play, play it by
	/// default, and deviate toward a best response only while the opponent model is confident - see
	/// <see cref="MixWith"/>. Conflating the two would mean either being punished by an adapting
	/// opponent or being needlessly slow against a predictable one.
	/// </para>
	/// </summary>
	public sealed class RegretMatching
	{
		readonly double[] cumulativeRegret;
		readonly double[] cumulativeStrategy;
		readonly int actions;

		public RegretMatching(int actions)
		{
			ArgumentOutOfRangeException.ThrowIfLessThan(actions, 1);

			this.actions = actions;
			cumulativeRegret = new double[actions];
			cumulativeStrategy = new double[actions];
		}

		public int Actions => actions;

		public int Updates { get; private set; }

		/// <summary>
		/// The mixture to play now, proportional to positive regret. With no regret accumulated yet
		/// - or none positive, meaning nothing has ever done better than what was played - it is
		/// uniform, which is the correct expression of knowing nothing.
		/// </summary>
		public float[] CurrentStrategy()
		{
			var strategy = new float[actions];
			var total = 0.0;

			for (var i = 0; i < actions; i++)
			{
				// Negative regret means the action would have done worse. It is clamped rather than
				// carried, because an action that has been bad in the past is not thereby forbidden
				// - only unfavoured.
				var positive = Math.Max(0.0, cumulativeRegret[i]);
				strategy[i] = (float)positive;
				total += positive;
			}

			if (total <= 0.0)
			{
				var uniform = 1f / actions;
				for (var i = 0; i < actions; i++)
					strategy[i] = uniform;

				return strategy;
			}

			for (var i = 0; i < actions; i++)
				strategy[i] = (float)(strategy[i] / total);

			return strategy;
		}

		/// <summary>
		/// <para>
		/// The average of every strategy played so far. <b>This</b> is the one that converges to
		/// Nash, not the current strategy - a distinction that is easy to get wrong and quietly
		/// costs the guarantee.
		/// </para>
		/// </summary>
		public float[] AverageStrategy()
		{
			var average = new float[actions];
			var total = 0.0;
			for (var i = 0; i < actions; i++)
				total += cumulativeStrategy[i];

			if (total <= 0.0)
			{
				var uniform = 1f / actions;
				for (var i = 0; i < actions; i++)
					average[i] = uniform;

				return average;
			}

			for (var i = 0; i < actions; i++)
				average[i] = (float)(cumulativeStrategy[i] / total);

			return average;
		}

		/// <summary>
		/// Folds in one round. <paramref name="payoffs"/> is what each strategy <i>would</i> have
		/// scored against what the opponent actually did - the counterfactual that gives the method
		/// its name and its power: it learns from options it did not take.
		/// </summary>
		public void Observe(IReadOnlyList<float> payoffs)
		{
			ArgumentNullException.ThrowIfNull(payoffs);
			if (payoffs.Count < actions)
				throw new ArgumentException($"Expected {actions} payoffs.", nameof(payoffs));

			var strategy = CurrentStrategy();

			// Value actually obtained: the payoff weighted by how often each action is played.
			var expected = 0.0;
			for (var i = 0; i < actions; i++)
				expected += strategy[i] * payoffs[i];

			for (var i = 0; i < actions; i++)
			{
				cumulativeRegret[i] += payoffs[i] - expected;
				cumulativeStrategy[i] += strategy[i];
			}

			Updates++;
		}

		/// <summary>
		/// How exploitable the current average strategy is: how much better the best single reply
		/// does than the mixture itself. Zero is Nash. This is the number that says whether the
		/// commander is actually unexploitable rather than merely unpredictable.
		/// </summary>
		public float Exploitability(IReadOnlyList<float> payoffs)
		{
			ArgumentNullException.ThrowIfNull(payoffs);
			if (payoffs.Count < actions)
				return 0f;

			var average = AverageStrategy();
			var expected = 0.0;
			var best = double.NegativeInfinity;

			for (var i = 0; i < actions; i++)
			{
				expected += average[i] * payoffs[i];
				best = Math.Max(best, payoffs[i]);
			}

			return (float)Math.Max(0.0, best - expected);
		}

		/// <summary>
		/// Blends the unexploitable mixture with a best response, by how confident the opponent
		/// model is. Confidence 0 plays pure Nash and cannot be punished; confidence 1 plays the
		/// best response and beats a predictable opponent quickly. Anything between hedges.
		/// </summary>
		public static float[] MixWith(IReadOnlyList<float> nash, IReadOnlyList<float> bestResponse, float confidence)
		{
			ArgumentNullException.ThrowIfNull(nash);
			ArgumentNullException.ThrowIfNull(bestResponse);

			confidence = Math.Clamp(confidence, 0f, 1f);
			var mixed = new float[nash.Count];
			var total = 0f;

			for (var i = 0; i < mixed.Length; i++)
			{
				var response = i < bestResponse.Count ? bestResponse[i] : 0f;
				mixed[i] = ((1f - confidence) * nash[i]) + (confidence * response);
				total += mixed[i];
			}

			if (total <= 0f)
				return CloneAsFloats(nash);

			for (var i = 0; i < mixed.Length; i++)
				mixed[i] /= total;

			return mixed;
		}

		/// <summary>
		/// Picks an action from a mixture using a caller-supplied value in [0,1). Deterministic given
		/// that value, so the caller decides where its randomness comes from - which matters because
		/// consuming the world's shared random stream inside a bot would desynchronise a replay.
		/// </summary>
		public static int Sample(IReadOnlyList<float> distribution, float uniformSample)
		{
			ArgumentNullException.ThrowIfNull(distribution);
			if (distribution.Count == 0)
				return 0;

			var target = Math.Clamp(uniformSample, 0f, 0.999999f);
			var cumulative = 0f;

			for (var i = 0; i < distribution.Count; i++)
			{
				cumulative += Math.Max(0f, distribution[i]);
				if (target < cumulative)
					return i;
			}

			// Floating-point drift can leave the total a hair below the sample; the last action with
			// any weight is the right answer, not an exception.
			for (var i = distribution.Count - 1; i >= 0; i--)
				if (distribution[i] > 0f)
					return i;

			return 0;
		}

		static float[] CloneAsFloats(IReadOnlyList<float> source)
		{
			var copy = new float[source.Count];
			for (var i = 0; i < source.Count; i++)
				copy[i] = source[i];

			return copy;
		}
	}
}
