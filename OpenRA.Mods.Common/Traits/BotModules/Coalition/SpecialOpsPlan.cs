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
	/// <summary>What a scarce asset is being sent to do, ordered by strategic consequence.</summary>
	public enum SpecialOpsObjective
	{
		None,
		ProductionDenial,
		TechnologyDenial,
		EconomicDenial,
		SupportPowerDenial,
		IsolatedHighValue
	}

	/// <summary>
	/// <para>
	/// Go/no-go arithmetic for committing a scarce asset - Tanya, a spy, an engineer - to a rear-area
	/// operation (reqs 286-301).
	/// </para>
	/// <para>
	/// These assets are irreplaceable in practice, so the decision is not "is there a target" but
	/// "does the expected strategic gain exceed the risk of losing the asset". Expressing that as
	/// arithmetic rather than as a sequence of ifs is what makes it possible to state - and test -
	/// that the AI declines a valuable target it cannot reach, and waits for a window instead of
	/// walking a spy into a dog.
	/// </para>
	/// </summary>
	public readonly struct SpecialOpsPlan
	{
		public readonly SpecialOpsObjective Objective;

		/// <summary>0..1 chance the asset reaches the target and completes the mission (req 295).</summary>
		public readonly float SuccessProbability;

		/// <summary>Strategic value of the objective if it succeeds (req 296).</summary>
		public readonly float StrategicValue;

		/// <summary>0..1 chance the asset is lost attempting it (req 297).</summary>
		public readonly float AssetLossRisk;

		/// <summary>Replacement cost of the asset, in the same units as <see cref="StrategicValue"/>.</summary>
		public readonly float AssetValue;

		public SpecialOpsPlan(SpecialOpsObjective objective, float successProbability,
			float strategicValue, float assetLossRisk, float assetValue)
		{
			Objective = objective;
			SuccessProbability = Math.Clamp(successProbability, 0f, 1f);
			StrategicValue = Math.Max(0f, strategicValue);
			AssetLossRisk = Math.Clamp(assetLossRisk, 0f, 1f);
			AssetValue = Math.Max(0f, assetValue);
		}

		/// <summary>Expected gain net of the expected cost of losing the asset.</summary>
		public float ExpectedValue => SuccessProbability * StrategicValue - AssetLossRisk * AssetValue;

		/// <summary>
		/// Whether the operation is worth launching now. A positive expected value is necessary but
		/// not sufficient: a near-certain loss is refused however rich the prize, because the asset
		/// is not replaceable and a coin-flip on it is not a plan.
		/// </summary>
		public bool ShouldLaunch(float maximumAcceptableRisk = 0.6f)
		{
			return Objective != SpecialOpsObjective.None
				&& ExpectedValue > 0f
				&& AssetLossRisk <= maximumAcceptableRisk;
		}

		/// <summary>
		/// Whether to hold the asset for a better moment rather than abandoning the objective
		/// (req 298): the prize is worth taking, but the current approach is too dangerous.
		/// </summary>
		public bool ShouldWaitForWindow(float maximumAcceptableRisk = 0.6f)
		{
			return Objective != SpecialOpsObjective.None
				&& StrategicValue > AssetValue
				&& AssetLossRisk > maximumAcceptableRisk;
		}

		/// <summary>
		/// Whether an operation already under way is compromised and should abort (req 300). Once the
		/// asset has been detected the approach it planned around no longer exists, so continuing
		/// spends the asset for a mission that is already unlikely to succeed.
		/// </summary>
		public static bool ShouldAbort(bool assetDetected, float remainingSuccessProbability,
			float minimumViableProbability = 0.25f)
		{
			return assetDetected || remainingSuccessProbability < minimumViableProbability;
		}

		/// <summary>
		/// Whether a surviving asset should be extracted rather than left in place (req 301). An
		/// asset that lives is available for the next operation; one abandoned in enemy territory is
		/// a loss that simply has not been counted yet.
		/// </summary>
		public static bool ShouldExtract(bool objectiveComplete, bool assetAlive, bool extractionRouteExists)
		{
			return assetAlive && extractionRouteExists && objectiveComplete;
		}

		/// <summary>
		/// Ranks objectives by strategic consequence (req 289). Denying production or technology
		/// compounds over the rest of the match; destroying an isolated building is a one-off.
		/// </summary>
		public static int ConsequenceRank(SpecialOpsObjective objective)
		{
			return objective switch
			{
				SpecialOpsObjective.TechnologyDenial => 5,
				SpecialOpsObjective.ProductionDenial => 4,
				SpecialOpsObjective.SupportPowerDenial => 3,
				SpecialOpsObjective.EconomicDenial => 2,
				SpecialOpsObjective.IsolatedHighValue => 1,
				_ => 0
			};
		}
	}
}
