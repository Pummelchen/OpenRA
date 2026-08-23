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
	/// The commander's mental map: the playable area cut into a few dozen regions joined by
	/// chokepoints of known width.
	/// </para>
	/// <para>
	/// Tile-level reasoning is far too fine to search over - a 128x128 map is 16,384 cells and a
	/// two-minute lookahead over them is hopeless. This decomposition reduces the same terrain to
	/// 20-40 nodes, which is small enough to plan on and coarse enough that the plan still means
	/// something. It is built by watershed segmentation of the distance transform: flood the open
	/// ground from its most open points downhill, and wherever two floods meet is a narrow place -
	/// which is the definition of a chokepoint.
	/// </para>
	/// <para>
	/// The graph is what makes terrain doctrine derivable instead of hand-written. A map whose
	/// region graph has one path between the bases is a map where feints are worthless and siege is
	/// everything; a map with four is one where the main force should never be the only force.
	/// Water is not a special case to detect - it is simply cells the ground locomotor cannot
	/// enter, so a naval question becomes a reachability query on a graph built with a different
	/// passability predicate.
	/// </para>
	/// </summary>
	public sealed class RegionGraph
	{
		/// <summary>Marks a cell no unit of this movement class can enter.</summary>
		public const int Impassable = -1;

		const int Unlabelled = -2;
		const int Watershed = -3;

		static readonly int[] NeighbourX = [-1, 0, 1, -1, 1, -1, 0, 1];
		static readonly int[] NeighbourY = [-1, -1, -1, 0, 0, 1, 1, 1];

		/// <summary>Tuning for the decomposition. The defaults suit 96x96 to 160x160 maps.</summary>
		public readonly record struct Settings(int SeedDistance, int MinRegionCells, int MaxChokeCells)
		{
			/// <summary>
			/// A new region may only be seeded at a cell at least this far from any obstruction, in
			/// the scaled units <see cref="DistanceTransform"/> returns. The default of 12 is four
			/// cells of clearance: below that you are in a corridor, not an area.
			/// </summary>
			public static readonly Settings Default = new(12, 48, 16);

			/// <summary>Fills in zeroed fields, so a partially specified Settings is still usable.</summary>
			public Settings OrDefault()
			{
				return new Settings(
					SeedDistance > 0 ? SeedDistance : Default.SeedDistance,
					MinRegionCells > 0 ? MinRegionCells : Default.MinRegionCells,
					MaxChokeCells > 0 ? MaxChokeCells : Default.MaxChokeCells);
			}
		}

		/// <summary>An area of open ground.</summary>
		public sealed class Region
		{
			public int Id { get; init; }
			public int CellCount { get; init; }

			/// <summary>Centre of mass, in cells. Where "move to this region" actually means.</summary>
			public int CentreX { get; init; }
			public int CentreY { get; init; }

			/// <summary>Distance-transform value at the most open cell: how much room there is here.</summary>
			public int Openness { get; init; }

			/// <summary>Indices into <see cref="RegionGraph.Chokepoints"/>.</summary>
			public int[] Chokepoints { get; init; } = [];
		}

		/// <summary>A narrow place joining two regions.</summary>
		public sealed class Chokepoint
		{
			public int Id { get; init; }
			public int RegionA { get; init; }
			public int RegionB { get; init; }

			/// <summary>
			/// How many units can pass abreast, in cells. This is the edge capacity that
			/// <see cref="MaxFlow"/> reasons about, and the reason a min-cut here corresponds to a
			/// defensible line rather than an arbitrary graph partition.
			/// </summary>
			public int Capacity { get; init; }

			public int CentreX { get; init; }
			public int CentreY { get; init; }

			/// <summary>The region on the other side of this choke from <paramref name="region"/>.</summary>
			public int Other(int region) => region == RegionA ? RegionB : RegionA;
		}

		public int Width { get; }
		public int Height { get; }

		/// <summary>Region id per cell, or <see cref="Impassable"/>. Every passable cell has a region.</summary>
		public int[] Labels { get; }

		/// <summary>The distance transform the decomposition was built from.</summary>
		public int[] Distance { get; }

		public Region[] Regions { get; }
		public Chokepoint[] Chokepoints { get; }

		RegionGraph(int width, int height, int[] labels, int[] distance, Region[] regions, Chokepoint[] chokepoints)
		{
			Width = width;
			Height = height;
			Labels = labels;
			Distance = distance;
			Regions = regions;
			Chokepoints = chokepoints;
		}

		/// <summary>The region containing a cell, or <see cref="Impassable"/>.</summary>
		public int RegionAt(int x, int y)
		{
			if (x < 0 || y < 0 || x >= Width || y >= Height)
				return Impassable;

			return Labels[(y * Width) + x];
		}

		/// <summary>The regions reachable from this one in a single step.</summary>
		public IEnumerable<int> Neighbours(int region)
		{
			if (region < 0 || region >= Regions.Length)
				yield break;

			foreach (var c in Regions[region].Chokepoints)
				yield return Chokepoints[c].Other(region);
		}

		/// <summary>
		/// The narrowest set of chokepoints separating two regions, and the rate at which units can
		/// cross between them. This is the defensive line, and the bound on how fast an attack from
		/// <paramref name="from"/> can arrive at <paramref name="to"/>.
		/// </summary>
		public MaxFlow.Result MinCutBetween(int from, int to)
		{
			var edges = new MaxFlow.Edge[Chokepoints.Length];
			for (var i = 0; i < Chokepoints.Length; i++)
				edges[i] = new MaxFlow.Edge(Chokepoints[i].RegionA, Chokepoints[i].RegionB, Chokepoints[i].Capacity);

			return MaxFlow.MinCut(Regions.Length, edges, from, to);
		}

		/// <summary>
		/// Decomposes a passability grid. <paramref name="passable"/> defines the movement class:
		/// pass a ground predicate for the army's map, a water predicate for the navy's.
		/// </summary>
		public static RegionGraph Build(int width, int height, Func<int, int, bool> passable, Settings settings = default)
		{
			ArgumentNullException.ThrowIfNull(passable);
			settings = settings.OrDefault();

			if (width <= 0 || height <= 0)
				return new RegionGraph(Math.Max(0, width), Math.Max(0, height), [], [], [], []);

			var cells = width * height;
			var distance = DistanceTransform.Compute(width, height, passable);

			var labels = new int[cells];
			for (var i = 0; i < cells; i++)
				labels[i] = distance[i] == 0 ? Impassable : Unlabelled;

			var regionCount = Flood(width, height, distance, labels, settings.SeedDistance);
			FillUnlabelled(width, height, labels);
			AbsorbWatershedCells(width, height, labels);

			// Watershed is deliberately over-segmented at this point; the merge pass below is where
			// that is paid back, and it is the step that decides what the commander thinks a region
			// is.
			regionCount = MergeUntilStable(width, height, labels, regionCount, settings);

			var boundaries = Boundaries(width, height, labels, regionCount);
			var chokepoints = BuildChokepoints(boundaries);
			var regions = BuildRegions(width, height, labels, distance, regionCount, chokepoints);
			return new RegionGraph(width, height, labels, distance, regions, chokepoints);
		}

		/// <summary>The shared frontier between two regions.</summary>
		sealed class Boundary
		{
			public int RegionA;
			public int RegionB;

			/// <summary>Cells of A that touch B, and cells of B that touch A.</summary>
			public int FrontageA;
			public int FrontageB;

			public long SumX;
			public long SumY;
			public int CellCount;

			/// <summary>
			/// How many units can pass abreast. The narrower of the two frontages: a passage is only
			/// as wide as its tightest side, and taking the minimum keeps the measure symmetric.
			/// </summary>
			public int Capacity => Math.Min(FrontageA, FrontageB);
		}

		/// <summary>
		/// Every pair of regions that touch, with the width of their shared frontier. Runs on a
		/// total labelling, so "touch" means cell-adjacent rather than mediated by watershed cells.
		/// </summary>
		static Dictionary<(int, int), Boundary> Boundaries(int width, int height, int[] labels, int regionCount)
		{
			var boundaries = new Dictionary<(int, int), Boundary>();
			var touchedByThisCell = new List<int>(8);

			for (var y = 0; y < height; y++)
			{
				for (var x = 0; x < width; x++)
				{
					var label = labels[(y * width) + x];
					if (label < 0 || label >= regionCount)
						continue;

					touchedByThisCell.Clear();
					for (var n = 0; n < 8; n++)
					{
						var nx = x + NeighbourX[n];
						var ny = y + NeighbourY[n];
						if (nx < 0 || ny < 0 || nx >= width || ny >= height)
							continue;

						var other = labels[(ny * width) + nx];
						if (other < 0 || other >= regionCount || other == label || touchedByThisCell.Contains(other))
							continue;

						touchedByThisCell.Add(other);
					}

					// Count this cell once per neighbouring region, so frontage measures the width
					// of the frontier rather than how ragged its edge happens to be.
					foreach (var other in touchedByThisCell)
					{
						var key = label < other ? (label, other) : (other, label);
						if (!boundaries.TryGetValue(key, out var boundary))
						{
							boundary = new Boundary { RegionA = key.Item1, RegionB = key.Item2 };
							boundaries[key] = boundary;
						}

						if (label == boundary.RegionA)
							boundary.FrontageA++;
						else
							boundary.FrontageB++;

						boundary.SumX += x;
						boundary.SumY += y;
						boundary.CellCount++;
					}
				}
			}

			return boundaries;
		}

		/// <summary>
		/// <para>
		/// Repeatedly merges regions that should not have been separated, until nothing changes.
		/// </para>
		/// <para>
		/// Two criteria, and the first is the one that matters. A frontier wider than
		/// <see cref="Settings.MaxChokeCells"/> is <b>not a chokepoint</b> - it is a line drawn
		/// across open ground, and the areas on either side of it are one area. Watershed
		/// segmentation produces these constantly: a rectangular room has a ridge running down its
		/// middle, and the flood arrives at that ridge from both ends, splitting a single field into
		/// halves that no commander would ever treat as different places. Without this test a plain
		/// three-room map decomposes into five regions joined by ten "chokepoints" of thirty-cell
		/// width, which is worse than useless - it is confidently wrong.
		/// </para>
		/// <para>
		/// The second criterion folds away regions too small to be worth planning about, into
		/// whichever neighbour they share the most frontier with.
		/// </para>
		/// <para>
		/// Iterating matters: merging two halves of a room can produce a region whose frontier with
		/// a third is now wide, and that must be caught in turn.
		/// </para>
		/// </summary>
		static int MergeUntilStable(int width, int height, int[] labels, int regionCount, Settings settings)
		{
			// Each round strictly reduces the region count, so this cannot spin; the bound is a
			// backstop against a future change breaking that property.
			for (var round = 0; round < 64 && regionCount > 1; round++)
			{
				var boundaries = Boundaries(width, height, labels, regionCount);

				var size = new int[regionCount];
				foreach (var label in labels)
					if (label >= 0 && label < regionCount)
						size[label]++;

				var parent = new int[regionCount];
				for (var i = 0; i < regionCount; i++)
					parent[i] = i;

				int Find(int x)
				{
					while (parent[x] != x)
						x = parent[x] = parent[parent[x]];
					return x;
				}

				void Union(int a, int b)
				{
					var ra = Find(a);
					var rb = Find(b);
					if (ra == rb)
						return;

					// Lower id wins so the outcome does not depend on iteration order.
					if (ra < rb)
						parent[rb] = ra;
					else
						parent[ra] = rb;
				}

				var merged = false;

				// Deterministic order: dictionary enumeration order is not guaranteed, and the whole
				// decomposition has to be reproducible.
				var keys = new List<(int, int)>(boundaries.Keys);
				keys.Sort((p, q) => p.Item1 != q.Item1 ? p.Item1.CompareTo(q.Item1) : p.Item2.CompareTo(q.Item2));

				foreach (var key in keys)
				{
					if (boundaries[key].Capacity > settings.MaxChokeCells)
					{
						Union(key.Item1, key.Item2);
						merged = true;
					}
				}

				if (!merged)
				{
					// Only fold small regions once the frontier test has settled, so a region is not
					// absorbed on account of a size it was about to gain by merging anyway.
					var bySize = new List<int>();
					for (var i = 0; i < regionCount; i++)
						bySize.Add(i);
					bySize.Sort((a, b) => size[a] != size[b] ? size[a].CompareTo(size[b]) : a.CompareTo(b));

					foreach (var region in bySize)
					{
						if (size[Find(region)] >= settings.MinRegionCells)
							continue;

						var best = -1;
						var bestFrontage = 0;
						foreach (var key in keys)
						{
							var boundary = boundaries[key];
							var ra = Find(boundary.RegionA);
							var rb = Find(boundary.RegionB);
							if (ra == rb)
								continue;

							int other;
							if (ra == Find(region))
								other = rb;
							else if (rb == Find(region))
								other = ra;
							else
								continue;

							var frontage = boundary.Capacity;
							if (frontage > bestFrontage || (frontage == bestFrontage && (best == -1 || other < best)))
							{
								best = other;
								bestFrontage = frontage;
							}
						}

						if (best == -1)
							continue;

						var absorbed = size[Find(region)];
						Union(region, best);
						size[Find(region)] = size[best] + absorbed;
						merged = true;
					}
				}

				if (!merged)
					break;

				regionCount = Compact(labels, parent, regionCount, Find);
			}

			return regionCount;
		}

		/// <summary>Renumbers merged labels down to a contiguous 0..n-1 range.</summary>
		static int Compact(int[] labels, int[] parent, int regionCount, Func<int, int> find)
		{
			var remap = new int[regionCount];
			Array.Fill(remap, -1);
			var next = 0;
			for (var i = 0; i < regionCount; i++)
			{
				var root = find(i);
				if (remap[root] == -1)
					remap[root] = next++;

				remap[i] = remap[root];
			}

			for (var i = 0; i < labels.Length; i++)
				if (labels[i] >= 0 && labels[i] < regionCount)
					labels[i] = remap[labels[i]];

			return next;
		}

		/// <summary>One chokepoint per pair of regions that touch, ordered for reproducibility.</summary>
		static Chokepoint[] BuildChokepoints(Dictionary<(int, int), Boundary> boundaries)
		{
			var keys = new List<(int, int)>(boundaries.Keys);
			keys.Sort((p, q) => p.Item1 != q.Item1 ? p.Item1.CompareTo(q.Item1) : p.Item2.CompareTo(q.Item2));

			var chokepoints = new List<Chokepoint>();
			foreach (var key in keys)
			{
				var boundary = boundaries[key];
				if (boundary.Capacity <= 0 || boundary.CellCount == 0)
					continue;

				chokepoints.Add(new Chokepoint
				{
					Id = chokepoints.Count,
					RegionA = boundary.RegionA,
					RegionB = boundary.RegionB,
					Capacity = boundary.Capacity,
					CentreX = (int)(boundary.SumX / boundary.CellCount),
					CentreY = (int)(boundary.SumY / boundary.CellCount),
				});
			}

			return chokepoints.ToArray();
		}

		/// <summary>
		/// Watershed by descending flood. Cells are visited from the most open to the least; each
		/// joins the single region already touching it, seeds a new one if it is open enough to be
		/// an area in its own right, and becomes a watershed cell if two different regions have
		/// arrived from opposite sides - which is exactly the narrow place between them.
		/// </summary>
		static int Flood(int width, int height, int[] distance, int[] labels, int seedDistance)
		{
			var order = OrderByDistanceDescending(width, height, distance);
			var regionCount = 0;
			var adjacent = new int[8];

			foreach (var i in order)
			{
				if (labels[i] != Unlabelled)
					continue;

				var x = i % width;
				var y = i / width;
				var distinct = 0;

				for (var n = 0; n < 8; n++)
				{
					var nx = x + NeighbourX[n];
					var ny = y + NeighbourY[n];
					if (nx < 0 || ny < 0 || nx >= width || ny >= height)
						continue;

					var label = labels[(ny * width) + nx];
					if (label < 0)
						continue;

					var seen = false;
					for (var k = 0; k < distinct; k++)
						if (adjacent[k] == label)
						{
							seen = true;
							break;
						}

					if (!seen)
						adjacent[distinct++] = label;
				}

				if (distinct == 0)
				{
					// Only genuinely open ground may start a region. Without this every dead-end
					// crevice becomes its own region and the graph is useless.
					if (distance[i] >= seedDistance)
						labels[i] = regionCount++;
				}
				else if (distinct == 1)
					labels[i] = adjacent[0];
				else
					labels[i] = Watershed;
			}

			return regionCount;
		}

		/// <summary>
		/// Counting sort of cell indices by descending distance. Deterministic - cells of equal
		/// distance keep their index order - which matters because the whole decomposition, and so
		/// every plan built on it, must be reproducible from a seed.
		/// </summary>
		static int[] OrderByDistanceDescending(int width, int height, int[] distance)
		{
			var cells = width * height;
			var max = 0;
			for (var i = 0; i < cells; i++)
				if (distance[i] > max)
					max = distance[i];

			var counts = new int[max + 2];
			for (var i = 0; i < cells; i++)
				counts[distance[i]]++;

			// Prefix sums from the top so the highest distance lands first.
			var offset = 0;
			var start = new int[max + 2];
			for (var d = max; d >= 0; d--)
			{
				start[d] = offset;
				offset += counts[d];
			}

			var order = new int[cells];
			var cursor = new int[max + 2];
			for (var i = 0; i < cells; i++)
			{
				var d = distance[i];
				order[start[d] + cursor[d]] = i;
				cursor[d]++;
			}

			return order;
		}

		/// <summary>
		/// Cells too narrow to seed a region and never reached by one - the insides of corridors -
		/// join the nearest region by breadth-first growth. Anything still stranded becomes its own
		/// region, which the merge step then folds into a neighbour.
		/// </summary>
		static void FillUnlabelled(int width, int height, int[] labels)
		{
			var cells = width * height;
			var queue = new Queue<int>();
			for (var i = 0; i < cells; i++)
				if (labels[i] >= 0)
					queue.Enqueue(i);

			while (queue.Count > 0)
			{
				var i = queue.Dequeue();
				var label = labels[i];
				var x = i % width;
				var y = i / width;

				for (var n = 0; n < 8; n++)
				{
					var nx = x + NeighbourX[n];
					var ny = y + NeighbourY[n];
					if (nx < 0 || ny < 0 || nx >= width || ny >= height)
						continue;

					var j = (ny * width) + nx;
					if (labels[j] != Unlabelled)
						continue;

					labels[j] = label;
					queue.Enqueue(j);
				}
			}

			// Pockets enclosed entirely by watershed cells. Rare, but they must not stay unlabelled
			// or later passes would treat them as impassable.
			for (var i = 0; i < cells; i++)
				if (labels[i] == Unlabelled)
					labels[i] = Watershed;
		}

		/// <summary>
		/// Gives every watershed cell to a neighbouring region so that the labelling is total: after
		/// this, every passable cell belongs to exactly one region. Callers that ask "which region
		/// is this unit in" must always get an answer.
		/// </summary>
		static void AbsorbWatershedCells(int width, int height, int[] labels)
		{
			var cells = width * height;
			var queue = new Queue<int>();
			for (var i = 0; i < cells; i++)
				if (labels[i] >= 0)
					queue.Enqueue(i);

			while (queue.Count > 0)
			{
				var i = queue.Dequeue();
				var label = labels[i];
				var x = i % width;
				var y = i / width;

				for (var n = 0; n < 8; n++)
				{
					var nx = x + NeighbourX[n];
					var ny = y + NeighbourY[n];
					if (nx < 0 || ny < 0 || nx >= width || ny >= height)
						continue;

					var j = (ny * width) + nx;
					if (labels[j] != Watershed)
						continue;

					labels[j] = label;
					queue.Enqueue(j);
				}
			}

			// A map with no seeded region at all leaves watershed cells with nowhere to go.
			for (var i = 0; i < cells; i++)
				if (labels[i] == Watershed)
					labels[i] = Impassable;
		}

		static Region[] BuildRegions(int width, int height, int[] labels, int[] distance, int regionCount,
			Chokepoint[] chokepoints)
		{
			var count = new int[regionCount];
			var sumX = new long[regionCount];
			var sumY = new long[regionCount];
			var openness = new int[regionCount];

			for (var i = 0; i < labels.Length; i++)
			{
				var label = labels[i];
				if (label < 0 || label >= regionCount)
					continue;

				count[label]++;
				sumX[label] += i % width;
				sumY[label] += i / width;
				if (distance[i] > openness[label])
					openness[label] = distance[i];
			}

			var attached = new List<int>[regionCount];
			for (var i = 0; i < regionCount; i++)
				attached[i] = [];

			foreach (var choke in chokepoints)
			{
				if (choke.RegionA >= 0 && choke.RegionA < regionCount)
					attached[choke.RegionA].Add(choke.Id);

				if (choke.RegionB >= 0 && choke.RegionB < regionCount)
					attached[choke.RegionB].Add(choke.Id);
			}

			var regions = new Region[regionCount];
			for (var i = 0; i < regionCount; i++)
			{
				regions[i] = new Region
				{
					Id = i,
					CellCount = count[i],
					CentreX = count[i] == 0 ? 0 : (int)(sumX[i] / count[i]),
					CentreY = count[i] == 0 ? 0 : (int)(sumY[i] / count[i]),
					Openness = openness[i],
					Chokepoints = attached[i].ToArray(),
				};
			}

			return regions;
		}
	}
}
