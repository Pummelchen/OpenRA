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
using System.Linq;

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
		float friendlyPeak;
		float enemyPeak;
		float idleFractionSum;
		float cohesionSum;
		float cashSum;
		float productionIdleFractionSum;
		float reserveAvailabilitySum;
		int operationsSamples;

		// Economic damage: refineries/harvesters destroyed (friendly and enemy), tracked via peak deltas.
		int friendlyRefineryPeak;
		int enemyRefineryPeak;

		// Expansion timings: ticks when MCVs deployed (req 608).
		readonly List<int> expansionTimings = [];

		// Synchronization errors: (tick, errorTicks) from wave launches (req 612).
		readonly List<(int Tick, int ErrorTicks)> synchronizationErrors = [];

		// Retreat timings: (tick, unitCount) when retreats happen (req 614).
		readonly List<(int Tick, int UnitCount)> retreatTimings = [];
		int retreatSurvivors;
		int completedRetreats;

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

		// Local combat superiority: engagements fought and how many were fought with superior
		// local numbers (req 613).
		int engagements;
		int engagementsSuperior;

		// Feint effectiveness: feints launched and how many opened a launch window (req 627).
		int feintsLaunched;
		int feintsOpenedWindow;

		// Win/loss result (set at game end).
		public bool? Won;
		public int DurationTicks { get; private set; }

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
			Samples++;

			// Losses are measured as the drop from the highest value seen. This under-counts when
			// production outpaces destruction, but is a stable deterministic proxy for real losses.
			if (friendlyValue > friendlyPeak)
				friendlyPeak = friendlyValue;
			else if (friendlyPeak > 0 && friendlyValue < friendlyPeak)
			{
				FriendlyValueLost += friendlyPeak - friendlyValue;
				friendlyPeak = friendlyValue;
			}

			if (enemyValue > enemyPeak)
				enemyPeak = enemyValue;
			else if (enemyPeak > 0 && enemyValue < enemyPeak)
			{
				EnemyValueDestroyed += enemyPeak - enemyValue;
				enemyPeak = enemyValue;
			}

			idleFractionSum += idleFraction;
			cohesionSum += cohesion;
			cashSum += cash;
		}

		public int Samples { get; private set; }
		public float FriendlyValueLost { get; private set; }
		public float EnemyValueDestroyed { get; private set; }

		/// <summary>Destroyed/lost ratio; 1 means we traded evenly, above 1 we came out ahead.</summary>
		public float ExchangeRatio => FriendlyValueLost <= 0 ? (EnemyValueDestroyed > 0 ? 1f : 0f)
			: EnemyValueDestroyed / FriendlyValueLost;

		public float AverageIdleFraction => Samples == 0 ? 0f : idleFractionSum / Samples;

		public float AverageCohesion => Samples == 0 ? 0f : cohesionSum / Samples;

		public float AverageCash => Samples == 0 ? 0f : cashSum / Samples;

		public float AverageProductionIdleFraction => operationsSamples == 0 ? 0f
			: productionIdleFractionSum / operationsSamples;

		public float AverageReserveAvailability => operationsSamples == 0 ? 0f
			: reserveAvailabilitySum / operationsSamples;

		public int FriendlyRefineryLosses { get; private set; }
		public int EnemyRefineryLosses { get; private set; }

		/// <summary>Expansion (MCV deployment) ticks, in order (req 608).</summary>
		public IReadOnlyList<int> ExpansionTimings => expansionTimings;

		/// <summary>Base-defense response times as (threatTick, responseTick) pairs (req 621).</summary>
		public IReadOnlyList<(int ThreatTick, int ResponseTick)> BaseDefenseResponseTime => baseDefenseResponseTimes;

		/// <summary>Wave count plus average and worst synchronization error in ticks.</summary>
		public SynchronizationStats Synchronization => synchronizationErrors.Count == 0
			? new SynchronizationStats(0, 0f, 0)
			: new SynchronizationStats(synchronizationErrors.Count,
				(float)synchronizationErrors.Average(e => e.ErrorTicks),
				synchronizationErrors.Max(e => e.ErrorTicks));

		/// <summary>Retreat starts and completed outcomes, including the preserved-unit fraction.</summary>
		public RetreatStats RetreatEffectiveness
		{
			get
			{
				var committed = retreatTimings.Sum(e => e.UnitCount);
				return new RetreatStats(retreatTimings.Count, completedRetreats, committed, retreatSurvivors,
					committed == 0 ? 0f : (float)retreatSurvivors / committed);
			}
		}

		/// <summary>Recon missions sent and how many produced useful intel (req 616).</summary>
		public ReconStats ReconEfficiency => new(reconMissionsSent, reconUsefulIntel);

		/// <summary>Transport missions launched and how many survived (req 617).</summary>
		public TransportStats TransportSurvivalCount => new(transportTotal, transportSurvived);

		/// <summary>Counterattacks launched and enemy units destroyed (req 620).</summary>
		public CounterattackStats CounterattackEffectiveness => new(counterattacksLaunched, counterattackEnemyDestroyed);

		/// <summary>Engagements fought and how many were local-superiority (req 613).</summary>
		public LocalSuperiorityStats EngagementSuperiority => new(engagements, engagementsSuperior);

		/// <summary>Feints launched and how many opened a window for the main wave (req 627).</summary>
		public FeintStats FeintEffectiveness => new(feintsLaunched, feintsOpenedWindow);

		public readonly record struct ReconStats(int MissionsSent, int UsefulIntelGained);

		public readonly record struct TransportStats(int Total, int Survived);

		public readonly record struct CounterattackStats(int Counterattacks, int EnemyDestroyed);

		public readonly record struct LocalSuperiorityStats(int Engagements, int Superior);

		public readonly record struct FeintStats(int Feints, int OpenedWindow);

		public readonly record struct SynchronizationStats(int Waves, float AverageErrorTicks, int MaximumErrorTicks);

		public readonly record struct RetreatStats(int Started, int Completed, int UnitsCommitted,
			int UnitsSurvived, float PreservationRate);

		/// <summary>Records production-queue idle time and the fraction of combat power held in reserve.</summary>
		public void SampleOperations(float productionIdleFraction, float reserveAvailability)
		{
			operationsSamples++;
			productionIdleFractionSum += Math.Clamp(productionIdleFraction, 0f, 1f);
			reserveAvailabilitySum += Math.Clamp(reserveAvailability, 0f, 1f);
		}

		/// <summary>Records one sample of economic infrastructure (refinery counts) for damage tracking.</summary>
		public void SampleEconomy(int friendlyRefineries, int enemyRefineries)
		{
			if (friendlyRefineries > friendlyRefineryPeak)
				friendlyRefineryPeak = friendlyRefineries;
			else if (friendlyRefineryPeak > 0 && friendlyRefineries < friendlyRefineryPeak)
			{
				FriendlyRefineryLosses += friendlyRefineryPeak - friendlyRefineries;
				friendlyRefineryPeak = friendlyRefineries;
			}

			if (enemyRefineries > enemyRefineryPeak)
				enemyRefineryPeak = enemyRefineries;
			else if (enemyRefineryPeak > 0 && enemyRefineries < enemyRefineryPeak)
			{
				EnemyRefineryLosses += enemyRefineryPeak - enemyRefineries;
				enemyRefineryPeak = enemyRefineries;
			}
		}

		/// <summary>Records the final win/loss result at game end.</summary>
		public void RecordResult(bool won, int durationTicks = 0)
		{
			Won = won;
			DurationTicks = Math.Max(0, durationTicks);
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
			retreatTimings.Add((tick, Math.Max(0, unitCount)));
		}

		/// <summary>Records how many of the most recently committed retreat force survived withdrawal.</summary>
		public void RecordRetreatOutcome(int survivingUnits)
		{
			if (completedRetreats >= retreatTimings.Count)
				return;

			var committed = retreatTimings[completedRetreats].UnitCount;
			retreatSurvivors += Math.Clamp(survivingUnits, 0, committed);
			completedRetreats++;
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

		/// <summary>Records an engagement and whether the coalition held local numerical superiority (req 613).</summary>
		public void RecordEngagement(bool localSuperiority)
		{
			engagements++;
			if (localSuperiority)
				engagementsSuperior++;
		}

		/// <summary>Records a feint launch (req 627).</summary>
		public void RecordFeintLaunch()
		{
			feintsLaunched++;
		}

		/// <summary>Records that a feint opened a launch window for the main wave (req 627).</summary>
		public void RecordFeintOpenedWindow()
		{
			feintsOpenedWindow++;
		}

		/// <summary>One-line quality summary for the telemetry log.</summary>
		public string Summary()
		{
			var result = Won == null ? "ongoing" : Won.Value ? "WIN" : "LOSS";
			return Samples == 0
				? "Match metrics: no samples"
				: $"Match metrics: exchange {ExchangeRatio:0.00} (enemy {EnemyValueDestroyed:0} / friendly {FriendlyValueLost:0} lost), " +
					$"econ dmg (enemy refineries lost {EnemyRefineryLosses}, friendly {FriendlyRefineryLosses}), " +
					$"avg army idle {AverageIdleFraction * 100:0}%, production idle {AverageProductionIdleFraction * 100:0}%, " +
					$"cohesion {AverageCohesion:0.00}, avg cash {AverageCash:0}, reserve {AverageReserveAvailability * 100:0}%, " +
					$"predicted win ratio {LastWinRatioEstimate:0.00}, result {result}, " +
					$"duration {DurationTicks} ticks, samples {Samples}, " +
					$"expansions {expansionTimings.Count}, sync {Synchronization.AverageErrorTicks:0.0} avg/{Synchronization.MaximumErrorTicks} max ticks, " +
					$"retreats {RetreatEffectiveness.Completed}/{RetreatEffectiveness.Started} complete " +
					$"({RetreatEffectiveness.PreservationRate * 100:0}% preserved), " +
					$"recon {reconMissionsSent}/{reconUsefulIntel} useful, " +
					$"transports {transportSurvived}/{transportTotal} survived, " +
					$"counterattacks {counterattacksLaunched} ({counterattackEnemyDestroyed} destroyed), " +
					$"base defense responses {baseDefenseResponseTimes.Count}, " +
					$"engagements {engagements} ({engagementsSuperior} superior), " +
					$"feints {feintsLaunched} ({feintsOpenedWindow} window)";
		}
	}
}
