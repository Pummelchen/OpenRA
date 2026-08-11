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

		[TestCase(TestName = "The summary reports the exchange ratio.")]
		public void SummaryContent()
		{
			var metrics = new CoalitionMatchMetrics();

			metrics.Sample(100f, 100f, 0f, 1f, 500f);
			metrics.Sample(80f, 60f, 0.1f, 0.9f, 500f);

			var summary = metrics.Summary();
			Assert.That(summary, Does.Contain("exchange"));
			Assert.That(summary, Does.Contain("idle"));
			Assert.That(summary, Does.Contain("cohesion"));
			Assert.That(summary, Does.Contain("samples 2"));
		}
	}
}
