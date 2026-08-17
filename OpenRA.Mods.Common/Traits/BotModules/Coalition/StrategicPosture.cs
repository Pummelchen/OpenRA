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
	/// <summary>The coalition's strategic posture: its overall operational stance for this review.</summary>
	public enum StrategicPosture
	{
		/// <summary>No local posture set — use the global posture. Only valid as a region's LocalPosture.</summary>
		None,
		Opening,
		Expansion,
		Pressure,
		Containment,
		Attrition,
		Breakthrough,
		Siege,
		Raiding,
		Defensive,
		Counterattack,
		Recovery,
		Desperation,
		AllIn
	}

	/// <summary>
	/// Deterministic strategic-posture selection from the force balance and the enemy's shape. Pure
	/// and engine-free so it can be unit-tested without a World.
	/// </summary>
	public static class PostureSelection
	{
		public static StrategicPosture Select(float enemyToFriendlyRatio, float enemyStaticDefense,
			int ownArmy, bool enemyEconomyStrong)
		{
			if (ownArmy < 8)
				return StrategicPosture.Opening;

			if (enemyToFriendlyRatio >= 3f)
				return StrategicPosture.Desperation;

			if (enemyToFriendlyRatio >= 1.5f)
				return StrategicPosture.Defensive;

			if (enemyStaticDefense > 0.7f)
				return StrategicPosture.Siege;

			if (enemyToFriendlyRatio <= 0.15f)
				return StrategicPosture.AllIn;

			if (enemyToFriendlyRatio <= 0.4f)
				return StrategicPosture.Breakthrough;

			if (enemyToFriendlyRatio <= 0.7f)
				return enemyEconomyStrong ? StrategicPosture.Raiding : StrategicPosture.Pressure;

			// Roughly even: grind the enemy down or contain them.
			return enemyEconomyStrong ? StrategicPosture.Containment : StrategicPosture.Attrition;
		}

		/// <summary>The target-scoring profile a posture implies. The TARGET_WEIGHT_PROFILE env var
		/// (req 723) overrides the profile selection for self-play parameter sweeps: "balanced",
		/// "breakthrough", or "raiding".</summary>
		public static TargetWeights TargetWeightsFor(StrategicPosture posture)
		{
			// Target weight profile from env var (req 723): allows self-play sweeps to force
			// a specific target-scoring profile regardless of the global posture.
			var profile = Environment.GetEnvironmentVariable("TARGET_WEIGHT_PROFILE");
			if (!string.IsNullOrEmpty(profile))
			{
				return profile.ToLowerInvariant() switch
				{
					"breakthrough" => TargetWeights.Breakthrough(),
					"raiding" => TargetWeights.Raiding(),
					_ => TargetWeights.Balanced()
				};
			}

			return posture switch
			{
				StrategicPosture.Breakthrough or StrategicPosture.Siege or StrategicPosture.AllIn
					=> TargetWeights.Breakthrough(),
				StrategicPosture.Raiding => TargetWeights.Raiding(),
				_ => TargetWeights.Balanced()
			};
		}

		/// <summary>Whether the posture should commit the strategic reserve (an all-or-nothing push).</summary>
		public static bool CommitsReserve(StrategicPosture posture)
		{
			return posture is StrategicPosture.AllIn or StrategicPosture.Desperation;
		}
	}
}
