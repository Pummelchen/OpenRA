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

using NUnit.Framework;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

namespace OpenRA.Test
{
	/// <summary>
	/// Time-on-target scheduling (reqs 234-236, 253-259, 261). Previously every domain was ordered at
	/// the target on the same tick and the arrival gap was merely recorded; a slow ground column and
	/// a fast air strike launched together arrive minutes apart, so the strike is spent before the
	/// assault is in position.
	/// </summary>
	[TestFixture]
	sealed class OperationScheduleTest
	{
		const int Interval = 100;
		const int TimeOnTarget = 5000;

		[TestCase(TestName = "Slower components launch earlier so they converge on the objective (reqs 258, 259).")]
		public void TravelTimeDeterminesLaunch()
		{
			var schedule = new OperationSchedule(TimeOnTarget);
			var slow = schedule.Add(OperationComponent.GroundAssault, travelTicks: 900, Interval);
			var fast = schedule.Add(OperationComponent.Reserve, travelTicks: 100, Interval);

			Assert.That(slow.LaunchTick, Is.EqualTo(TimeOnTarget - 900));
			Assert.That(slow.ArrivalTick, Is.EqualTo(TimeOnTarget));
			Assert.That(fast.LaunchTick, Is.GreaterThan(slow.LaunchTick),
				"A fast force ordered as early as a slow one arrives alone and fights alone.");
		}

		[TestCase(TestName = "Recon, deception and shaping all arrive before the ground assault (reqs 234-236).")]
		public void ShapingPrecedesTheBreach()
		{
			var schedule = new OperationSchedule(TimeOnTarget);
			schedule.Add(OperationComponent.Reconnaissance, 200, Interval);
			schedule.Add(OperationComponent.Deception, 300, Interval);
			schedule.Add(OperationComponent.AirStrike, 100, Interval);
			schedule.Add(OperationComponent.SpecialOperation, 400, Interval);
			schedule.Add(OperationComponent.GroundAssault, 900, Interval);

			Assert.That(schedule.Precedes(OperationComponent.Reconnaissance), Is.True);
			Assert.That(schedule.Precedes(OperationComponent.Deception), Is.True);
			Assert.That(schedule.Precedes(OperationComponent.AirStrike), Is.True);
			Assert.That(schedule.Precedes(OperationComponent.SpecialOperation), Is.True);
		}

		[TestCase(TestName = "Reconnaissance leads the whole operation and the reserve trails it.")]
		public void DoctrinalOrdering()
		{
			var recon = OperationSchedule.DoctrinalOffset(OperationComponent.Reconnaissance, Interval);
			var deception = OperationSchedule.DoctrinalOffset(OperationComponent.Deception, Interval);
			var strike = OperationSchedule.DoctrinalOffset(OperationComponent.AirStrike, Interval);
			var ground = OperationSchedule.DoctrinalOffset(OperationComponent.GroundAssault, Interval);
			var reserve = OperationSchedule.DoctrinalOffset(OperationComponent.Reserve, Interval);

			Assert.That(recon, Is.LessThan(deception));
			Assert.That(deception, Is.LessThan(strike));
			Assert.That(strike, Is.LessThan(ground));
			Assert.That(reserve, Is.GreaterThan(ground),
				"A reserve committed on the breach tick is not a reserve.");
		}

		[TestCase(TestName = "Naval bombardment is scheduled to land with the air strike, before the assault (req 255).")]
		public void NavalBombardmentSynchronizes()
		{
			var schedule = new OperationSchedule(TimeOnTarget);
			schedule.Add(OperationComponent.NavalBombardment, 300, Interval);
			schedule.Add(OperationComponent.GroundAssault, 900, Interval);

			Assert.That(schedule.Precedes(OperationComponent.NavalBombardment), Is.True);
			Assert.That(OperationSchedule.DoctrinalOffset(OperationComponent.NavalBombardment, Interval),
				Is.EqualTo(OperationSchedule.DoctrinalOffset(OperationComponent.AirStrike, Interval)),
				"Shaping fires are planned to land together.");
		}

		[TestCase(TestName = "A special operation is timed into the distraction window (req 256).")]
		public void SpecialOpsUseTheDistractionWindow()
		{
			var deception = OperationSchedule.DoctrinalOffset(OperationComponent.Deception, Interval);
			var special = OperationSchedule.DoctrinalOffset(OperationComponent.SpecialOperation, Interval);
			var ground = OperationSchedule.DoctrinalOffset(OperationComponent.GroundAssault, Interval);

			Assert.That(special, Is.GreaterThan(deception),
				"The insertion happens after the deception has drawn attention...");
			Assert.That(special, Is.LessThan(ground),
				"...and before the main assault refocuses it.");
		}

		[TestCase(TestName = "The operation starts at the earliest launch, which may be the slowest component.")]
		public void OperationStartIsTheEarliestLaunch()
		{
			var schedule = new OperationSchedule(TimeOnTarget);
			var recon = schedule.Add(OperationComponent.Reconnaissance, 200, Interval);
			var ground = schedule.Add(OperationComponent.GroundAssault, 900, Interval);

			// Recon arrives first but is fast, so it leaves later; the slow ground column has to set
			// off before it. Scheduling backwards from arrival is what surfaces that - ordering by
			// doctrinal sequence alone would have launched the column too late to arrive on time.
			Assert.That(recon.ArrivalTick, Is.LessThan(ground.ArrivalTick));
			Assert.That(ground.LaunchTick, Is.LessThan(recon.LaunchTick));
			Assert.That(schedule.OperationStartTick, Is.EqualTo(ground.LaunchTick));
			Assert.That(schedule.OperationStartTick, Is.EqualTo(TimeOnTarget - 900));
		}

		[TestCase(TestName = "Synchronization is judged on arrival spread against a tolerance (req 259).")]
		public void SynchronizationTolerance()
		{
			var schedule = new OperationSchedule(TimeOnTarget);
			schedule.Add(OperationComponent.AirStrike, 100, Interval);
			schedule.Add(OperationComponent.GroundAssault, 900, Interval);

			Assert.That(schedule.ArrivalSpread, Is.EqualTo(Interval));
			Assert.That(schedule.IsSynchronized(Interval), Is.True);
			Assert.That(schedule.IsSynchronized(Interval - 1), Is.False);
		}

		[TestCase(TestName = "Synchronization error is the absolute miss against the plan (req 260).")]
		public void SynchronizationErrorIsAbsolute()
		{
			Assert.That(OperationSchedule.SynchronizationError(5000, 5120), Is.EqualTo(120));
			Assert.That(OperationSchedule.SynchronizationError(5000, 4880), Is.EqualTo(120),
				"Arriving early is as much a coordination failure as arriving late.");
			Assert.That(OperationSchedule.SynchronizationError(5000, 5000), Is.Zero);
		}

		[TestCase(TestName = "An empty or single-component schedule is trivially synchronized.")]
		public void DegenerateSchedules()
		{
			var empty = new OperationSchedule(TimeOnTarget);
			Assert.That(empty.ArrivalSpread, Is.Zero);
			Assert.That(empty.OperationStartTick, Is.EqualTo(TimeOnTarget));
			Assert.That(empty.Precedes(OperationComponent.AirStrike), Is.False,
				"Nothing precedes a ground assault that was never scheduled.");

			empty.Add(OperationComponent.GroundAssault, 500, Interval);
			Assert.That(empty.ArrivalSpread, Is.Zero);
			Assert.That(empty.Entries.Count, Is.EqualTo(1));
		}

		[TestCase(TestName = "A zero interval degrades to an ordered schedule rather than dividing by zero.")]
		public void ZeroIntervalIsSafe()
		{
			Assert.That(OperationSchedule.DoctrinalOffset(OperationComponent.Reconnaissance, 0), Is.EqualTo(-4));
			Assert.That(OperationSchedule.DoctrinalOffset(OperationComponent.GroundAssault, 0), Is.Zero);
		}
	}
}
