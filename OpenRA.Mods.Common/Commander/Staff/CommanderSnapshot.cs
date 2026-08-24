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
using OpenRA.Mods.Common.Commander.Model;
using OpenRA.Mods.Common.Commander.Terrain;

namespace OpenRA.Mods.Common.Commander.Staff
{
	/// <summary>
	/// <para>
	/// An immutable view of the world for one review cycle, built on the game thread and then handed
	/// to every manager.
	/// </para>
	/// <para>
	/// Immutability is what makes parallel thinking safe. If managers read live world state on
	/// worker threads they would see it mid-mutation, and two managers reading the same actor could
	/// disagree about whether it was alive - which produces a desync that reproduces only under
	/// load, which is the worst class of bug this project could acquire.
	/// </para>
	/// </summary>
	public sealed class CommanderSnapshot
	{
		public int Tick { get; init; }

		/// <summary>Seconds of game time, for managers that reason about match phase.</summary>
		public float Seconds => Tick / (float)AbstractState.TicksPerSecond;

		/// <summary>The abstract state: economy, forces by region, control, visibility.</summary>
		public AbstractState State { get; init; }

		/// <summary>The map decomposed into regions and chokepoints. Constant for the match.</summary>
		public RegionGraph Graph { get; init; }

		/// <summary>Where the enemy is believed to be, including places nobody has looked.</summary>
		public EnemyBelief Belief { get; init; }

		/// <summary>What the opponent is probably doing.</summary>
		public StrategyPosterior Opponent { get; init; }

		/// <summary>Cash in hand, and everything earned so far.</summary>
		public int Cash { get; init; }
		public int Earned { get; init; }
		public int Spent { get; init; }

		/// <summary>Production queues by type, with what each is currently building.</summary>
		public IReadOnlyList<QueueSnapshot> Queues { get; init; } = [];

		/// <summary>Structures owned, by actor name.</summary>
		public IReadOnlyDictionary<string, int> Structures { get; init; } =
			new Dictionary<string, int>();

		/// <summary>Combat units owned, by actor name.</summary>
		public IReadOnlyDictionary<string, int> Units { get; init; } =
			new Dictionary<string, int>();

		/// <summary>Fraction of everything ever earned that is still sitting in the bank.</summary>
		public float BankedFraction => Earned <= 0 ? 0f : Math.Clamp(Cash / (float)Earned, 0f, 1f);

		/// <summary>One production queue as the staff sees it.</summary>
		public readonly record struct QueueSnapshot(string Type, string Building, int QueuedItems)
		{
			public bool IsIdle => string.IsNullOrEmpty(Building);
		}
	}
}
