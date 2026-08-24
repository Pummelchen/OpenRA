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
using OpenRA.Mods.Common.Commander.Terrain;

namespace OpenRA.Test
{
	/// <summary>
	/// <para>
	/// An audit of how the staff talks to itself, kept as tests so the contracts cannot quietly
	/// break again.
	/// </para>
	/// <para>
	/// Every case here is a defect that was actually present the first time the staff was wired up
	/// and run. They are the failures a diagram does not show: each manager was correct in
	/// isolation, and the conversation between them was not.
	/// </para>
	/// </summary>
	[TestFixture]
	sealed class StaffCommunicationAuditTest
	{
		static readonly string[] AllManagers =
		[
			"map-analysis", "intelligence", "scouting", "economy", "building-production",
			"unit-production", "tactical-analysis", "defence", "attack-coordination",
			"special-operations", "ground-force", "air-force", "naval-force",
		];

		static CommanderStaff FullStaff()
		{
			var staff = new CommanderStaff { ThinkInParallel = false };
			staff.Add(new MapAnalysisManager());
			staff.Add(new IntelligenceManager());
			staff.Add(new ScoutingManager());
			staff.Add(new EconomyManager());
			staff.Add(new BuildingProductionManager());
			staff.Add(new UnitProductionManager());
			staff.Add(new TacticalAnalysisManager());
			staff.Add(new DefenceManager());
			staff.Add(new AttackCoordinationManager());
			staff.Add(new SpecialOperationsManager());
			staff.Add(new ForceArmManager { Name = "ground-force", Order = 70, Role = CombatRole.Armor });
			staff.Add(new ForceArmManager { Name = "air-force", Order = 71, Role = CombatRole.Aircraft });
			staff.Add(new ForceArmManager { Name = "naval-force", Order = 72, Role = CombatRole.Naval });
			staff.Add(new TacticalManager());
			return staff;
		}

		static CommanderSnapshot Snapshot(int tick = 20000, int cash = 40000, int earned = 60000)
		{
			var graph = RegionGraph.Build(61, 41, (x, y) =>
			{
				if (x <= 0 || y <= 0 || x >= 60 || y >= 40)
					return false;

				return x != 30 || (y >= 18 && y < 22);
			});

			var state = new AbstractState(graph.Regions.Length);
			state.Self.SetForce(0, CombatRole.Armor, 6000f);
			state.Self.BaseIntegrity = 9000f;
			state.Self.PeakBaseIntegrity = 10000f;
			state.Self.Harvesters = 6;
			state.Self.Refineries = 2;

			if (state.RegionCount > 1)
			{
				state.Enemy.SetForce(1, CombatRole.Armor, 4000f);
				state.Enemy.AddStructures(1, 6000f);
			}

			return new CommanderSnapshot
			{
				Tick = tick,
				State = state,
				Graph = graph,
				Belief = new EnemyBelief(graph.Regions.Length, r => graph.Neighbours(r)),
				Opponent = new StrategyPosterior(),
				Cash = cash,
				Earned = earned,
				Spent = earned - cash,
				Queues = [new CommanderSnapshot.QueueSnapshot("Vehicle", "", 0)],
				Structures = new Dictionary<string, int> { ["proc"] = 2, ["powr"] = 3, ["weap"] = 2 },
				Units = new Dictionary<string, int> { ["harv"] = 6, ["dog"] = 2 },
			};
		}

		[TestCase(TestName = "Every specialist files a report each cycle.")]
		public void EverySpecialistReports()
		{
			// A silent manager is invisible to the chief, and a chief cannot weigh a domain it never
			// hears from. This also catches a manager added later and never wired in.
			var staff = FullStaff();
			staff.Think(Snapshot());

			var reported = staff.LastReports.Select(r => r.Manager).ToHashSet();
			foreach (var manager in AllManagers)
				Assert.That(reported, Does.Contain(manager), $"{manager} filed nothing.");
		}

		[TestCase(TestName = "Every manager the chief consults answers.")]
		public void ChiefConsultsOnlyManagersThatExist()
		{
			// The chief asks for specialists by name. A rename would silently turn its judgement
			// into a null check that always fails the same way.
			var staff = FullStaff();
			staff.Think(Snapshot());

			var reported = staff.LastReports.Select(r => r.Manager).ToHashSet();
			foreach (var name in new[] { "economy", "unit-production", "intelligence", "defence", "tactical-analysis" })
				Assert.That(reported, Does.Contain(name),
					$"the chief consults '{name}' but nothing by that name reports.");
		}

		[TestCase(TestName = "Only the production managers issue production.")]
		public void ProductionHasOneOwner()
		{
			// The audit that produced this test found SIX managers queueing items independently.
			// Production is one domain and needs one owner, or the staff reproduces exactly the
			// "nobody is responsible" failure it was created to end.
			var staff = FullStaff();
			staff.Think(Snapshot());

			var producers = new List<string>();
			foreach (var manager in staff.Managers.Where(m => !m.IsChief))
			{
				var mine = new List<IManagerIntent>();
				manager.Think(Snapshot(), new StaffContext(staff.Directive, mine, [], staff.PendingRequests, []));

				if (mine.Any(i => i is ProduceUnitIntent or ConstructIntent))
					producers.Add(manager.Name);
			}

			Assert.That(producers, Is.SubsetOf(new[] { "unit-production", "building-production" }),
				$"these managers issue production without owning it: {string.Join(", ", producers)}");
		}

		[TestCase(TestName = "A request from one manager reaches another.")]
		public void RequestsCrossManagerBoundaries()
		{
			// Scouting needs dogs and does not get to build them. The request must survive the cycle
			// boundary and be served by whoever owns production.
			var staff = FullStaff();
			staff.Think(Snapshot());
			Assert.That(staff.PendingRequests, Is.Not.Empty, "nobody asked anybody for anything");

			var intents = staff.Think(Snapshot(tick: 20125));
			var reasons = intents.OfType<ConstructIntent>().Select(i => i.Reason)
				.Concat(intents.OfType<ProduceUnitIntent>().Select(i => i.Reason))
				.ToArray();

			Assert.That(reasons.Any(r => r.Contains(':')),
				$"no cross-manager request was served: {string.Join(" | ", reasons)}");
		}

		[TestCase(TestName = "The chief's directive reaches the specialists.")]
		public void DirectiveFlowsDownward()
		{
			// Command has to be two-way. The audit found nine of eleven specialists ignoring the
			// directive entirely, which made the chief an observer with opinions.
			int Expansions(Stance stance)
			{
				var requests = new List<ProductionRequest>();
				var directive = new Directive { Stance = stance, ValidUntilTick = 999999 };
				new EconomyManager().Think(Snapshot(cash: 2000), new StaffContext(directive, [], [], [], requests));
				return requests.Count(r => r.Item == "proc");
			}

			Assert.That(Expansions(Stance.Assault), Is.LessThanOrEqualTo(Expansions(Stance.Build)),
				"the economy kept expanding while the army was committed");
		}

		[TestCase(TestName = "An assault waits only on domains it depends on.")]
		public void AssaultGatingIgnoresIrrelevantDomains()
		{
			// Special operations answering "forty-five seconds to build a spy" must not delay a
			// committed assault, and a naval arm on a landlocked map must not delay it forever.
			var context = new StaffContext(Directive.Initial, [], [
				new ManagerReport { Manager = "unit-production", ReadyInSeconds = 0 },
				new ManagerReport { Manager = "special-operations", ReadyInSeconds = 45 },
				new ManagerReport { Manager = "naval-force", ReadyInSeconds = 9999 },
			]);

			Assert.That(context.LongestWait, Is.EqualTo(0),
				"an assault waited on a domain it does not depend on");
		}

		[TestCase(TestName = "A long estimate is a reason to wait, not to charge.")]
		public void EstimateIsNotElapsedTime()
		{
			// Visible in the very first live run: five seconds into a match the chief announced it
			// had "waited 352s for readiness" and committed a non-existent army. It had been told it
			// NEEDED 352 seconds and read that as having spent them.
			var staff = new CommanderStaff { ThinkInParallel = false };
			staff.Add(new StubReporter("unit-production", new ManagerReport
			{
				Manager = "unit-production",
				Readiness = Readiness.Strained,
				ReadyInSeconds = 352,
			}));

			staff.Add(new StubReporter("tactical-analysis", new ManagerReport
			{
				Manager = "tactical-analysis",
				RegionOfInterest = 1,
			}));

			staff.Add(new TacticalManager());
			staff.Think(Snapshot(tick: 125));

			Assert.That(staff.Directive.Stance, Is.EqualTo(Stance.Pressure),
				$"committed immediately on a long estimate: {staff.Directive.Rationale}");
		}

		[TestCase(TestName = "An arm that is not fielded is not a crisis.")]
		public void AbsentArmsAreNotFailures()
		{
			// The first live run had every specialist reporting Critical within five seconds - no
			// air force, no war factory, no army - which pinned the chief in Recover for the whole
			// match. "Not yet" is not "broken".
			var staff = FullStaff();
			staff.Think(Snapshot(tick: 125));

			var falseAlarms = staff.LastReports
				.Where(r => r.Readiness == Readiness.Critical)
				.Select(r => $"{r.Manager}: {r.Headline}")
				.ToArray();

			Assert.That(falseAlarms, Is.Empty,
				$"domains reported critical seconds into a match: {string.Join(" | ", falseAlarms)}");
		}

		[TestCase(TestName = "Reports carry the fields the chief actually reads.")]
		public void ReportFieldsAreConsumed()
		{
			// A field nobody reads is a claim nobody checks; a field the chief reads and nobody
			// writes is a decision made on a default.
			var staff = FullStaff();
			staff.Think(Snapshot());

			var reports = staff.LastReports;
			Assert.That(reports.Any(r => r.ReadyInSeconds.HasValue), "nothing reports a readiness time");
			Assert.That(reports.Any(r => r.RegionOfInterest.HasValue), "nothing reports a place");
			Assert.That(reports.All(r => !string.IsNullOrEmpty(r.Headline)), "a report had no headline");
			Assert.That(reports.All(r => !string.IsNullOrEmpty(r.Manager)), "a report had no author");
		}

		[TestCase(TestName = "The whole staff is deterministic under threads.")]
		public void FullStaffIsDeterministic()
		{
			IReadOnlyList<string> Run(bool parallel)
			{
				var staff = FullStaff();
				staff.ThinkInParallel = parallel;
				staff.Think(Snapshot());
				return staff.Think(Snapshot(tick: 20125)).Select(i => i.Describe()).ToList();
			}

			Assert.That(Run(parallel: true), Is.EqualTo(Run(parallel: false)));
			Assert.That(Run(parallel: true), Is.EqualTo(Run(parallel: true)));
		}

		sealed class StubReporter : ICommanderManager
		{
			readonly ManagerReport report;

			public StubReporter(string name, ManagerReport report)
			{
				Name = name;
				this.report = report;
			}

			public string Name { get; }
			public int Order => 1;
			public int Interval => 1;
			public bool CanThinkInParallel => false;

			public void Think(CommanderSnapshot snapshot, StaffContext context) => context.Report(report);
		}
	}
}
