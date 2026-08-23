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

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	/// <summary>Actor-independent inputs used by the tactical target selector.</summary>
	public readonly struct TacticalTargetProfile
	{
		public readonly int Cost;
		public readonly int HealthPercent;
		public readonly bool Armed;
		public readonly bool Structure;
		public readonly bool Production;

		public TacticalTargetProfile(int cost, int healthPercent, bool armed, bool structure, bool production)
		{
			Cost = Math.Max(0, cost);
			HealthPercent = Math.Clamp(healthPercent, 0, 100);
			Armed = armed;
			Structure = structure;
			Production = production;
		}
	}

	/// <summary>
	/// Deterministic combat-target policy shared by the domain controllers. Keeping the arithmetic
	/// actor-independent makes the priority and overkill rules directly testable.
	/// </summary>
	public static class TacticalEngagement
	{
		const long WorldUnitsPerCellSquared = 1024L * 1024L;

		/// <summary>
		/// Scores a currently visible, weapon-valid target. Combat threats are removed first,
		/// production structures are preferred over generic buildings, damaged targets receive a
		/// finish-off bonus, and distance prevents the force from chasing irrelevant contacts.
		/// </summary>
		public static long TargetScore(TacticalTargetProfile target, long distanceSquared)
		{
			var score = (long)target.Cost * 4;
			if (target.Armed)
				score += 2400;
			if (target.Structure)
				score += 800;
			if (target.Production)
				score += 2200;

			// Finishing a damaged actor reduces return fire and prevents repair/rearm cycles.
			score += (100 - target.HealthPercent) * 24L;

			var distanceCellsSquared = Math.Max(0, distanceSquared) / WorldUnitsPerCellSquared;
			return score - Math.Min(5000, distanceCellsSquared * 10);
		}

		/// <summary>
		/// Caps attackers assigned to one target. The cap scales with value and remaining health,
		/// preventing the whole army from wasting a volley on one wounded infantry unit.
		/// </summary>
		public static int FocusSlots(TacticalTargetProfile target)
		{
			var fullHealthSlots = 1 + target.Cost / 450;
			if (target.Armed)
				fullHealthSlots++;
			if (target.Structure)
				fullHealthSlots += 2;
			if (target.Production)
				fullHealthSlots++;

			var healthScaled = (fullHealthSlots * Math.Max(15, target.HealthPercent) + 99) / 100;
			return Math.Clamp(healthScaled, 1, 10);
		}

		/// <summary>True when an idle/stale directive should be refreshed without per-tick order churn.</summary>
		public static bool ShouldRefreshOrder(bool idle, int currentTick, int lastOrderTick, int refreshInterval)
		{
			return idle || lastOrderTick <= 0 || currentTick - lastOrderTick >= Math.Max(1, refreshInterval);
		}

		/// <summary>Scales close asset-defense commitment while preserving deterministic bounds.</summary>
		public static int DefenseCommitment(int observedAttackers, int availableUnits, int minimumWave,
			int unitsPerAttacker)
		{
			if (availableUnits <= 0)
				return 0;

			var minimum = Math.Min(Math.Max(0, minimumWave), availableUnits);
			return Math.Clamp(Math.Max(0, observedAttackers) * Math.Max(1, unitsPerAttacker),
				minimum, availableUnits);
		}
	}
}
