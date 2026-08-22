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
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Pathfinder;
using OpenRA.Primitives;

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	/// <summary>Movement domains for the region graph. Air ignores terrain.</summary>
	public enum MovementClass
	{
		Ground,
		Naval,
		Air,
		Foot
	}

	/// <summary>
	/// <para>
	/// Static terrain analysis of a map: the region adjacency graph per movement class,
	/// chokepoints (narrow region crossings), connected components (continents/islands and
	/// sea bodies), bridges, and per-region resource/defensibility scores.
	/// </para>
	/// <para>
	/// Everything here is terrain-derived and shroud-independent, so the analysis is computed
	/// once per map and cached; dynamic state (threats, control) lives in the blackboard.
	/// </para>
	/// </summary>
	public sealed class CoalitionMapAnalysis
	{
		public readonly int Width;
		public readonly int Height;

		/// <summary>Region partition, matching the blackboard's grid.</summary>
		public readonly CoalitionRegion[] Regions;

		/// <summary>Adjacency[class][region] = regions reachable by that movement class.</summary>
		public readonly List<int>[][] Adjacency;

		/// <summary>Chokepoints[class][region] = set of adjacent regions reached through a narrow crossing.</summary>
		public readonly FrozenSet<int>[][] Chokepoints;

		/// <summary>Components[class][region] = connected-component id (continents, sea bodies).</summary>
		public readonly int[][] Components;

		/// <summary>Number of connected components per movement class.</summary>
		public readonly int[] ComponentCount;

		/// <summary>Terrain cells that are bridges.</summary>
		public readonly HashSet<CPos> BridgeCells;

		/// <summary>Terrain cells that are rivers. Rivers are not bridges and are tracked separately.</summary>
		public readonly HashSet<CPos> RiverCells;

		/// <summary>Ground regions outside the largest connected land component.</summary>
		public readonly FrozenSet<int> IslandRegions;

		/// <summary>Regions that border at least one narrow naval crossing.</summary>
		public readonly FrozenSet<int> NarrowNavalPassageRegions;

		/// <summary>Valuable resource cells per region.</summary>
		public readonly int[] ResourceCells;

		/// <summary>Resource richness per region, normalized to 0..1.</summary>
		public readonly float[] ResourceRichness;

		/// <summary>Defensibility per region, 0..1 (land fraction weighted against border crossings).</summary>
		public readonly float[] Defensibility;

		/// <summary>Passable land cells per region — where buildings and bases can be placed.</summary>
		public readonly int[] BuildableCells;

		/// <summary>Static expansion value per region: buildable land fraction weighted by resource richness.</summary>
		public readonly float[] ExpansionValue;

		/// <summary>BridgeConnections[region] = regions reached from it by a bridge crossing.</summary>
		public readonly FrozenSet<int>[] BridgeConnections;

		/// <summary>Rally/staging value per region: defensible open ground to mass forces safely.</summary>
		public readonly float[] RallyValue;

		/// <summary>Artillery value per region: defensible ground overlooking chokepoint approach corridors.</summary>
		public readonly float[] ArtilleryValue;

		/// <summary>
		/// Builds an analysis from precomputed graph data. Production code should use
		/// <see cref="ForMap"/>; this constructor is also used by unit tests to exercise the
		/// pure graph algorithms with synthetic maps.
		/// </summary>
		public CoalitionMapAnalysis(CoalitionRegion[] regions, List<int>[][] adjacency, FrozenSet<int>[][] chokepoints,
			int[][] components, int[] componentCount, HashSet<CPos> bridgeCells, int width, int height,
			int[] resourceCells, float[] resourceRichness, float[] defensibility,
			int[] buildableCells = null, float[] expansionValue = null,
			FrozenSet<int>[] bridgeConnections = null, float[] rallyValue = null, float[] artilleryValue = null,
			HashSet<CPos> riverCells = null)
		{
			Regions = regions;
			Adjacency = adjacency;
			Chokepoints = chokepoints;
			Components = components;
			ComponentCount = componentCount;
			BridgeCells = bridgeCells;
			RiverCells = riverCells ?? [];
			Width = width;
			Height = height;
			ResourceCells = resourceCells;
			ResourceRichness = resourceRichness;
			Defensibility = defensibility;
			BuildableCells = buildableCells ?? new int[regions.Length];
			ExpansionValue = expansionValue ?? new float[regions.Length];
			BridgeConnections = bridgeConnections ?? regions.Select(_ => Array.Empty<int>().ToFrozenSet()).ToArray();
			RallyValue = rallyValue ?? new float[regions.Length];
			ArtilleryValue = artilleryValue ?? new float[regions.Length];
			IslandRegions = ComputeIslandRegions(components[(int)MovementClass.Ground]).ToFrozenSet();
			NarrowNavalPassageRegions = Enumerable.Range(0, regions.Length)
				.Where(i => chokepoints[(int)MovementClass.Naval][i].Count > 0)
				.ToFrozenSet();
		}

		/// <summary>True when a region pair is reachable by the given movement class.</summary>
		public bool IsAdjacent(MovementClass movementClass, int a, int b)
		{
			return a >= 0 && a < Adjacency[(int)movementClass].Length && Adjacency[(int)movementClass][a].Contains(b);
		}

		/// <summary>True when the crossing between two adjacent regions is a chokepoint for the class.</summary>
		public bool IsChokepoint(MovementClass movementClass, int a, int b)
		{
			return a >= 0 && a < Chokepoints[(int)movementClass].Length && Chokepoints[(int)movementClass][a].Contains(b);
		}

		/// <summary>The component (continent/sea body) a region belongs to for a movement class.</summary>
		public int ComponentOf(MovementClass movementClass, int region)
		{
			return Components[(int)movementClass][region];
		}

		/// <summary>Returns regions outside the largest connected ground component.</summary>
		public static IEnumerable<int> ComputeIslandRegions(int[] groundComponents)
		{
			if (groundComponents.Length == 0)
				return [];

			var mainland = groundComponents
				.GroupBy(component => component)
				.OrderByDescending(group => group.Count())
				.ThenBy(group => group.Key)
				.First().Key;
			return Enumerable.Range(0, groundComponents.Length)
				.Where(region => groundComponents[region] != mainland)
				.ToArray();
		}

		/// <summary>Regions meeting a minimum static defensibility score, best first.</summary>
		public IEnumerable<int> DefensibleRegions(float minimumScore = 0.25f)
		{
			return Enumerable.Range(0, Regions.Length)
				.Where(region => Defensibility[region] >= minimumScore)
				.OrderByDescending(region => Defensibility[region]);
		}

		// ------------------------------------------------------------------------------------
		// Pure graph algorithms. These take explicit grids so they can be unit-tested without
		// constructing a World.
		// ------------------------------------------------------------------------------------

		/// <summary>Boolean passability grid of width*height cells from a per-cell predicate.</summary>
		public static bool[] ComputePassability(int width, int height, Func<int, int, bool> isPassable)
		{
			var passable = new bool[width * height];
			for (var y = 0; y < height; y++)
				for (var x = 0; x < width; x++)
					passable[y * width + x] = isPassable(x, y);

			return passable;
		}

		/// <summary>
		/// Builds the region adjacency graph plus chokepoint crossings for one movement class.
		/// Two regions are adjacent when an orthogonal cell pair across their shared border is
		/// passable; a border with few crossings is a chokepoint.
		/// </summary>
		public static (List<int>[] Adjacency, FrozenSet<int>[] Chokepoints) BuildRegionGraph(
			int width, int height, bool[] passable, CoalitionRegion[] regions, int chokepointMaxWidth = 3)
		{
			var adjacency = Enumerable.Range(0, regions.Length).Select(_ => new HashSet<int>()).ToArray();
			var crossings = Enumerable.Range(0, regions.Length).Select(_ => new int[regions.Length]).ToArray();

			for (var y = 0; y < height; y++)
				for (var x = 0; x < width; x++)
				{
					var cell = new CPos(x, y);
					if (!passable[y * width + x])
						continue;

					// Horizontal neighbor.
					if (x + 1 < width && passable[y * width + x + 1])
						Link(regions, adjacency, crossings, cell, new CPos(x + 1, y));

					// Vertical neighbor.
					if (y + 1 < height && passable[(y + 1) * width + x])
						Link(regions, adjacency, crossings, cell, new CPos(x, y + 1));
				}

			var chokepoints = new FrozenSet<int>[regions.Length];
			for (var i = 0; i < regions.Length; i++)
				chokepoints[i] = crossings[i].Select((count, j) => new { Count = count, Index = j })
					.Where(kv => kv.Count > 0 && kv.Count <= chokepointMaxWidth)
					.Select(kv => kv.Index)
					.ToFrozenSet();

			return (adjacency.Select(set => set.ToList()).ToArray(), chokepoints);
		}

		static void Link(CoalitionRegion[] regions, HashSet<int>[] adjacency, int[][] crossings, CPos a, CPos b)
		{
			var ra = RegionOf(regions, a);
			var rb = RegionOf(regions, b);
			if (ra == rb)
				return;

			adjacency[ra].Add(rb);
			adjacency[rb].Add(ra);
			crossings[ra][rb]++;
			crossings[rb][ra]++;
		}

		/// <summary>Connected components of the region graph (flood fill over adjacency).</summary>
		public static (int[] Components, int Count) ConnectedComponents(List<int>[] adjacency)
		{
			var components = new int[adjacency.Length];
			Array.Fill(components, -1);
			var count = 0;
			for (var start = 0; start < adjacency.Length; start++)
			{
				if (components[start] != -1)
					continue;

				var stack = new Stack<int>();
				stack.Push(start);
				components[start] = count;
				while (stack.Count > 0)
				{
					var region = stack.Pop();
					foreach (var next in adjacency[region])
						if (components[next] == -1)
						{
							components[next] = count;
							stack.Push(next);
						}
				}

				count++;
			}

			return (components, count);
		}

		static int RegionOf(CoalitionRegion[] regions, CPos cell)
		{
			foreach (var region in regions)
				if (region.Bounds.Contains(cell.X, cell.Y))
					return region.Index;

			return 0;
		}

		// ------------------------------------------------------------------------------------
		// World-backed builder. The result holds no World references and is cached per map.
		// ------------------------------------------------------------------------------------
		static readonly Dictionary<string, CoalitionMapAnalysis> Cache = [];

		public static CoalitionMapAnalysis ForMap(World world, FrozenSet<string> waterTerrainTypes,
			FrozenSet<string> valuableResourceTypes)
		{
			var map = world.Map;
			var key = map.Uid;
			if (Cache.TryGetValue(key, out var cached))
				return cached;

			// The same 4x4 partition as the blackboard, so region indices match.
			var width = map.MapSize.Width;
			var height = map.MapSize.Height;
			var cols = Math.Min(4, Math.Max(1, width / 16));
			var rows = Math.Min(4, Math.Max(1, height / 16));
			var regions = new List<CoalitionRegion>();
			for (var r = 0; r < rows; r++)
				for (var c = 0; c < cols; c++)
				{
					var x0 = width * c / cols;
					var y0 = height * r / rows;
					var x1 = width * (c + 1) / cols;
					var y1 = height * (r + 1) / rows;
					regions.Add(new CoalitionRegion(regions.Count, Rectangle.FromLTRB(x0, y0, x1, y1)));
				}

			var regionArray = regions.ToArray();
			var groundLocomotor = FindLocomotor(world, "tracked") ?? FindLocomotor(world, "wheeled") ?? FindLocomotor(world, "foot");
			var navalLocomotor = FindLocomotor(world, "naval") ?? FindLocomotor(world, "lcraft");
			var footLocomotor = FindLocomotor(world, "foot");

			bool TerrainIsWater(int x, int y)
			{
				var cell = new CPos(x, y);
				return map.Contains(cell) && waterTerrainTypes.Contains(map.GetTerrainInfo(cell).Type);
			}

			bool GroundPassable(int x, int y)
			{
				if (groundLocomotor == null)
					return !TerrainIsWater(x, y);

				var cell = new CPos(x, y);
				return map.Contains(cell) && groundLocomotor.MovementCostForCell(cell) != PathGraph.MovementCostForUnreachableCell;
			}

			bool FootPassable(int x, int y)
			{
				// Infantry can cross terrain vehicles cannot. Without a distinct foot locomotor the
				// foot graph collapses to the ground graph (no terrain difference on this map).
				if (footLocomotor == null || footLocomotor == groundLocomotor)
					return GroundPassable(x, y);

				var cell = new CPos(x, y);
				return map.Contains(cell) && footLocomotor.MovementCostForCell(cell) != PathGraph.MovementCostForUnreachableCell;
			}

			bool NavalPassable(int x, int y)
			{
				if (navalLocomotor == null)
					return TerrainIsWater(x, y);

				var cell = new CPos(x, y);
				return map.Contains(cell) && navalLocomotor.MovementCostForCell(cell) != PathGraph.MovementCostForUnreachableCell;
			}

			var groundPassable = ComputePassability(width, height, GroundPassable);
			var footPassable = ComputePassability(width, height, FootPassable);
			var navalPassable = ComputePassability(width, height, NavalPassable);
			var airPassable = ComputePassability(width, height, (x, y) => true);

			var groundGraph = BuildRegionGraph(width, height, groundPassable, regionArray);
			var footGraph = BuildRegionGraph(width, height, footPassable, regionArray);
			var navalGraph = BuildRegionGraph(width, height, navalPassable, regionArray);
			var airGraph = BuildRegionGraph(width, height, airPassable, regionArray);

			var groundComponents = ConnectedComponents(groundGraph.Adjacency);
			var footComponents = ConnectedComponents(footGraph.Adjacency);
			var navalComponents = ConnectedComponents(navalGraph.Adjacency);
			var airComponents = ConnectedComponents(airGraph.Adjacency);

			var adjacency = new[] { groundGraph.Adjacency, navalGraph.Adjacency, airGraph.Adjacency, footGraph.Adjacency };
			var chokepoints = new[] { groundGraph.Chokepoints, navalGraph.Chokepoints, airGraph.Chokepoints, footGraph.Chokepoints };
			var components = new[] { groundComponents.Components, navalComponents.Components, airComponents.Components, footComponents.Components };
			var componentCount = new[] { groundComponents.Count, navalComponents.Count, airComponents.Count, footComponents.Count };

			// Bridges and rivers are distinct terrain features. Treating river cells as bridges would
			// incorrectly make an impassable river look like a fixed crossing to the route planner.
			var bridgeCells = new HashSet<CPos>();
			var riverCells = new HashSet<CPos>();
			for (var y = 0; y < height; y++)
				for (var x = 0; x < width; x++)
				{
					var cell = new CPos(x, y);
					if (!map.Contains(cell))
						continue;

					var type = map.GetTerrainInfo(cell).Type;
					if (type == "Bridge")
						bridgeCells.Add(cell);
					else if (type == "River")
						riverCells.Add(cell);
				}

			// Per-region valuable resource cells and defensibility.
			var resourceLayer = world.WorldActor.TraitOrDefault<IResourceLayer>();
			var resourceCells = new int[regionArray.Length];
			var groundCells = new int[regionArray.Length];
			for (var y = 0; y < height; y++)
				for (var x = 0; x < width; x++)
				{
					var cell = new CPos(x, y);
					if (!map.Contains(cell))
						continue;

					var region = RegionOf(regionArray, cell);
					if (groundPassable[y * width + x])
						groundCells[region]++;

					if (resourceLayer == null)
						continue;

					var resource = resourceLayer.GetResource(cell);
					if (resource.Type != null && valuableResourceTypes.Contains(resource.Type))
						resourceCells[region]++;
				}

			var maxResources = resourceCells.DefaultIfEmpty(0).Max();
			var resourceRichness = resourceCells.Select(c => maxResources == 0 ? 0f : c * 1f / maxResources).ToArray();
			var expansionValue = ComputeExpansionValue(regionArray, groundCells, resourceRichness);

			// Defensibility: land fraction of the region, discounted by how many other regions it
			// borders on the ground (more entries = harder to hold).
			var defensibility = new float[regionArray.Length];
			for (var i = 0; i < regionArray.Length; i++)
			{
				var landFraction = 0f;
				if (regionArray[i].Bounds.Width > 0 && regionArray[i].Bounds.Height > 0)
					landFraction = groundCells[i] * 1f / (regionArray[i].Bounds.Width * regionArray[i].Bounds.Height);

				var entries = groundGraph.Adjacency[i].Count;
				defensibility[i] = landFraction / (1 + entries);
			}

			var analysis = new CoalitionMapAnalysis(regionArray, adjacency, chokepoints, components,
				componentCount, bridgeCells, width, height, resourceCells, resourceRichness, defensibility,
				groundCells, expansionValue,
				ComputeBridgeConnections(regionArray, bridgeCells, groundPassable, width, height),
				ComputeRallyValue(regionArray, defensibility, groundCells),
				ComputeArtilleryValue(regionArray, defensibility, groundGraph.Chokepoints), riverCells);
			Cache[key] = analysis;
			return analysis;
		}

		/// <summary>
		/// Scores each region's value as an expansion site: how much of it is buildable land, weighted
		/// by how rich in resources it is. Pure so it can be unit-tested without a World.
		/// </summary>
		public static float[] ComputeExpansionValue(CoalitionRegion[] regions, int[] buildableCells, float[] resourceRichness)
		{
			var value = new float[regions.Length];
			for (var i = 0; i < regions.Length; i++)
			{
				var area = regions[i].Bounds.Width * regions[i].Bounds.Height;
				var buildableFraction = area == 0 ? 0f : buildableCells[i] * 1f / area;
				value[i] = buildableFraction * (1f + resourceRichness[i]);
			}

			return value;
		}

		/// <summary>Region pairs whose shared border is crossed by a bridge cell, for route weighting.</summary>
		public static FrozenSet<int>[] ComputeBridgeConnections(CoalitionRegion[] regions, HashSet<CPos> bridgeCells,
			bool[] passable, int width, int height)
		{
			var connections = regions.Select(_ => new HashSet<int>()).ToArray();
			foreach (var cell in bridgeCells)
			{
				var region = RegionOf(regions, cell);
				foreach (var neighbor in new[]
				{
					new CPos(cell.X + 1, cell.Y), new CPos(cell.X - 1, cell.Y),
					new CPos(cell.X, cell.Y + 1), new CPos(cell.X, cell.Y - 1)
				})
				{
					if (neighbor.X < 0 || neighbor.Y < 0 || neighbor.X >= width || neighbor.Y >= height)
						continue;
					if (!passable[neighbor.Y * width + neighbor.X])
						continue;

					var other = RegionOf(regions, neighbor);
					if (other != region)
					{
						connections[region].Add(other);
						connections[other].Add(region);
					}
				}
			}

			return connections.Select(h => h.ToFrozenSet()).ToArray();
		}

		/// <summary>Rally/staging value: defensible open ground to mass forces safely.</summary>
		public static float[] ComputeRallyValue(CoalitionRegion[] regions, float[] defensibility, int[] buildableCells)
		{
			var value = new float[regions.Length];
			for (var i = 0; i < regions.Length; i++)
			{
				var area = regions[i].Bounds.Width * regions[i].Bounds.Height;
				var buildableFraction = area == 0 ? 0f : buildableCells[i] * 1f / area;
				value[i] = defensibility[i] * buildableFraction;
			}

			return value;
		}

		/// <summary>Artillery value: defensible ground that overlooks chokepoint approach corridors.</summary>
		public static float[] ComputeArtilleryValue(CoalitionRegion[] regions, float[] defensibility, FrozenSet<int>[] groundChokepoints)
		{
			var value = new float[regions.Length];
			var maxExits = groundChokepoints.Max(c => c.Count);
			for (var i = 0; i < regions.Length; i++)
			{
				var exits = maxExits == 0 ? 0f : groundChokepoints[i].Count * 1f / maxExits;
				value[i] = defensibility[i] * (0.5f + 0.5f * exits);
			}

			return value;
		}

		/// <summary>
		/// Scores regions as transport insertion zones: buildable rear-area land near the enemy,
		/// discounted near our own base. Home/enemy regions are dynamic, so this is computed on demand.
		/// </summary>
		public float[] InsertionValue(int homeRegion, int enemyRegion)
		{
			var value = new float[Regions.Length];
			for (var i = 0; i < Regions.Length; i++)
			{
				var area = Regions[i].Bounds.Width * Regions[i].Bounds.Height;
				var buildableFraction = area == 0 ? 0f : BuildableCells[i] * 1f / area;
				var nearEnemy = i == enemyRegion || IsAdjacent(MovementClass.Ground, i, enemyRegion);
				var nearHome = i == homeRegion || IsAdjacent(MovementClass.Ground, i, homeRegion);
				value[i] = buildableFraction * (nearEnemy ? 1f : 0.5f) * (nearHome ? 0.25f : 1f);
			}

			return value;
		}

		/// <summary>
		/// Describes a planned region path as a corridor: labels each step open, chokepoint, or bridge,
		/// so attack and retreat corridors are identifiable as their chokepoint/bridge structure.
		/// </summary>
		public static (int[] Regions, string[] Features) DescribeCorridor(CoalitionMapAnalysis map, int[] routeRegions,
			MovementClass movementClass)
		{
			var features = new List<string>();
			for (var i = 1; i < routeRegions.Length; i++)
			{
				var a = routeRegions[i - 1];
				var b = routeRegions[i];
				if (map.IsChokepoint(movementClass, a, b))
					features.Add($"chokepoint:{a}-{b}");
				else if (map.BridgeConnections[a].Contains(b))
					features.Add($"bridge:{a}-{b}");
				else
					features.Add($"open:{a}-{b}");
			}

			return (routeRegions, features.ToArray());
		}

		static Locomotor FindLocomotor(World world, string name)
		{
			return world.WorldActor.TraitsImplementing<Locomotor>()
				.FirstOrDefault(l => string.Equals(l.Info.Name, name, StringComparison.OrdinalIgnoreCase));
		}
	}
}
