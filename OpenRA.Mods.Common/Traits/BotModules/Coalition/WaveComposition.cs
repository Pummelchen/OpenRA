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
	/// The combined-arms make-up of one launched wave, and the doctrine questions that can be asked
	/// of it (reqs 198, 228-233, 237, 350).
	/// </para>
	/// <para>
	/// These counts were previously computed inline purely to format a telemetry string, which meant
	/// "armour is escorted by infantry" was a property the wave happened to have rather than one
	/// anything could assert. Naming the composition makes each combined-arms rule checkable, and the
	/// same value now feeds both the telemetry line and the tests.
	/// </para>
	/// </summary>
	public readonly struct WaveComposition
	{
		public const int MassAirMinimum = 3;

		public readonly int Armor;
		public readonly int Infantry;
		public readonly int Artillery;
		public readonly int AntiAir;
		public readonly int Air;
		public readonly int Naval;
		public readonly int Special;

		/// <summary>Everything that is not air or naval: the ground component of the wave.</summary>
		public int Land => Armor + Infantry + Artillery + AntiAir + Special;

		public int Total => Land + Air + Naval;

		public WaveComposition(int armor, int infantry, int artillery, int antiAir, int air, int naval, int special = 0)
		{
			Armor = Math.Max(0, armor);
			Infantry = Math.Max(0, infantry);
			Artillery = Math.Max(0, artillery);
			AntiAir = Math.Max(0, antiAir);
			Air = Math.Max(0, air);
			Naval = Math.Max(0, naval);
			Special = Math.Max(0, special);
		}

		/// <summary>Armour advancing with infantry rather than unsupported (req 228).</summary>
		public bool ArmorHasInfantrySupport => Armor > 0 && Infantry > 0;

		/// <summary>Artillery committed behind a force that can screen it (req 229).</summary>
		public bool ArtilleryHasScreen => Artillery > 0 && Armor + Infantry > 0;

		/// <summary>Anti-air travelling with the ground force it protects (req 230).</summary>
		public bool GroundHasAntiAirEscort => Land - AntiAir > 0 && AntiAir > 0;

		/// <summary>Ground and air committed to the same objective (req 231).</summary>
		public bool GroundHasAirSupport => Land > 0 && Air > 0;

		/// <summary>Ground and naval committed to the same objective (req 232).</summary>
		public bool GroundHasNavalSupport => Land > 0 && Naval > 0;

		/// <summary>Ground advance accompanied by a scarce special asset (req 233).</summary>
		public bool GroundHasSpecialSupport => Land > 0 && Special > 0;

		/// <summary>
		/// A concentrated air effort rather than aircraft trickling in one at a time (req 198).
		/// Three is the smallest count that survives a single interceptor and still lands damage.
		/// </summary>
		public bool IsMassAirAttack => Air >= MassAirMinimum;

		/// <summary>
		/// How many distinct combat arms the wave fields. One arm is a blob; three or more is a
		/// genuinely combined operation.
		/// </summary>
		public int ArmsRepresented
		{
			get
			{
				var arms = 0;
				if (Armor > 0) arms++;
				if (Infantry > 0) arms++;
				if (Artillery > 0) arms++;
				if (Air > 0) arms++;
				if (Naval > 0) arms++;
				return arms;
			}
		}

		/// <summary>True when the wave is a combined-arms operation rather than a single-arm push.</summary>
		public bool IsCombinedArms => ArmsRepresented >= 2;

		/// <summary>
		/// Whether a breach force can be followed by a separate exploitation force (reqs 237, 350).
		/// Splitting requires enough units that both halves remain viable; below that, committing
		/// everything to the breach is correct and the split would produce two useless fragments.
		/// </summary>
		public static bool CanSeparateExploitationForce(int committed, int reserve, int minimumViableForce)
		{
			return committed >= minimumViableForce * 2 && reserve >= minimumViableForce;
		}

		/// <summary>Telemetry form; also the string the scenario assertions read.</summary>
		public override string ToString()
		{
			return $"[{Land} land ({Armor} armor, {Infantry} infantry, {Artillery} artillery, {AntiAir} aa), " +
				$"{Air} air, {Naval} naval, {Special} special; arms {ArmsRepresented}]";
		}
	}
}
