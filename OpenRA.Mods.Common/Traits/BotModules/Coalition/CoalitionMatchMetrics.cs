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
	/// <summary>
	/// Aggregates match-quality metrics from periodic samples: friendly and enemy combat value
	/// destroyed (via peak-alive deltas, which is robust when production is continuous), the
	/// exchange ratio, army idle fraction, force cohesion, and floating cash. Pure math, so the
	/// sampler feeds it blackboard data and it can be unit-tested without a World.
	/// </summary>
	public sealed class CoalitionMatchMetrics
	{
		int samples;
		float friendlyPeak;
		float enemyPeak;
		float friendlyValueLost;
		float enemyValueDestroyed;
		float idleFractionSum;
		float cohesionSum;
		float cashSum;

		// Economic damage: refineries/harvesters destroyed (friendly and enemy), tracked via peak deltas.
		int friendlyRefineryPeak;
		int friendlyRefineryLosses;
		int enemyRefineryPeak;
		int enemyRefineryLosses;

		// Expansion timings: ticks when MCVs deployed (req 608).
		readonly List<int> expansionTimings = [];

		// Synchronization errors: (tick, errorTicks) from wave launches (req 612).
		readonly List<(int Tick, int ErrorTicks)> synchronizationErrors = [];

		// Retreat timings: (tick, unitCount) when retreats happen (req 614).
		readonly List<(int Tick, int UnitCount)> retreatTimings = [];

		// Recon efficiency: (missionsSent, usefulIntelGained) (req 616).
		int reconMissionsSent;
		int reconUsefulIntel;

		// Transport survival: (total, survived) (req 617).
		int transportTotal;
		int transportSurvived;

		// Counterattack effectiveness: (counterattacks, enemyDestroyed) (req 620).
		int counterattacksLaunched;
		int counterattackEnemyDestroyed;

		// Base defense response time: (threatTick, responseTick) pairs (req 621).
		readonly List<(int ThreatTick, int ResponseTick)> baseDefenseResponseTimes = [];

		// Win/loss result (set at game end).
		public bool? Won;

		/// <summary>The most recent combat win-ratio estimate, for comparing predictions against actual outcomes.</summary>
		public float LastWinRatioEstimate;

		/// <summary>Records the commander's current win-ratio estimate (predicted-vs-actual telemetry).</summary>
		public void RecordEstimate(float winRatio)
		{
			LastWinRatioEstimate = winRatio;
		}

		/// <summary>Records one sample of the coalition's state.</summary>
		public void Sample(float friendlyValue, float enemyValue, float idleFraction, float cohesion, float cash)
		{
			samples++;

			// Losses are measured as the drop from the highest value seen. This under-counts when
			// production outpaces destruction, but is a stable deterministic proxy for real losses.
			if (friendlyValue > friendlyPeak)
				friendlyPeak = friendlyValue;
			else if (friendlyPeak > 0 && friendlyValue < friendlyPeak)
			{
				friendlyValueLost += friendlyPeak - friendlyValue;
				friendlyPeak = friendlyValue;
			}

			if (enemyValue > enemyPeak)
				enemyPeak = enemyValue;
			else if (enemyPeak > 0 && enemyValue < enemyPeak)
			{
				enemyValueDestroyed += enemyPeak - enemyValue;
				enemyPeak = enemyValue;
			}

			idleFractionSum += idleFraction;
			cohesionSum += cohesion;
			cashSum += cash;
		}

		public int Samples => samples;
		public float FriendlyValueLost => friendlyValueLost;
		public float EnemyValueDestroyed => enemyValueDestroyed;

		/// <summary>Destroyed/lost ratio; 1 means we traded evenly, above 1 we came out ahead.</summary>
		public float ExchangeRatio => friendlyValueLost <= 0 ? (enemyValueDestroyed > 0 ? 1f : 0f)
			: enemyValueDestroyed / friendlyValueLost;

		public float AverageIdleFraction => samples == 0 ? 0f : idleFractionSum / samples;

		public float AverageCohesion => samples == 0 ? 0f : cohesionSum / samples;

		public float AverageCash => samples == 0 ? 0f : cashSum / samples;

		public int FriendlyRefineryLosses => friendlyRefineryLosses;
		public int EnemyRefineryLosses => enemyRefineryLosses;
		/// <summary>Expansion (MCV deployment) ticks, in order (req 608).</summary>
		public IReadOnlyList<int> ExpansionTimings => expansionTimings;

		/// <summary>Base-defense response times as (threatTick, responseTick) pairs (req 621).</summary>
		public IReadOnlyList<(int ThreatTick, int ResponseTick)> BaseDefenseResponseTime => baseDefenseResponseTimes;

		/// <summary>Recon missions sent and how many produced useful intel (req 616).</summary>
		public ReconStats ReconEfficiency => new(reconMissionsSent, reconUsefulIntel);

		/// <summary>Transport missions launched and how many survived (req 617).</summary>
		public TransportStats TransportSurvivalCount => new(transportTotal, transportSurvived);

		/// <summary>Counterattacks launched and enemy units destroyed (req 620).</summary>
		public CounterattackStats CounterattackEffectiveness => new(counterattacksLaunched, counterattackEnemyDestroyed);

		public readonly record struct ReconStats(int MissionsSent, int UsefulIntelGained);

		public readonly record struct TransportStats(int Total, int Survived);

		public readonly record struct CounterattackStats(int Counterattacks, int EnemyDestroyed);


		/// <summary>Records one sample of economic infrastructure (refinery counts) for damage tracking.</summary>
		public void SampleEconomy(int friendlyRefineries, int enemyRefineries)
		{
			if (friendlyRefineries > friendlyRefineryPeak)
				friendlyRefineryPeak = friendlyRefineries;
			else if (friendlyRefineryPeak > 0 && friendlyRefineries < friendlyRefineryPeak)
			{
				friendlyRefineryLosses += friendlyRefineryPeak - friendlyRefineries;
				friendlyRefineryPeak = friendlyRefineries;
			}

			if (enemyRefineries > enemyRefineryPeak)
				enemyRefineryPeak = enemyRefineries;
			else if (enemyRefineryPeak > 0 && enemyRefineries < enemyRefineryPeak)
			{
				enemyRefineryLosses += enemyRefineryPeak - enemyRefineries;
				enemyRefineryPeak = enemyRefineries;
			}
		}

		/// <summary>Records the final win/loss result at game end.</summary>
		public void RecordResult(bool won)
		{
			Won = won;
		}

		/// <summary>Records the tick of an MCV deployment/expansion (req 608).</summary>
		public void RecordExpansion(int tick)
		{
			expansionTimings.Add(tick);
		}

		/// <summary>Records a wave-launch synchronization error (req 612).</summary>
		public void RecordSyncError(int tick, int errorTicks)
		{
			synchronizationErrors.Add((tick, errorTicks));
		}

		/// <summary>Records a retreat event with the unit count that withdrew (req 614).</summary>
		public void RecordRetreat(int tick, int unitCount)
		{
			retreatTimings.Add((tick, unitCount));
		}

		/// <summary>Records a recon mission and whether it produced useful intel (req 616).</summary>
		public void RecordReconMission(bool usefulIntel)
		{
			reconMissionsSent++;
			if (usefulIntel)
				reconUsefulIntel++;
		}

		/// <summary>Records a transport mission outcome (req 617).</summary>
		public void RecordTransport(bool survived)
		{
			transportTotal++;
			if (survived)
				transportSurvived++;
		}

		/// <summary>Records a counterattack launch and enemy units destroyed (req 620).</summary>
		public void RecordCounterattack(int enemyDestroyed)
		{
			counterattacksLaunched++;
			counterattackEnemyDestroyed += enemyDestroyed;
		}

		/// <summary>Records a base-defense response time from threat detection to response (req 621).</summary>
		public void RecordBaseDefenseResponse(int threatTick, int responseTick)
		{
			baseDefenseResponseTimes.Add((threatTick, responseTick));
		}

		/// <summary>One-line quality summary for the telemetry log.</summary>
		public string Summary()
		{
			return samples == 0
				? "Match metrics: no samples"
				: $"Match metrics: exchange {ExchangeRatio:0.00} (enemy {enemyValueDestroyed:0} / friendly {friendlyValueLost:0} lost), " +
					$"econ dmg (enemy refineries lost {enemyRefineryLosses}, friendly {friendlyRefineryLosses}), " +
					$"avg idle {AverageIdleFraction * 100:0}%, cohesion {AverageCohesion:0.00}, avg cash {AverageCash:0}, " +
					$"predicted win ratio {LastWinRatioEstimate:0.00}, result {(Won == null ? "ongoing" : Won.Value ? "WIN" : "LOSS")}, samples {samples}, " +
					$"expansions {expansionTimings.Count}, sync errors {synchronizationErrors.Count}, " +
					$"retreats {retreatTimings.Count}, recon {reconMissionsSent}/{reconUsefulIntel} useful, " +
					$"transports {transportSurvived}/{transportTotal} survived, " +
					$"counterattacks {counterattacksLaunched} ({counterattackEnemyDestroyed} destroyed), " +
					$"base defense responses {baseDefenseResponseTimes.Count}";
		}
	}
}
