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

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	public enum SupportPowerRole
	{
		Unsupported,
		Recon,
		Reinforcement,
		Strike,

		/// <summary>Teleports a friendly force onto an objective (Chronosphere).</summary>
		Redeployment,

		/// <summary>Makes a committed friendly force temporarily invulnerable (Iron Curtain).</summary>
		Protection
	}

	/// <summary>RA support-power classification and conservative fire policy.</summary>
	public static class SupportPowerPolicy
	{
		/// <summary>
		/// Minimum friendly units that must already be committed at the target before a force-multiplier
		/// power (Chronosphere, Iron Curtain) is worth spending. Below this the power is wasted on a
		/// force too small to convert the advantage.
		/// </summary>
		public const int MinimumEscortedForce = 3;

		public static SupportPowerRole Classify(string orderName)
		{
			return orderName switch
			{
				"SovietSpyPlane" => SupportPowerRole.Recon,
				"SovietParatroopers" => SupportPowerRole.Reinforcement,
				"UkraineParabombs" or "NukePowerInfoOrder" => SupportPowerRole.Strike,
				"Chronoshift" or "AdvancedChronoshift" => SupportPowerRole.Redeployment,
				"GrantExternalConditionPowerInfoOrder" => SupportPowerRole.Protection,
				_ => SupportPowerRole.Unsupported
			};
		}

		public static bool ShouldFire(SupportPowerRole role, float targetValue,
			int friendlyUnitsNearTarget, bool shapingWindowOpen)
		{
			if (role == SupportPowerRole.Unsupported || !shapingWindowOpen)
				return false;

			return role switch
			{
				SupportPowerRole.Recon => true,
				SupportPowerRole.Reinforcement => targetValue >= 1f,
				SupportPowerRole.Strike => targetValue >= 3f
					&& !StrategicBrainBotModule.ShouldWithholdSupportPower(friendlyUnitsNearTarget),

				// Force multipliers invert the friendly-fire rule: they are only worth firing when a
				// real friendly force is already committed at a target worth the investment.
				SupportPowerRole.Redeployment => targetValue >= 3f
					&& friendlyUnitsNearTarget >= MinimumEscortedForce,
				SupportPowerRole.Protection => targetValue >= 2f
					&& friendlyUnitsNearTarget >= MinimumEscortedForce,
				_ => false
			};
		}

		/// <summary>True for powers that buff or move friendly forces rather than damaging the enemy.</summary>
		public static bool IsForceMultiplier(SupportPowerRole role)
		{
			return role is SupportPowerRole.Redeployment or SupportPowerRole.Protection;
		}
	}
}
