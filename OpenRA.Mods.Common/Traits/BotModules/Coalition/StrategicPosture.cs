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
			int ownArmy, bool enemyEconomyStrong, bool expansionOpportunity = false,
			bool recentlyDefended = false, float casualtyFraction = 0f)
		{
			if (ownArmy < 8)
				return StrategicPosture.Opening;

			if (casualtyFraction >= 0.5f)
				return StrategicPosture.Recovery;

			if (enemyToFriendlyRatio >= 3f)
				return StrategicPosture.Desperation;

			if (recentlyDefended && enemyToFriendlyRatio < 1f)
				return StrategicPosture.Counterattack;

			if (enemyToFriendlyRatio >= 1.5f)
				return StrategicPosture.Defensive;

			if (expansionOpportunity && enemyToFriendlyRatio <= 1f)
				return StrategicPosture.Expansion;

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

		/// <summary>
		/// Selects an override for one theater from that region's own control, pressure, and expansion
		/// value. This deliberately does not accept the global force ratio: fronts must be able to make
		/// different decisions during the same review.
		/// </summary>
		public static StrategicPosture SelectLocal(float friendlyControl, float enemyPressure, float expansionValue)
		{
			if (enemyPressure > 0.5f && enemyPressure > friendlyControl)
				return StrategicPosture.Defensive;

			if (friendlyControl >= 0.6f && friendlyControl >= enemyPressure * 2f && enemyPressure > 0f)
				return StrategicPosture.Breakthrough;

			if (expansionValue >= 0.6f && enemyPressure <= 0.2f)
				return StrategicPosture.Expansion;

			return StrategicPosture.None;
		}

		/// <summary>
		/// The operational constraints implied by a posture. Keeping them in one exhaustive mapping
		/// prevents target selection, production, reserves, combat risk, expansion, and secondary
		/// operations from silently drifting into contradictory stances.
		/// </summary>
		public static PosturePolicy PolicyFor(StrategicPosture posture)
		{
			return posture switch
			{
				StrategicPosture.Opening => new(["recon", "base_defense"], 0.20f, 4, 0, 0.05f, 0.30f, false),
				StrategicPosture.Expansion => new(["recon", "base_defense"], 0.20f, 4, 1, 0.10f, 0.35f, false),
				StrategicPosture.Pressure => new(["anti_armor", "artillery"], 0.45f, 5, 0, 0.20f, 0.25f, false),
				StrategicPosture.Containment => new(["artillery", "base_defense"], 0.30f, 4, 0, 0.15f, 0.35f, false),
				StrategicPosture.Attrition => new(["artillery", "anti_armor"], 0.35f, 4, -1, 0.15f, 0.35f, false),
				StrategicPosture.Breakthrough => new(["artillery", "anti_armor"], 0.60f, 6, -1, 0.25f, 0.20f, false),
				StrategicPosture.Siege => new(["artillery", "anti_air"], 0.35f, 4, -1, 0.20f, 0.35f, false),
				StrategicPosture.Raiding => new(["recon", "transport"], 0.35f, 5, 0, 0.30f, 0.30f, false),
				StrategicPosture.Defensive => new(["base_defense", "anti_air"], 0.15f, 3, -1, 0.05f, 0.55f, false),
				StrategicPosture.Counterattack => new(["anti_armor", "recon"], 0.50f, 5, -1, 0.20f, 0.25f, false),
				StrategicPosture.Recovery => new(["base_defense"], 0.10f, 3, -1, 0f, 0.60f, false),
				StrategicPosture.Desperation => new(["base_defense", "anti_armor"], 0.75f, 10, -1, 0.10f, 0.15f, true),
				StrategicPosture.AllIn => new(["anti_armor", "artillery"], 0.85f, 10, -1, 0.10f, 0.10f, true),
				_ => new([], 0.30f, 4, 0, 0.10f, 0.30f, false)
			};
		}
	}

	/// <summary>Immutable cross-system policy derived from one strategic posture.</summary>
	public sealed class PosturePolicy
	{
		public readonly IReadOnlyList<string> ProductionCapabilities;
		public readonly float AcceptableLossFraction;
		public readonly int ReserveFraction;
		public readonly int ExpansionPriority;
		public readonly float SecondaryOperationBudget;
		public readonly float RequiredDefensiveFraction;
		public readonly bool CommitReserve;

		public PosturePolicy(IReadOnlyList<string> productionCapabilities, float acceptableLossFraction,
			int reserveFraction, int expansionPriority, float secondaryOperationBudget,
			float requiredDefensiveFraction, bool commitReserve)
		{
			ProductionCapabilities = productionCapabilities;
			AcceptableLossFraction = acceptableLossFraction;
			ReserveFraction = reserveFraction;
			ExpansionPriority = expansionPriority;
			SecondaryOperationBudget = secondaryOperationBudget;
			RequiredDefensiveFraction = requiredDefensiveFraction;
			CommitReserve = commitReserve;
		}
	}
}
