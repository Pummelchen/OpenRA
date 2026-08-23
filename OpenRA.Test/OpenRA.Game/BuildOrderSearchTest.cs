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
using OpenRA.Mods.Common.Commander.Search;

namespace OpenRA.Test
{
	/// <summary>
	/// Build-order scheduling. "Six tanks and a second refinery" has a right answer, and a bot that
	/// picks greedily is routinely a minute late to it - which at this level of play is the game.
	/// </summary>
	[TestFixture]
	sealed class BuildOrderSearchTest
	{
		static BuildOrderSearch.Item Item(string name, int cost, float seconds, string queue = "main",
			float income = 0f, params string[] prerequisites) =>
			new()
			{
				Name = name,
				Cost = cost,
				BuildSeconds = seconds,
				Queue = queue,
				IncomePerSecond = income,
				Prerequisites = prerequisites,
			};

		static BuildOrderSearch Standard() => new(
		[
			Item("refinery", 1400, 20f, "building", income: 15f),
			Item("factory", 2000, 25f, "building", prerequisites: "refinery"),
			Item("tank", 800, 12f, "vehicles", prerequisites: "factory"),
			Item("barracks", 500, 10f, "building"),
			Item("rifle", 100, 4f, "infantry", prerequisites: "barracks"),
		]);

		[TestCase(TestName = "Prerequisites are respected.")]
		public void PrerequisitesAreRespected()
		{
			var search = Standard();
			var result = search.Find(
				new BuildOrderSearch.Situation { Cash = 10000f, IncomePerSecond = 50f },
				["tank", "factory", "refinery"]);

			Assert.That(result.Feasible, Is.True);
			var order = result.Order.ToArray();
			Assert.That(System.Array.IndexOf(order, "refinery"), Is.LessThan(System.Array.IndexOf(order, "factory")));
			Assert.That(System.Array.IndexOf(order, "factory"), Is.LessThan(System.Array.IndexOf(order, "tank")));
		}

		[TestCase(TestName = "An unreachable goal is reported, not guessed at.")]
		public void ImpossibleGoalsAreInfeasible()
		{
			var search = new BuildOrderSearch([Item("tank", 800, 12f, prerequisites: "factory")]);
			var result = search.Find(new BuildOrderSearch.Situation { Cash = 10000f }, ["tank"]);

			Assert.That(result.Feasible, Is.False, "Nothing can build the factory, so nothing can build the tank.");

			// A name that is not in the catalogue at all is a caller error, not an ordering problem.
			var unknown = Standard().Find(new BuildOrderSearch.Situation { Cash = 1000f }, ["dreadnought"]);
			Assert.That(unknown.Feasible, Is.False);
		}

		[TestCase(TestName = "What already exists does not need building again.")]
		public void ExistingStructuresSatisfyPrerequisites()
		{
			var search = Standard();
			var result = search.Find(
				new BuildOrderSearch.Situation
				{
					Cash = 5000f,
					IncomePerSecond = 50f,
					Existing = ["refinery", "factory"],
				},
				["tank"]);

			Assert.That(result.Feasible, Is.True);
			Assert.That(result.Order, Is.EqualTo(new[] { "tank" }));
			Assert.That(result.CompletionSeconds, Is.EqualTo(12f).Within(0.1f));
		}

		[TestCase(TestName = "Separate queues build in parallel.")]
		public void QueuesRunInParallel()
		{
			// A barracks and a refinery share the building queue and must be sequential; rifles and
			// tanks do not, and a schedule that serialised them would be needlessly slow.
			var search = Standard();
			var result = search.Find(
				new BuildOrderSearch.Situation
				{
					Cash = 20000f,
					IncomePerSecond = 100f,
					Existing = ["refinery", "factory", "barracks"],
				},
				["tank", "rifle"]);

			Assert.That(result.Feasible, Is.True);
			Assert.That(result.CompletionSeconds, Is.EqualTo(12f).Within(0.5f),
				"The tank takes twelve seconds and the rifle finishes inside that; the total is not sixteen.");
		}

		[TestCase(TestName = "Money it does not have yet is waited for.")]
		public void PoorStartsWaitForIncome()
		{
			var search = Standard();

			var rich = search.Find(
				new BuildOrderSearch.Situation { Cash = 10000f, IncomePerSecond = 50f, Existing = ["refinery", "factory"] },
				["tank", "tank"]);

			var poor = search.Find(
				new BuildOrderSearch.Situation { Cash = 100f, IncomePerSecond = 50f, Existing = ["refinery", "factory"] },
				["tank", "tank"]);

			Assert.That(poor.CompletionSeconds, Is.GreaterThan(rich.CompletionSeconds),
				"A plan that ignores the bank is not a plan.");
		}

		[TestCase(TestName = "An economic building that pays for itself is scheduled early.")]
		public void EconomyFirstWhenItPays()
		{
			// The classic opening decision, and the one greedy ordering gets wrong: the refinery
			// costs time now and buys the rest of the order sooner.
			var search = Standard();
			var result = search.Find(
				new BuildOrderSearch.Situation { Cash = 1500f, IncomePerSecond = 10f },
				["refinery", "barracks", "rifle", "rifle", "rifle"]);

			Assert.That(result.Feasible, Is.True);
			Assert.That(result.Order[0], Is.EqualTo("refinery"),
				$"Got {string.Join(" -> ", result.Order)}; the refinery pays for everything after it.");
		}

		[TestCase(TestName = "The bound cuts the tree rather than the answer.")]
		public void BoundingPreservesOptimality()
		{
			// Branch and bound must return the same answer an exhaustive search would; the bound is
			// admissible precisely so that cutting is safe.
			var search = Standard();
			var situation = new BuildOrderSearch.Situation
			{
				Cash = 3000f,
				IncomePerSecond = 40f,
				Existing = ["refinery", "factory", "barracks"],
			};

			var goal = new[] { "tank", "tank", "rifle", "rifle" };
			var result = search.Find(situation, goal);

			// Every ordering, computed the slow way.
			var bestExhaustive = float.PositiveInfinity;
			foreach (var permutation in Permutations(goal))
			{
				var single = new BuildOrderSearch(
				[
					Item("refinery", 1400, 20f, "building", income: 15f),
					Item("factory", 2000, 25f, "building", prerequisites: "refinery"),
					Item("tank", 800, 12f, "vehicles", prerequisites: "factory"),
					Item("barracks", 500, 10f, "building"),
					Item("rifle", 100, 4f, "infantry", prerequisites: "barracks"),
				]);

				var forced = single.Find(situation, permutation.ToArray());
				if (forced.Feasible)
					bestExhaustive = System.Math.Min(bestExhaustive, forced.CompletionSeconds);
			}

			Assert.That(result.CompletionSeconds, Is.EqualTo(bestExhaustive).Within(0.01f));
		}

		[TestCase(TestName = "A large goal stays within the node limit.")]
		public void LargeGoalsAreBounded()
		{
			var search = new BuildOrderSearch(
			[
				Item("refinery", 1400, 20f, "building", income: 15f),
				Item("factory", 2000, 25f, "building", prerequisites: "refinery"),
				Item("tank", 800, 12f, "vehicles", prerequisites: "factory"),
			])
			{ NodeLimit = 5000 };

			var goal = Enumerable.Repeat("tank", 10).Concat(["refinery", "factory"]).ToArray();
			var result = search.Find(
				new BuildOrderSearch.Situation { Cash = 20000f, IncomePerSecond = 120f }, goal);

			Assert.That(result.NodesExplored, Is.LessThanOrEqualTo(5000));
			Assert.That(result.Feasible, Is.True,
				"Hitting the limit must return the best order found, not nothing at all.");
			Assert.That(result.Order, Has.Count.EqualTo(goal.Length));
		}

		[TestCase(TestName = "An empty goal is already achieved.")]
		public void EmptyGoal()
		{
			var result = Standard().Find(new BuildOrderSearch.Situation { Cash = 1000f }, []);
			Assert.That(result.Feasible, Is.True);
			Assert.That(result.CompletionSeconds, Is.EqualTo(0f));
			Assert.That(result.Order, Is.Empty);
		}

		static IEnumerable<IEnumerable<string>> Permutations(IReadOnlyList<string> items)
		{
			if (items.Count <= 1)
			{
				yield return items;
				yield break;
			}

			for (var i = 0; i < items.Count; i++)
			{
				var rest = items.Where((_, index) => index != i).ToList();
				foreach (var permutation in Permutations(rest))
					yield return new[] { items[i] }.Concat(permutation);
			}
		}
	}
}
