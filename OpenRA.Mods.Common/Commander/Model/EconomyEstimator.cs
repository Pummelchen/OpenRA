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

namespace OpenRA.Mods.Common.Commander.Model
{
	/// <summary>
	/// <para>
	/// Estimates what one harvester is currently worth, in credits per second, from what the
	/// player has actually earned.
	/// </para>
	/// <para>
	/// This exists because deriving income from first principles does not survive contact with a
	/// real map. A load is worth capacity times resource value - both stated in the rules - but the
	/// round trip that turns loads into a rate depends on how far the ore is, how much is left, and
	/// whether the near patch is mined out, none of which the rules know. Measured against real
	/// games, a from-first-principles income model was beaten by simply assuming income would not
	/// change, which is the clearest possible signal that it was adding noise rather than
	/// information.
	/// </para>
	/// <para>
	/// So it is measured instead. <c>PlayerResources.Earned</c> is income integrated over time, so
	/// its difference across a sample divided by the harvesters that produced it is exactly the
	/// quantity wanted - and it tracks the map going dry, which no static formula would.
	/// </para>
	/// </summary>
	public sealed class EconomyEstimator
	{
		/// <summary>
		/// Per-sample decay on the accumulated totals. Old evidence fades so the estimate can track
		/// a patch running out, but nothing is ever thrown away in one step.
		/// </summary>
		const float Decay = 0.85f;

		float earnedTotal;
		float harvesterSecondsTotal;

		readonly float initial;

		public EconomyEstimator(float initialIncomePerHarvester = 16f)
		{
			initial = Math.Max(0f, initialIncomePerHarvester);
		}

		/// <summary>Credits per second per harvester, as last measured.</summary>
		public float IncomePerHarvester =>
			harvesterSecondsTotal <= 0f ? initial : earnedTotal / harvesterSecondsTotal;

		public int Samples { get; private set; }

		/// <summary>
		/// <para>
		/// Folds in one observation. <paramref name="earnedDelta"/> is the change in total earnings
		/// across <paramref name="seconds"/>, which is income by definition.
		/// </para>
		/// <para>
		/// The totals are accumulated and then divided, rather than each sample being turned into a
		/// ratio and the ratios averaged. That distinction is not pedantry: a harvester delivers a
		/// load roughly every half minute, so a ten-second sample contains zero, one or two
		/// deliveries, and a window with none contributes a ratio of zero. Averaging those zeroes in
		/// biases the estimate low - which it measurably did, leaving the income forecast worse than
		/// assuming income never changes. Summing first weights each sample by how much evidence it
		/// actually carries.
		/// </para>
		/// </summary>
		public void Observe(int harvesters, float earnedDelta, float seconds)
		{
			if (harvesters <= 0 || seconds <= 0f || earnedDelta < 0f)
				return;

			earnedTotal = (earnedTotal * Decay) + earnedDelta;
			harvesterSecondsTotal = (harvesterSecondsTotal * Decay) + (harvesters * seconds);
			Samples++;
		}
	}
}
