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
	/// <summary>The strategies an opponent might be playing.</summary>
	public enum OpponentStrategy
	{
		Rush,
		Expand,
		Tech,
		Turtle,
		Air,
		Naval,
	}

	/// <summary>
	/// <para>
	/// What the opponent is probably doing, updated by Bayes as evidence arrives.
	/// </para>
	/// <para>
	/// This is what makes adaptation fast rather than reactive. A commander that waits to see
	/// aircraft before building anti-air is already too late; one that has a posterior can swing
	/// hard on a single airfield sighting and price the counter before the first aircraft arrives.
	/// The same evidence that would otherwise be one data point becomes a claim about everything the
	/// opponent is likely to do next.
	/// </para>
	/// <para>
	/// Two properties are deliberate. It starts uniform, because knowing nothing should look like
	/// knowing nothing rather than like a default assumption. And it never reaches certainty -
	/// likelihoods are bounded away from zero - because an opponent who has been teching for ten
	/// minutes can still build a barracks, and a posterior that has collapsed cannot notice.
	/// </para>
	/// </summary>
	public sealed class StrategyPosterior
	{
		public const int Count = (int)OpponentStrategy.Naval + 1;

		/// <summary>
		/// Floor on any strategy's probability. Without it a run of consistent evidence drives a
		/// hypothesis to zero, and no later evidence can ever revive it - the classic way a Bayesian
		/// filter becomes unable to change its mind.
		/// </summary>
		public const float MinimumProbability = 0.01f;

		readonly float[] probability = new float[Count];

		public StrategyPosterior()
		{
			var uniform = 1f / Count;
			for (var i = 0; i < Count; i++)
				probability[i] = uniform;
		}

		public int Observations { get; private set; }

		public float this[OpponentStrategy strategy] => probability[(int)strategy];

		/// <summary>The most likely strategy, and how sure we are of it.</summary>
		public (OpponentStrategy Strategy, float Probability) Best()
		{
			var best = 0;
			for (var i = 1; i < Count; i++)
				if (probability[i] > probability[best])
					best = i;

			return ((OpponentStrategy)best, probability[best]);
		}

		/// <summary>
		/// <para>
		/// How concentrated the belief is, from 0 (no idea) to 1 (certain). Derived from entropy, so
		/// it accounts for the whole distribution rather than only its peak - two hypotheses at 45%
		/// each is not confidence, even though the leader looks strong.
		/// </para>
		/// <para>
		/// This is the number that decides whether to exploit or to play the unexploitable mixture.
		/// </para>
		/// </summary>
		public float Confidence()
		{
			var entropy = 0f;
			foreach (var p in probability)
				if (p > 0f)
					entropy -= p * MathF.Log(p);

			var maximum = MathF.Log(Count);
			return maximum <= 0f ? 0f : Math.Clamp(1f - (entropy / maximum), 0f, 1f);
		}

		/// <summary>
		/// Folds in one observation. <paramref name="likelihood"/> is how probable that observation
		/// would be under each strategy - the numbers that come from self-play statistics rather
		/// than from anybody's opinion.
		/// </summary>
		public void Observe(IReadOnlyList<float> likelihood)
		{
			ArgumentNullException.ThrowIfNull(likelihood);
			if (likelihood.Count < Count)
				throw new ArgumentException($"Expected {Count} likelihoods.", nameof(likelihood));

			var total = 0f;
			var posterior = new float[Count];

			for (var i = 0; i < Count; i++)
			{
				posterior[i] = probability[i] * Math.Max(0f, likelihood[i]);
				total += posterior[i];
			}

			// Evidence impossible under every hypothesis says the hypothesis set is wrong, not that
			// the world is. Leaving the posterior alone is the honest response; normalising zeroes
			// would throw.
			if (total <= 0f)
				return;

			for (var i = 0; i < Count; i++)
				probability[i] = posterior[i] / total;

			Renormalise();
			Observations++;
		}

		/// <summary>
		/// Applies the floor and rescales. Kept separate because it must happen after every update,
		/// and forgetting it is what makes a filter unable to change its mind.
		/// </summary>
		void Renormalise()
		{
			var total = 0f;
			for (var i = 0; i < Count; i++)
			{
				probability[i] = Math.Max(MinimumProbability, probability[i]);
				total += probability[i];
			}

			for (var i = 0; i < Count; i++)
				probability[i] /= total;
		}

		/// <summary>The whole distribution, for logging and for mixing strategies against.</summary>
		public float[] Distribution()
		{
			var copy = new float[Count];
			Array.Copy(probability, copy, Count);
			return copy;
		}

		/// <summary>
		/// Likelihood of seeing a particular structure early, under each strategy. These are the
		/// tells a good player reads without naming them: a barracks and no refinery is a rush; a
		/// second refinery before any army is an expansion; an airfield is an airfield.
		/// </summary>
		public static float[] StructureLikelihood(string category)
		{
			// Order: Rush, Expand, Tech, Turtle, Air, Naval.
			return category switch
			{
				"barracks" => [3.0f, 0.8f, 0.6f, 1.0f, 0.6f, 0.6f],
				"refinery" => [0.5f, 3.0f, 1.2f, 1.0f, 1.0f, 1.0f],
				"defence" => [0.4f, 0.7f, 0.8f, 3.0f, 0.8f, 0.8f],
				"tech" => [0.4f, 0.9f, 3.0f, 1.2f, 1.5f, 1.0f],
				"airfield" => [0.5f, 0.6f, 1.2f, 0.7f, 4.0f, 0.6f],
				"shipyard" => [0.4f, 0.7f, 0.9f, 0.8f, 0.6f, 4.0f],
				_ => [1f, 1f, 1f, 1f, 1f, 1f],
			};
		}

		/// <summary>
		/// Likelihood of the enemy's army being this size at this point in the match. An army far
		/// larger than the clock would suggest is the tell for a rush; a small one with a big
		/// economy is the tell for greed.
		/// </summary>
		public static float[] ArmySizeLikelihood(float armyValue, float minutes)
		{
			if (minutes <= 0f)
				return [1f, 1f, 1f, 1f, 1f, 1f];

			// Credits per minute of army accumulated. Roughly 1,000 is ordinary at this scale.
			var rate = armyValue / minutes;

			if (rate > 1800f)
				return [3.0f, 0.4f, 0.5f, 0.8f, 0.9f, 0.9f];

			if (rate < 500f)
				return [0.4f, 2.0f, 2.0f, 1.4f, 1.0f, 1.0f];

			return [1f, 1f, 1f, 1f, 1f, 1f];
		}
	}
}
