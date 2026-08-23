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
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Commander.Model;
using OpenRA.Mods.Common.Commander.Search;
using OpenRA.Mods.Common.Commander.Terrain;

namespace OpenRA.Test
{
	/// <summary>
	/// The search. Its job is to pick, from a dozen plans, one worth committing two minutes to - and
	/// the only way to know whether it does is to put it in positions with an obvious right answer
	/// and check that it finds them.
	/// </summary>
	[TestFixture]
	sealed class PuppetSearchTest
	{
		const int R = RoleStats.Roles;

		static RegionGraph ThreeRooms()
		{
			const int W = 91, H = 41;
			return RegionGraph.Build(W, H, (x, y) =>
			{
				if (x <= 0 || y <= 0 || x >= W - 1 || y >= H - 1)
					return false;

				if (x == 30)
					return y >= 16 && y < 26;

				if (x == 60)
					return y >= 19 && y < 22;

				return true;
			}, RegionGraph.Settings.Default);
		}

		static ForwardModel Model(out RegionGraph graph)
		{
			graph = ThreeRooms();
			var damage = new float[R * R];
			Array.Fill(damage, 0.02f);
			var hp = new float[R];
			Array.Fill(hp, 1f);
			return new ForwardModel(graph, new RoleStats(damage, hp));
		}

		static AbstractState Position(RegionGraph graph, out int ourRegion, out int theirRegion)
		{
			ourRegion = graph.RegionAt(10, 20);
			theirRegion = graph.RegionAt(80, 20);

			var state = new AbstractState(graph.Regions.Length);
			state.Self.Cash = 3000f;
			state.Self.Harvesters = 4;
			state.Self.Refineries = 2;
			state.Self.ObservedIncomePerSecond = 60f;
			state.Self.ObservedHarvesters = 4;
			state.Self.ProductionThroughput = 40f;
			state.Self.ArmyGrowthPerSecond = 30f;
			state.Self.BaseIntegrity = 8000f;
			state.Enemy.BaseIntegrity = 8000f;
			return state;
		}

		[TestCase(TestName = "It commits to attacking when it is overwhelmingly stronger.")]
		public void AttacksWhenDominant()
		{
			var model = Model(out var graph);
			var state = Position(graph, out var ours, out var theirs);

			state.Self.SetForce(ours, CombatRole.Armor, 12000f);
			state.Enemy.SetForce(theirs, CombatRole.Armor, 1500f);
			state.Enemy.SetForce(theirs, CombatRole.Defense, 500f);

			var search = new PuppetSearch(model, WinProbabilityModel.Default());
			var result = search.Search(state);

			// This is the position the previous commander could not act on: it had a decisive
			// advantage and still never named an objective.
			Assert.That(result.Best.Verb, Is.EqualTo(MacroVerb.Attack),
				$"With eight times the army it must attack; it chose {result.Best}.");
			Assert.That(result.Best.Region, Is.EqualTo(theirs));
			Assert.That(result.BestValue, Is.GreaterThan(0.5f));
		}

		[TestCase(TestName = "It does not attack into a hopeless fight.")]
		public void DoesNotAttackWhenOutmatched()
		{
			var model = Model(out var graph);
			var state = Position(graph, out var ours, out var theirs);

			state.Self.SetForce(ours, CombatRole.Armor, 800f);
			state.Enemy.SetForce(theirs, CombatRole.Armor, 15000f);
			state.Enemy.SetForce(theirs, CombatRole.Defense, 4000f);

			var search = new PuppetSearch(model, WinProbabilityModel.Default());
			var result = search.Search(state);

			Assert.That(result.Best.Verb, Is.Not.EqualTo(MacroVerb.Attack),
				$"Marching eight hundred credits into nineteen thousand is not a plan; it chose {result.Best}.");
		}

		[TestCase(TestName = "It prefers economy when nothing is contested.")]
		public void BuildsWhenUncontested()
		{
			var model = Model(out var graph);
			var state = Position(graph, out var ours, out _);

			// Nobody in sight, ore available. The right answer is to get stronger, not to march
			// somewhere and stand about.
			state.Self.SetForce(ours, CombatRole.Armor, 2000f);
			for (var region = 0; region < state.RegionCount; region++)
				state.Value[region] = 5000f;

			var search = new PuppetSearch(model, WinProbabilityModel.Default());
			var result = search.Search(state);

			Assert.That(new[] { MacroVerb.Produce, MacroVerb.Expand, MacroVerb.Tech, MacroVerb.Consolidate },
				Does.Contain(result.Best.Verb), $"Chose {result.Best} with no enemy anywhere.");
		}

		[TestCase(TestName = "The same position always produces the same plan.")]
		public void SearchIsDeterministic()
		{
			// Every measurement in this project depends on this. A commander whose decisions cannot
			// be reproduced cannot be debugged, benchmarked, or replayed.
			var model = Model(out var graph);
			var state = Position(graph, out var ours, out var theirs);
			state.Self.SetForce(ours, CombatRole.Armor, 5000f);
			state.Enemy.SetForce(theirs, CombatRole.Armor, 4000f);

			var search = new PuppetSearch(model, WinProbabilityModel.Default());
			var a = search.Search(state);
			var b = search.Search(state);

			Assert.That(b.Best, Is.EqualTo(a.Best));
			Assert.That(b.BestValue, Is.EqualTo(a.BestValue));
			Assert.That(b.Ranked.Select(r => r.Action), Is.EqualTo(a.Ranked.Select(r => r.Action)));
			Assert.That(b.Ranked.Select(r => r.Visits), Is.EqualTo(a.Ranked.Select(r => r.Visits)));
		}

		[TestCase(TestName = "The chosen plan is the most explored, not the luckiest.")]
		public void ChoosesTheRobustAction()
		{
			var model = Model(out var graph);
			var state = Position(graph, out var ours, out var theirs);
			state.Self.SetForce(ours, CombatRole.Armor, 6000f);
			state.Enemy.SetForce(theirs, CombatRole.Armor, 3000f);

			var search = new PuppetSearch(model, WinProbabilityModel.Default());
			var result = search.Search(state);

			// In UCT the most-visited child is the robust choice: one visited twice with a lucky
			// evaluation can out-score one visited four hundred times, and acting on that would be
			// acting on noise.
			Assert.That(result.Ranked, Is.Not.Empty);
			Assert.That(result.Ranked[0].Action, Is.EqualTo(result.Best));
			for (var i = 1; i < result.Ranked.Count; i++)
				Assert.That(result.Ranked[i].Visits, Is.LessThanOrEqualTo(result.Ranked[0].Visits));
		}

		[TestCase(TestName = "Branching stays small enough to search.")]
		public void BranchingIsBounded()
		{
			var model = Model(out var graph);
			var state = Position(graph, out var ours, out var theirs);
			state.Self.SetForce(ours, CombatRole.Armor, 5000f);
			state.Enemy.SetForce(theirs, CombatRole.Armor, 5000f);
			for (var region = 0; region < state.RegionCount; region++)
				state.Value[region] = 1000f;

			var actions = MacroActionGenerator.Generate(state, model);

			// The whole premise of a puppet search: a dozen choices, not the raw action space.
			Assert.That(actions, Is.Not.Empty);
			Assert.That(actions.Count, Is.LessThanOrEqualTo(20),
				"Branching much beyond this and a two-minute lookahead stops fitting in the budget.");
			Assert.That(actions.Distinct(), Is.EqualTo(actions), "Duplicate actions waste search on nothing.");
		}

		[TestCase(TestName = "A full search fits inside a review.")]
		public void SearchFitsTheBudget()
		{
			var model = Model(out var graph);
			var state = Position(graph, out var ours, out var theirs);
			state.Self.SetForce(ours, CombatRole.Armor, 5000f);
			state.Enemy.SetForce(theirs, CombatRole.Armor, 5000f);

			var search = new PuppetSearch(model, WinProbabilityModel.Default());
			search.Search(state);

			// Best of several runs, not a single one. The question this test asks is whether the
			// search *can* run inside the budget, which is a capability rather than an average - and
			// a single measurement on a machine that is also running a self-play batch reports the
			// scheduler's mood rather than the code's speed.
			var best = double.MaxValue;
			PuppetSearch.Result result = null;
			for (var attempt = 0; attempt < 3; attempt++)
			{
				var timer = Stopwatch.StartNew();
				result = search.Search(state);
				timer.Stop();
				best = Math.Min(best, timer.Elapsed.TotalMilliseconds);
			}

			// The review interval is fifteen seconds of game time; the search must be a rounding
			// error against it and must not stall the tick it runs on.
			Assert.That(best, Is.LessThan(500.0), $"Search took {best:F1} ms at best.");
			Assert.That(result.NodesExpanded, Is.GreaterThan(100));
			TestContext.Out.WriteLine($"PuppetSearch: {best:F1} ms, {result.NodesExpanded} nodes");
		}

		[TestCase(TestName = "A degenerate position is answered, not thrown on.")]
		public void DegeneratePositions()
		{
			var model = Model(out var graph);
			var search = new PuppetSearch(model, WinProbabilityModel.Default());

			// Nothing anywhere. It must still return something the executor can act on.
			var empty = new AbstractState(graph.Regions.Length);
			Assert.That(() => search.Search(empty), Throws.Nothing);

			var zeroRegions = new AbstractState(0);
			var result = search.Search(zeroRegions);
			Assert.That(result.Best.Region, Is.EqualTo(0));
		}
	}
}
