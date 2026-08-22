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
	sealed class RoutePlannerTest
	{
		// A 2x3 region grid: 0-1-2 on top, 3-4-5 on bottom, connected by vertical columns
		// (0-3, 1-4, 2-5). Multiple alternative routes exist between 0 and 5.
		static CoalitionRegion[] GridRegions()
		{
			return
			[
				new CoalitionRegion(0, Rectangle.FromLTRB(0, 0, 5, 5)),
				new CoalitionRegion(1, Rectangle.FromLTRB(5, 0, 10, 5)),
				new CoalitionRegion(2, Rectangle.FromLTRB(10, 0, 15, 5)),
				new CoalitionRegion(3, Rectangle.FromLTRB(0, 5, 5, 10)),
				new CoalitionRegion(4, Rectangle.FromLTRB(5, 5, 10, 10)),
				new CoalitionRegion(5, Rectangle.FromLTRB(10, 5, 15, 10))
			];
		}

		static CoalitionMapAnalysis MapWith(List<int>[] adjacency)
		{
			// Build a minimal analysis: chokepoints empty, components derived from the adjacency,
			// no resources.
			var regions = GridRegions();
			var chokepoints = regions.Select(_ => System.Array.Empty<int>().ToFrozenSet()).ToArray();
			var (components, count) = CoalitionMapAnalysis.ConnectedComponents(adjacency);
			var allComponents = new[] { components, components, components, components };
			return new CoalitionMapAnalysis(regions, [adjacency, adjacency, adjacency, adjacency],
				[chokepoints, chokepoints, chokepoints, chokepoints],
				allComponents, [count, count, count, count], [], 15, 10,
				new int[regions.Length], new float[regions.Length], new float[regions.Length]);
		}

		static List<int>[] Grid()
		{
			var adjacency = Enumerable.Range(0, 6).Select(_ => new List<int>()).ToArray();
			void Link(int a, int b)
			{
				adjacency[a].Add(b);
				adjacency[b].Add(a);
			}

			// Top row and bottom row.
			Link(0, 1);
			Link(1, 2);
			Link(3, 4);
			Link(4, 5);

			// Vertical columns.
			Link(0, 3);
			Link(1, 4);
			Link(2, 5);
			return adjacency;
		}

		static float[][] Threats(params (int, float[])[] entries)
		{
			var threats = Enumerable.Range(0, 6)
				.Select(_ => new float[System.Enum.GetValues<CoalitionCapability>().Length]).ToArray();
			foreach (var (region, values) in entries)
				threats[region] = values;
			return threats;
		}

		[TestCase(TestName = "A zero-threat grid finds a shortest path.")]
		public void ZeroThreatDirectPath()
		{
			var map = MapWith(Grid());
			var threats = Threats((0, new float[System.Enum.GetValues<CoalitionCapability>().Length]),
				(5, new float[System.Enum.GetValues<CoalitionCapability>().Length]));
			var route = CoalitionRoutePlanner.FindRoute(map, threats, 0, 5, MovementClass.Ground, RouteWeights.Assault());

			Assert.That(route.Found, Is.True);
			Assert.That(route.Regions[0], Is.EqualTo(0));
			Assert.That(route.Regions[^1], Is.EqualTo(5));
			Assert.That(route.Regions.Length, Is.LessThanOrEqualTo(4),
				"A shortest path in the grid should use at most 4 regions.");
		}

		[TestCase(TestName = "High combat threat on a region diverts a stealth route around it.")]
		public void StealthAvoidsThreat()
		{
			var map = MapWith(Grid());

			// Heavy enemy armor concentration on region 1 (the direct top corridor). The bottom
			// corridor (0-3-4-5) avoids it entirely.
			var threats = Threats((1, ThreatValues(combat: 10)));
			var route = CoalitionRoutePlanner.FindRoute(map, threats, 0, 5, MovementClass.Ground, RouteWeights.Stealth());

			Assert.That(route.Found, Is.True);
			Assert.That(route.Regions, Does.Not.Contain(1));
			Assert.That(route.Regions[0], Is.EqualTo(0));
			Assert.That(route.Regions[^1], Is.EqualTo(5));
		}

		[TestCase(TestName = "Assault profile pays less than stealth for crossing a threatened corridor.")]
		public void AssaultAcceptsRisk()
		{
			// Remove the bottom detour so any 0->5 route must cross region 1: the only remaining
			// paths are 0-1-2-5 and 0-1-4-5.
			var adjacency = Grid();
			adjacency[0].Remove(3);
			adjacency[3].Remove(0);
			adjacency[2].Remove(5);
			adjacency[5].Remove(2);

			var map = MapWith(adjacency);
			var threats = Threats((1, ThreatValues(combat: 10)));

			var assault = CoalitionRoutePlanner.FindRoute(map, threats, 0, 5, MovementClass.Ground, RouteWeights.Assault());
			var stealth = CoalitionRoutePlanner.FindRoute(map, threats, 0, 5, MovementClass.Ground, RouteWeights.Stealth());

			Assert.That(assault.Found, Is.True);
			Assert.That(stealth.Found, Is.True);
			Assert.That(assault.Regions, Does.Contain(1));
			Assert.That(stealth.Regions, Does.Contain(1));
			Assert.That(assault.Cost, Is.LessThan(stealth.Cost),
				"Assault pays less for crossing the threatened corridor than stealth does.");
		}

		[TestCase(TestName = "AA threat weighs heavier under the stealth profile.")]
		public void AntiAirWeighting()
		{
			// Force the route through region 1 (no detour) which carries an AA concentration.
			var adjacency = Grid();
			adjacency[0].Remove(3);
			adjacency[3].Remove(0);
			adjacency[2].Remove(5);
			adjacency[5].Remove(2);

			var map = MapWith(adjacency);
			var threats = Threats((1, ThreatValues(antiAir: 10)));

			var light = CoalitionRoutePlanner.FindRoute(map, threats, 0, 5, MovementClass.Ground, RouteWeights.Assault());
			var heavy = CoalitionRoutePlanner.FindRoute(map, threats, 0, 5, MovementClass.Ground, RouteWeights.Stealth());

			Assert.That(heavy.Cost, Is.GreaterThan(light.Cost),
				"Stealth weights AA threat higher than assault does.");
		}

		[TestCase(TestName = "Infantry routes over the foot movement graph.")]
		public void FootRouting()
		{
			var map = MapWith(Grid());
			var threats = Threats();
			var route = CoalitionRoutePlanner.FindRoute(map, threats, 0, 2, MovementClass.Foot, RouteWeights.Assault());

			Assert.That(route.Found, Is.True);
			Assert.That(route.Regions[0], Is.EqualTo(0));
			Assert.That(route.Regions[^1], Is.EqualTo(2));
		}

		[TestCase(TestName = "Ground, foot, naval, and air routes use explicit movement domains.")]
		public void ExplicitMovementDomains()
		{
			var map = MapWith(Grid());
			var threats = Threats();

			foreach (var movementClass in new[] { MovementClass.Ground, MovementClass.Foot, MovementClass.Naval, MovementClass.Air })
			{
				var route = CoalitionRoutePlanner.FindRoute(map, threats, 0, 5, movementClass, RouteWeights.Assault());
				Assert.That(route.Found, Is.True, $"{movementClass} graph must be independently routable.");
			}
		}

		[TestCase(TestName = "Retreat routes use a separate risk profile and expose a corridor description.")]
		public void RetreatRouteAndCorridor()
		{
			var map = MapWith(Grid());
			var threats = Threats((1, ThreatValues(combat: 5)));
			var retreat = CoalitionRoutePlanner.FindRoute(map, threats, 0, 2, MovementClass.Ground, RouteWeights.Retreat());

			Assert.That(retreat.Found, Is.True);
			Assert.That(retreat.Regions, Does.Not.Contain(1));
			var (_, features) = CoalitionMapAnalysis.DescribeCorridor(map, retreat.Regions, MovementClass.Ground);
			Assert.That(features, Has.Length.EqualTo(retreat.Regions.Length - 1));
		}

		[TestCase(TestName = "Routes are recalculated when dynamic threats change.")]
		public void RecalculatesAfterThreatChange()
		{
			var map = MapWith(Grid());
			var before = CoalitionRoutePlanner.FindRoute(map, Threats(), 0, 2, MovementClass.Ground, RouteWeights.Assault());
			var after = CoalitionRoutePlanner.FindRoute(map, Threats((1, ThreatValues(combat: 20))), 0, 2,
				MovementClass.Ground, RouteWeights.Assault());

			Assert.That(before.Regions, Does.Contain(1));
			Assert.That(after.Regions, Does.Not.Contain(1));
		}

		[TestCase(TestName = "Enemy reinforcement potential contributes to route cost and selection.")]
		public void ReinforcementPotential()
		{
			var map = MapWith(Grid());
			var route = CoalitionRoutePlanner.FindRoute(map, Threats((1, ThreatValues(reinforcement: 20))), 0, 2,
				MovementClass.Ground, RouteWeights.Stealth());

			Assert.That(route.Found, Is.True);
			Assert.That(route.Regions, Does.Not.Contain(1));
		}

		[TestCase(TestName = "Routes bend around congested active-combat zones.")]
		public void AvoidsCongestionAndActiveCombat()
		{
			var map = MapWith(Grid());
			var contested = new float[System.Enum.GetValues<CoalitionCapability>().Length];
			contested[(int)CoalitionCapability.ActiveCombat] = 1f;
			contested[(int)CoalitionCapability.Congestion] = 1f;

			var route = CoalitionRoutePlanner.FindRoute(map, Threats((1, contested)), 0, 2,
				MovementClass.Ground, RouteWeights.Assault());

			Assert.That(route.Found, Is.True);
			Assert.That(route.Regions, Does.Not.Contain(1), "The congested, contested corridor must be avoided.");
			Assert.That(route.Regions[0], Is.EqualTo(0));
			Assert.That(route.Regions[^1], Is.EqualTo(2));
		}

		[TestCase(TestName = "No path between disconnected components.")]
		public void DisconnectedRegions()
		{
			// Region 2 and 5 are cut off from the rest.
			var adjacency = Grid();
			adjacency[1].Remove(2);
			adjacency[2].Remove(1);
			adjacency[4].Remove(5);
			adjacency[5].Remove(4);

			var map = MapWith(adjacency);
			var threats = Threats();
			var route = CoalitionRoutePlanner.FindRoute(map, threats, 0, 5, MovementClass.Ground, RouteWeights.Assault());

			Assert.That(route.Found, Is.False);
			Assert.That(CoalitionRoutePlanner.RouteExists(map, 0, 5, MovementClass.Ground), Is.False);
		}

		[TestCase(TestName = "Same region is a zero-cost route.")]
		public void SameRegion()
		{
			var map = MapWith(Grid());
			var threats = Threats();
			var route = CoalitionRoutePlanner.FindRoute(map, threats, 3, 3, MovementClass.Ground, RouteWeights.Assault());

			Assert.That(route.Found, Is.True);
			Assert.That(route.Cost, Is.EqualTo(0f));
			Assert.That(route.Regions, Is.EquivalentTo([3]));
		}

		[TestCase(TestName = "Invalid region indices return no route.")]
		public void InvalidRegions()
		{
			var map = MapWith(Grid());
			var threats = Threats();
			Assert.That(CoalitionRoutePlanner.FindRoute(map, threats, -1, 5, MovementClass.Ground, RouteWeights.Assault()).Found, Is.False);
			Assert.That(CoalitionRoutePlanner.FindRoute(map, threats, 0, 99, MovementClass.Ground, RouteWeights.Assault()).Found, Is.False);
		}

		static float[] ThreatValues(float combat = 0, float antiAir = 0, float reinforcement = 0)
		{
			var values = new float[System.Enum.GetValues<CoalitionCapability>().Length];
			values[(int)CoalitionCapability.GroundAntiArmor] = combat;
			values[(int)CoalitionCapability.AntiAir] = antiAir;
			values[(int)CoalitionCapability.Reinforcement] = reinforcement;
			return values;
		}
	}
}
