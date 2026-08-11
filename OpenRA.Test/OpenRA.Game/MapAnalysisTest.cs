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

using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;
using OpenRA.Primitives;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class MapAnalysisTest
	{
		static CoalitionRegion[] TwoByTwoRegions()
		{
			return
			[
				new CoalitionRegion(0, Rectangle.FromLTRB(0, 0, 5, 5)),
				new CoalitionRegion(1, Rectangle.FromLTRB(5, 0, 10, 5)),
				new CoalitionRegion(2, Rectangle.FromLTRB(0, 5, 5, 10)),
				new CoalitionRegion(3, Rectangle.FromLTRB(5, 5, 10, 10))
			];
		}

		[TestCase(TestName = "Fully open grid connects orthogonally adjacent regions with no chokepoints.")]
		public void OpenGrid()
		{
			var passable = CoalitionMapAnalysis.ComputePassability(10, 10, (x, y) => true);
			var (adjacency, chokepoints) = CoalitionMapAnalysis.BuildRegionGraph(10, 10, passable, TwoByTwoRegions());

			// Region 0 (top-left) touches 1 (top-right) and 2 (bottom-left) orthogonally;
			// region 3 (bottom-right) is only diagonal to 0, so it is not linked.
			Assert.That(adjacency[0], Is.EquivalentTo(new[] { 1, 2 }));
			Assert.That(adjacency[3], Is.EquivalentTo(new[] { 1, 2 }));
			Assert.That(chokepoints[0], Is.Empty, "A wide open border is not a chokepoint.");
		}

		[TestCase(TestName = "A single-cell land bridge between regions is a chokepoint.")]
		public void NarrowBridge()
		{
			// Only one passable cell crosses the vertical border at row 2.
			var passable = CoalitionMapAnalysis.ComputePassability(10, 10, (x, y) => x < 5 || (x == 5 && y == 2));
			var (adjacency, chokepoints) = CoalitionMapAnalysis.BuildRegionGraph(10, 10, passable, TwoByTwoRegions());

			Assert.That(adjacency[0], Does.Contain(1));
			Assert.That(chokepoints[0], Does.Contain(1), "A one-cell crossing is a chokepoint.");
		}

		[TestCase(TestName = "Disconnected halves produce two ground components.")]
		public void DisconnectedHalves()
		{
			// Left half passable, right half passable, no crossing.
			var passable = CoalitionMapAnalysis.ComputePassability(10, 10, (x, y) => x < 5 || x >= 6);
			var (adjacency, _) = CoalitionMapAnalysis.BuildRegionGraph(10, 10, passable, TwoByTwoRegions());
			var (components, count) = CoalitionMapAnalysis.ConnectedComponents(adjacency);

			Assert.That(count, Is.EqualTo(2));
			Assert.That(components[0], Is.EqualTo(components[2]), "Left half is one component.");
			Assert.That(components[1], Is.EqualTo(components[3]), "Right half is one component.");
			Assert.That(components[0], Is.Not.EqualTo(components[1]));
		}

		[TestCase(TestName = "A fully impassable region (sea) connects to nothing.")]
		public void PondDoesNotConnect()
		{
			// Region 2 (bottom-left) is entirely water; land elsewhere.
			var passable = CoalitionMapAnalysis.ComputePassability(10, 10, (x, y) => !(x < 5 && y >= 5));
			var (adjacency, _) = CoalitionMapAnalysis.BuildRegionGraph(10, 10, passable, TwoByTwoRegions());

			Assert.That(adjacency[2], Is.Empty, "An isolated sea region connects to nothing.");
			Assert.That(adjacency[0], Does.Contain(1));
		}

		[TestCase(TestName = "Chokepoint threshold is configurable.")]
		public void ChokepointThreshold()
		{
			// Two-cell crossing: a chokepoint at width 3, not at width 1.
			var passable = CoalitionMapAnalysis.ComputePassability(10, 10, (x, y) => x < 5 || (x == 5 && (y == 2 || y == 3)));
			var (_, wide) = CoalitionMapAnalysis.BuildRegionGraph(10, 10, passable, TwoByTwoRegions(), chokepointMaxWidth: 3);
			var (_, narrow) = CoalitionMapAnalysis.BuildRegionGraph(10, 10, passable, TwoByTwoRegions(), chokepointMaxWidth: 1);

			Assert.That(wide[0], Does.Contain(1));
			Assert.That(narrow[0], Does.Not.Contain(1));
		}

		[TestCase(TestName = "ComponentOf resolves the connected component for a region.")]
		public void ComponentLookup()
		{
			var passable = CoalitionMapAnalysis.ComputePassability(10, 10, (x, y) => x < 5 || x >= 6);
			var (adjacency, _) = CoalitionMapAnalysis.BuildRegionGraph(10, 10, passable, TwoByTwoRegions());
			var (components, _) = CoalitionMapAnalysis.ConnectedComponents(adjacency);

			Assert.That(components[0], Is.EqualTo(components[2]));
			Assert.That(components[1], Is.EqualTo(components[3]));
		}

		[TestCase(TestName = "Adjacency is symmetric.")]
		public void SymmetricAdjacency()
		{
			var passable = CoalitionMapAnalysis.ComputePassability(10, 10, (x, y) => x < 5 || (x == 5 && y == 2));
			var (adjacency, _) = CoalitionMapAnalysis.BuildRegionGraph(10, 10, passable, TwoByTwoRegions());

			for (var i = 0; i < adjacency.Length; i++)
				foreach (var j in adjacency[i])
					Assert.That(adjacency[j], Does.Contain(i));
		}
	}
}
