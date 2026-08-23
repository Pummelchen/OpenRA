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
	/// The opponent model. Its job is to let the commander price a counter before the counter
	/// arrives, and to say honestly how sure it is - because that confidence decides whether to
	/// exploit the opponent or to fall back on the mixture that cannot be punished.
	/// </summary>
	[TestFixture]
	sealed class StrategyPosteriorTest
	{
		[TestCase(TestName = "Knowing nothing looks like knowing nothing.")]
		public void StartsUniform()
		{
			var posterior = new StrategyPosterior();
			var distribution = posterior.Distribution();

			foreach (var p in distribution)
				Assert.That(p, Is.EqualTo(1f / StrategyPosterior.Count).Within(1e-6f));

			Assert.That(posterior.Confidence(), Is.EqualTo(0f).Within(1e-5f),
				"A uniform posterior must report no confidence, not a default assumption.");
		}

		[TestCase(TestName = "One airfield swings the belief hard toward air.")]
		public void EvidenceMovesThePosterior()
		{
			// The point of having a posterior at all: a commander that waits to see aircraft before
			// building anti-air is already too late.
			var posterior = new StrategyPosterior();
			posterior.Observe(StrategyPosterior.StructureLikelihood("airfield"));

			var (best, probability) = posterior.Best();
			Assert.That(best, Is.EqualTo(OpponentStrategy.Air));
			Assert.That(probability, Is.GreaterThan(1f / StrategyPosterior.Count));
			Assert.That(posterior.Confidence(), Is.GreaterThan(0f));
		}

		[TestCase(TestName = "Consistent evidence accumulates into confidence.")]
		public void ConfidenceGrowsWithEvidence()
		{
			var posterior = new StrategyPosterior();
			var first = posterior.Confidence();

			for (var i = 0; i < 5; i++)
				posterior.Observe(StrategyPosterior.StructureLikelihood("defence"));

			Assert.That(posterior.Best().Strategy, Is.EqualTo(OpponentStrategy.Turtle));
			Assert.That(posterior.Confidence(), Is.GreaterThan(first));
			Assert.That(posterior.Confidence(), Is.LessThan(1f), "But never certainty.");
		}

		[TestCase(TestName = "It can always change its mind.")]
		public void NeverCollapses()
		{
			// The classic Bayesian failure: a run of consistent evidence drives a hypothesis to
			// zero, and no later evidence can revive it. An opponent who has teched for ten minutes
			// can still build a barracks, and a filter that has collapsed cannot notice.
			var posterior = new StrategyPosterior();
			for (var i = 0; i < 200; i++)
				posterior.Observe(StrategyPosterior.StructureLikelihood("tech"));

			Assert.That(posterior.Best().Strategy, Is.EqualTo(OpponentStrategy.Tech));
			Assert.That(posterior[OpponentStrategy.Rush],
				Is.GreaterThanOrEqualTo(StrategyPosterior.MinimumProbability * 0.9f),
				"No hypothesis may be extinguished.");

			for (var i = 0; i < 30; i++)
				posterior.Observe(StrategyPosterior.StructureLikelihood("barracks"));

			Assert.That(posterior.Best().Strategy, Is.EqualTo(OpponentStrategy.Rush),
				"And a genuine change of plan must be recoverable.");
		}

		[TestCase(TestName = "Confidence reflects the whole distribution, not just its peak.")]
		public void ConfidenceUsesTheWholeDistribution()
		{
			// Two hypotheses at 45% each is not confidence, even though the leader looks strong -
			// and treating it as confidence is how a commander commits to the wrong counter.
			var split = new StrategyPosterior();
			split.Observe([3f, 3f, 0.2f, 0.2f, 0.2f, 0.2f]);

			var focused = new StrategyPosterior();
			focused.Observe([6f, 0.2f, 0.2f, 0.2f, 0.2f, 0.2f]);

			Assert.That(focused.Confidence(), Is.GreaterThan(split.Confidence()));
		}

		[TestCase(TestName = "Army size at a given minute is evidence in itself.")]
		public void ArmySizeIsEvidence()
		{
			var early = new StrategyPosterior();
			early.Observe(StrategyPosterior.ArmySizeLikelihood(armyValue: 6000f, minutes: 3f));
			Assert.That(early.Best().Strategy, Is.EqualTo(OpponentStrategy.Rush),
				"Six thousand credits of army at three minutes is not a coincidence.");

			var greedy = new StrategyPosterior();
			greedy.Observe(StrategyPosterior.ArmySizeLikelihood(armyValue: 1000f, minutes: 6f));
			Assert.That(new[] { OpponentStrategy.Expand, OpponentStrategy.Tech },
				Does.Contain(greedy.Best().Strategy));

			// An ordinary rate says nothing, and must be allowed to say nothing.
			var ordinary = new StrategyPosterior();
			ordinary.Observe(StrategyPosterior.ArmySizeLikelihood(armyValue: 5000f, minutes: 5f));
			Assert.That(ordinary.Confidence(), Is.EqualTo(0f).Within(1e-5f));
		}

		[TestCase(TestName = "The distribution stays a distribution.")]
		public void AlwaysNormalised()
		{
			var posterior = new StrategyPosterior();
			for (var i = 0; i < 50; i++)
			{
				posterior.Observe(StrategyPosterior.StructureLikelihood(i % 2 == 0 ? "refinery" : "tech"));
				Assert.That(posterior.Distribution().Sum(), Is.EqualTo(1f).Within(1e-4f));
			}
		}

		[TestCase(TestName = "Impossible evidence leaves the belief alone.")]
		public void ImpossibleEvidenceIsIgnored()
		{
			// Evidence impossible under every hypothesis says the hypothesis set is wrong, not that
			// the world is. Normalising zeroes would divide by zero.
			var posterior = new StrategyPosterior();
			posterior.Observe(StrategyPosterior.StructureLikelihood("airfield"));
			var before = posterior.Distribution();

			posterior.Observe([0f, 0f, 0f, 0f, 0f, 0f]);
			Assert.That(posterior.Distribution(), Is.EqualTo(before).Within(1e-6f));

			Assert.That(() => posterior.Observe([1f, 1f]), Throws.ArgumentException);
			Assert.That(() => posterior.Observe(null), Throws.ArgumentNullException);
		}

		[TestCase(TestName = "An unknown tell is not evidence.")]
		public void UnknownStructuresAreNeutral()
		{
			var posterior = new StrategyPosterior();
			posterior.Observe(StrategyPosterior.StructureLikelihood("something-nobody-classified"));

			Assert.That(posterior.Confidence(), Is.EqualTo(0f).Within(1e-5f),
				"A structure with no known meaning must not shift the belief in some arbitrary direction.");
		}
	}
}
