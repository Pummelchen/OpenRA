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
		/// <summary>Cap on production entries so a malformed reply cannot flood the directive list.</summary>
		public const int MaxProduceEntries = 64;

		/// <summary>The maximum reserve fraction the commander may request (1/N of the army held
		/// back). 0 means no override; the brain clamps to this same ceiling.</summary>
		public const int MaxReserveFraction = 10;

		/// <summary>The mission types the commander may request, in their canonical wire form.</summary>
		public static readonly IReadOnlySet<string> KnownMissionTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"attack", "defend", "recon", "raid", "feint", "retreat", "transport", "counterattack",
			"specialops", "bait", "breakthrough", "siege", "harassment", "economyraid", "productionraid",
			"expansiondenial", "chokepointseizure", "flank", "airstrike", "navalstrike", "supportpowerstrike",
			"mobiledefense", "antiairumbrella", "navalscreen", "delayingaction", "evacuation", "escort",
			"deeprecon", "airrecon", "navalrecon", "routerecon", "expansionsearch", "defenseprobe",
			"demonstration", "decoytransport", "pincer", "navalblockade", "fakebuildup",
			"exploitation", "emergencyreinforcement", "interception"
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
		/// Resolves a posture hint into intent flags for mission creation. A "build" posture collapses
		/// into an economy stance and suppresses attack/defend intents; otherwise the deterministic
		/// force-ratio thresholds are combined with explicit attack/defend/turtle hints. Automatic
		/// attacks require a material advantage because fair-fog enemy strength is a lower-bound estimate.
		/// </summary>
		public static (bool Attack, bool Defend, bool Build) ResolveCommanderIntent(string posture, float ratio)
		{
			var normalized = posture?.Trim();
			var build = string.Equals(normalized, "build", StringComparison.OrdinalIgnoreCase);

			// Seek an objective unless materially outnumbered.
			//
			// This previously required ratio <= 0.75 - a 33% GLOBAL strength advantage before the
			// commander would even name a target. In an even match that is never true, so the
			// coalition never set a main effort, never created an attack mission, and spent every
			// game fighting the enemy field army it happened to meet. Measured: zero enemy
			// structures destroyed across a full mirror match, and 38 of 58 benchmark matches ending
			// in a time-limit draw. It held even under omniscience, which is what proved the gate
			// rather than reconnaissance was the cause.
			//
			// Whether an individual attack is survivable is decided downstream, where it belongs:
			// LanchesterModel.RequiredStrength sizes the force against the observed defender, and
			// SiegeTargeting requires local superiority at the objective. A global ratio cannot
			// express either, and using one as the gate is how a commander turtles into a draw.
			var attack = !build && (ratio < 1.25f || string.Equals(normalized, "attack", StringComparison.OrdinalIgnoreCase));
			var defend = !build && (ratio >= 1.25f || string.Equals(normalized, "defend", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(normalized, "turtle", StringComparison.OrdinalIgnoreCase));
			return (attack, defend, build);
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

		/// <summary>The capability vocabulary the LLM may request via request_capability.</summary>
		public static readonly IReadOnlySet<string> KnownCapabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"anti_air", "anti_armor", "anti_infantry", "artillery", "naval", "recon", "mobility",
			"fast_raiding", "air_superiority", "transport", "special_operations", "base_defense"
		};

		/// <summary>
		/// Validates a capability directive. A null/empty capability is valid (no directive); an
		/// unknown value is rejected with a machine-readable reason.
		/// </summary>
		public static string ValidateCapability(string capability)
		{
			if (string.IsNullOrWhiteSpace(capability))
				return null;

			return KnownCapabilities.Contains(capability.Trim())
				? null
				: $"REJECTED_UNKNOWN_CAPABILITY: unknown capability \"{capability}\"";
		}

		/// <summary>
		/// Validates an expansion priority override. A value of 0 is valid (no override); only -1,
		/// 0, and 1 are accepted. Out-of-range values are rejected with a machine-readable reason.
		/// </summary>
		public static string ValidateExpansionPriority(int priority)
		{
			return priority is -1 or 0 or 1
				? null
				: $"REJECTED_INVALID_EXPANSION_PRIORITY: expansion priority {priority} must be -1, 0, or 1";
		}

		/// <summary>
		/// Validates a reserve-fraction override. 0 is valid (no override); values outside 0..
		/// <see cref="MaxReserveFraction"/> are rejected with a machine-readable reason.
		/// </summary>
		public static string ValidateReserveFraction(int fraction)
		{
			return fraction >= 0 && fraction <= MaxReserveFraction
				? null
				: $"REJECTED_INVALID_RESERVE_FRACTION: reserve fraction {fraction} must be 0..{MaxReserveFraction}";
		}

		/// <summary>
		/// Reducing the reserve below roughly 15% (a denominator of seven or more) consumes the last
		/// meaningful safety margin. The model must state a concrete justification of sufficient detail
		/// for that exceptional choice; ordinary 20-25% reserves do not require one.
		/// </summary>
		public static string ValidateReserveJustification(int fraction, string justification)
		{
			return fraction < 7 || justification?.Trim().Length >= 20
				? null
				: "REJECTED_UNJUSTIFIED_RESERVE_COMMITMENT: reserve below 15% requires a concrete justification of at least 20 characters";
		}

		/// <summary>
		/// Validates production-directive unit names against the buildable-item set of the live
		/// ruleset. Blank entries are reported by <see cref="ValidateProduce"/>, so they are
		/// skipped here; unknown names are rejected with a machine-readable reason.
		/// </summary>
		public static IReadOnlyList<(int Index, string Reason)> ValidateUnitNames(
		IReadOnlyList<string> units, IReadOnlySet<string> buildable, string fieldName)
		{
			var rejections = new List<(int Index, string Reason)>();
			if (units == null)
				return rejections;

			for (var i = 0; i < units.Count; i++)
			{
				var name = units[i];
				if (string.IsNullOrWhiteSpace(name))
					continue;

				if (!buildable.Contains(name))
					rejections.Add((i, $"REJECTED_UNKNOWN_UNIT: {fieldName} entry \"{name}\" is not buildable"));
			}

			return rejections;
		}
	}
}
