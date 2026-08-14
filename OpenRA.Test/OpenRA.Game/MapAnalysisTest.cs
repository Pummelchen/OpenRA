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

using System.Collections.Frozen;
using System.Collections.Generic;
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

		[TestCase(TestName = "Expansion value weighs buildable land by resource richness.")]
		public void ExpansionValue()
		{
			var regions = TwoByTwoRegions();
			var buildable = new[] { 25, 25, 0, 25 };       // region 2 is impassable (sea)
			var richness = new[] { 1f, 0f, 1f, 0.5f };

			var value = CoalitionMapAnalysis.ComputeExpansionValue(regions, buildable, richness);

			// Each region is 5x5 = 25 cells.
			Assert.That(value[0], Is.EqualTo(2f).Within(0.001f), "Fully buildable, rich.");
			Assert.That(value[1], Is.EqualTo(1f).Within(0.001f), "Fully buildable, no resources.");
			Assert.That(value[2], Is.EqualTo(0f).Within(0.001f), "Not buildable at all.");
			Assert.That(value[3], Is.EqualTo(1.5f).Within(0.001f), "Fully buildable, half rich.");
		}

		[TestCase(TestName = "Rally value is defensibility weighted by buildable land.")]
		public void RallyValue()
		{
			var regions = TwoByTwoRegions();
			var buildable = new[] { 25, 20, 0, 15 };
			var defensibility = new[] { 0.8f, 0.6f, 0.4f, 0.5f };

			var value = CoalitionMapAnalysis.ComputeRallyValue(regions, defensibility, buildable);

			Assert.That(value[0], Is.EqualTo(0.8f).Within(0.001f));
			Assert.That(value[1], Is.EqualTo(0.48f).Within(0.001f));
			Assert.That(value[2], Is.EqualTo(0f).Within(0.001f), "No buildable land to mass on.");
			Assert.That(value[3], Is.EqualTo(0.3f).Within(0.001f));
		}

		[TestCase(TestName = "Artillery value favors defensible ground overlooking chokepoints.")]
		public void ArtilleryValue()
		{
			var regions = TwoByTwoRegions();
			var defensibility = new[] { 0.8f, 0.6f, 0.4f, 0.5f };
			var chokepoints = new[]
			{
				new[] { 1 }.ToFrozenSet(),
				new[] { 0, 2 }.ToFrozenSet(),
				new int[0].ToFrozenSet(),
				new int[0].ToFrozenSet()
			};

			var value = CoalitionMapAnalysis.ComputeArtilleryValue(regions, defensibility, chokepoints);

			// Region 1 overlooks two chokepoint exits: the most valuable artillery position.
			Assert.That(value[1], Is.EqualTo(0.6f).Within(0.001f));
			Assert.That(value[2], Is.EqualTo(0.2f).Within(0.001f), "No chokepoints to overlook.");
		}

		[TestCase(TestName = "A bridge cell on a region border records a bridge connection.")]
		public void BridgeConnections()
		{
			var regions = TwoByTwoRegions();
			var passable = CoalitionMapAnalysis.ComputePassability(10, 10, (x, y) => true);
			var bridges = new HashSet<CPos> { new CPos(5, 2) }; // on the region 0-1 border

			var connections = CoalitionMapAnalysis.ComputeBridgeConnections(regions, bridges, passable, 10, 10);

			Assert.That(connections[0], Does.Contain(1));
			Assert.That(connections[1], Does.Contain(0));
			Assert.That(connections[2], Is.Empty);
		}

		static CoalitionMapAnalysis MapWith(List<int>[] adjacency, FrozenSet<int>[] chokepoints = null,
			int[] buildable = null)
		{
			var regions = TwoByTwoRegions();
			chokepoints ??= regions.Select(_ => new int[0].ToFrozenSet()).ToArray();
			var (components, count) = CoalitionMapAnalysis.ConnectedComponents(adjacency);
			var allComponents = new[] { components, components, components, components };
			return new CoalitionMapAnalysis(regions, new[] { adjacency, adjacency, adjacency, adjacency },
				new[] { chokepoints, chokepoints, chokepoints, chokepoints },
				allComponents, new[] { count, count, count, count }, new HashSet<CPos>(), 10, 10,
				new int[regions.Length], new float[regions.Length], new float[regions.Length],
				buildable ?? new int[regions.Length]);
		}

		[TestCase(TestName = "A corridor is described by its chokepoint and open steps.")]
		public void DescribeCorridor()
		{
			var adjacency = Enumerable.Range(0, 4).Select(_ => new List<int>()).ToArray();
			void Link(int a, int b)
			{
				adjacency[a].Add(b);
				adjacency[b].Add(a);
			}

			Link(0, 1);
			Link(1, 2);
			Link(1, 3);
			var chokepoints = new[]
			{
				new int[0].ToFrozenSet(),
				new[] { 2 }.ToFrozenSet(),
				new int[0].ToFrozenSet(),
				new int[0].ToFrozenSet()
			};
			var map = MapWith(adjacency, chokepoints);

			var (regions, features) = CoalitionMapAnalysis.DescribeCorridor(map, new[] { 0, 1, 2 }, MovementClass.Ground);

			Assert.That(regions, Is.EqualTo(new[] { 0, 1, 2 }));
			Assert.That(features, Is.EqualTo(new[] { "open:0-1", "chokepoint:1-2" }));
		}

		[TestCase(TestName = "Insertion value favors buildable rear-area land near the enemy.")]
		public void InsertionValue()
		{
			var adjacency = Enumerable.Range(0, 4).Select(_ => new List<int>()).ToArray();
			void Link(int a, int b)
			{
				adjacency[a].Add(b);
				adjacency[b].Add(a);
			}

			Link(0, 1);
			Link(1, 2);
			Link(1, 3);
			var map = MapWith(adjacency, buildable: new[] { 25, 0, 25, 25 });

			var value = map.InsertionValue(homeRegion: 0, enemyRegion: 3);

			// Region 3 is buildable and enemy-facing: the top insertion score; region 0 is our home (discounted).
			Assert.That(value[3], Is.GreaterThan(value[0]));
			Assert.That(value[1], Is.EqualTo(0f), "No buildable land to insert onto.");
		}
	}
}
