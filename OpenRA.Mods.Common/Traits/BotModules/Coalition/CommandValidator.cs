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

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	/// <summary>
	/// Validates commander intent (mission requests and reply rounds) before execution and returns
	/// machine-readable rejections. Pure and engine-free so it can be unit-tested without a World.
	/// The engine is authoritative: a rejected request is logged and ignored, never silently honored.
	/// </summary>
	public static class CommandValidator
	{
		/// <summary>The mission types the commander may request, in their canonical wire form.</summary>
		public static readonly IReadOnlySet<string> KnownMissionTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"attack", "defend", "recon", "raid", "feint", "retreat", "transport", "counterattack",
			"specialops", "bait", "breakthrough", "siege", "harassment", "economyraid", "productionraid",
			"expansiondenial", "chokepointseizure", "flank", "airstrike", "navalstrike", "supportpowerstrike",
			"mobiledefense", "antiairumbrella", "navalscreen", "delayingaction", "evacuation", "escort",
			"deeprecon", "airrecon", "navalrecon", "routerecon", "expansionsearch", "defenseprobe",
			"demonstration", "decoytransport"
		};

		/// <summary>
		/// Validates a batch of mission requests. Returns the machine-readable rejections (index into the
		/// request list plus the reason); accepted requests are not reported. Checks the type, target
		/// bounds, priority, and duplicate/conflicting requests (same type at the same target).
		/// </summary>
		public static IReadOnlyList<(int Index, string Reason)> ValidateMissions(
			IReadOnlyList<(string Type, int X, int Y, int Priority)> requests, int mapWidth, int mapHeight)
		{
			var rejections = new List<(int Index, string Reason)>();
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			for (var i = 0; i < requests.Count; i++)
			{
				var (type, x, y, priority) = requests[i];

				if (string.IsNullOrWhiteSpace(type))
				{
					rejections.Add((i, "REJECTED_INVALID_MISSION: empty mission type"));
					continue;
				}

				if (!KnownMissionTypes.Contains(type))
				{
					rejections.Add((i, $"REJECTED_UNKNOWN_TYPE: unknown mission type \"{type}\""));
					continue;
				}

				if (x < 0 || y < 0 || x >= mapWidth || y >= mapHeight)
				{
					rejections.Add((i, $"REJECTED_OUT_OF_BOUNDS: target ({x},{y}) is outside the {mapWidth}x{mapHeight} map"));
					continue;
				}

				if (priority < 0)
				{
					rejections.Add((i, $"REJECTED_INVALID_PRIORITY: priority {priority} is negative"));
					continue;
				}

				var key = $"{type}:{x}:{y}";
				if (!seen.Add(key))
					rejections.Add((i, $"REJECTED_CONFLICT: duplicate mission {type} at ({x},{y})"));
			}

			return rejections;
		}

		/// <summary>True when a reply round is older than the current round (a stale reply).</summary>
		public static bool IsStale(int replyRound, int currentRound)
		{
			return replyRound >= 0 && currentRound >= 0 && replyRound < currentRound;
		}
	}
}
