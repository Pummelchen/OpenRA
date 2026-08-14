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
using System.Linq;

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	/// <summary>Cost weights applied to a route for one mission intent.</summary>
	public sealed class RouteWeights
	{
		public float Distance = 1f;
		public float CombatThreat = 1f;
		public float AntiAirThreat = 1f;
		public float ArtilleryThreat = 1f;
		public float VisionExposure = 1f;
		public float DetectionExposure = 1f;
		public float ChokepointRisk = 1f;
		public float ReinforcementRisk = 1f;
		public float SupportPowerRisk = 1f;

		/// <summary>Extra cost for a crossing that is a bridge (a fixed, easily contested connector).</summary>
		public float BridgeRisk = 0.5f;

		/// <summary>Cost of moving through a region with live enemy presence (an active combat zone).</summary>
		public float ActiveCombatZone = 2f;

		/// <summary>Cost of moving through a region overloaded with our own units (congestion).</summary>
		public float Congestion = 1.5f;

		/// <summary>Stealth-biased profile for transports and special forces.</summary>
		public static RouteWeights Stealth()
		{
			return new RouteWeights
			{
				CombatThreat = 2f,
				VisionExposure = 3f,
				DetectionExposure = 3f,
				ChokepointRisk = 2f,
				ReinforcementRisk = 1.5f,
				AntiAirThreat = 1.5f
			};
		}

		/// <summary>Combat-efficiency profile for main assaults: accepts risk to close with the target.</summary>
		public static RouteWeights Assault()
		{
			return new RouteWeights
			{
				Distance = 1.2f,
				CombatThreat = 1f,
				VisionExposure = 0.5f,
				ChokepointRisk = 0.5f
			};
		}

		/// <summary>Low-risk profile for recon probes.</summary>
		public static RouteWeights Recon()
		{
			return new RouteWeights
			{
				CombatThreat = 1.5f,
				AntiAirThreat = 1.5f,
				VisionExposure = 1f,
				ChokepointRisk = 1.5f
			};
		}

		/// <summary>Escape profile for withdrawals: distance to safety outweighs everything.</summary>
		public static RouteWeights Retreat()
		{
			return new RouteWeights
			{
				Distance = 2f,
				CombatThreat = 2f,
				VisionExposure = 1f
			};
		}
	}

	/// <summary>A planned region path between two points with its aggregated cost.</summary>
	public sealed class PlannedRoute
	{
		public readonly int[] Regions;
		public readonly float Cost;
		public readonly bool Found;

		public PlannedRoute(int[] regions, float cost, bool found)
		{
			Regions = regions;
			Cost = cost;
			Found = found;
		}

		public static readonly PlannedRoute None = new(null, float.MaxValue, false);
	}

	/// <summary>
	/// Finds the least-cost route between two regions on the region graph for a movement class,
	/// weighting each crossed region by a mission-specific profile of threat, exposure, and
	/// chokepoint risk. Route cost is additive so the same planner serves main assaults, stealth
	/// transports, recon, and retreats with different weight profiles.
	/// </summary>
	public static class CoalitionRoutePlanner
	{
		/// <summary>
		/// Dijkstra over the region graph. Costs are computed per *entered* region from its threat
		/// fields, so the route naturally bends around AA concentrations, exposed ground, and
		/// chokepoints when the profile demands it.
		/// </summary>
		public static PlannedRoute FindRoute(
			CoalitionMapAnalysis map, float[][] threats, int from, int to,
			MovementClass movementClass, RouteWeights weights)
		{
			if (from < 0 || to < 0 || from >= map.Regions.Length || to >= map.Regions.Length)
				return PlannedRoute.None;

			if (from == to)
				return new PlannedRoute([from], 0f, true);

			var adjacency = map.Adjacency[(int)movementClass];
			var chokepoints = map.Chokepoints[(int)movementClass];

			var dist = new float[map.Regions.Length];
			var prev = new int[map.Regions.Length];
			Array.Fill(dist, float.MaxValue);
			Array.Fill(prev, -1);
			dist[from] = 0;

			var open = new SortedSet<(float Cost, int Region)>(Comparer<(float, int)>.Create((a, b) =>
				a.Item1 != b.Item1 ? a.Item1.CompareTo(b.Item1) : a.Item2.CompareTo(b.Item2)));
			open.Add((0f, from));

			while (open.Count > 0)
			{
				var (currentCost, current) = open.Min;
				open.Remove(open.Min);
				if (current == to)
					break;

				foreach (var next in adjacency[current])
				{
					var regionThreats = threats[next];
					var cost = weights.Distance
						+ weights.CombatThreat * regionThreats[(int)CoalitionCapability.GroundAntiArmor]
						+ weights.AntiAirThreat * regionThreats[(int)CoalitionCapability.AntiAir]
						+ weights.ArtilleryThreat * regionThreats[(int)CoalitionCapability.Artillery]
						+ weights.VisionExposure * regionThreats[(int)CoalitionCapability.VisionExposure]
						+ weights.DetectionExposure * regionThreats[(int)CoalitionCapability.Detection]
						+ weights.ReinforcementRisk * regionThreats[(int)CoalitionCapability.Reinforcement]
						+ weights.SupportPowerRisk * regionThreats[(int)CoalitionCapability.SupportPowerRisk]
						+ weights.ActiveCombatZone * regionThreats[(int)CoalitionCapability.ActiveCombat]
						+ weights.Congestion * regionThreats[(int)CoalitionCapability.Congestion];

					if (chokepoints[current].Contains(next))
						cost += weights.ChokepointRisk * 4f;

					if (map.BridgeConnections[current].Contains(next))
						cost += weights.BridgeRisk * 2f;

					var nextCost = currentCost + cost;
					if (nextCost >= dist[next])
						continue;

					dist[next] = nextCost;
					prev[next] = current;
					open.Add((nextCost, next));
				}
			}

			if (dist[to] == float.MaxValue)
				return PlannedRoute.None;

			// Reconstruct the path from the predecessor chain.
			var path = new List<int> { to };
			for (var node = to; node != from && prev[node] >= 0; node = prev[node])
				path.Insert(0, prev[node]);

			return new PlannedRoute(path.ToArray(), dist[to], true);
		}

		/// <summary>True when a route exists at all between two regions for the movement class.</summary>
		public static bool RouteExists(CoalitionMapAnalysis map, int from, int to, MovementClass movementClass)
		{
			return from >= 0 && to >= 0 && from < map.Regions.Length && to < map.Regions.Length
				&& map.ComponentOf(movementClass, from) == map.ComponentOf(movementClass, to);
		}
	}
}
