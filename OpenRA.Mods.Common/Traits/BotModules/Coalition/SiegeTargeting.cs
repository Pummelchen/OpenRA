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

using System.Collections.Generic;
using System.Linq;

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	/// <summary>One structure the assault can see and could attack.</summary>
	public readonly record struct SiegeCandidate(string Type, CPos Cell, int DistanceCells, bool IsDefence);

	/// <summary>
	/// <para>
	/// Chooses what an assault actually shoots once it reaches the enemy base (handbook §7).
	/// </para>
	/// <para>
	/// The assault previously issued an attack-move to a map cell, which means the force engages
	/// whatever it happens to meet — in practice the first pillbox on the perimeter. It then grinds
	/// against static defence while the economy that replaces those defences keeps running. That is
	/// the mechanism behind the measured record: high exchange ratio, no kills that matter, and a
	/// time-limit draw.
	/// </para>
	/// <para>
	/// The doctrine is to kill the economy first, then production, and to treat defences as an
	/// obstacle to be removed by artillery rather than a target worth the main force's time.
	/// </para>
	/// </summary>
	public static class SiegeTargeting
	{
		/// <summary>
		/// Value of a structure as an assault objective. Economy outranks production because an
		/// opponent with no income cannot replace what it loses; defences score lowest because
		/// killing them changes nothing about the opponent's ability to fight back.
		/// </summary>
		public static float ObjectiveValue(string actorType)
		{
			var economy = TargetEvaluator.EconomicValue(actorType);
			var production = TargetEvaluator.ProductionValue(actorType);
			var technology = TargetEvaluator.TechnologyValue(actorType);

			return economy * 3f + production * 2f + technology * 1.5f;
		}

		/// <summary>
		/// Ranks what the main force should attack. Distance is a mild penalty, not a decider: an
		/// assault that always picks the nearest building is an assault that dies on the perimeter.
		/// Returns null when nothing worth attacking is visible.
		/// </summary>
		public static SiegeCandidate? SelectMainForceTarget(IEnumerable<SiegeCandidate> visible)
		{
			var candidates = (visible ?? []).Where(c => !c.IsDefence).ToArray();
			if (candidates.Length == 0)
				return null;

			return candidates
				.OrderByDescending(c => ObjectiveValue(c.Type) - c.DistanceCells * 0.02f)
				.ThenBy(c => c.DistanceCells)
				.ThenBy(c => c.Cell.X)
				.ThenBy(c => c.Cell.Y)
				.First();
		}

		/// <summary>
		/// What the artillery should be reducing: the defence covering the approach. Artillery
		/// out-ranges base defence, which is its entire purpose - sending the main force in first
		/// spends armour on a job a 850-credit gun does for free.
		/// </summary>
		public static SiegeCandidate? SelectArtilleryTarget(IEnumerable<SiegeCandidate> visible)
		{
			var defences = (visible ?? []).Where(c => c.IsDefence).ToArray();
			if (defences.Length == 0)
				return null;

			return defences
				.OrderBy(c => c.DistanceCells)
				.ThenBy(c => c.Cell.X)
				.ThenBy(c => c.Cell.Y)
				.First();
		}

		/// <summary>
		/// Whether the main force should wait for the defences to be reduced before entering.
		/// Requires artillery that can actually do the reducing; with none, waiting achieves
		/// nothing and the assault goes in regardless.
		/// </summary>
		public static bool ShouldReduceBeforeEntering(int visibleDefences, int artilleryAvailable)
		{
			return visibleDefences > 0 && artilleryAvailable > 0;
		}

		/// <summary>
		/// Whether an assault is strong enough to commit at all. Local superiority, not global
		/// parity: an army that is larger overall but equal at the point of contact takes no ground.
		/// </summary>
		public const float RequiredLocalSuperiority = 1.5f;

		public static bool HasLocalSuperiority(float ownStrengthAtObjective, float enemyStrengthAtObjective)
		{
			if (ownStrengthAtObjective <= 0f)
				return false;

			return enemyStrengthAtObjective <= 0f
				|| ownStrengthAtObjective / enemyStrengthAtObjective >= RequiredLocalSuperiority;
		}
	}
}
