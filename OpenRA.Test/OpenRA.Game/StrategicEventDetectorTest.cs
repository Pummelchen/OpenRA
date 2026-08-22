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
	sealed class StrategicEventDetectorTest
	{
		[TestCase(TestName = "Enemy base discovery fires once when the region is first located.")]
		public void EnemyBaseDiscovery()
		{
			var detector = new StrategicEventDetector();

			Assert.That(detector.Detect(-1, 0, 3, 0, false), Is.Null);
			Assert.That(detector.Detect(2, 1, 3, 1, false), Is.EqualTo("enemy base discovered"));
			Assert.That(detector.Detect(2, 2, 3, 2, false), Is.Null, "Discovery fires only once.");
		}

		[TestCase(TestName = "A doubling of enemy structures triggers a composition review.")]
		public void CompositionChange()
		{
			var detector = new StrategicEventDetector();
			detector.Detect(2, 2, 3, 2, false);

			Assert.That(detector.Detect(2, 6, 3, 6, false), Is.EqualTo("enemy composition changed (new structures)"));
			Assert.That(detector.Detect(2, 7, 3, 7, false), Is.Null, "Small increments do not re-trigger.");
		}

		[TestCase(TestName = "Losing an allied structure triggers a review.")]
		public void AlliedProductionLost()
		{
			var detector = new StrategicEventDetector();
			detector.Detect(2, 1, 4, 1, false);

			Assert.That(detector.Detect(2, 1, 3, 1, false), Is.EqualTo("allied production lost"));
		}

		[TestCase(TestName = "First observation does not count as a loss.")]
		public void InitialObservationNoLoss()
		{
			var detector = new StrategicEventDetector();
			detector.Detect(-1, 1, 4, 1, false); // Establish baseline without a located enemy.

			Assert.That(detector.Detect(2, 1, 4, 1, false), Is.EqualTo("enemy base discovered"));
			Assert.That(detector.Detect(2, 1, 5, 1, false), Is.Null, "Gaining structures is not a loss.");
		}

		[TestCase(TestName = "Losing all contact with the enemy triggers an intelligence review.")]
		public void ContactLost()
		{
			var detector = new StrategicEventDetector();
			detector.Detect(2, 2, 4, 3, false);

			Assert.That(detector.Detect(2, 2, 4, 0, false), Is.EqualTo("contact with enemy main army lost"));
		}

		[TestCase(TestName = "Discovering a high-value enemy structure triggers a review.")]
		public void HighValueDiscovery()
		{
			var detector = new StrategicEventDetector();
			detector.Detect(2, 2, 4, 2, false);

			Assert.That(detector.Detect(2, 2, 4, 2, true), Is.EqualTo("high-value enemy structure discovered"));
			Assert.That(detector.Detect(2, 2, 4, 2, true), Is.Null, "High-value discovery fires once.");
		}

		[TestCase(TestName = "A quiet board produces no events.")]
		public void QuietBoard()
		{
			var detector = new StrategicEventDetector();
			detector.Detect(2, 3, 4, 3, true); // Establish baseline.

			for (var i = 0; i < 10; i++)
				Assert.That(detector.Detect(2, 3, 4, 3, true), Is.Null);
		}

		[TestCase(TestName = "Operational state changes trigger immediate strategic reviews.")]
		public void OperationalTriggers()
		{
			string Trigger(System.Func<int, int, int, int, int, int, (int Attack, int Failed, int Transport, int Complete, int Route, int Cash)> next)
			{
				var detector = new StrategicEventDetector();
				detector.Detect(2, 2, 4, 3, true, 0, 0, 0, 0, 10, 4000);
				var state = next(0, 0, 0, 0, 10, 4000);
				return detector.Detect(2, 2, 4, 3, true, state.Attack, state.Failed,
					state.Transport, state.Complete, state.Route, state.Cash);
			}

			Assert.That(Trigger((_, f, t, c, r, cash) => (1, f, t, c, r, cash)), Is.EqualTo("major allied attack started"));
			Assert.That(Trigger((a, _, t, c, r, cash) => (a, 1, t, c, r, cash)), Is.EqualTo("major attack failed"));
			Assert.That(Trigger((a, f, _, c, r, cash) => (a, f, 1, c, r, cash)), Is.EqualTo("transport ready"));
			Assert.That(Trigger((a, f, t, _, r, cash) => (a, f, t, 1, r, cash)), Is.EqualTo("mission completed"));
			Assert.That(Trigger((a, f, t, c, _, cash) => (a, f, t, c, 11, cash)), Is.EqualTo("major route or bridge changed"));
			Assert.That(Trigger((a, f, t, c, r, _) => (a, f, t, c, r, 6000)), Is.EqualTo("major economy change"));
		}
	}
}
