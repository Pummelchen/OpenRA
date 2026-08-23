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

using System;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Commander.Search;

namespace OpenRA.Test
{
	/// <summary>
	/// <para>
	/// Regret matching, which replaces UCB1 for choosing between strategies.
	/// </para>
	/// <para>
	/// The property under test is unexploitability, not strength. UCB1 converges to a single best
	/// arm, and a pure strategy is exactly what an adapting opponent punishes. These tests use
	/// rock-paper-scissors because its Nash equilibrium is known exactly - uniform thirds, zero
	/// exploitability - so a claim to have found it can be checked rather than believed.
	/// </para>
	/// </summary>
	[TestFixture]
	sealed class RegretMatchingTest
	{
		/// <summary>Payoff to each of rock, paper, scissors against a given opponent action.</summary>
		static float[] RockPaperScissors(int opponent)
		{
			// rows: rock, paper, scissors
			var payoff = new float[3];
			for (var i = 0; i < 3; i++)
			{
				if (i == opponent)
					payoff[i] = 0f;
				else if ((i == 0 && opponent == 2) || (i == 1 && opponent == 0) || (i == 2 && opponent == 1))
					payoff[i] = 1f;
				else
					payoff[i] = -1f;
			}

			return payoff;
		}

		[TestCase(TestName = "Knowing nothing is expressed as a uniform mixture.")]
		public void StartsUniform()
		{
			var matcher = new RegretMatching(3);
			Assert.That(matcher.CurrentStrategy(), Is.EqualTo(new[] { 1f / 3f, 1f / 3f, 1f / 3f }).Within(1e-6f));
			Assert.That(matcher.AverageStrategy(), Is.EqualTo(new[] { 1f / 3f, 1f / 3f, 1f / 3f }).Within(1e-6f));
		}

		[TestCase(TestName = "It finds the equilibrium of a game whose equilibrium is known.")]
		public void ConvergesToNash()
		{
			// Self-play: both sides use the same learner, which is the standard way regret matching
			// is run and the setting in which the average strategy converges to Nash.
			var matcher = new RegretMatching(3);
			var opponent = new RegretMatching(3);

			for (var round = 0; round < 20000; round++)
			{
				var theirs = opponent.AverageStrategy();

				// Expected payoff of each of our actions against their current mixture - the
				// counterfactual that gives the method its power: it learns from options not taken.
				var payoffs = new float[3];
				for (var ours = 0; ours < 3; ours++)
					for (var t = 0; t < 3; t++)
						payoffs[ours] += theirs[t] * RockPaperScissors(t)[ours];

				matcher.Observe(payoffs);

				var mine = matcher.AverageStrategy();
				var theirPayoffs = new float[3];
				for (var t = 0; t < 3; t++)
					for (var ours = 0; ours < 3; ours++)
						theirPayoffs[t] += mine[ours] * RockPaperScissors(ours)[t];

				opponent.Observe(theirPayoffs);
			}

			var average = matcher.AverageStrategy();
			foreach (var p in average)
				Assert.That(p, Is.EqualTo(1f / 3f).Within(0.05f),
					$"Rock-paper-scissors has exactly one equilibrium; got [{string.Join(", ", average.Select(x => x.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)))}].");
		}

		[TestCase(TestName = "Against a fixed opponent it best-responds, which is correct.")]
		public void ExploitsAStationaryOpponent()
		{
			// Worth being precise about, because it is easy to overclaim here. Regret matching does
			// NOT play a mixed strategy against everything - facing an opponent who always plays
			// rock, it converges onto paper almost entirely, exactly as a bandit would. That is the
			// right answer: best-responding to a fixed opponent is optimal against that opponent,
			// and refusing to would be leaving the win on the table.
			//
			// The Nash guarantee comes from somewhere else - from the average strategy learned in
			// self-play, tested above. The unexploitable mixture and the best response are two
			// different objects, and MixWith is where the commander decides how much of each to
			// play. Conflating them would mean either being punished by an adapting opponent or
			// being needlessly slow against a predictable one.
			var matcher = new RegretMatching(3);
			for (var round = 0; round < 5000; round++)
				matcher.Observe(RockPaperScissors(0));

			var average = matcher.AverageStrategy();
			Assert.That(average[1], Is.GreaterThan(0.9f),
				"Paper beats rock, and an opponent who only plays rock should be punished for it.");
		}

		[TestCase(TestName = "Exploitability falls as the mixture settles.")]
		public void ExploitabilityFalls()
		{
			// The number that says whether the commander is actually unexploitable rather than
			// merely unpredictable: how much better the best single reply does than the mixture.
			var matcher = new RegretMatching(3);
			var payoffs = new[] { 0.2f, 0.5f, 0.3f };

			var initial = matcher.Exploitability(payoffs);
			for (var round = 0; round < 2000; round++)
				matcher.Observe(payoffs);

			Assert.That(matcher.Exploitability(payoffs), Is.LessThan(initial));
			Assert.That(matcher.Exploitability(payoffs), Is.GreaterThanOrEqualTo(0f), "Never negative.");
		}

		[TestCase(TestName = "Confidence blends Nash with a best response.")]
		public void MixingHedgesOnConfidence()
		{
			var nash = new[] { 1f / 3f, 1f / 3f, 1f / 3f };
			var bestResponse = new[] { 0f, 1f, 0f };

			// Knowing nothing about the opponent, play the mixture that cannot be punished.
			var cautious = RegretMatching.MixWith(nash, bestResponse, 0f);
			Assert.That(cautious, Is.EqualTo(nash).Within(1e-6f));

			// Certain of them, punish them.
			var confident = RegretMatching.MixWith(nash, bestResponse, 1f);
			Assert.That(confident[1], Is.EqualTo(1f).Within(1e-6f));

			// In between, hedge - and the result must still be a probability distribution.
			var hedged = RegretMatching.MixWith(nash, bestResponse, 0.5f);
			Assert.That(hedged.Sum(), Is.EqualTo(1f).Within(1e-5f));
			Assert.That(hedged[1], Is.GreaterThan(hedged[0]));
			Assert.That(hedged[0], Is.GreaterThan(0f), "Hedging means not abandoning the alternatives.");
		}

		[TestCase(TestName = "Sampling is deterministic and respects the weights.")]
		public void SamplingIsDeterministic()
		{
			// The caller supplies the random value, because consuming the world's shared random
			// stream inside a bot would desynchronise a replay.
			var distribution = new[] { 0.2f, 0.5f, 0.3f };

			Assert.That(RegretMatching.Sample(distribution, 0.0f), Is.EqualTo(0));
			Assert.That(RegretMatching.Sample(distribution, 0.19f), Is.EqualTo(0));
			Assert.That(RegretMatching.Sample(distribution, 0.21f), Is.EqualTo(1));
			Assert.That(RegretMatching.Sample(distribution, 0.69f), Is.EqualTo(1));
			Assert.That(RegretMatching.Sample(distribution, 0.71f), Is.EqualTo(2));

			// Rounding can leave the total a hair under 1; the last weighted action is the right
			// answer, not an exception.
			Assert.That(RegretMatching.Sample([0.5f, 0.4999f], 0.99999f), Is.EqualTo(1));
			Assert.That(RegretMatching.Sample([], 0.5f), Is.EqualTo(0));
		}

		[TestCase(TestName = "Malformed input is rejected rather than half-applied.")]
		public void RejectsMalformedInput()
		{
			var matcher = new RegretMatching(3);
			Assert.That(() => matcher.Observe(new[] { 1f, 2f }), Throws.ArgumentException);
			Assert.That(() => matcher.Observe(null), Throws.ArgumentNullException);
			Assert.That(() => new RegretMatching(0), Throws.TypeOf<ArgumentOutOfRangeException>());

			// And the rejected round must not have been counted.
			Assert.That(matcher.Updates, Is.EqualTo(0));
		}
	}
}
