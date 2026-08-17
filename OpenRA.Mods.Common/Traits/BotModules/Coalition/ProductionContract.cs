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
	/// Deterministic production contracts derived from the enemy capability threat profile. The
	/// blackboard's per-region threat fields (built from <see cref="CoalitionBlackboard.CapabilitiesFor"/>
	/// intel) are aggregated into one per-capability profile, and each material enemy capability is
	/// answered by its configured counter units - strongest threat first, skipping counters the
	/// coalition already fields. Pure and engine-free so it can be unit-tested without a World.
	/// </summary>
	public static class ProductionContract
	{
		/// <summary>Enemy capability threats below this intensity are not worth contracting against.</summary>
		public const float MaterialThreatThreshold = 0.2f;

		/// <summary>Contracts are skipped once the coalition already fields this many units of the counter types.</summary>
		public const int FieldedCounterMinimum = 4;

		/// <summary>
		/// Multiplier applied to capability threat weights when resolving production contracts.
		/// Tunable via the PRODUCTION_CAPABILITY_WEIGHT_SCALE environment variable (req 722) for
		/// self-play parameter sweeps: &gt;1 makes threats material sooner (more counter
		/// production), &lt;1 later. Non-positive values fall back to the default of 1.
		/// </summary>
		public static float CapabilityWeightScale
		{
			get
			{
				var env = Environment.GetEnvironmentVariable("PRODUCTION_CAPABILITY_WEIGHT_SCALE");
				return float.TryParse(env, out var scale) && scale > 0f ? scale : 1f;
			}
		}

		/// <summary>
		/// Aggregates the per-region threat arrays into a single per-capability profile (max across
		/// regions): a capability that is material in any region drives production.
		/// </summary>
		public static float[] Aggregate(CoalitionRegion[] regions)
		{
			var profile = new float[Enum.GetValues<CoalitionCapability>().Length];
			foreach (var region in regions)
				for (var c = 0; c < profile.Length; c++)
					if (region.Threats[c] > profile[c])
						profile[c] = region.Threats[c];

			return profile;
		}

		/// <summary>
		/// Resolves the production contract: every material enemy capability (at or above
		/// <see cref="MaterialThreatThreshold"/> after the optional <paramref name="weightScale"/>
		/// multiplier) is answered with its counter units, ordered by threat strength (ties keep
		/// the configured contract order). A contract is skipped when the coalition already fields
		/// at least <see cref="FieldedCounterMinimum"/> units of its counter types, or when naval
		/// counters are requested but no usable water body is explored. Returns null when nothing
		/// is worth contracting.
		/// </summary>
		public static string[] Resolve(float[] capabilityThreats,
			IReadOnlyList<(CoalitionCapability Capability, string[] CounterUnits)> contracts,
			Func<string, int> fieldedCount, bool hasBigWater, float weightScale = 1f)
		{
			var selected = new List<(float Threat, int Index, string[] Units)>();
			for (var i = 0; i < contracts.Count; i++)
			{
				var (capability, units) = contracts[i];
				if (units.Length == 0)
					continue;

				if ((capability == CoalitionCapability.Naval || capability == CoalitionCapability.Submarine) && !hasBigWater)
					continue;

				var threat = capabilityThreats[(int)capability] * weightScale;
				if (threat < MaterialThreatThreshold)
					continue;

				// Gap check: only contract counters the coalition does not already field in numbers.
				var fielded = 0;
				foreach (var unit in units)
					fielded += fieldedCount(unit);
				if (fielded >= FieldedCounterMinimum)
					continue;

				selected.Add((threat, i, units));
			}

			if (selected.Count == 0)
				return null;

			// Strongest threat first; ties keep the configured contract order.
			var ordered = selected.OrderByDescending(s => s.Threat).ThenBy(s => s.Index);
			var result = new List<string>();
			foreach (var (_, _, units) in ordered)
				foreach (var unit in units)
					if (!result.Contains(unit))
						result.Add(unit);

			return result.ToArray();
		}
	}
}
