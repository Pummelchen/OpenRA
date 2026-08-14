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
	/// Independent difficulty axes for the coalition commander. Each axis is configurable on its
	/// own (0..3 where 3 is hardest) and scales a distinct behavior, so a fair-but-brutal profile
	/// can set extreme command quality and coordination while keeping the economic bonus at zero
	/// and intelligence at fair fog. The scalar <see cref="FromScalar"/> convenience sets all axes
	/// together for the legacy single-knob configuration.
	/// </summary>
	public sealed class CoalitionDifficulty
	{
		/// <summary>0..3: how demanding the coordinated-attack threshold and reserve policy are.</summary>
		public int CommandQuality = 3;

		/// <summary>0..3: how quickly the bot reacts (attack-window tolerances, response delays).</summary>
		public int ReactionSpeed = 3;

		/// <summary>0..3: fractional economic bonus; 0 is a strictly fair game.</summary>
		public int EconomicBonus = 0;

		/// <summary>0..3: micro precision - how aggressively the bot preserves damaged units and stages waves.</summary>
		public int MicroPrecision = 3;

		/// <summary>0..3: coalition coordination strength - how much the team trusts feints and commits reserves.</summary>
		public int CoordinationStrength = 3;

		/// <summary>0..3: intelligence/fog advantage. 0 = fair fog (default), 2 = structures always visible, 3 = omniscient.</summary>
		public int Intelligence = 0;

		/// <summary>True at the top intelligence setting: the coalition sees every enemy actor regardless of fog.</summary>
		public bool IsOmniscient => Intelligence >= 3;

		/// <summary>Multiplier in 0.75..1.5 by difficulty, shared by the existing scaled thresholds.</summary>
		public float Scale(float baseValue)
		{
			return baseValue * (1.5f - 0.25f * CommandQuality);
		}

		/// <summary>Reserve fraction tightens with coordination: 8 at easy, 3 at supreme.</summary>
		public int ScaledReserveFraction()
		{
			var fractions = new[] { 8, 6, 4, 3 };
			return fractions[Math.Clamp(CoordinationStrength, 0, 3)];
		}

		/// <summary>Reaction-speed delay multiplier: slower bots take longer to respond.</summary>
		public float ReactionMultiplier()
		{
			return 1.5f - 0.25f * ReactionSpeed;
		}

		/// <summary>Micro-precision retreat threshold: precise bots pull units earlier.</summary>
		public int RetreatHealthPercent()
		{
			var thresholds = new[] { 45, 40, 35, 30 };
			return thresholds[Math.Clamp(MicroPrecision, 0, 3)];
		}

		/// <summary>Sets every axis from one scalar (legacy single-knob difficulty).</summary>
		public static CoalitionDifficulty FromScalar(int difficulty)
		{
			var clamped = Math.Clamp(difficulty, 0, 3);
			return new CoalitionDifficulty
			{
				CommandQuality = clamped,
				ReactionSpeed = clamped,
				MicroPrecision = clamped,
				CoordinationStrength = clamped
			};
		}
	}
}
