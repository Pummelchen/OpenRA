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
	/// <summary>
	/// <para>
	/// When and where the coalition expands, and how the resulting economy is protected
	/// (reqs 407-412).
	/// </para>
	/// <para>
	/// Expansion is the decision most often got wrong in both directions: expanding under pressure
	/// loses the harvesters and the escort sent with them, while never expanding loses the long game
	/// to an opponent who did. The policy therefore keys off posture and local threat rather than a
	/// fixed timer, and it treats a new expansion as something that must be defended from the moment
	/// it is planned rather than after it is first raided.
	/// </para>
	/// </summary>
	public static class ExpansionPolicy
	{
		/// <summary>
		/// Risk of taking an expansion site (req 407): threat at the site itself plus the exposure of
		/// being far from the forces that would relieve it. Distance matters because an expansion
		/// nobody can reach in time is undefended however quiet it looks when it is planted.
		/// </summary>
		public static float ExpansionRisk(float siteThreat, int distanceFromBase, int mapSpan)
		{
			var reach = mapSpan <= 0 ? 0f : Math.Clamp(distanceFromBase / (float)mapSpan, 0f, 1f);
			return Math.Clamp(Math.Clamp(siteThreat, 0f, 1f) * 0.7f + reach * 0.3f, 0f, 1f);
		}

		/// <summary>
		/// Whether the posture permits expanding now (req 408). Expansion is an investment: it pays
		/// off only if the coalition survives to collect, so it is taken while building or containing
		/// and refused while breaking through, defending or desperate.
		/// </summary>
		public static bool PostureAllowsExpansion(StrategicPosture posture)
		{
			return posture switch
			{
				StrategicPosture.Opening or StrategicPosture.Expansion
					or StrategicPosture.Containment or StrategicPosture.Recovery
					or StrategicPosture.Attrition => true,
				_ => false
			};
		}

		/// <summary>Whether to commit to an expansion, weighing its value against its risk.</summary>
		public static bool ShouldExpand(StrategicPosture posture, float siteValue, float risk,
			float maximumRisk = 0.6f)
		{
			return PostureAllowsExpansion(posture) && siteValue > 0f && risk <= maximumRisk;
		}

		/// <summary>
		/// Defensive force a new expansion warrants (req 410), scaled to its risk. A site is escorted
		/// from the moment it is planned, not after it is first raided.
		/// </summary>
		public static int DefensiveGarrison(float risk, int baseGarrison)
		{
			var scaled = (int)Math.Ceiling(Math.Max(0, baseGarrison) * (0.5f + Math.Clamp(risk, 0f, 1f)));
			return Math.Max(1, scaled);
		}

		/// <summary>
		/// Share of defensive strength that economic assets warrant (req 411). An economy that is
		/// already the weaker half of the match cannot afford to lose any more of it, so the share
		/// rises as economic strength falls.
		/// </summary>
		public static float EconomicDefenseShare(float ownEconomicStrength, float enemyEconomicStrength,
			float floor = 0.15f, float ceiling = 0.5f)
		{
			if (ownEconomicStrength <= 0f)
				return ceiling;

			var ratio = enemyEconomicStrength <= 0f ? 0f : enemyEconomicStrength / ownEconomicStrength;
			return Math.Clamp(floor + (ratio - 1f) * 0.2f, floor, ceiling);
		}

		/// <summary>
		/// Whether an enemy economic weakness is worth raiding (req 412). Raiding a thin economy
		/// compounds; raiding a strong one just loses the raiders. Requires a force that can survive
		/// the trip, so this never sends a lone unit to die for a gesture.
		/// </summary>
		public static bool ShouldRaidEconomy(float enemyEconomicStrength, float ownEconomicStrength,
			int availableRaiders, int minimumRaidForce)
		{
			return availableRaiders >= Math.Max(1, minimumRaidForce)
				&& ownEconomicStrength > 0f
				&& enemyEconomicStrength < ownEconomicStrength;
		}

		/// <summary>
		/// Whether one ally should specialize in economy while the others fight (req 409). Only worth
		/// doing with enough allies that the fighting share is still viable - specializing in a
		/// two-player coalition halves the army.
		/// </summary>
		public static bool ShouldSpecializeEconomy(int alliedPlayers, int minimumForSpecialization = 3)
		{
			return alliedPlayers >= Math.Max(2, minimumForSpecialization);
		}
	}
}
