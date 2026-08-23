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
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

namespace OpenRA.Test
{
	/// <summary>
	/// <para>
	/// A fixed corpus of representative Red Alert engagements with known qualitative outcomes, used
	/// as a benchmark for <see cref="CombatEstimator"/> (reqs 157, 158, 713).
	/// </para>
	/// <para>
	/// Previously the estimator was tested only on invariants - that a ratio is between 0 and 1, that
	/// more power is better than less. Those hold for an estimator that is uniformly wrong. A corpus
	/// with expected outcomes is what turns "the arithmetic is self-consistent" into "the arithmetic
	/// agrees with how these units actually fight", and it is the historical baseline a change to the
	/// estimator can be scored against rather than merely re-derived from.
	/// </para>
	/// </summary>
	[TestFixture]
	sealed class EstimatorBenchmarkTest
	{
		/// <summary>One benchmark engagement: a named matchup and the outcome it should predict.</summary>
		sealed record Engagement(string Name, int[] Friendly, int[] Enemy, bool FriendlyShouldWin);

		static int[] Force(int infantry = 0, int armor = 0, int air = 0, int naval = 0, int structure = 0)
		{
			var counts = new int[Enum.GetValues<UnitClass>().Length];
			counts[(int)UnitClass.Infantry] = infantry;
			counts[(int)UnitClass.Armor] = armor;
			counts[(int)UnitClass.Air] = air;
			counts[(int)UnitClass.Naval] = naval;
			counts[(int)UnitClass.Structure] = structure;
			return counts;
		}

		/// <summary>
		/// The corpus. Each entry is a matchup whose outcome is not in dispute among players: armour
		/// beats an equal count of rifle infantry, infantry cannot shoot down aircraft, ships cannot
		/// fight tanks inland, and a large enough numerical edge decides regardless of composition.
		/// </summary>
		static readonly Engagement[] Corpus =
		[
			new("armor overruns equal infantry", Force(armor: 8), Force(infantry: 8), true),
			new("infantry loses to equal armor", Force(infantry: 8), Force(armor: 8), false),
			new("massed infantry beats a token tank force", Force(infantry: 30), Force(armor: 4), true),
			new("infantry cannot fight aircraft", Force(infantry: 10), Force(air: 10), false),
			new("aircraft harass shipping effectively", Force(air: 8), Force(naval: 8), true),
			new("ships cannot engage armor inland", Force(naval: 8), Force(armor: 8), false),
			new("armor cracks static defenses", Force(armor: 12), Force(structure: 6), true),
			new("overwhelming numbers decide", Force(armor: 24), Force(armor: 4), true),
			new("being overwhelmed is predicted as a loss", Force(armor: 4), Force(armor: 24), false),
			new("a lone unit against a base loses", Force(infantry: 1), Force(armor: 10, structure: 4), false)
		];

		static (float WinRatio, float LossFraction) Predict(Engagement e)
		{
			var friendly = CombatEstimator.MatchupPower(e.Friendly, e.Enemy, health: 1f);
			var enemy = CombatEstimator.MatchupPower(e.Enemy, e.Friendly, health: 1f);
			return CombatEstimator.Estimate(friendly, enemy);
		}

		[TestCase(TestName = "158/713: the estimator predicts every representative engagement correctly.")]
		public void CorpusIsPredictedCorrectly()
		{
			var wrong = Corpus
				.Where(e => Predict(e).WinRatio >= 1f != e.FriendlyShouldWin)
				.Select(e => $"{e.Name} (ratio {Predict(e).WinRatio:0.00})")
				.ToArray();

			Assert.That(wrong, Is.Empty,
				"The estimator disagrees with known outcomes for: " + string.Join("; ", wrong));
		}

		[TestCase(TestName = "713: the corpus Brier score stays at the recorded historical baseline.")]
		public void CorpusBrierScoreDoesNotRegress()
		{
			// Scored the same way live engagements are, so a change to the estimator is measured
			// against this corpus rather than only against its own invariants.
			var log = new EngagementOutcomeLog();
			for (var i = 0; i < Corpus.Length; i++)
			{
				var (ratio, loss) = Predict(Corpus[i]);

				// A ratio is unbounded above; the win *probability* it implies is what gets scored.
				var probability = Math.Clamp(ratio / (ratio + 1f), 0f, 1f);
				log.Predict($"bench-{i}", i, probability, loss, 100f);
				log.Resolve($"bench-{i}", i + 1, Corpus[i].FriendlyShouldWin, loss);
			}

			Assert.That(log.ResolvedCount, Is.EqualTo(Corpus.Length));
			Assert.That(log.DirectionalAccuracy, Is.EqualTo(1f),
				"Every corpus engagement must fall on the correct side of even odds.");

			// The recorded baseline. A change that raises this is making the estimator worse on
			// engagements whose outcome is not in dispute.
			Assert.That(log.BrierScore, Is.LessThanOrEqualTo(0.16f),
				$"Corpus Brier score regressed to {log.BrierScore:0.000} (historical baseline 0.16).");
		}

		[TestCase(TestName = "157: estimates are deterministic, so the commander cannot invent a ratio.")]
		public void EstimatesAreDeterministic()
		{
			// The point of routing the model through the engine estimator is that the number it acts
			// on is computed, not asserted. Identical inputs must therefore give identical output.
			foreach (var e in Corpus)
			{
				var first = Predict(e);
				var second = Predict(e);
				Assert.That(second, Is.EqualTo(first), $"Estimate for '{e.Name}' is not reproducible.");
			}
		}

		[TestCase(TestName = "157: a stronger force never receives a worse estimate than a weaker one.")]
		public void EstimatesAreMonotonic()
		{
			// Monotonicity is what makes the estimate usable as a decision input: adding units must
			// never make the engine report a worse fight, or the commander could be pushed into a
			// smaller commitment by reinforcing.
			var enemy = Force(armor: 10);
			var previous = 0f;
			for (var n = 1; n <= 20; n++)
			{
				var friendly = Force(armor: n);
				var ratio = CombatEstimator.Estimate(
					CombatEstimator.MatchupPower(friendly, enemy, 1f),
					CombatEstimator.MatchupPower(enemy, friendly, 1f)).WinRatio;

				Assert.That(ratio, Is.GreaterThanOrEqualTo(previous), $"Adding the {n}th tank lowered the estimate.");
				previous = ratio;
			}
		}

		[TestCase(TestName = "157: damaged forces are estimated lower than healthy ones.")]
		public void HealthLowersTheEstimate()
		{
			var friendly = Force(armor: 10);
			var enemy = Force(armor: 10);

			var healthy = CombatEstimator.MatchupPower(friendly, enemy, health: 1f);
			var damaged = CombatEstimator.MatchupPower(friendly, enemy, health: 0.4f);

			Assert.That(damaged, Is.LessThan(healthy));
		}
	}
}
