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
	/// <summary>The strategies the commander can run. Arms of the bandit, not a priority list.</summary>
	public enum StrategyArm
	{
		/// <summary>Economy first: expand, add refineries, defend what pays.</summary>
		Expand,

		/// <summary>Early pressure: cheap units at the enemy economy before defences exist.</summary>
		Harass,

		/// <summary>Mass a concentrated force and take ground.</summary>
		Assault,

		/// <summary>Reduce a fortified position with artillery before entering.</summary>
		Siege,

		/// <summary>Buy capability the enemy cannot answer.</summary>
		Tech,

		/// <summary>Hold, trade favourably, and rebuild.</summary>
		Consolidate
	}

	/// <summary>
	/// <para>
	/// Strategy selection as a multi-armed bandit, using UCB1 (handbook §15.3).
	/// </para>
	/// <para>
	/// A commander that follows one doctrine is readable after two matches: a human learns that it
	/// always masses at 24 units and plans around it. A portfolio is not. Each strategy is an arm,
	/// the reward is measured progress rather than an assumption, and UCB1's exploration term forces
	/// under-tried arms while decaying as evidence accumulates - so the commander keeps testing
	/// alternatives early and commits to what is working later.
	/// </para>
	/// <para>
	/// This is the self-improving component. Within a match it shifts weight toward what works
	/// against this opponent on this terrain; the same record carried across matches is a prior.
	/// </para>
	/// </summary>
	public sealed class StrategyPortfolio
	{
		sealed class ArmRecord
		{
			public int Plays;
			public float TotalReward;
			public float MeanReward => Plays == 0 ? 0f : TotalReward / Plays;
		}

		readonly Dictionary<StrategyArm, ArmRecord> arms = [];
		readonly float explorationConstant;

		public int TotalPlays { get; private set; }

		/// <summary>
		/// <paramref name="explorationConstant"/> is UCB1's c. Higher explores longer; the classic
		/// value is sqrt(2), which is the default because nothing here justifies tuning it blind.
		/// </summary>
		public StrategyPortfolio(float explorationConstant = 1.41421356f)
		{
			this.explorationConstant = Math.Max(0f, explorationConstant);
			foreach (var arm in Enum.GetValues<StrategyArm>())
				arms[arm] = new ArmRecord();
		}

		/// <summary>
		/// UCB1 score. An arm that has never been tried scores infinite, so every strategy is
		/// attempted once before any is dismissed - a commander that never tries harassment cannot
		/// discover that this opponent is vulnerable to it.
		/// </summary>
		public float Score(StrategyArm arm)
		{
			var record = arms[arm];
			if (record.Plays == 0)
				return float.PositiveInfinity;

			var exploration = explorationConstant
				* MathF.Sqrt(2f * MathF.Log(Math.Max(1, TotalPlays)) / record.Plays);

			return record.MeanReward + exploration;
		}

		/// <summary>
		/// Picks the next strategy from those the situation actually permits. Feasibility is the
		/// caller's business - there is no point selecting Siege with no artillery - and the bandit
		/// only ranks what is offered.
		/// </summary>
		public StrategyArm Select(IEnumerable<StrategyArm> feasible)
		{
			var options = (feasible ?? []).Distinct().ToArray();
			if (options.Length == 0)
				return StrategyArm.Consolidate;

			return options
				.OrderByDescending(Score)
				.ThenBy(a => (int)a)
				.First();
		}

		/// <summary>
		/// Records what an arm achieved. Reward is normalised progress in [0,1]: ground taken,
		/// economy gained, enemy value destroyed per credit committed. Recording raw kills would
		/// reward the trading behaviour that produces draws.
		/// </summary>
		public void Record(StrategyArm arm, float reward)
		{
			var record = arms[arm];
			record.Plays++;
			record.TotalReward += Math.Clamp(reward, 0f, 1f);
			TotalPlays++;
		}

		public int Plays(StrategyArm arm) => arms[arm].Plays;

		public float MeanReward(StrategyArm arm) => arms[arm].MeanReward;

		/// <summary>The arm with the best observed record, ignoring exploration.</summary>
		public StrategyArm BestKnown()
		{
			return arms.Where(kv => kv.Value.Plays > 0)
				.OrderByDescending(kv => kv.Value.MeanReward)
				.ThenBy(kv => (int)kv.Key)
				.Select(kv => kv.Key)
				.DefaultIfEmpty(StrategyArm.Expand)
				.First();
		}

		/// <summary>One-line telemetry summary of what the portfolio has learned.</summary>
		public string Summary()
		{
			var parts = arms.Where(kv => kv.Value.Plays > 0)
				.OrderBy(kv => (int)kv.Key)
				.Select(kv => $"{kv.Key}={kv.Value.MeanReward:0.00}x{kv.Value.Plays}");

			var joined = string.Join(" ", parts);
			return string.IsNullOrEmpty(joined)
				? "Strategy portfolio: nothing tried yet"
				: $"Strategy portfolio: {joined} (best {BestKnown()})";
		}
	}
}
