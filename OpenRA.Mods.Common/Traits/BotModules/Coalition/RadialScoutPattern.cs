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
	/// <summary>
	/// <para>
	/// Radial map sweep: scouts sent outward on evenly spaced bearings, each aimed at the map edge
	/// (handbook §6).
	/// </para>
	/// <para>
	/// A scout aimed at a nearby point stops there. A scout aimed at the far edge walks the whole
	/// way and reveals everything along the line, so the same 100-credit rifleman buys several times
	/// the map knowledge - and if it dies en route, where it died is itself information about where
	/// the enemy is. Spacing the bearings evenly guarantees the coverage is radial rather than three
	/// probes down the same lane.
	/// </para>
	/// <para>
	/// Ordering matters as much as the pattern: consecutive scouts are sent to bearings roughly
	/// opposite each other rather than to neighbouring ones, so early losses still leave the sweep
	/// spread around the compass instead of concentrated on one arc.
	/// </para>
	/// </summary>
	public static class RadialScoutPattern
	{
		/// <summary>
		/// Target cells for a full sweep around <paramref name="home"/>, one per bearing, each pushed
		/// out to the map edge and clamped inside the playable area.
		/// </summary>
		public static IReadOnlyList<CPos> Sweep(CPos home, int mapWidth, int mapHeight, int stepDegrees = 10)
		{
			if (mapWidth <= 0 || mapHeight <= 0)
				return [];

			var step = Math.Clamp(stepDegrees, 1, 180);
			const int Margin = 2;
			var targets = new List<CPos>();

			for (var degrees = 0; degrees < 360; degrees += step)
			{
				var radians = degrees * MathF.PI / 180f;
				var dx = MathF.Cos(radians);
				var dy = MathF.Sin(radians);

				// Longest ray from home to the map boundary along this bearing: the point of aiming
				// at the edge is that the scout keeps walking, revealing the whole line.
				var distanceX = dx > 0 ? (mapWidth - Margin - home.X) / dx
					: dx < 0 ? (Margin - home.X) / dx : float.MaxValue;
				var distanceY = dy > 0 ? (mapHeight - Margin - home.Y) / dy
					: dy < 0 ? (Margin - home.Y) / dy : float.MaxValue;

				var distance = Math.Min(distanceX, distanceY);
				if (distance <= 0f || float.IsInfinity(distance) || float.IsNaN(distance))
					continue;

				var x = Math.Clamp((int)MathF.Round(home.X + dx * distance), Margin, mapWidth - Margin - 1);
				var y = Math.Clamp((int)MathF.Round(home.Y + dy * distance), Margin, mapHeight - Margin - 1);
				var target = new CPos(x, y);

				if (target != home && !targets.Contains(target))
					targets.Add(target);
			}

			return targets;
		}

		/// <summary>
		/// Reorders a sweep so consecutive scouts go to widely separated bearings. Walking the list
		/// in order would send the first several probes down neighbouring lanes, which is the same
		/// mistake as sending them all one way.
		/// </summary>
		public static IReadOnlyList<CPos> Interleave(IReadOnlyList<CPos> sweep)
		{
			if (sweep == null || sweep.Count <= 2)
				return sweep ?? [];

			var ordered = new List<CPos>(sweep.Count);
			var remaining = new List<CPos>(sweep);
			var index = 0;

			while (remaining.Count > 0)
			{
				index %= remaining.Count;
				ordered.Add(remaining[index]);
				remaining.RemoveAt(index);

				// Step roughly half way round the remaining circle each time.
				index += Math.Max(1, remaining.Count / 2);
			}

			return ordered;
		}

		/// <summary>
		/// The sweep a commander should actually use: interleaved bearings, filtered to those still
		/// worth visiting, and skipping any that is already explored.
		/// </summary>
		public static IReadOnlyList<CPos> UnexploredSweep(CPos home, int mapWidth, int mapHeight,
			Func<CPos, bool> isExplored, Func<CPos, bool> isReachable = null, int stepDegrees = 10)
		{
			var sweep = Interleave(Sweep(home, mapWidth, mapHeight, stepDegrees));

			return sweep
				.Where(c => !(isExplored?.Invoke(c) ?? false) && (isReachable?.Invoke(c) ?? true))
				.ToArray();
		}
	}
}
