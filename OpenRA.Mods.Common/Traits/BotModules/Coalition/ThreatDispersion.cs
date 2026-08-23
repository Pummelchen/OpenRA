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
	/// <summary>One simultaneous threat the coalition is presenting.</summary>
	public readonly record struct PresentedThreat(MissionType Type, int Region, string Domain, int Priority);

	/// <summary>
	/// <para>
	/// Measures whether the coalition is presenting several coordinated threats at once, or one blob
	/// (reqs 277-284).
	/// </para>
	/// <para>
	/// The distinction that matters is not "many missions" but "many threats a defender must answer
	/// separately, all serving one purpose". Two raids in the same region are one problem; a main
	/// assault plus a rear raid plus an air strike in three regions is three problems, and a human
	/// can only be in one place. Equally, threats that share no main effort are not a plan - they are
	/// the army spread evenly, which is the failure mode this exists to detect.
	/// </para>
	/// </summary>
	public static class ThreatDispersion
	{
		/// <summary>Distinct map regions under simultaneous pressure (req 278).</summary>
		public static int DistinctRegions(IEnumerable<PresentedThreat> threats)
		{
			return (threats ?? []).Where(t => t.Region >= 0).Select(t => t.Region).Distinct().Count();
		}

		/// <summary>Distinct domains (land/air/naval/special) in simultaneous use (req 279).</summary>
		public static int DistinctDomains(IEnumerable<PresentedThreat> threats)
		{
			return (threats ?? []).Where(t => !string.IsNullOrEmpty(t.Domain))
				.Select(t => t.Domain).Distinct(StringComparer.OrdinalIgnoreCase).Count();
		}

		/// <summary>
		/// A multi-threat picture requires pressure in at least two separate places. One region is a
		/// single front however many missions are pointed at it (req 277).
		/// </summary>
		public static bool IsMultiThreat(IEnumerable<PresentedThreat> threats)
		{
			return DistinctRegions(threats) >= 2;
		}

		/// <summary>
		/// The full combined picture of req 280: a main assault supported by a raid, a strike and a
		/// special operation, across several regions and domains.
		/// </summary>
		public static bool IsFullSpectrum(IEnumerable<PresentedThreat> threats)
		{
			var list = (threats ?? []).ToArray();
			var hasAssault = list.Any(t => t.Type is MissionType.Attack or MissionType.Breakthrough
				or MissionType.Siege or MissionType.Exploitation);
			var hasRaid = list.Any(t => t.Type is MissionType.Raid or MissionType.EconomyRaid
				or MissionType.ProductionRaid or MissionType.Harassment);
			var hasStrike = list.Any(t => t.Type is MissionType.AirStrike or MissionType.NavalStrike
				or MissionType.SupportPowerStrike or MissionType.NavalBlockade);
			var hasSpecial = list.Any(t => t.Type is MissionType.SpecialOps or MissionType.Transport);

			return hasAssault && hasRaid && hasStrike && hasSpecial
				&& DistinctRegions(list) >= 3 && DistinctDomains(list) >= 2;
		}

		/// <summary>
		/// Whether simultaneous threats serve one purpose rather than being scattered attacks
		/// (req 281). Exactly one threat must carry the top priority: that is the main effort, and
		/// everything else is supporting it. Two co-equal maxima mean the army is split, not
		/// concentrated.
		/// </summary>
		public static bool SharesCommonPurpose(IEnumerable<PresentedThreat> threats)
		{
			var list = (threats ?? []).ToArray();
			if (list.Length < 2)
				return list.Length == 1;

			var top = list.Max(t => t.Priority);
			return list.Count(t => t.Priority == top) == 1;
		}

		/// <summary>
		/// Whether a distraction is in place before a high-value operation is committed (reqs 283, 299).
		/// The distraction must already be running - one launched simultaneously distracts nobody.
		/// </summary>
		public static bool DistractionPrecedes(int distractionLaunchTick, int operationLaunchTick, int minimumLeadTicks)
		{
			return distractionLaunchTick >= 0
				&& operationLaunchTick - distractionLaunchTick >= Math.Max(1, minimumLeadTicks);
		}

		/// <summary>
		/// Whether the defender is being forced to choose (req 284): several valuable things are
		/// threatened at once and the defender cannot cover them all with the force it has.
		/// </summary>
		public static bool ForcesDefenderChoice(int threatenedAssets, int defenderMobileGroups)
		{
			return threatenedAssets >= 2 && defenderMobileGroups < threatenedAssets;
		}

		/// <summary>
		/// Whether an observed overreaction is exploitable (req 282): the defender committed a large
		/// share of its army to answer a threat, leaving somewhere else thin. Requires the opponent
		/// model to be reliable, so a single coincidence is not mistaken for a pattern.
		/// </summary>
		public static bool OverreactionIsExploitable(float enemyShareDrawn, float modelConfidence,
			float minimumShare = 0.4f, float minimumConfidence = 0.6f)
		{
			return enemyShareDrawn >= minimumShare && modelConfidence >= minimumConfidence;
		}
	}
}
