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
	/// Income as a queueing problem (handbook §15.4). Ore is not background activity - it is the
	/// rate that sets everything else, and the commander should compute it rather than assume it.
	/// </para>
	/// <para>
	/// Little's Law applies directly: income is harvesters times load divided by round-trip time.
	/// The consequences are not obvious from unit counts, which is why they need computing. A
	/// refinery placed next to ore can be worth more than another harvester, because travel usually
	/// dominates the round trip. And the marginal harvester is worth exactly zero once the refinery's
	/// unload queue saturates - at which point it is 1100 credits of nothing.
	/// </para>
	/// </summary>
	public static class HarvesterEconomics
	{
		/// <summary>
		/// Round-trip time in seconds: out to the ore, harvest, back, unload. Travel is counted both
		/// ways because that is where the time actually goes on a mid-size map.
		/// </summary>
		public static float RoundTripSeconds(float distanceCells, float speedCellsPerSecond,
			float harvestSeconds, float unloadSeconds)
		{
			if (speedCellsPerSecond <= 0f)
				return float.PositiveInfinity;

			return 2f * Math.Max(0f, distanceCells) / speedCellsPerSecond
				+ Math.Max(0f, harvestSeconds) + Math.Max(0f, unloadSeconds);
		}

		/// <summary>
		/// Credits per second from a harvester fleet, capped by refinery unload throughput. The cap
		/// is the point: without it the model recommends buying harvesters forever.
		/// </summary>
		public static float IncomePerSecond(int harvesters, float loadValue, float roundTripSeconds,
			int refineries, float unloadSeconds)
		{
			if (harvesters <= 0 || loadValue <= 0f || roundTripSeconds <= 0f
				|| float.IsInfinity(roundTripSeconds))
				return 0f;

			var demand = harvesters * loadValue / roundTripSeconds;

			if (refineries <= 0 || unloadSeconds <= 0f)
				return 0f;

			// A refinery can absorb one load per unload cycle; beyond that harvesters queue.
			var capacity = refineries * loadValue / unloadSeconds;
			return Math.Min(demand, capacity);
		}

		/// <summary>
		/// Extra credits per second from one more harvester. Zero once the refineries are saturated,
		/// which is the signal to build a refinery instead of another harvester.
		/// </summary>
		public static float MarginalHarvesterValue(int harvesters, float loadValue,
			float roundTripSeconds, int refineries, float unloadSeconds)
		{
			var before = IncomePerSecond(harvesters, loadValue, roundTripSeconds, refineries, unloadSeconds);
			var after = IncomePerSecond(harvesters + 1, loadValue, roundTripSeconds, refineries, unloadSeconds);
			return Math.Max(0f, after - before);
		}

		/// <summary>Extra credits per second from one more refinery at the same distance.</summary>
		public static float MarginalRefineryValue(int harvesters, float loadValue,
			float roundTripSeconds, int refineries, float unloadSeconds)
		{
			var before = IncomePerSecond(harvesters, loadValue, roundTripSeconds, refineries, unloadSeconds);
			var after = IncomePerSecond(harvesters, loadValue, roundTripSeconds, refineries + 1, unloadSeconds);
			return Math.Max(0f, after - before);
		}

		/// <summary>
		/// Which purchase buys more income per credit. This is the decision the community advice
		/// ("place refineries next to ore") encodes, made explicit so the commander can act on it
		/// with the actual distances rather than a rule of thumb.
		/// </summary>
		public static bool RefineryBeatsHarvester(int harvesters, float loadValue, float roundTripSeconds,
			int refineries, float unloadSeconds, int harvesterCost, int refineryCost)
		{
			if (harvesterCost <= 0 || refineryCost <= 0)
				return false;

			var perHarvesterCredit = MarginalHarvesterValue(harvesters, loadValue, roundTripSeconds,
				refineries, unloadSeconds) / harvesterCost;
			var perRefineryCredit = MarginalRefineryValue(harvesters, loadValue, roundTripSeconds,
				refineries, unloadSeconds) / refineryCost;

			return perRefineryCredit > perHarvesterCredit;
		}

		/// <summary>
		/// Payback time in seconds for an investment. An expansion that pays for itself after the
		/// match would have ended is not an expansion, it is a donation.
		/// </summary>
		public static float PaybackSeconds(int cost, float extraIncomePerSecond)
		{
			return extraIncomePerSecond <= 0f ? float.PositiveInfinity : cost / extraIncomePerSecond;
		}

		/// <summary>
		/// Value of an expansion site: ore volume per unit of round-trip time, discounted by the
		/// risk of holding it. Distance enters twice - once in the trip and once in the risk - which
		/// is why a rich patch on the far side of the map is usually worth less than a modest one
		/// nearby.
		/// </summary>
		public static float ExpansionValue(float oreVolume, float roundTripSeconds, float risk)
		{
			if (oreVolume <= 0f || roundTripSeconds <= 0f || float.IsInfinity(roundTripSeconds))
				return 0f;

			return oreVolume / roundTripSeconds * (1f - Math.Clamp(risk, 0f, 1f));
		}

		/// <summary>
		/// Whether income has dropped enough to treat as an emergency. A fall in income is as
		/// serious as losing a production building, because it is the same loss arriving slowly.
		/// </summary>
		public static bool IsEconomicEmergency(float currentIncome, float peakIncome, float threshold = 0.6f)
		{
			return peakIncome > 0f && currentIncome < peakIncome * Math.Clamp(threshold, 0f, 1f);
		}
	}
}
