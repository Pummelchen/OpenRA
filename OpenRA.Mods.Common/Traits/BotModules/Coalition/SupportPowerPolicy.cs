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
		Strike
	}

	/// <summary>RA support-power classification and conservative fire policy.</summary>
	public static class SupportPowerPolicy
	{
		public static SupportPowerRole Classify(string orderName)
		{
			return orderName switch
			{
				"SovietSpyPlane" => SupportPowerRole.Recon,
				"SovietParatroopers" => SupportPowerRole.Reinforcement,
				"UkraineParabombs" or "NukePowerInfoOrder" => SupportPowerRole.Strike,
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
				_ => false
			};
		}
	}
}
