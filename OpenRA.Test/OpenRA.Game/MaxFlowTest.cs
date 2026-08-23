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

using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Commander.Terrain;

namespace OpenRA.Test
{
	/// <summary>
	/// Max-flow and min-cut on the region graph, against hand-computed answers. The max-flow bounds
	/// how fast an enemy can physically deliver units; the min-cut names the chokepoints that form
	/// the defensive line.
	/// </summary>
	[TestFixture]
	sealed class MaxFlowTest
	{
		static MaxFlow.Edge E(int a, int b, int c) => new(a, b, c);

		[TestCase(TestName = "A chain is limited by its narrowest link.")]
		public void ChainBottleneck()
		{
			var result = MaxFlow.MinCut(3, [E(0, 1, 5), E(1, 2, 3)], 0, 2);

			Assert.That(result.Value, Is.EqualTo(3));
			Assert.That(result.CutEdges, Has.Length.EqualTo(1));
			Assert.That(result.CutEdges[0].Capacity, Is.EqualTo(3));
			Assert.That(result.SourceSide[0], Is.True);
			Assert.That(result.SourceSide[2], Is.False);
		}

		[TestCase(TestName = "Two routes add, and cutting takes both.")]
		public void ParallelRoutes()
		{
			// 0→1→3 carries 2, 0→2→3 carries 2.
			var result = MaxFlow.MinCut(4, [E(0, 1, 3), E(1, 3, 2), E(0, 2, 2), E(2, 3, 3)], 0, 3);

			Assert.That(result.Value, Is.EqualTo(4));
			Assert.That(result.CutEdges.Sum(e => e.Capacity), Is.EqualTo(4),
				"The cut's capacity equals the flow - that is the theorem this rests on.");
			Assert.That(result.CutEdges, Has.Length.EqualTo(2),
				"Sealing a two-route map requires closing both routes.");
		}

		[TestCase(TestName = "Parallel chokes between the same regions add capacity.")]
		public void ParallelEdgesAccumulate()
		{
			// Two separate corridors joining the same pair of regions genuinely pass the sum.
			var result = MaxFlow.MinCut(2, [E(0, 1, 3), E(0, 1, 4)], 0, 1);
			Assert.That(result.Value, Is.EqualTo(7));
		}

		[TestCase(TestName = "Capacity applies in both directions.")]
		public void EdgesAreUndirected()
		{
			// Terrain does not care which way you walk through it, so the graph must not either.
			var forward = MaxFlow.MinCut(3, [E(0, 1, 5), E(1, 2, 3)], 0, 2);
			var backward = MaxFlow.MinCut(3, [E(0, 1, 5), E(1, 2, 3)], 2, 0);
			Assert.That(backward.Value, Is.EqualTo(forward.Value));
		}

		[TestCase(TestName = "Disconnected nodes carry no flow and need no cut.")]
		public void Disconnected()
		{
			var result = MaxFlow.MinCut(4, [E(0, 1, 5), E(2, 3, 5)], 0, 3);
			Assert.That(result.Value, Is.EqualTo(0));
			Assert.That(result.CutEdges, Is.Empty);
			Assert.That(result.SourceSide[1], Is.True, "Node 1 is reachable from the source.");
			Assert.That(result.SourceSide[3], Is.False);
		}

		[TestCase(TestName = "Degenerate arguments are answered, not thrown.")]
		public void DegenerateArguments()
		{
			// These arise from empty or single-region maps, and must not take the bot down mid-match.
			Assert.That(MaxFlow.MinCut(0, [], 0, 0).Value, Is.EqualTo(0));
			Assert.That(MaxFlow.MinCut(3, [E(0, 1, 2)], 1, 1).Value, Is.EqualTo(0),
				"A region cannot be separated from itself at any price.");
			Assert.That(MaxFlow.MinCut(3, [E(0, 1, 2)], -1, 2).Value, Is.EqualTo(0));
			Assert.That(MaxFlow.MinCut(2, [E(0, 1, 0), E(0, 5, 3)], 0, 1).Value, Is.EqualTo(0),
				"Zero-capacity and out-of-range edges are ignored rather than trusted.");
		}

		[TestCase(TestName = "A wide front is cut at its cheapest point, not its first.")]
		public void CutsAtTheCheapestPoint()
		{
			// Source reaches the sink by three routes of very different cost. The cut must be the
			// cheapest set, which is what makes it a defensive line worth holding rather than
			// merely a partition.
			var result = MaxFlow.MinCut(5,
			[
				E(0, 1, 10), E(1, 4, 10),
				E(0, 2, 1), E(2, 4, 1),
				E(0, 3, 10), E(3, 4, 2),
			], 0, 4);

			Assert.That(result.Value, Is.EqualTo(13));
			Assert.That(result.CutEdges.Sum(e => e.Capacity), Is.EqualTo(13));
			Assert.That(result.CutEdges.Select(e => e.Capacity), Does.Contain(1).And.Contain(2),
				"The narrow branches are cut on their tight side, not their wide one.");
		}
	}
}
