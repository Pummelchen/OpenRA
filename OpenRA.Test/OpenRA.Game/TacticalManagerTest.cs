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

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Commander.Model;
using OpenRA.Mods.Common.Commander.Staff;

namespace OpenRA.Test
{
	/// <summary>
	/// <para>
	/// The tactical chief's judgement. Every test here is a situation with a right answer that this
	/// commander has previously got wrong in a measured match.
	/// </para>
	/// </summary>
	[TestFixture]
	sealed class TacticalManagerTest
	{
		/// <summary>A specialist that files a fixed report, so the chief can be examined in isolation.</summary>
		sealed class StubManager : ICommanderManager
		{
			public string Name { get; init; } = "";
			public int Order => 1;
			public int Interval => 1;
			public bool CanThinkInParallel => false;
			public ManagerReport Report { get; init; }

			public void Think(CommanderSnapshot snapshot, StaffContext context) => context.Report(Report);
		}

		static CommanderSnapshot Snapshot(int tick = 1000, int regions = 8)
		{
			var state = new AbstractState(regions);
			state.Self.BaseIntegrity = 10000f;
			state.Self.PeakBaseIntegrity = 10000f;
			state.Enemy.AddStructures(5, 8000f);
			return new CommanderSnapshot { Tick = tick, State = state, Cash = 5000, Earned = 20000 };
		}

		static Directive Run(CommanderSnapshot snapshot, params ManagerReport[] reports)
		{
			var staff = new CommanderStaff { ThinkInParallel = false };
			foreach (var report in reports)
				staff.Add(new StubManager { Name = report.Manager, Report = report });

			staff.Add(new TacticalManager());
			staff.Think(snapshot);
			return staff.Directive;
		}

		[TestCase(TestName = "A failing domain outranks any opportunity.")]
		public void CriticalDomainTakesPrecedence()
		{
			// There is no objective worth trading the base for, and an attack launched with no
			// economy behind it is a one-way trip.
			var directive = Run(Snapshot(),
				new ManagerReport { Manager = "economy", Readiness = Readiness.Critical, Headline = "no refinery" },
				new ManagerReport { Manager = "tactical-analysis", Readiness = Readiness.Surplus, RegionOfInterest = 5 });

			Assert.That(directive.Stance, Is.EqualTo(Stance.Recover));
			Assert.That(directive.Rationale, Does.Contain("economy"));
		}

		[TestCase(TestName = "Pressure at home outranks an opportunity away.")]
		public void HomeThreatOutranksOpportunity()
		{
			var directive = Run(Snapshot(),
				new ManagerReport { Manager = "defence", Readiness = Readiness.Strained, Headline = "enemy in R2", RegionOfInterest = 2 },
				new ManagerReport { Manager = "tactical-analysis", Readiness = Readiness.Healthy, RegionOfInterest = 5 });

			Assert.That(directive.Stance, Is.EqualTo(Stance.Defend));
			Assert.That(directive.MainEffortRegion, Is.EqualTo(2));
			Assert.That(directive.ReserveFraction, Is.GreaterThan(0.4f), "Holding needs something to hold with.");
		}

		[TestCase(TestName = "Not knowing where they are is a decision, not a drift.")]
		public void UnknownObjectiveProducesProbe()
		{
			// A commander that attacks before finding the base takes empty ground, which this one
			// did for entire matches.
			var directive = Run(Snapshot(),
				new ManagerReport { Manager = "economy", Readiness = Readiness.Surplus },
				new ManagerReport { Manager = "unit-production", Readiness = Readiness.Surplus, ReadyInSeconds = 0 });

			Assert.That(directive.Stance, Is.EqualTo(Stance.Probe));
		}

		[TestCase(TestName = "It commits when the army is ready and the objective is known.")]
		public void CommitsWhenReady()
		{
			var directive = Run(Snapshot(),
				new ManagerReport { Manager = "economy", Readiness = Readiness.Healthy },
				new ManagerReport { Manager = "unit-production", Readiness = Readiness.Surplus, ReadyInSeconds = 0 },
				new ManagerReport { Manager = "tactical-analysis", Readiness = Readiness.Healthy, RegionOfInterest = 5 });

			Assert.That(directive.Stance, Is.EqualTo(Stance.Assault));
			Assert.That(directive.MainEffortRegion, Is.EqualTo(5));
		}

		[TestCase(TestName = "It waits for the slowest necessary part, not the fastest.")]
		public void WaitsForTheSlowestDomain()
		{
			// An assault is ready when its slowest part is. Committing on the strength of the arm
			// that happens to be finished is how a force arrives piecemeal.
			var directive = Run(Snapshot(),
				new ManagerReport { Manager = "economy", Readiness = Readiness.Healthy, ReadyInSeconds = 0 },
				new ManagerReport { Manager = "unit-production", Readiness = Readiness.Strained, ReadyInSeconds = 45 },
				new ManagerReport { Manager = "tactical-analysis", Readiness = Readiness.Healthy, RegionOfInterest = 5 });

			Assert.That(directive.Stance, Is.EqualTo(Stance.Pressure));
			Assert.That(directive.Rationale, Does.Contain("45"));
		}

		[TestCase(TestName = "It will not wait forever.")]
		public void CommitsRatherThanDriftingForever()
		{
			// A commander that waits for perfect readiness never attacks at all.
			var directive = Run(Snapshot(),
				new ManagerReport { Manager = "unit-production", Readiness = Readiness.Strained, ReadyInSeconds = 600 },
				new ManagerReport { Manager = "tactical-analysis", Readiness = Readiness.Healthy, RegionOfInterest = 5 });

			Assert.That(directive.Stance, Is.EqualTo(Stance.Assault));
			Assert.That(directive.Rationale, Does.Contain("rather than drifting"));
		}

		[TestCase(TestName = "A surplus is treated as a fault, not a comfort.")]
		public void SurplusForcesCommitment()
		{
			// Credits in the bank have never won anything. This commander banked 74% of everything
			// it earned across a match and lost on structures while doing it.
			var directive = Run(Snapshot(),
				new ManagerReport { Manager = "economy", Readiness = Readiness.Surplus, Headline = "74% unspent" },
				new ManagerReport { Manager = "unit-production", Readiness = Readiness.Strained, ReadyInSeconds = 30 },
				new ManagerReport { Manager = "tactical-analysis", Readiness = Readiness.Healthy, RegionOfInterest = 5 });

			Assert.That(directive.Stance, Is.EqualTo(Stance.Assault));
			Assert.That(directive.Rationale, Does.Contain("surplus"));
		}

		[TestCase(TestName = "Deception is funded only once the opponent is understood.")]
		public void DeceptionRequiresConfidence()
		{
			ManagerReport Intel(float confidence) => new()
			{
				Manager = "intelligence",
				Readiness = Readiness.Healthy,
				Confidence = confidence,
			};

			var blind = Run(Snapshot(),
				Intel(0.05f),
				new ManagerReport { Manager = "unit-production", Readiness = Readiness.Surplus, ReadyInSeconds = 0 },
				new ManagerReport { Manager = "tactical-analysis", RegionOfInterest = 5 });

			// A feint against an opponent whose behaviour we cannot predict is a detachment thrown
			// away, and a spy sent at an unidentified base is spent on a guess.
			Assert.That(blind.FeintRegion, Is.Null);
			Assert.That(blind.AuthoriseSpecialOperations, Is.False);

			var informed = Run(Snapshot(),
				Intel(0.8f),
				new ManagerReport { Manager = "unit-production", Readiness = Readiness.Surplus, ReadyInSeconds = 0 },
				new ManagerReport { Manager = "tactical-analysis", RegionOfInterest = 5 });

			Assert.That(informed.AuthoriseSpecialOperations, Is.True);
		}

		[TestCase(TestName = "A feint never lands on the main effort.")]
		public void FeintIsElsewhere()
		{
			var snapshot = Snapshot();
			snapshot.State.Enemy.AddStructures(3, 4000f);

			var directive = Run(snapshot,
				new ManagerReport { Manager = "intelligence", Confidence = 0.9f, Readiness = Readiness.Healthy },
				new ManagerReport { Manager = "unit-production", Readiness = Readiness.Surplus, ReadyInSeconds = 0 },
				new ManagerReport { Manager = "tactical-analysis", RegionOfInterest = 5 });

			Assert.That(directive.MainEffortRegion, Is.EqualTo(5));
			Assert.That(directive.FeintRegion, Is.Not.EqualTo(5),
				"A feint that lands where the assault is going is not a feint.");
		}

		[TestCase(TestName = "A standing directive is not reconsidered until it expires.")]
		public void DirectivesBindForTheirPeriod()
		{
			// The rule that stops the commander cancelling its own attacks. Every assault makes the
			// army ratio worse before it makes it better, and a chief that re-decides continuously
			// will always call it off at the worst moment.
			var staff = new CommanderStaff { ThinkInParallel = false };
			staff.Add(new StubManager
			{
				Name = "tactical-analysis",
				Report = new ManagerReport { Manager = "tactical-analysis", RegionOfInterest = 5 },
			});
			staff.Add(new StubManager
			{
				Name = "unit-production",
				Report = new ManagerReport { Manager = "unit-production", Readiness = Readiness.Surplus, ReadyInSeconds = 0 },
			});
			staff.Add(new TacticalManager { DirectiveTicks = 1500 });

			staff.Think(Snapshot(1000));
			var committed = staff.Directive;
			Assert.That(committed.Stance, Is.EqualTo(Stance.Assault));

			staff.Think(Snapshot(1400));
			Assert.That(staff.Directive, Is.SameAs(committed), "Still inside its period.");

			staff.Think(Snapshot(3000));
			Assert.That(staff.Directive, Is.Not.SameAs(committed), "Reconsidered once it expired.");
		}

		[TestCase(TestName = "A collapsing domain interrupts even a standing directive.")]
		public void CriticalInterruptsCommitment()
		{
			var staff = new CommanderStaff { ThinkInParallel = false };
			var defence = new ManagerReport { Manager = "defence", Readiness = Readiness.Healthy };
			var stub = new StubManager { Name = "defence", Report = defence };

			staff.Add(stub);
			staff.Add(new StubManager
			{
				Name = "tactical-analysis",
				Report = new ManagerReport { Manager = "tactical-analysis", RegionOfInterest = 5 },
			});
			staff.Add(new StubManager
			{
				Name = "unit-production",
				Report = new ManagerReport { Manager = "unit-production", Readiness = Readiness.Surplus, ReadyInSeconds = 0 },
			});
			staff.Add(new TacticalManager { DirectiveTicks = 100000 });

			staff.Think(Snapshot(1000));
			Assert.That(staff.Directive.Stance, Is.EqualTo(Stance.Assault));

			// Commitment is not stubbornness: the base falling apart is grounds to tear the plan up.
			var staff2 = new CommanderStaff { ThinkInParallel = false };
			staff2.Add(new StubManager
			{
				Name = "defence",
				Report = new ManagerReport { Manager = "defence", Readiness = Readiness.Critical, Headline = "base collapsing" },
			});
			staff2.Add(new TacticalManager { DirectiveTicks = 100000 });
			staff2.Think(Snapshot(1000));
			staff2.Think(Snapshot(1100));

			Assert.That(staff2.Directive.Stance, Is.EqualTo(Stance.Defend));
		}
	}
}
