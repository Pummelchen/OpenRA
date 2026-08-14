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
using System.Linq;

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	/// <summary>
	/// Weights applied to each factor of a target score. Different strategic postures shift the
	/// weights so the same target is evaluated differently when raiding, defending, or pushing
	/// for a breakthrough.
	/// </summary>
	public sealed class TargetWeights
	{
		public float StrategicValue = 1f;
		public float EconomicDamage = 1f;
		public float ProductionDenial = 1f;
		public float TechnologyDenial = 1f;
		public float InformationWeight = 1f;
		public float PositionalValue = 1f;
		public float FollowOnOpportunity = 1f;
		public float FriendlyLossRisk = 1f;
		public float TravelCost = 1f;
		public float ReinforcementRisk = 1f;
		public float CounterattackRisk = 1f;
		public float IntelligenceUncertainty = 1f;

		/// <summary>Balanced weights for the default posture.</summary>
		public static TargetWeights Balanced()
		{
			return new TargetWeights();
		}

		/// <summary>Raid posture: economic and production damage matter most.</summary>
		public static TargetWeights Raiding()
		{
			return new TargetWeights
			{
				EconomicDamage = 2.5f,
				ProductionDenial = 2f,
				FriendlyLossRisk = 1.5f
			};
		}

		/// <summary>Siege/breakthrough posture: positional and follow-on value dominate.</summary>
		public static TargetWeights Breakthrough()
		{
			return new TargetWeights
			{
				StrategicValue = 1.5f,
				PositionalValue = 2.5f,
				FollowOnOpportunity = 2f,
				TravelCost = 1.5f
			};
		}
	}

	/// <summary>The evaluated components of one target, for telemetry and debugging.</summary>
	public sealed class TargetScoreBreakdown
	{
		public float StrategicValue;
		public float EconomicDamage;
		public float ProductionDenial;
		public float TechnologyDenial;
		public float InformationValue;
		public float PositionalValue;
		public float FollowOnOpportunity;
		public float FriendlyLossRisk;
		public float TravelCost;
		public float ReinforcementRisk;
		public float CounterattackRisk;
		public float IntelligenceUncertainty;

		public float Total =>
			StrategicValue + EconomicDamage + ProductionDenial + TechnologyDenial + InformationValue
			+ PositionalValue + FollowOnOpportunity
			- FriendlyLossRisk - TravelCost - ReinforcementRisk - CounterattackRisk - IntelligenceUncertainty;
	}

	/// <summary>
	/// Full target evaluation model. Scores an enemy target by its strategic consequence (economy,
	/// production, technology, position, follow-on opportunity) minus the cost of reaching and
	/// holding it (friendly losses, travel, reinforcements, counterattack risk) and the fog cost
	/// (intelligence uncertainty). Pure and deterministic so it is unit-testable without a World.
	/// </summary>
	public static class TargetEvaluator
	{
		/// <summary>Economy structures, by base value.</summary>
		public static float EconomicValue(string actorType)
		{
			switch (actorType)
			{
				case "proc":
					return 10f;
				case "silo":
					return 6f;
				case "harv":
					return 5f;
				default:
					return 0f;
			}
		}

		/// <summary>Production structures (deny the enemy new units).</summary>
		public static float ProductionValue(string actorType)
		{
			switch (actorType)
			{
				case "weap":
				case "barr":
				case "tent":
				case "fact":
				case "spen":
				case "syrd":
				case "afld":
				case "hpad":
					return 10f;
				default:
					return 0f;
			}
		}

		/// <summary>Technology structures (deny advanced units and support powers).</summary>
		public static float TechnologyValue(string actorType)
		{
			switch (actorType)
			{
				case "atek":
				case "stek":
				case "dome":
				case "iron":
				case "pdox":
					return 10f;
				default:
					return 0f;
			}
		}

		/// <summary>Information value: targets that reveal the map or deny the enemy vision.</summary>
		public static float InformationValue(string actorType)
		{
			switch (actorType)
			{
				case "rdr":
				case "dome":
				case "spen":
					return 6f;
				default:
					return 0f;
			}
		}

		/// <summary>Positional value: chokepoint-adjacent or near-water targets control the map.</summary>
		public static float PositionalValue(int regionIndex, CoalitionMapAnalysis map, MovementClass movementClass)
		{
			if (regionIndex < 0 || map == null)
				return 0f;

			// A region that is a chokepoint connector on several borders dominates the map.
			var chokepointExits = map.Chokepoints[(int)movementClass][regionIndex].Count;
			return 2f * chokepointExits;
		}

		/// <summary>Follow-on opportunity: reaching this target opens the adjacent region.</summary>
		public static float FollowOnValue(int regionIndex, CoalitionMapAnalysis map, MovementClass movementClass)
		{
			if (regionIndex < 0 || map == null)
				return 0f;

			return map.Adjacency[(int)movementClass][regionIndex].Count * 0.5f;
		}

		/// <summary>
		/// Scores a target. <paramref name="isEconomy"/>/<paramref name="isProduction"/>/
		/// <paramref name="isTechnology"/> are typically derived from the actor type by the caller.
		/// Uncertainty is 0 for OBSERVED, 0.3 for LAST_KNOWN, 1 for INFERRED/SUSPECTED.
		/// </summary>
		public static TargetScoreBreakdown Score(
			string actorType,
			bool isEconomy,
			bool isProduction,
			bool isTechnology,
			int regionIndex,
			float routeCost,
			float friendlyLossRisk,
			float enemyReinforcementRisk,
			float enemyCounterattackRisk,
			float uncertainty,
			CoalitionMapAnalysis map,
			MovementClass movementClass,
			TargetWeights weights = null)
		{
			weights ??= TargetWeights.Balanced();
			var positional = PositionalValue(regionIndex, map, movementClass);
			var followOn = FollowOnValue(regionIndex, map, movementClass);

			return new TargetScoreBreakdown
			{
				StrategicValue = weights.StrategicValue * (isEconomy || isProduction || isTechnology ? 3f : 1f),
				EconomicDamage = weights.EconomicDamage * (isEconomy ? EconomicValue(actorType) : 0f),
				ProductionDenial = weights.ProductionDenial * (isProduction ? ProductionValue(actorType) : 0f),
				TechnologyDenial = weights.TechnologyDenial * (isTechnology ? TechnologyValue(actorType) : 0f),
				InformationValue = weights.InformationWeight * InformationValue(actorType),
				PositionalValue = weights.PositionalValue * positional,
				FollowOnOpportunity = weights.FollowOnOpportunity * followOn,
				FriendlyLossRisk = weights.FriendlyLossRisk * friendlyLossRisk,
				TravelCost = weights.TravelCost * routeCost,
				ReinforcementRisk = weights.ReinforcementRisk * enemyReinforcementRisk,
				CounterattackRisk = weights.CounterattackRisk * enemyCounterattackRisk,
				IntelligenceUncertainty = weights.IntelligenceUncertainty * uncertainty
			};
		}

		/// <summary>Classifies an actor type into the three denial dimensions.</summary>
		public static (bool Economy, bool Production, bool Technology) Classify(string actorType)
		{
			return (EconomicValue(actorType) > 0, ProductionValue(actorType) > 0, TechnologyValue(actorType) > 0);
		}

		/// <summary>Deterministic main-effort selection: the highest-scoring target is the main effort.</summary>
		public static CPos? SelectMainEffort(CoalitionBlackboard blackboard, Func<CPos, float> scoreAt)
		{
			return blackboard.EnemyIntel
				.Where(i => i.Class == UnitClass.Structure)
				.Select(i => (i.LastSeenCell, Score: scoreAt(i.LastSeenCell)))
				.OrderByDescending(kv => kv.Score)
				.Select(kv => (CPos?)kv.LastSeenCell)
				.FirstOrDefault();
		}
	}
}
