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

namespace OpenRA.Mods.Common.Commander.Terrain
{
	/// <summary>
	/// <para>
	/// How far every passable cell is from the nearest obstruction - the "how open is it here"
	/// field that the region decomposition is built on.
	/// </para>
	/// <para>
	/// Computed with the chamfer 3-4 approximation: two sweeps over the grid, taking an orthogonal
	/// step as 3 and a diagonal step as 4. That ratio approximates Euclidean distance to within
	/// about 2%, which is far tighter than the region decomposition needs, and it costs two linear
	/// passes rather than the priority queue an exact transform would want.
	/// </para>
	/// </summary>
	public static class DistanceTransform
	{
		/// <summary>One orthogonal step, in the scaled units this transform returns.</summary>
		public const int Orthogonal = 3;

		/// <summary>One diagonal step, in the scaled units this transform returns.</summary>
		public const int Diagonal = 4;

		/// <summary>
		/// Distance from each cell to the nearest impassable cell, in units of <see cref="Orthogonal"/>
		/// per cell. Impassable cells are 0. Everything outside the grid counts as impassable, so a
		/// map with no obstacles still produces a sensible field that peaks at its centre.
		/// </summary>
		public static int[] Compute(int width, int height, Func<int, int, bool> passable)
		{
			ArgumentNullException.ThrowIfNull(passable);
			if (width <= 0 || height <= 0)
				return [];

			var distance = new int[width * height];

			// int.MaxValue would overflow when a step is added to it, so seed with a value that is
			// unreachably large for any real map but still leaves room to add a diagonal step.
			const int Unreached = int.MaxValue / 2;
			for (var y = 0; y < height; y++)
				for (var x = 0; x < width; x++)
					distance[(y * width) + x] = passable(x, y) ? Unreached : 0;

			// Forward sweep: each cell takes the best of the neighbours already finalised above and
			// to the left of it.
			for (var y = 0; y < height; y++)
			{
				for (var x = 0; x < width; x++)
				{
					var i = (y * width) + x;
					if (distance[i] == 0)
						continue;

					var best = distance[i];
					best = Math.Min(best, Sample(distance, width, height, x - 1, y) + Orthogonal);
					best = Math.Min(best, Sample(distance, width, height, x, y - 1) + Orthogonal);
					best = Math.Min(best, Sample(distance, width, height, x - 1, y - 1) + Diagonal);
					best = Math.Min(best, Sample(distance, width, height, x + 1, y - 1) + Diagonal);
					distance[i] = best;
				}
			}

			// Backward sweep: the same from below and to the right, which completes the transform.
			for (var y = height - 1; y >= 0; y--)
			{
				for (var x = width - 1; x >= 0; x--)
				{
					var i = (y * width) + x;
					if (distance[i] == 0)
						continue;

					var best = distance[i];
					best = Math.Min(best, Sample(distance, width, height, x + 1, y) + Orthogonal);
					best = Math.Min(best, Sample(distance, width, height, x, y + 1) + Orthogonal);
					best = Math.Min(best, Sample(distance, width, height, x + 1, y + 1) + Diagonal);
					best = Math.Min(best, Sample(distance, width, height, x - 1, y + 1) + Diagonal);
					distance[i] = best;
				}
			}

			return distance;
		}

		/// <summary>
		/// Off-grid cells read as 0, which is what makes the map border behave as an obstruction
		/// rather than as an infinitely open plain.
		/// </summary>
		static int Sample(int[] distance, int width, int height, int x, int y)
		{
			if (x < 0 || y < 0 || x >= width || y >= height)
				return 0;

			return distance[(y * width) + x];
		}
	}
}
