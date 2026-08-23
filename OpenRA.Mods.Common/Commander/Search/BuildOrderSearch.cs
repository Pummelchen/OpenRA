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
using System.Collections.Generic;

namespace OpenRA.Mods.Common.Commander.Search
{
	/// <summary>
	/// <para>
	/// Finds the fastest order in which to build a set of things, by depth-first branch and bound.
	/// </para>
	/// <para>
	/// This is a scheduling problem, not a heuristic one, and treating it as scheduling is worth
	/// real time on the clock. "Six tanks and a second refinery" has a right answer - an ordering
	/// that reaches the goal soonest given prerequisites, queue capacity and income - and a bot that
	/// picks the next item greedily will routinely be a minute late to it, which at this level of
	/// play is the whole game.
	/// </para>
	/// <para>
	/// The bound is what makes it tractable. Any remaining item must at minimum be paid for, so the
	/// time to earn the outstanding cost is a floor on the time to finish; a branch already past the
	/// best complete answer by that margin cannot win and is cut. Without it the search is
	/// factorial in the goal size.
	/// </para>
	/// </summary>
	public sealed class BuildOrderSearch
	{
		/// <summary>One buildable thing: what it costs, how long it takes, and what it needs first.</summary>
		public sealed class Item
		{
			public string Name { get; init; } = "";
			public int Cost { get; init; }

			/// <summary>Seconds to produce, at normal speed.</summary>
			public float BuildSeconds { get; init; }

			/// <summary>Names that must already exist before this can start.</summary>
			public IReadOnlyList<string> Prerequisites { get; init; } = [];

			/// <summary>Which queue produces it. Separate queues build in parallel.</summary>
			public string Queue { get; init; } = "";

			/// <summary>Credits per second this adds once finished, for a refinery or harvester.</summary>
			public float IncomePerSecond { get; init; }
		}

		/// <summary>The starting position a plan is searched from.</summary>
		public sealed class Situation
		{
			public float Cash { get; init; }
			public float IncomePerSecond { get; init; }

			/// <summary>What already exists, by name.</summary>
			public IReadOnlyCollection<string> Existing { get; init; } = [];
		}

		/// <summary>The order to build in, and when it finishes.</summary>
		public sealed class Result
		{
			public IReadOnlyList<string> Order { get; init; } = [];
			public float CompletionSeconds { get; init; }
			public bool Feasible { get; init; }
			public int NodesExplored { get; init; }
		}

		readonly Dictionary<string, Item> catalogue = [];

		/// <summary>Nodes to explore before giving up and returning the best order found so far.</summary>
		public int NodeLimit { get; init; } = 20000;

		public BuildOrderSearch(IEnumerable<Item> items)
		{
			ArgumentNullException.ThrowIfNull(items);
			foreach (var item in items)
				if (item != null && !string.IsNullOrEmpty(item.Name))
					catalogue[item.Name] = item;
		}

		/// <summary>Search state: one partial ordering under consideration.</summary>
		sealed class Frame
		{
			/// <summary>
			/// When the most recent item was <i>ordered</i>, not when it finished. A build order is a
			/// sequence of decisions in time; advancing this to the last completion instead would
			/// serialise every queue and make parallel production impossible to express.
			/// </summary>
			public float OrderedAt;

			/// <summary>When the last item finishes: the time the goal is actually achieved.</summary>
			public float Completion;

			public float Cash;
			public float Income;
			public HashSet<string> Built;
			public Dictionary<string, float> QueueFree;
			public List<string> Order;
			public List<string> Remaining;
		}

		/// <summary>
		/// The fastest order that produces <paramref name="goal"/>. Names may repeat, and a repeated
		/// name means "build that many".
		/// </summary>
		public Result Find(Situation situation, IReadOnlyList<string> goal)
		{
			ArgumentNullException.ThrowIfNull(situation);
			ArgumentNullException.ThrowIfNull(goal);

			foreach (var name in goal)
				if (!catalogue.ContainsKey(name))
					return new Result { Feasible = false };

			var best = float.PositiveInfinity;
			List<string> bestOrder = null;
			var nodes = 0;

			var root = new Frame
			{
				OrderedAt = 0f,
				Completion = 0f,
				Cash = situation.Cash,
				Income = situation.IncomePerSecond,
				Built = new HashSet<string>(situation.Existing),
				QueueFree = [],
				Order = [],
				Remaining = new List<string>(goal),
			};

			var stack = new Stack<Frame>();
			stack.Push(root);

			while (stack.Count > 0 && nodes < NodeLimit)
			{
				var frame = stack.Pop();
				nodes++;

				if (frame.Remaining.Count == 0)
				{
					if (frame.Completion < best)
					{
						best = frame.Completion;
						bestOrder = frame.Order;
					}

					continue;
				}

				// Bound: everything still outstanding must at least be paid for, so the time to earn
				// the shortfall is a floor on finishing. A branch already past the best answer by
				// that margin cannot win.
				if (LowerBound(frame) >= best)
					continue;

				// Children pushed in reverse so the first candidate is explored first, which finds a
				// complete answer early and makes the bound bite sooner.
				var candidates = new List<Frame>();
				var tried = new HashSet<string>();

				foreach (var name in frame.Remaining)
				{
					// The same name twice in the goal is the same choice; exploring it twice at this
					// level would double the tree for nothing.
					if (!tried.Add(name))
						continue;

					var item = catalogue[name];
					if (!PrerequisitesMet(item, frame.Built))
						continue;

					candidates.Add(Advance(frame, item));
				}

				for (var i = candidates.Count - 1; i >= 0; i--)
					stack.Push(candidates[i]);
			}

			return new Result
			{
				Order = bestOrder ?? [],
				CompletionSeconds = float.IsInfinity(best) ? 0f : best,
				Feasible = bestOrder != null,
				NodesExplored = nodes,
			};
		}

		/// <summary>Applies one item and returns the resulting position.</summary>
		static Frame Advance(Frame frame, Item item)
		{
			var queueFree = new Dictionary<string, float>(frame.QueueFree);
			queueFree.TryGetValue(item.Queue, out var queueReady);

			// When the money is there. Note that cost is drawn down over build time in this engine,
			// so a queue that builds instantly still needs the whole price at once - exactly the
			// coupling that made instant-build cheats a handicap when income did not match.
			var affordableAt = frame.Cash >= item.Cost || frame.Income <= 0f
				? frame.OrderedAt
				: frame.OrderedAt + ((item.Cost - frame.Cash) / frame.Income);

			// Three constraints, and the latest of them governs: orders are given in sequence, the
			// queue has to be free, and the money has to exist. Crucially this does NOT wait for
			// unrelated items to finish - a barracks and a war factory build at the same time, and a
			// schedule that pretended otherwise would be needlessly slow.
			var startAt = Math.Max(Math.Max(frame.OrderedAt, queueReady), affordableAt);
			var finishAt = startAt + item.BuildSeconds;

			var cash = frame.Cash + ((startAt - frame.OrderedAt) * frame.Income) - item.Cost;
			queueFree[item.Queue] = finishAt;

			var built = new HashSet<string>(frame.Built) { item.Name };
			var order = new List<string>(frame.Order) { item.Name };

			var remaining = new List<string>(frame.Remaining);
			remaining.Remove(item.Name);

			return new Frame
			{
				OrderedAt = startAt,
				Completion = Math.Max(frame.Completion, finishAt),
				Cash = Math.Max(0f, cash),

				// Income rises only once the thing is actually finished, which is why an early
				// refinery is worth its delay and a late one is not.
				Income = frame.Income + item.IncomePerSecond,
				Built = built,
				QueueFree = queueFree,
				Order = order,
				Remaining = remaining,
			};
		}

		/// <summary>
		/// A floor on how long this branch can possibly take: the outstanding bill, divided by
		/// income. Admissible - it never over-estimates - which is what makes cutting on it safe.
		/// </summary>
		float LowerBound(Frame frame)
		{
			var outstanding = 0f;
			foreach (var name in frame.Remaining)
				if (catalogue.TryGetValue(name, out var item))
					outstanding += item.Cost;

			var shortfall = outstanding - frame.Cash;
			if (shortfall <= 0f || frame.Income <= 0f)
				return frame.Completion;

			return Math.Max(frame.Completion, frame.OrderedAt + (shortfall / frame.Income));
		}

		static bool PrerequisitesMet(Item item, HashSet<string> built)
		{
			foreach (var prerequisite in item.Prerequisites)
				if (!built.Contains(prerequisite))
					return false;

			return true;
		}
	}
}
