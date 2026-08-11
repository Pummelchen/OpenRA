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
		Air
	}

	/// <summary>
	/// Static terrain analysis of a map: the region adjacency graph per movement class,
	/// chokepoints (narrow region crossings), connected components (continents/islands and
	/// sea bodies), bridges, and per-region resource/defensibility scores.
	///
	/// Everything here is terrain-derived and shroud-independent, so the analysis is computed
	/// once per map and cached; dynamic state (threats, control) lives in the blackboard.
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

		/// <summary>Valuable resource cells per region.</summary>
		public readonly int[] ResourceCells;

		/// <summary>Resource richness per region, normalized to 0..1.</summary>
		public readonly float[] ResourceRichness;

		/// <summary>Defensibility per region, 0..1 (land fraction weighted against border crossings).</summary>
		public readonly float[] Defensibility;

		/// <summary>
		/// Builds an analysis from precomputed graph data. Production code should use
		/// <see cref="ForMap"/>; this constructor is also used by unit tests to exercise the
		/// pure graph algorithms with synthetic maps.
		/// </summary>
		public CoalitionMapAnalysis(CoalitionRegion[] regions, List<int>[][] adjacency, FrozenSet<int>[][] chokepoints,
			int[][] components, int[] componentCount, HashSet<CPos> bridgeCells, int width, int height,
			int[] resourceCells, float[] resourceRichness, float[] defensibility)
		{
			Regions = regions;
			Adjacency = adjacency;
			Chokepoints = chokepoints;
			Components = components;
			ComponentCount = componentCount;
			BridgeCells = bridgeCells;
			Width = width;
			Height = height;
			ResourceCells = resourceCells;
			ResourceRichness = resourceRichness;
			Defensibility = defensibility;
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

			bool NavalPassable(int x, int y)
			{
				if (navalLocomotor == null)
					return TerrainIsWater(x, y);

				var cell = new CPos(x, y);
				return map.Contains(cell) && navalLocomotor.MovementCostForCell(cell) != PathGraph.MovementCostForUnreachableCell;
			}

			var groundPassable = ComputePassability(width, height, GroundPassable);
			var navalPassable = ComputePassability(width, height, NavalPassable);
			var airPassable = ComputePassability(width, height, (x, y) => true);

			var groundGraph = BuildRegionGraph(width, height, groundPassable, regionArray);
			var navalGraph = BuildRegionGraph(width, height, navalPassable, regionArray);
			var airGraph = BuildRegionGraph(width, height, airPassable, regionArray);

			var groundComponents = ConnectedComponents(groundGraph.Adjacency);
			var navalComponents = ConnectedComponents(navalGraph.Adjacency);
			var airComponents = ConnectedComponents(airGraph.Adjacency);

			var adjacency = new[] { groundGraph.Adjacency, navalGraph.Adjacency, airGraph.Adjacency };
			var chokepoints = new[] { groundGraph.Chokepoints, navalGraph.Chokepoints, airGraph.Chokepoints };
			var components = new[] { groundComponents.Components, navalComponents.Components, airComponents.Components };
			var componentCount = new[] { groundComponents.Count, navalComponents.Count, airComponents.Count };

			// Bridges: terrain cells whose type is a bridge (fixed crossings between land masses).
			var bridgeCells = new HashSet<CPos>();
			for (var y = 0; y < height; y++)
				for (var x = 0; x < width; x++)
				{
					var cell = new CPos(x, y);
					if (!map.Contains(cell))
						continue;

					var type = map.GetTerrainInfo(cell).Type;
					if (type == "Bridge" || type == "River")
						bridgeCells.Add(cell);
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
				componentCount, bridgeCells, width, height, resourceCells, resourceRichness, defensibility);
			Cache[key] = analysis;
			return analysis;
		}

		static Locomotor FindLocomotor(World world, string name)
		{
			return world.WorldActor.TraitsImplementing<Locomotor>()
				.FirstOrDefault(l => string.Equals(l.Info.Name, name, StringComparison.OrdinalIgnoreCase));
		}
	}
}
