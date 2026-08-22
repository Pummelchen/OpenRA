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

using NUnit.Framework;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class MatchMetricsTest
	{
		[TestCase(TestName = "No samples produce an empty summary.")]
		public void Empty()
		{
			var metrics = new CoalitionMatchMetrics();
			Assert.That(metrics.Samples, Is.EqualTo(0));
			Assert.That(metrics.Summary(), Does.Contain("no samples"));
		}

		[TestCase(TestName = "A value drop is counted as lost.")]
		public void ValueLost()
		{
			var metrics = new CoalitionMatchMetrics();

			metrics.Sample(100f, 100f, 0f, 1f, 500f);
			metrics.Sample(80f, 60f, 0f, 1f, 500f);

			Assert.That(metrics.FriendlyValueLost, Is.EqualTo(20f).Within(0.001f));
			Assert.That(metrics.EnemyValueDestroyed, Is.EqualTo(40f).Within(0.001f));
			Assert.That(metrics.ExchangeRatio, Is.EqualTo(2f).Within(0.001f));
		}

		[TestCase(TestName = "Recovering value does not subtract from prior losses.")]
		public void RecoveryNotCredited()
		{
			var metrics = new CoalitionMatchMetrics();

			metrics.Sample(100f, 100f, 0f, 1f, 500f);
			metrics.Sample(50f, 50f, 0f, 1f, 500f);
			metrics.Sample(120f, 120f, 0f, 1f, 500f);

			Assert.That(metrics.FriendlyValueLost, Is.EqualTo(50f).Within(0.001f),
				"Losses are cumulative; a later rebuild does not erase them.");
		}

		[TestCase(TestName = "Idle and cohesion are averaged over samples.")]
		public void Averages()
		{
			var metrics = new CoalitionMatchMetrics();

			metrics.Sample(100f, 100f, 0.2f, 0.8f, 500f);
			metrics.Sample(100f, 100f, 0.4f, 0.6f, 500f);

			Assert.That(metrics.AverageIdleFraction, Is.EqualTo(0.3f).Within(0.001f));
			Assert.That(metrics.AverageCohesion, Is.EqualTo(0.7f).Within(0.001f));
			Assert.That(metrics.AverageCash, Is.EqualTo(500f).Within(0.001f));
		}

		[TestCase(TestName = "Production idle time and reserve availability are averaged independently.")]
		public void OperationalAverages()
		{
			var metrics = new CoalitionMatchMetrics();
			metrics.SampleOperations(0.25f, 0.2f);
			metrics.SampleOperations(0.75f, 0.3f);

			Assert.That(metrics.AverageProductionIdleFraction, Is.EqualTo(0.5f).Within(0.001f));
			Assert.That(metrics.AverageReserveAvailability, Is.EqualTo(0.25f).Within(0.001f));
		}

		[TestCase(TestName = "The summary reports the exchange ratio.")]
		public void SummaryContent()
		{
			var metrics = new CoalitionMatchMetrics();

			metrics.Sample(100f, 100f, 0f, 1f, 500f);
			metrics.Sample(80f, 60f, 0.1f, 0.9f, 500f);

			var summary = metrics.Summary();
			Assert.That(summary, Does.Contain("exchange"));
			Assert.That(summary, Does.Contain("army idle"));
			Assert.That(summary, Does.Contain("cohesion"));
			Assert.That(summary, Does.Contain("samples 2"));
		}

		[TestCase(TestName = "Engagement and feint counters accumulate and appear in the summary.")]
		public void EngagementAndFeintCounters()
		{
			var metrics = new CoalitionMatchMetrics();
			metrics.Sample(100f, 100f, 0f, 1f, 500f);

			metrics.RecordEngagement(true);
			metrics.RecordEngagement(false);
			metrics.RecordEngagement(true);
			metrics.RecordFeintLaunch();
			metrics.RecordFeintLaunch();
			metrics.RecordFeintOpenedWindow();

			Assert.That(metrics.EngagementSuperiority, Is.EqualTo(new CoalitionMatchMetrics.LocalSuperiorityStats(3, 2)));
			Assert.That(metrics.FeintEffectiveness, Is.EqualTo(new CoalitionMatchMetrics.FeintStats(2, 1)));
			Assert.That(metrics.Summary(), Does.Contain("engagements 3 (2 superior)"));
			Assert.That(metrics.Summary(), Does.Contain("feints 2 (1 window)"));
		}

		[TestCase(TestName = "Synchronization telemetry exposes average and worst launch error.")]
		public void SynchronizationOutcomes()
		{
			var metrics = new CoalitionMatchMetrics();
			metrics.RecordSyncError(100, 4);
			metrics.RecordSyncError(200, 10);
			metrics.Sample(100f, 100f, 0f, 1f, 500f);

			Assert.That(metrics.Synchronization,
				Is.EqualTo(new CoalitionMatchMetrics.SynchronizationStats(2, 7f, 10)));
			Assert.That(metrics.Summary(), Does.Contain("sync 7.0 avg/10 max ticks"));
		}

		[TestCase(TestName = "Retreat telemetry measures force preservation and ignores duplicate outcomes.")]
		public void RetreatOutcomes()
		{
			var metrics = new CoalitionMatchMetrics();
			metrics.RecordRetreat(100, 10);
			metrics.RecordRetreatOutcome(7);
			metrics.RecordRetreatOutcome(10);
			metrics.Sample(100f, 100f, 0f, 1f, 500f);

			Assert.That(metrics.RetreatEffectiveness,
				Is.EqualTo(new CoalitionMatchMetrics.RetreatStats(1, 1, 10, 7, 0.7f)));
			Assert.That(metrics.Summary(), Does.Contain("retreats 1/1 complete (70% preserved)"));
		}

		[TestCase(TestName = "Final result records match duration.")]
		public void MatchDuration()
		{
			var metrics = new CoalitionMatchMetrics();
			metrics.Sample(100f, 100f, 0f, 1f, 500f);
			metrics.RecordResult(true, 12345);

			Assert.That(metrics.Won, Is.True);
			Assert.That(metrics.DurationTicks, Is.EqualTo(12345));
			Assert.That(metrics.Summary(), Does.Contain("duration 12345 ticks"));
		}

		[TestCase(TestName = "Production priorities are logged only when their resolved directive changes.")]
		public void ProductionPriorityChanges()
		{
			Assert.That(CoalitionCommandCenterBotModule.ProductionDirectiveChanged(null, "[\"e1\"]"), Is.True);
			Assert.That(CoalitionCommandCenterBotModule.ProductionDirectiveChanged("[\"e1\"]", "[\"e1\"]"), Is.False);
			Assert.That(CoalitionCommandCenterBotModule.ProductionDirectiveChanged("[\"e1\"]", "[\"e3\"]"), Is.True);
		}
	}
}
