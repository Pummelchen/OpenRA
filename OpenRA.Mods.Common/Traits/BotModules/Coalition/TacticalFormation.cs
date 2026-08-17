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

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	/// <summary>
	/// Pure formation math for the ground controller. Extracted so the artillery screening and
	/// speed-coordination decisions are unit-testable without a World.
	/// </summary>
	public static class TacticalFormation
	{
		/// <summary>
		/// Pulls the artillery firing position back toward the base by <paramref name="offset"/>
		/// world-units along the base-to-target axis, so artillery fires from range instead of
		/// charging into melee. Returns the target unchanged when the base is at the target.
		/// </summary>
		public static WPos ArtilleryPullbackTarget(WPos target, WPos baseCenter, int offset)
		{
			var dir = target - baseCenter;
			var len = dir.Length;
			if (len <= 0)
				return target;

			var pullbackX = 0;
			var pullbackY = 0;
			if (len > offset)
			{
				pullbackX = -dir.X * offset / len;
				pullbackY = -dir.Y * offset / len;
			}

			return new WPos(target.X + pullbackX, target.Y + pullbackY, target.Z);
		}

		/// <summary>
		/// True when <paramref name="position"/> is more than <paramref name="spreadSquared"/> distance
		/// ahead of the group <paramref name="center"/> along the axis toward <paramref name="target"/>.
		/// </summary>
		public static bool IsAheadOfCenter(WPos position, WPos target, WPos center, long spreadSquared)
		{
			return (position - target).LengthSquared < (center - target).LengthSquared - spreadSquared;
		}
	}
}
