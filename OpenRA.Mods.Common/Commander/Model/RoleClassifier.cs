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

namespace OpenRA.Mods.Common.Commander.Model
{
	/// <summary>
	/// <para>
	/// Assigns an actor type to one of the seven combat roles, from what the rules say about it
	/// rather than from a list somebody maintains.
	/// </para>
	/// <para>
	/// Hand-written role lists - "AntiArmorUnits: 3tnk, 4tnk" - encode one person's reading of the
	/// balance at one moment, go stale silently when the mod changes, and cannot answer for a unit
	/// nobody thought of. Every test below is a property of the loaded ruleset instead, so a new
	/// unit is classified the day it is added.
	/// </para>
	/// </summary>
	public static class RoleClassifier
	{
		/// <summary>
		/// The facts about an actor the classification depends on. Passed as plain values so the
		/// rule is testable without loading a ruleset or standing up a world.
		/// </summary>
		public readonly record struct Traits(
			bool IsAircraft,
			bool IsBuilding,
			bool IsMobile,
			string Armor,
			int RangeCells,
			bool CanTargetAir,
			bool CanTargetGround,
			bool IsArmed);

		/// <summary>Range at or beyond which an armed ground unit is treated as artillery, in cells.</summary>
		public const int ArtilleryRangeCells = 8;

		/// <summary>
		/// The role an actor fills. Order matters: the tests run from the least ambiguous fact to
		/// the most, so a naval anti-air gunboat is Naval rather than AntiAir - what it floats on
		/// constrains where it can fight far more than what it shoots at.
		/// </summary>
		public static CombatRole Classify(Traits traits)
		{
			// What domain it occupies comes first, because that decides where it can be at all.
			if (traits.IsAircraft)
				return CombatRole.Aircraft;

			if (string.Equals(traits.Armor, "Ship", StringComparison.OrdinalIgnoreCase))
				return CombatRole.Naval;

			// A structure that shoots is a defence; one that does not is economy or production and
			// is accounted for as base integrity rather than as force.
			if (traits.IsBuilding || !traits.IsMobile)
				return CombatRole.Defense;

			// Dedicated anti-air: it can reach aircraft and nothing else. A unit that can do both is
			// counted for the harder job it does on the ground.
			if (traits.CanTargetAir && !traits.CanTargetGround)
				return CombatRole.AntiAir;

			// Long reach and no answer to aircraft is the artillery signature - it outranges what it
			// shoots at and needs covering from what it cannot.
			if (traits.IsArmed && !traits.CanTargetAir && traits.RangeCells >= ArtilleryRangeCells)
				return CombatRole.Artillery;

			// Unarmoured and on foot.
			if (string.Equals(traits.Armor, "None", StringComparison.OrdinalIgnoreCase))
				return CombatRole.Infantry;

			return CombatRole.Armor;
		}
	}
}
