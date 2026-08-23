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
	/// What the army should be made of (handbook §7.3).
	/// </para>
	/// <para>
	/// The build plan gives each idle production queue the best unit it can build. That is locally
	/// sensible and globally wrong: the mod's default base has six infantry buildings against four
	/// war factories, infantry cost a seventh of a tank and build far faster, so every cycle the
	/// barracks win the race. Measured over a full match the coalition fielded waves of 43 infantry
	/// and 6 tanks, with the armour count falling as the match went on - and a rifle army loses to a
	/// tank army however many riflemen it has.
	/// </para>
	/// <para>
	/// Infantry are a screen, not the force. This caps their share so the credits go where the
	/// fighting power is.
	/// </para>
	/// </summary>
	public static class ArmyComposition
	{
		/// <summary>
		/// Infantry per armour unit the doctrine wants. Roughly a squad screening each tank: enough
		/// to absorb anti-tank fire and hold ground, not enough to become the army.
		/// </summary>
		public const float TargetInfantryPerArmor = 1.5f;

		/// <summary>Infantry to keep producing while the coalition has no armour at all.</summary>
		public const int MinimumInfantry = 6;

		/// <summary>Artillery per armour unit: enough to reduce a defence line, not a siege train.</summary>
		public const float ArtilleryPerArmor = 0.2f;

		/// <summary>Anti-air per armour unit: enough that a column is not free kills for aircraft.</summary>
		public const float AntiAirPerArmor = 0.15f;

		/// <summary>
		/// Whether another infantry unit is worth building. Below the floor the answer is always yes
		/// - an armour-only force with no screen is its own failure mode - and above the ratio the
		/// answer is no, because those credits buy more fighting power as armour.
		/// </summary>
		public static bool ShouldProduceInfantry(int infantry, int armor)
		{
			if (infantry < MinimumInfantry)
				return true;

			return infantry < armor * TargetInfantryPerArmor;
		}

		/// <summary>
		/// Whether the army is infantry-heavy enough that the imbalance is costing fighting power.
		/// Reported so the failure is visible in telemetry rather than only in the loss column.
		/// </summary>
		public static bool IsInfantryHeavy(int infantry, int armor)
		{
			return infantry > MinimumInfantry && infantry > armor * TargetInfantryPerArmor * 2f;
		}

		/// <summary>
		/// Support units the coalition should field for a given armour count. Artillery out-ranges
		/// base defence and anti-air keeps a column from being free kills; waves launched with zero
		/// of both is what the measured composition actually showed.
		/// </summary>
		public static int DesiredSupport(int armor, float perArmor)
		{
			if (armor <= 0)
				return 0;

			return Math.Max(1, (int)Math.Round(armor * Math.Max(0f, perArmor)));
		}

		/// <summary>Whether more artillery is worth building for the armour the coalition fields.</summary>
		public static bool ShouldProduceArtillery(int artillery, int armor)
		{
			return armor > 0 && artillery < DesiredSupport(armor, ArtilleryPerArmor);
		}

		/// <summary>Whether more anti-air is worth building for the armour the coalition fields.</summary>
		public static bool ShouldProduceAntiAir(int antiAir, int armor, bool enemyAirSeen)
		{
			if (armor <= 0)
				return false;

			// Before enemy air is seen a token escort is enough; after, it is a real requirement.
			var wanted = DesiredSupport(armor, enemyAirSeen ? AntiAirPerArmor * 2f : AntiAirPerArmor);
			return antiAir < wanted;
		}
	}
}
