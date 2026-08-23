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
	/// <summary>One front the coalition is holding or pressing.</summary>
	public readonly record struct Front(int Region, float OwnStrength, float EnemyStrength, bool IsMainEffort);

	/// <summary>
	/// <para>
	/// Force concentration: choosing a main effort and deciding how much may be spent elsewhere
	/// (reqs 343-349).
	/// </para>
	/// <para>
	/// The rule this encodes is that local superiority decides battles and total army size does not.
	/// An army that is larger overall but equal everywhere wins nothing; the same army concentrated
	/// on one front wins there. Spreading effort evenly across fronts is therefore treated as a
	/// failure state to be detected, not a neutral default - and concentration is refused when it
	/// would leave the base open, because a breakthrough that loses the base is not a win.
	/// </para>
	/// </summary>
	public static class ConcentrationPolicy
	{
		/// <summary>Ratio of own to enemy strength on a front; 0 enemy strength is uncontested.</summary>
		public static float LocalRatio(Front front)
		{
			return front.EnemyStrength <= 0f ? float.PositiveInfinity : front.OwnStrength / front.EnemyStrength;
		}

		/// <summary>
		/// Local superiority on a front (req 346). The threshold is a real edge, not parity: attacking
		/// at 1:1 trades evenly, which does not take ground.
		/// </summary>
		public const float SuperiorityRatio = 1.5f;

		public static bool HasLocalSuperiority(Front front)
		{
			return LocalRatio(front) >= SuperiorityRatio;
		}

		/// <summary>
		/// Picks the front to concentrate on (req 343): the one where the coalition's strength buys
		/// the most relative advantage. Ties break on the lowest region index so every allied bot
		/// chooses identically.
		/// </summary>
		public static int SelectMainEffort(IReadOnlyList<Front> fronts)
		{
			if (fronts == null || fronts.Count == 0)
				return -1;

			return fronts
				.OrderByDescending(LocalRatio)
				.ThenByDescending(f => f.OwnStrength)
				.ThenBy(f => f.Region)
				.First().Region;
		}

		/// <summary>
		/// Whether effort is spread evenly rather than concentrated (req 345). Even distribution
		/// across every front means no main effort exists in practice, whatever the plan says.
		/// </summary>
		public static bool IsSpreadEvenly(IReadOnlyList<Front> fronts, float tolerance = 0.15f)
		{
			if (fronts == null || fronts.Count < 2)
				return false;

			var total = fronts.Sum(f => f.OwnStrength);
			if (total <= 0f)
				return false;

			var even = 1f / fronts.Count;
			return fronts.All(f => Math.Abs(f.OwnStrength / total - even) <= tolerance);
		}

		/// <summary>
		/// Whether a front retains enough to hold while the main effort is elsewhere (req 348).
		/// </summary>
		public static bool CanHold(Front front, float minimumHoldingRatio = 0.5f)
		{
			return LocalRatio(front) >= minimumHoldingRatio;
		}

		/// <summary>
		/// Whether massing on the main effort would expose the coalition unacceptably (req 349).
		/// Concentration is correct only while everything left behind can still hold; a breakthrough
		/// bought by losing the base is not an advantage.
		/// </summary>
		public static bool ConcentrationIsSafe(IReadOnlyList<Front> fronts, int mainEffortRegion,
			float minimumHoldingRatio = 0.5f)
		{
			if (fronts == null || fronts.Count == 0)
				return false;

			return fronts.Where(f => f.Region != mainEffortRegion)
				.All(f => CanHold(f, minimumHoldingRatio));
		}

		/// <summary>
		/// How much of the army may go to secondary operations (req 344). Secondary effort supports
		/// the main one; when the main effort lacks superiority everything goes there instead.
		/// </summary>
		public static float SecondaryBudget(Front mainEffort, float configuredBudget)
		{
			var budget = Math.Clamp(configuredBudget, 0f, 1f);
			return HasLocalSuperiority(mainEffort) ? budget : 0f;
		}

		/// <summary>
		/// Whether the coalition can mass decisively against a vulnerable front (req 347): it has the
		/// superiority to win there and can afford to leave the other fronts holding.
		/// </summary>
		public static bool ShouldMass(IReadOnlyList<Front> fronts, int candidateRegion,
			float minimumHoldingRatio = 0.5f)
		{
			if (fronts == null)
				return false;

			var candidate = fronts.FirstOrDefault(f => f.Region == candidateRegion);
			if (candidate.Region != candidateRegion)
				return false;

			return HasLocalSuperiority(candidate)
				&& ConcentrationIsSafe(fronts, candidateRegion, minimumHoldingRatio);
		}
	}
}
