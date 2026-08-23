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
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;
using OpenRA.Primitives;

namespace OpenRA.Test
{
	/// <summary>
	/// <para>
	/// Cost budgets for the subsystems that run on the tick path (reqs 702-705).
	/// </para>
	/// <para>
	/// The headless suite measures whole-tick cost, which catches a catastrophic regression but not a
	/// subsystem quietly going quadratic - by the time that shows up in the tick average it is
	/// already severe. These bound the individual subsystems on a map far larger than any that ships,
	/// so a change in complexity is caught where it happens. Thresholds are deliberately generous:
	/// this is a guard against a change in growth rate, not a microbenchmark of the machine.
	/// </para>
	/// </summary>
	[TestFixture]
	sealed class SubsystemPerformanceTest
	{
		const int Regions = 256;
		const int Capabilities = 14;

		static CoalitionMapAnalysis LargeMap()
		{
			// A 16x16 region lattice - four times the region count of a large shipping map.
			const int Side = 16;
			var regions = new CoalitionRegion[Regions];
			for (var i = 0; i < Regions; i++)
			{
				var x = i % Side;
				var y = i / Side;
				regions[i] = new CoalitionRegion(i, Rectangle.FromLTRB(x * 8, y * 8, x * 8 + 8, y * 8 + 8));
			}

			var adjacency = Enumerable.Range(0, Regions).Select(_ => new List<int>()).ToArray();
			for (var i = 0; i < Regions; i++)
			{
				var x = i % Side;
				var y = i / Side;
				if (x + 1 < Side) { adjacency[i].Add(i + 1); adjacency[i + 1].Add(i); }
				if (y + 1 < Side) { adjacency[i].Add(i + Side); adjacency[i + Side].Add(i); }
			}

			var chokepoints = regions.Select(_ => Array.Empty<int>().ToFrozenSet()).ToArray();
			var (components, count) = CoalitionMapAnalysis.ConnectedComponents(adjacency);
			var all = new[] { components, components, components, components };

			return new CoalitionMapAnalysis(regions, [adjacency, adjacency, adjacency, adjacency],
				[chokepoints, chokepoints, chokepoints, chokepoints],
				all, [count, count, count, count], [], Side * 8, Side * 8,
				new int[Regions], new float[Regions], new float[Regions]);
		}

		static float[][] Threats()
		{
			var threats = new float[Regions][];
			for (var i = 0; i < Regions; i++)
			{
				threats[i] = new float[Capabilities];
				for (var c = 0; c < Capabilities; c++)
					threats[i][c] = (i * 7 + c * 13) % 100 / 100f;
			}

			return threats;
		}

		[TestCase(TestName = "703: route planning over a 256-region map stays well inside budget.")]
		public void RoutePlanningIsBounded()
		{
			var map = LargeMap();
			var threats = Threats();
			var profiles = new[]
			{
				RouteWeights.Assault(), RouteWeights.Stealth(), RouteWeights.Recon(), RouteWeights.Retreat()
			};

			var timer = Stopwatch.StartNew();
			var found = 0;
			for (var i = 0; i < 200; i++)
			{
				var route = CoalitionRoutePlanner.FindRoute(map, threats,
					i % Regions, (i * 37 + 11) % Regions, MovementClass.Ground, profiles[i % profiles.Length]);
				if (route.Found)
					found++;
			}

			timer.Stop();

			Assert.That(found, Is.GreaterThan(0), "The benchmark must actually be planning routes.");
			Assert.That(timer.Elapsed.TotalMilliseconds, Is.LessThan(2000),
				$"200 routes over 256 regions took {timer.Elapsed.TotalMilliseconds:0} ms; "
				+ "route planning has changed complexity class.");
		}

		[TestCase(TestName = "702: threat-field aggregation over every region and capability stays bounded.")]
		public void ThreatAggregationIsBounded()
		{
			var threats = Threats();

			var timer = Stopwatch.StartNew();
			var total = 0f;
			for (var pass = 0; pass < 500; pass++)
				for (var c = 0; c < Capabilities; c++)
				{
					var worst = 0f;
					for (var r = 0; r < Regions; r++)
						worst = Math.Max(worst, threats[r][c]);
					total += worst;
				}

			timer.Stop();

			Assert.That(total, Is.GreaterThan(0f));
			Assert.That(timer.Elapsed.TotalMilliseconds, Is.LessThan(1000),
				$"500 full threat aggregations took {timer.Elapsed.TotalMilliseconds:0} ms.");
		}

		[TestCase(TestName = "704: mission management stays bounded as missions are created and concluded.")]
		public void MissionManagementIsBounded()
		{
			// The concern is unbounded growth: a manager that accumulates every mission ever created
			// grows without limit over a long match and slows every review that scans the list.
			var manager = new MissionManager();
			for (var i = 0; i < 2000; i++)
			{
				var mission = manager.CreateMission(MissionType.Attack, 50, new CPos(i % 100, i % 100), "objective");
				mission.Status = MissionStatus.Succeeded;
				manager.RecordOutcome(mission);
				manager.CancelMission(mission.Id);
			}

			Assert.That(manager.Missions.Count, Is.Zero,
				"Concluded missions must be removed, or the active list grows for the whole match.");
			Assert.That(manager.MissionSuccesses, Is.EqualTo(2000),
				"Outcomes are still accounted after the mission object is dropped.");
		}

		[TestCase(TestName = "705: concluded missions release their forces, leaving no dangling commitments.")]
		public void NoDanglingForceCommitments()
		{
			// An arbiter that keeps commitments for dead missions leaks units: they are never
			// reassigned, so the army shrinks silently over a long match.
			var arbiter = new CoalitionOrderArbiter();
			for (var i = 0; i < 500; i++)
			{
				var missionId = $"OP-{i}";
				arbiter.Assign(missionId, "Attack", ArbiterPriority.ActiveCombat, "Alpha");
				arbiter.Assign(missionId, "Attack", ArbiterPriority.ActiveCombat, "Bravo");
				arbiter.ReleaseMission(missionId);
			}

			Assert.That(arbiter.Commitments.Count(c => !c.Released), Is.Zero,
				"Every commitment must be released with its mission.");
			Assert.That(arbiter.MissionOf("Alpha"), Is.Null);
			Assert.That(arbiter.MissionOf("Bravo"), Is.Null);
		}

		[TestCase(TestName = "705: the engagement and prediction logs do not grow without bound per match.")]
		public void OutcomeLogsStayProportional()
		{
			// These accumulate one entry per engagement, which is correct, but a repeated forecast
			// must not add an entry each review or the log grows with tick count instead of events.
			var predictions = new OpponentPredictionLog();
			for (var i = 0; i < 5000; i++)
				predictions.Predict("playstyle", "rush", i, 0.8f);

			Assert.That(predictions.Predictions.Count, Is.EqualTo(1),
				"Restating the same open forecast must not grow the log.");

			var engagements = new EngagementOutcomeLog();
			for (var i = 0; i < 5000; i++)
				engagements.Predict("OP-1", i, 0.7f, 0.2f, 100f);

			Assert.That(engagements.Engagements.Count, Is.EqualTo(1),
				"Re-predicting one open engagement must not grow the log.");
		}
	}
}
