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
	/// Whether the coalition should deliberately buy an arm it does not have (reqs 198, 231, 232).
	/// </para>
	/// <para>
	/// The build plan walks a priority list and gives each idle queue the highest-priority unit it can
	/// build. That is correct for choosing between comparable units, but it has a failure mode worth
	/// naming: aircraft cost several times what a tank does and sit late in the list, so a coalition
	/// with cash for one tank per cycle takes the tank every cycle and never fields an aircraft at
	/// all. Measured over a 20,000-tick four-bot match this fork produced zero air and zero naval
	/// units, which makes every combined-arms rule involving those arms unreachable in practice.
	/// </para>
	/// <para>
	/// This states the missing rule: once the infrastructure for an arm exists and the coalition
	/// fields none of it, that arm is worth buying even though a cheaper unit is available - because
	/// the value of the first aircraft is not its combat power, it is that it unlocks an entire
	/// doctrine. It is a floor, not a quota: past the minimum the normal priority order resumes.
	/// </para>
	/// </summary>
	public static class ProductionBalance
	{
		/// <summary>Units of an arm below which the coalition is treated as not fielding it at all.</summary>
		public const int ArmFloor = 2;

		/// <summary>
		/// Whether an arm should be bought out of order. Requires the production infrastructure to
		/// exist (there is no point queueing an aircraft with no airfield), the arm to be below its
		/// floor, and the coalition to be able to afford it without emptying the treasury - buying a
		/// 2000-credit aircraft with 2000 credits leaves nothing to replace losses with.
		/// </summary>
		public static bool ShouldBuyArm(bool infrastructureExists, int ownedOfArm, int cash, int unitCost,
			float reserveFraction = 0.5f)
		{
			if (!infrastructureExists || ownedOfArm >= ArmFloor || unitCost <= 0)
				return false;

			var affordable = (int)(cash * (1f - Math.Clamp(reserveFraction, 0f, 0.9f)));
			return affordable >= unitCost;
		}

		/// <summary>
		/// Whether the coalition is missing an arm it could field. Distinguishes "chose not to build
		/// air" from "cannot build air", which are very different diagnoses.
		/// </summary>
		public static bool ArmIsMissing(bool infrastructureExists, int ownedOfArm)
		{
			return infrastructureExists && ownedOfArm < ArmFloor;
		}

		/// <summary>
		/// How many cycles a coalition would need before affording a unit, given its income per
		/// cycle. Used to explain why an arm never appears rather than merely observing that it does
		/// not: a unit that needs more cycles than a match contains is unreachable by construction.
		/// </summary>
		public static int CyclesToAfford(int unitCost, int incomePerCycle)
		{
			if (unitCost <= 0)
				return 0;

			return incomePerCycle <= 0 ? int.MaxValue : (int)Math.Ceiling(unitCost / (double)incomePerCycle);
		}
	}
}
