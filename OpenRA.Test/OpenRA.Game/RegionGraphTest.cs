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
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Commander.Terrain;

namespace OpenRA.Test
{
	/// <summary>
	/// <para>
	/// Phase 1 of the commander rebuild: terrain understood as a graph of regions joined by
	/// chokepoints of known width, so that a two-minute plan can be searched over 30 nodes instead
	/// of 16,000 cells.
	/// </para>
	/// <para>
	/// These run on synthetic terrain with a known right answer. Real maps are checked separately by
	/// the <c>--region-graph</c> utility command, which can confirm the decomposition is plausible
	/// but cannot confirm it is correct - only a grid whose regions and corridors were placed on
	/// purpose can do that.
	/// </para>
	/// </summary>
	[TestFixture]
	sealed class RegionGraphTest
	{
		/// <summary>
		/// Two rooms of open ground separated by a wall, joined by a corridor of the given width.
		/// The archetypal RTS map: this is the shape the decomposition exists to recognise.
		/// </summary>
		static Func<int, int, bool> TwoRooms(int width, int height, int wallX, int gapCentre, int gapWidth)
		{
			return (x, y) =>
			{
				if (x <= 0 || y <= 0 || x >= width - 1 || y >= height - 1)
					return false;

				if (x != wallX)
					return true;

				var half = gapWidth / 2;
				return y >= gapCentre - half && y < gapCentre - half + gapWidth;
			};
		}

		[TestCase(TestName = "Distance transform peaks in the open and vanishes at a wall.")]
		public void DistanceTransformShape()
		{
			const int W = 21, H = 21;
			var distance = DistanceTransform.Compute(W, H, (x, y) => x > 0 && y > 0 && x < W - 1 && y < H - 1);

			// The border is impassable, so it is zero; the centre is as far from it as anything gets.
			Assert.That(distance[0], Is.EqualTo(0));
			Assert.That(distance[(10 * W) + 10], Is.EqualTo(distance.Max()),
				"The most open point of a square room is its centre.");

			// One cell in from the wall is exactly one orthogonal step.
			Assert.That(distance[(10 * W) + 1], Is.EqualTo(DistanceTransform.Orthogonal));
		}

		[TestCase(TestName = "Open ground with no obstruction is a single region.")]
		public void OpenGroundIsOneRegion()
		{
			const int W = 60, H = 60;
			var graph = RegionGraph.Build(W, H, (x, y) => x > 0 && y > 0 && x < W - 1 && y < H - 1,
				RegionGraph.Settings.Default);

			Assert.That(graph.Regions, Has.Length.EqualTo(1),
				"An empty field has no narrow places, so there is nothing to divide it at.");
			Assert.That(graph.Chokepoints, Is.Empty);
		}

		[TestCase(TestName = "Two rooms and a corridor decompose into two regions and one choke.")]
		public void TwoRoomsOneChoke()
		{
			const int W = 61, H = 41;
			var graph = RegionGraph.Build(W, H, TwoRooms(W, H, 30, 20, 3), RegionGraph.Settings.Default);

			Assert.That(graph.Regions, Has.Length.EqualTo(2),
				"A wall with one gap in it divides the map in two.");
			Assert.That(graph.Chokepoints, Has.Length.EqualTo(1));

			var choke = graph.Chokepoints[0];
			Assert.That(new[] { choke.RegionA, choke.RegionB }, Is.EquivalentTo(new[] { 0, 1 }));
			Assert.That(choke.Other(choke.RegionA), Is.EqualTo(choke.RegionB));

			// The choke should be found at the gap, not somewhere arbitrary in a room.
			Assert.That(choke.CentreX, Is.EqualTo(30).Within(2));
			Assert.That(choke.CentreY, Is.EqualTo(20).Within(3));
		}

		[TestCase(TestName = "Every passable cell ends up in exactly one region.")]
		public void LabellingIsTotal()
		{
			const int W = 61, H = 41;
			var passable = TwoRooms(W, H, 30, 20, 3);
			var graph = RegionGraph.Build(W, H, passable, RegionGraph.Settings.Default);

			// A unit standing anywhere must have an answer to "which region am I in". A partial
			// labelling would make that question return Impassable for cells units can occupy.
			for (var y = 0; y < H; y++)
			{
				for (var x = 0; x < W; x++)
				{
					var region = graph.RegionAt(x, y);
					if (passable(x, y))
						Assert.That(region, Is.InRange(0, graph.Regions.Length - 1),
							$"Passable cell {x},{y} has no region.");
					else
						Assert.That(region, Is.EqualTo(RegionGraph.Impassable),
							$"Impassable cell {x},{y} was given a region.");
				}
			}

			Assert.That(graph.Regions.Sum(r => r.CellCount),
				Is.EqualTo(Enumerable.Range(0, W * H).Count(i => passable(i % W, i / W))),
				"Region cell counts must account for the whole passable area exactly once.");
		}

		[TestCase(TestName = "Choke capacity tracks how wide the passage actually is.")]
		public void CapacityTracksWidth()
		{
			const int W = 61, H = 41;

			var narrow = RegionGraph.Build(W, H, TwoRooms(W, H, 30, 20, 2), RegionGraph.Settings.Default);
			var medium = RegionGraph.Build(W, H, TwoRooms(W, H, 30, 20, 6), RegionGraph.Settings.Default);
			var wide = RegionGraph.Build(W, H, TwoRooms(W, H, 30, 20, 12), RegionGraph.Settings.Default);

			var n = narrow.Chokepoints.Sum(c => c.Capacity);
			var m = medium.Chokepoints.Sum(c => c.Capacity);
			var w = wide.Chokepoints.Sum(c => c.Capacity);

			// The absolute number is an estimate; the ordering is the load-bearing property. A
			// commander that cannot tell a wide gap from a narrow one cannot choose where to hold.
			Assert.That(n, Is.LessThan(m), $"A 2-wide gap ({n}) must score below a 6-wide one ({m}).");
			Assert.That(m, Is.LessThan(w), $"A 6-wide gap ({m}) must score below a 12-wide one ({w}).");
			Assert.That(n, Is.GreaterThan(0));
		}

		[TestCase(TestName = "The min-cut across a chain is its narrowest link.")]
		public void MinCutFindsTheNarrowestLink()
		{
			// Three rooms in a row. The first corridor is wide, the second is narrow: to cut the
			// far room off from the near one you close the narrow one, and the commander must
			// discover that rather than be told it.
			const int W = 91, H = 41;
			var graph = RegionGraph.Build(W, H, (x, y) =>
			{
				if (x <= 0 || y <= 0 || x >= W - 1 || y >= H - 1)
					return false;

				if (x == 30)
					return y >= 16 && y < 26;   // wide gap, 10 cells

				if (x == 60)
					return y >= 19 && y < 22;   // narrow gap, 3 cells

				return true;
			}, RegionGraph.Settings.Default);

			Assert.That(graph.Regions, Has.Length.EqualTo(3));
			Assert.That(graph.Chokepoints, Has.Length.EqualTo(2));

			// Regions are numbered by flood order, so identify them by position rather than by id.
			var left = graph.RegionAt(10, 20);
			var right = graph.RegionAt(80, 20);
			Assert.That(left, Is.Not.EqualTo(right));

			var cut = graph.MinCutBetween(left, right);
			var narrowest = graph.Chokepoints.Min(c => c.Capacity);

			Assert.That(cut.Value, Is.EqualTo(narrowest),
				"Crossing the chain is limited by its tightest passage, not its widest.");
			Assert.That(cut.CutEdges, Has.Length.EqualTo(1),
				"One corridor is enough to seal the far room off.");
			Assert.That(cut.CutEdges[0].Capacity, Is.EqualTo(narrowest));
		}

		[TestCase(TestName = "Unreachable ground cannot be flowed to.")]
		public void SealedRoomsHaveNoFlow()
		{
			// A solid wall. This is the naval question in miniature: if the graph built with a
			// given movement class has no path, that class cannot make the trip, and no heuristic
			// about water is needed to discover it.
			const int W = 61, H = 41;
			var graph = RegionGraph.Build(W, H, (x, y) =>
				x > 0 && y > 0 && x < W - 1 && y < H - 1 && x != 30, RegionGraph.Settings.Default);

			Assert.That(graph.Regions, Has.Length.EqualTo(2));
			Assert.That(graph.Chokepoints, Is.Empty, "There is no passage, so there is no chokepoint.");

			var cut = graph.MinCutBetween(graph.RegionAt(10, 20), graph.RegionAt(50, 20));
			Assert.That(cut.Value, Is.EqualTo(0));
			Assert.That(cut.CutEdges, Is.Empty, "Nothing needs closing; it is already closed.");
		}

		[TestCase(TestName = "Decomposition is reproducible from identical input.")]
		public void DecompositionIsDeterministic()
		{
			// Every plan the commander makes is built on this graph. If the graph is not
			// reproducible then neither is a replay, a benchmark, or a bug report.
			const int W = 61, H = 41;
			var a = RegionGraph.Build(W, H, TwoRooms(W, H, 30, 20, 5), RegionGraph.Settings.Default);
			var b = RegionGraph.Build(W, H, TwoRooms(W, H, 30, 20, 5), RegionGraph.Settings.Default);

			Assert.That(b.Labels, Is.EqualTo(a.Labels));
			Assert.That(b.Regions.Select(r => (r.Id, r.CellCount, r.CentreX, r.CentreY)),
				Is.EqualTo(a.Regions.Select(r => (r.Id, r.CellCount, r.CentreX, r.CentreY))));
			Assert.That(b.Chokepoints.Select(c => (c.RegionA, c.RegionB, c.Capacity)),
				Is.EqualTo(a.Chokepoints.Select(c => (c.RegionA, c.RegionB, c.Capacity))));
		}

		[TestCase(TestName = "Degenerate grids do not throw.")]
		public void DegenerateInputs()
		{
			Assert.That(RegionGraph.Build(0, 0, (x, y) => true).Regions, Is.Empty);
			Assert.That(RegionGraph.Build(10, 10, (x, y) => false).Regions, Is.Empty,
				"Terrain with nothing passable has no regions.");

			var tiny = RegionGraph.Build(3, 3, (x, y) => x == 1 && y == 1);
			Assert.That(tiny.Regions.Length, Is.LessThanOrEqualTo(1),
				"A single passable cell is too small to be an area of its own.");
		}

		[TestCase(TestName = "Neighbours are reciprocal.")]
		public void NeighboursAreReciprocal()
		{
			const int W = 91, H = 41;
			var graph = RegionGraph.Build(W, H, (x, y) =>
			{
				if (x <= 0 || y <= 0 || x >= W - 1 || y >= H - 1)
					return false;

				if (x == 30)
					return y >= 16 && y < 26;

				if (x == 60)
					return y >= 19 && y < 22;

				return true;
			}, RegionGraph.Settings.Default);

			foreach (var region in graph.Regions)
				foreach (var neighbour in graph.Neighbours(region.Id))
					Assert.That(graph.Neighbours(neighbour), Has.Member(region.Id),
						$"Region {region.Id} reaches {neighbour} but not the other way round.");
		}
	}
}
