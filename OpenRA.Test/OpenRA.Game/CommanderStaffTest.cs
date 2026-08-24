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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using OpenRA.Mods.Common.Commander.Staff;

namespace OpenRA.Test
{
	/// <summary>
	/// <para>
	/// The staff scheduler. Its single load-bearing property is that a parallel staff produces the
	/// same schedule of effects as a serial one.
	/// </para>
	/// <para>
	/// OpenRA is lockstep: every client must derive byte-identical state from the same orders. A
	/// commander whose decisions depended on which worker thread finished first would desync
	/// intermittently, under load, on one machine and not another - and would take every benchmark,
	/// replay and bisect in this project with it.
	/// </para>
	/// </summary>
	[TestFixture]
	sealed class CommanderStaffTest
	{
		sealed class Intent : IManagerIntent
		{
			public string From { get; init; } = "";
			public string Describe() => From;
		}

		/// <summary>A manager whose thinking takes a deliberately unpredictable amount of time.</summary>
		sealed class SlowManager : ICommanderManager
		{
			public string Name { get; init; } = "";
			public int Order { get; init; }
			public int Interval { get; init; } = 1;
			public bool CanThinkInParallel { get; init; } = true;
			public int DelayMilliseconds { get; init; }

			public int Runs;

			public void Think(CommanderSnapshot snapshot, StaffContext context)
			{
				Interlocked.Increment(ref Runs);

				if (DelayMilliseconds > 0)
					Thread.Sleep(DelayMilliseconds);

				context.Add(new Intent { From = Name });
			}
		}

		static CommanderSnapshot Snapshot(int tick) => new() { Tick = tick };

		[TestCase(TestName = "Intents are ordered by manager, not by who finished first.")]
		public void OrderIsIndependentOfCompletion()
		{
			// The first manager in application order is made the slowest, so completion order is
			// guaranteed to be the reverse of application order. If the scheduler collected results
			// as they arrived, this is exactly the case that would betray it.
			var staff = new CommanderStaff();
			staff.Add(new SlowManager { Name = "first", Order = 1, DelayMilliseconds = 60 });
			staff.Add(new SlowManager { Name = "second", Order = 2, DelayMilliseconds = 30 });
			staff.Add(new SlowManager { Name = "third", Order = 3, DelayMilliseconds = 0 });

			var result = staff.Think(Snapshot(100)).Select(i => i.Describe()).ToArray();

			Assert.That(result, Is.EqualTo(new[] { "first", "second", "third" }));
		}

		[TestCase(TestName = "Parallel and serial staffs produce identical schedules.")]
		public void ParallelMatchesSerial()
		{
			IReadOnlyList<string> Run(bool parallel)
			{
				var staff = new CommanderStaff { ThinkInParallel = parallel };
				for (var i = 0; i < 8; i++)
					staff.Add(new SlowManager
					{
						Name = $"m{i}",
						Order = 8 - i,
						DelayMilliseconds = i % 3 == 0 ? 20 : 0,
					});

				return staff.Think(Snapshot(200)).Select(x => x.Describe()).ToList();
			}

			// This is the property the design rests on: turning the threads on must not change what
			// the commander does, only how quickly it decides it.
			Assert.That(Run(parallel: true), Is.EqualTo(Run(parallel: false)));
		}

		[TestCase(TestName = "Repeating a cycle repeats the schedule exactly.")]
		public void RepeatedCyclesAreIdentical()
		{
			IReadOnlyList<string> Once()
			{
				var staff = new CommanderStaff();
				staff.Add(new SlowManager { Name = "economy", Order = 10, DelayMilliseconds = 15 });
				staff.Add(new SlowManager { Name = "production", Order = 20 });
				staff.Add(new SlowManager { Name = "tactics", Order = 30, DelayMilliseconds = 5 });
				return staff.Think(Snapshot(300)).Select(x => x.Describe()).ToList();
			}

			var first = Once();
			for (var attempt = 0; attempt < 5; attempt++)
				Assert.That(Once(), Is.EqualTo(first), "A staff must decide the same thing every time it is asked.");
		}

		[TestCase(TestName = "Ties in order break by name, not by registration.")]
		public void TiesBreakDeterministically()
		{
			var forward = new CommanderStaff();
			forward.Add(new SlowManager { Name = "alpha", Order = 5 });
			forward.Add(new SlowManager { Name = "beta", Order = 5 });

			var backward = new CommanderStaff();
			backward.Add(new SlowManager { Name = "beta", Order = 5 });
			backward.Add(new SlowManager { Name = "alpha", Order = 5 });

			// Two managers given the same order must still be applied in a fixed sequence, or a
			// change in registration order silently changes behaviour.
			Assert.That(backward.Think(Snapshot(1)).Select(i => i.Describe()),
				Is.EqualTo(forward.Think(Snapshot(1)).Select(i => i.Describe())));
		}

		[TestCase(TestName = "Each manager runs on its own cadence.")]
		public void IntervalsAreRespected()
		{
			// A map analyser has no reason to run as often as a tactical controller, and separating
			// them is most of the point of having a staff at all.
			var fast = new SlowManager { Name = "fast", Order = 1, Interval = 10 };
			var slow = new SlowManager { Name = "slow", Order = 2, Interval = 100 };

			var staff = new CommanderStaff();
			staff.Add(fast);
			staff.Add(slow);

			for (var tick = 0; tick <= 200; tick += 10)
				staff.Think(Snapshot(tick));

			Assert.That(fast.Runs, Is.EqualTo(21), "Every ten ticks across two hundred.");
			Assert.That(slow.Runs, Is.EqualTo(3), "Only when its own interval has elapsed.");
		}

		[TestCase(TestName = "A manager that cannot think in parallel still runs.")]
		public void SerialManagersStillRun()
		{
			var serial = new SlowManager { Name = "serial", Order = 2, CanThinkInParallel = false };
			var staff = new CommanderStaff();
			staff.Add(new SlowManager { Name = "parallelA", Order = 1 });
			staff.Add(serial);
			staff.Add(new SlowManager { Name = "parallelB", Order = 3 });

			var result = staff.Think(Snapshot(50)).Select(i => i.Describe()).ToArray();

			Assert.That(serial.Runs, Is.EqualTo(1));
			Assert.That(result, Is.EqualTo(new[] { "parallelA", "serial", "parallelB" }),
				"Mixing serial and parallel managers must not disturb the application order.");
		}

		[TestCase(TestName = "An empty staff is answered, not thrown on.")]
		public void EmptyStaff()
		{
			var staff = new CommanderStaff();
			Assert.That(staff.Think(Snapshot(1)), Is.Empty);
			Assert.That(() => staff.Add(null), Throws.ArgumentNullException);
			Assert.That(() => staff.Think(null), Throws.ArgumentNullException);
		}

		[TestCase(TestName = "Reset clears the cadence for a new match.")]
		public void ResetClearsCadence()
		{
			var manager = new SlowManager { Name = "m", Order = 1, Interval = 1000 };
			var staff = new CommanderStaff();
			staff.Add(manager);

			staff.Think(Snapshot(0));
			staff.Think(Snapshot(10));
			Assert.That(manager.Runs, Is.EqualTo(1), "Still inside its interval.");

			staff.Reset();
			staff.Think(Snapshot(20));
			Assert.That(manager.Runs, Is.EqualTo(2), "A new match must not inherit the last one's timings.");
		}
	}
}
