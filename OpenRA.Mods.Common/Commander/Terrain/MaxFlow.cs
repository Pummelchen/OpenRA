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

namespace OpenRA.Mods.Common.Commander.Terrain
{
	/// <summary>
	/// <para>
	/// Maximum flow and minimum cut on the region graph, by Edmonds-Karp.
	/// </para>
	/// <para>
	/// Two questions on one algorithm. The <b>max-flow</b> between my base region and the enemy's is
	/// the rate at which they can physically deliver units to me, which is the honest upper bound on
	/// how bad an attack can get and therefore on how much defence is enough. The <b>min-cut</b> is
	/// the cheapest set of chokepoints that seals me off - the defensive line, computed from the
	/// terrain rather than guessed at.
	/// </para>
	/// <para>
	/// Edmonds-Karp is O(V·E²), which on a region graph of 20-40 nodes is microseconds. The
	/// asymptotically better algorithms are not worth their complexity at this size.
	/// </para>
	/// </summary>
	public static class MaxFlow
	{
		/// <summary>An edge of the graph, undirected: capacity applies in both directions.</summary>
		public readonly record struct Edge(int From, int To, int Capacity);

		/// <summary>
		/// The flow value, which side of the cut each node fell on, and the edges crossing it.
		/// </summary>
		public sealed class Result
		{
			/// <summary>Maximum flow from source to sink, equal to the capacity of the minimum cut.</summary>
			public int Value { get; init; }

			/// <summary>True for nodes reachable from the source once the graph is saturated.</summary>
			public bool[] SourceSide { get; init; } = [];

			/// <summary>The edges crossing the cut: the chokepoints that form the defensive line.</summary>
			public Edge[] CutEdges { get; init; } = [];
		}

		/// <summary>
		/// Maximum flow and the corresponding minimum cut between <paramref name="source"/> and
		/// <paramref name="sink"/>. Parallel edges between the same pair of nodes add their
		/// capacities, which is the correct reading: two chokes between the same regions pass the
		/// sum of what each passes.
		/// </summary>
		public static Result MinCut(int nodes, IEnumerable<Edge> edges, int source, int sink)
		{
			ArgumentNullException.ThrowIfNull(edges);
			ArgumentOutOfRangeException.ThrowIfNegative(nodes);

			var edgeList = new List<Edge>(edges);

			if (nodes == 0 || source < 0 || sink < 0 || source >= nodes || sink >= nodes)
				return new Result { SourceSide = new bool[Math.Max(0, nodes)] };

			// A source that is already the sink cannot be separated from itself at any price.
			if (source == sink)
			{
				var trivial = new bool[nodes];
				trivial[source] = true;
				return new Result { Value = 0, SourceSide = trivial };
			}

			// Dense residual matrix. At 40 nodes this is 1600 ints - far cheaper to walk than an
			// adjacency structure with the indirection it implies.
			var residual = new int[nodes, nodes];
			foreach (var e in edgeList)
			{
				if (e.From < 0 || e.To < 0 || e.From >= nodes || e.To >= nodes || e.Capacity <= 0)
					continue;

				// Undirected: capacity is available in both directions.
				residual[e.From, e.To] += e.Capacity;
				residual[e.To, e.From] += e.Capacity;
			}

			var flow = 0;
			var parent = new int[nodes];
			var queue = new Queue<int>();

			while (true)
			{
				// Breadth-first search for the shortest augmenting path. Taking the *shortest* path
				// each time is what bounds Edmonds-Karp; a depth-first search here would not
				// terminate in polynomial time.
				Array.Fill(parent, -1);
				parent[source] = source;
				queue.Clear();
				queue.Enqueue(source);

				while (queue.Count > 0 && parent[sink] == -1)
				{
					var u = queue.Dequeue();
					for (var v = 0; v < nodes; v++)
					{
						if (parent[v] != -1 || residual[u, v] <= 0)
							continue;

						parent[v] = u;
						queue.Enqueue(v);
					}
				}

				if (parent[sink] == -1)
					break;

				// Push the bottleneck of this path.
				var bottleneck = int.MaxValue;
				for (var v = sink; v != source; v = parent[v])
					bottleneck = Math.Min(bottleneck, residual[parent[v], v]);

				for (var v = sink; v != source; v = parent[v])
				{
					residual[parent[v], v] -= bottleneck;
					residual[v, parent[v]] += bottleneck;
				}

				flow += bottleneck;
			}

			// Everything still reachable in the residual graph is on the source side. By the
			// max-flow min-cut theorem the edges leaving that set are exactly the minimum cut.
			var sourceSide = new bool[nodes];
			sourceSide[source] = true;
			queue.Clear();
			queue.Enqueue(source);
			while (queue.Count > 0)
			{
				var u = queue.Dequeue();
				for (var v = 0; v < nodes; v++)
				{
					if (sourceSide[v] || residual[u, v] <= 0)
						continue;

					sourceSide[v] = true;
					queue.Enqueue(v);
				}
			}

			var cut = new List<Edge>();
			foreach (var e in edgeList)
			{
				if (e.From < 0 || e.To < 0 || e.From >= nodes || e.To >= nodes || e.Capacity <= 0)
					continue;

				if (sourceSide[e.From] != sourceSide[e.To])
					cut.Add(e);
			}

			return new Result { Value = flow, SourceSide = sourceSide, CutEdges = cut.ToArray() };
		}
	}
}
