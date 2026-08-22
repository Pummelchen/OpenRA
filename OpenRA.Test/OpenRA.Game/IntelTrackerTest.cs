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
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class IntelTrackerTest
	{
		static float ExpectedConfidence(int ageTicks, int timestep = 40)
		{
			return MathF.Pow(0.5f, ageTicks * timestep / 1000f / 30f);
		}

		[TestCase(TestName = "A fresh sighting is OBSERVED with full confidence and no error.")]
		public void FreshSightingObserved()
		{
			var tracker = new CoalitionIntelTracker(memoryTicks: 600, timestep: 40);
			tracker.Observe("3tnk", UnitClass.Armor, 5, new CPos(10, 10), 100);

			var intel = tracker.Age(100).Single();

			Assert.That(intel.Status, Is.EqualTo(IntelStatus.Observed));
			Assert.That(intel.Confidence, Is.EqualTo(1f));
			Assert.That(intel.AgeTicks, Is.EqualTo(0));
			Assert.That(intel.PositionErrorCells, Is.EqualTo(0));
			Assert.That(intel.MinCount, Is.EqualTo(1));
			Assert.That(intel.MaxCount, Is.EqualTo(1));
		}

		[TestCase(TestName = "Remembered coalition intel is a snapshot and retains no live actor reference.")]
		public void EnemyIntelDoesNotRetainActor()
		{
			var hasActorReference = typeof(EnemyIntel).GetFields()
				.Any(field => typeof(Actor).IsAssignableFrom(field.FieldType));

			Assert.That(hasActorReference, Is.False);
		}

		[TestCase(TestName = "Losing contact downgrades a mobile sighting to LAST_KNOWN with decaying confidence.")]
		public void MobileBecomesLastKnown()
		{
			var tracker = new CoalitionIntelTracker(memoryTicks: 600, timestep: 40);
			tracker.Observe("3tnk", UnitClass.Armor, 5, new CPos(10, 10), 100);
			tracker.Age(100);

			var intel = tracker.Age(200).Single();

			Assert.That(intel.Status, Is.EqualTo(IntelStatus.LastKnown));
			Assert.That(intel.Confidence, Is.EqualTo(ExpectedConfidence(100)).Within(0.001f));
			Assert.That(intel.PositionErrorCells, Is.GreaterThanOrEqualTo(1));
		}

		[TestCase(TestName = "A mobile sighting beyond the memory window is dropped back to UNKNOWN.")]
		public void MobileExpires()
		{
			var tracker = new CoalitionIntelTracker(memoryTicks: 600, timestep: 40);
			tracker.Observe("3tnk", UnitClass.Armor, 5, new CPos(10, 10), 0);
			tracker.Age(0);

			Assert.That(tracker.Age(700), Is.Empty);
		}

		[TestCase(TestName = "A structure sighting becomes INFERRED (still believed present) and persists.")]
		public void StructureInferred()
		{
			var tracker = new CoalitionIntelTracker(memoryTicks: 600, timestep: 40);
			tracker.Observe("weap", UnitClass.Structure, 5, new CPos(10, 10), 0);
			tracker.Age(0);

			var intel = tracker.Age(900).Single();

			Assert.That(intel.Status, Is.EqualTo(IntelStatus.Inferred));
			Assert.That(intel.Confidence, Is.GreaterThanOrEqualTo(0.3f));
			Assert.That(intel.PositionErrorCells, Is.EqualTo(0), "Structures do not move.");
		}

		[TestCase(TestName = "Position error grows as a last-known sighting ages.")]
		public void PositionErrorGrows()
		{
			var tracker = new CoalitionIntelTracker(memoryTicks: 600, timestep: 40);
			tracker.Observe("3tnk", UnitClass.Armor, 5, new CPos(10, 10), 0);
			tracker.Age(0);

			var intel = tracker.Age(500).Single();

			// 500 ticks = 20 s; error = age/5s = 4 cells.
			Assert.That(intel.PositionErrorCells, Is.EqualTo(4));
		}

		[TestCase(TestName = "Multiple sightings of a type aggregate into one observed entry with the count.")]
		public void CountsAggregate()
		{
			var tracker = new CoalitionIntelTracker(memoryTicks: 600, timestep: 40);
			tracker.Observe("3tnk", UnitClass.Armor, 5, new CPos(10, 10), 100);
			tracker.Observe("3tnk", UnitClass.Armor, 5, new CPos(11, 10), 100);
			tracker.Observe("3tnk", UnitClass.Armor, 5, new CPos(10, 11), 100);

			var intel = tracker.Age(100).Single();

			Assert.That(intel.MinCount, Is.EqualTo(3));
			Assert.That(intel.ExpectedCount, Is.EqualTo(3));
			Assert.That(intel.MaxCount, Is.EqualTo(3));
		}

		[TestCase(TestName = "Aging is idempotent within a tick.")]
		public void AgeIsIdempotent()
		{
			var tracker = new CoalitionIntelTracker(memoryTicks: 600, timestep: 40);
			tracker.Observe("3tnk", UnitClass.Armor, 5, new CPos(10, 10), 100);

			var first = tracker.Age(100);
			var second = tracker.Age(100);

			Assert.That(second, Is.SameAs(first));
		}
	}
}
