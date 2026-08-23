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
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common.Commander.Terrain
{
	/// <summary>
	/// <para>
	/// Adapter between an OpenRA map and the pure <see cref="RegionGraph"/> decomposition.
	/// </para>
	/// <para>
	/// The movement class is supplied as a locomotor, which is what makes the same code answer both
	/// the army's question and the navy's. A ground locomotor produces the graph tanks live on and
	/// water is simply missing from it; a naval locomotor produces the graph ships live on and the
	/// land is missing instead. "Can the fleet reach their base" is then a reachability query on the
	/// naval graph rather than a special case anybody had to write.
	/// </para>
	/// </summary>
	public static class MapRegions
	{
		/// <summary>
		/// Whether a locomotor can enter a terrain type at all. A type absent from its speed table,
		/// or listed at zero speed, is terrain that movement class cannot use.
		/// </summary>
		public static bool CanTraverse(LocomotorInfo locomotor, string terrainType)
		{
			ArgumentNullException.ThrowIfNull(locomotor);
			if (terrainType == null || locomotor.TerrainSpeeds == null)
				return false;

			return locomotor.TerrainSpeeds.TryGetValue(terrainType, out var speed) && speed.Speed > 0;
		}

		/// <summary>
		/// Decomposes a map's playable area for one movement class. Grid coordinates are relative to
		/// <see cref="Map.Bounds"/>, so a region's centre maps back to a cell by adding the bounds
		/// origin - see <see cref="ToCell"/>.
		/// </summary>
		public static RegionGraph Build(Map map, LocomotorInfo locomotor, RegionGraph.Settings settings = default)
		{
			ArgumentNullException.ThrowIfNull(map);
			ArgumentNullException.ThrowIfNull(locomotor);

			var bounds = map.Bounds;
			var originX = bounds.Left;
			var originY = bounds.Top;

			return RegionGraph.Build(bounds.Width, bounds.Height, (x, y) =>
			{
				var cell = new CPos(x + originX, y + originY);
				if (!map.Contains(cell))
					return false;

				return CanTraverse(locomotor, map.GetTerrainInfo(cell).Type);
			}, settings);
		}

		/// <summary>Converts a grid coordinate from a graph built by <see cref="Build"/> back to a cell.</summary>
		public static CPos ToCell(Map map, int x, int y)
		{
			ArgumentNullException.ThrowIfNull(map);
			return new CPos(x + map.Bounds.Left, y + map.Bounds.Top);
		}

		/// <summary>Converts a cell to a grid coordinate, or returns false if it is outside the playable area.</summary>
		public static bool ToGrid(Map map, CPos cell, out int x, out int y)
		{
			ArgumentNullException.ThrowIfNull(map);
			x = cell.X - map.Bounds.Left;
			y = cell.Y - map.Bounds.Top;
			return x >= 0 && y >= 0 && x < map.Bounds.Width && y < map.Bounds.Height;
		}
	}
}
