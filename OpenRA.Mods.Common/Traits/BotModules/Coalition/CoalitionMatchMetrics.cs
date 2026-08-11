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

		/// <summary>Records one sample of the coalition's state.</summary>
		public void Sample(float friendlyValue, float enemyValue, float idleFraction, float cohesion, float cash)
		{
			samples++;

			// Losses are measured as the drop from the highest value seen. This under-counts when
			// production outpaces destruction, but is a stable deterministic proxy for real losses.
			if (friendlyValue > friendlyPeak)
				friendlyPeak = friendlyValue;
			else if (friendlyPeak > 0)
				friendlyValueLost += friendlyPeak - friendlyValue;

			if (enemyValue > enemyPeak)
				enemyPeak = enemyValue;
			else if (enemyPeak > 0)
				enemyValueDestroyed += enemyPeak - enemyValue;

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

		/// <summary>One-line quality summary for the telemetry log.</summary>
		public string Summary()
		{
			return samples == 0
				? "Match metrics: no samples"
				: $"Match metrics: exchange {ExchangeRatio:0.00} (enemy {enemyValueDestroyed:0} / friendly {friendlyValueLost:0} lost), " +
					$"avg idle {AverageIdleFraction * 100:0}%, cohesion {AverageCohesion:0.00}, avg cash {AverageCash:0}, samples {samples}";
		}
	}
}
