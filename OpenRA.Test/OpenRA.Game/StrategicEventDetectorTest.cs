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
	}
}
