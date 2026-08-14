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

		/// <summary>The posture vocabulary the commander intent surface accepts (from the model prompt).</summary>
		public static readonly IReadOnlySet<string> KnownPostures = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"attack", "defend", "build", "turtle"
		};

		/// <summary>Cap on production entries so a malformed reply cannot flood the directive list.</summary>
		public const int MaxProduceEntries = 64;

		/// <summary>
		/// Validates a posture hint. A null/empty posture is valid (the deterministic posture applies);
		/// an unknown value is rejected with a machine-readable reason.
		/// </summary>
		public static string ValidatePosture(string posture)
		{
			if (string.IsNullOrWhiteSpace(posture))
				return null;

			return KnownPostures.Contains(posture.Trim())
				? null
				: $"REJECTED_UNKNOWN_POSTURE: unknown posture \"{posture}\"";
		}

		/// <summary>
		/// Validates production requests. A null/empty list is valid (no directive); blank entries and
		/// oversized lists are rejected. Unit-name existence is checked against the production queues by
		/// the caller, not here, because that requires the live ruleset.
		/// </summary>
		public static IReadOnlyList<(int Index, string Reason)> ValidateProduce(IReadOnlyList<string> produce)
		{
			var rejections = new List<(int Index, string Reason)>();
			if (produce == null)
				return rejections;

			if (produce.Count > MaxProduceEntries)
				rejections.Add((-1, $"REJECTED_INVALID_PRODUCE: {produce.Count} entries exceed the cap of {MaxProduceEntries}"));

			for (var i = 0; i < produce.Count; i++)
				if (string.IsNullOrWhiteSpace(produce[i]))
					rejections.Add((i, "REJECTED_INVALID_PRODUCE: blank production entry"));

			return rejections;
		}

		/// <summary>
		/// Merges the LLM's production boosts into an existing production list, deduplicated
		/// case-insensitively. A null boost list leaves the existing list unchanged; blank boost
		/// entries are ignored.
		/// </summary>
		public static IReadOnlyList<string> MergeProduce(IReadOnlyList<string> existing, IReadOnlyList<string> llmProduce)
		{
			var units = new List<string>(existing ?? []);
			if (llmProduce == null)
				return units;

			foreach (var unit in llmProduce)
				if (!string.IsNullOrWhiteSpace(unit) && !units.Contains(unit, StringComparer.OrdinalIgnoreCase))
					units.Add(unit);

			return units;
		}
	}
}
