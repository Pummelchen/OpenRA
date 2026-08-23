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
	/// <summary>The arms that make up a synchronized operation, in the order they are committed.</summary>
	public enum OperationComponent
	{
		Reconnaissance,
		Deception,
		AirStrike,
		NavalBombardment,
		SpecialOperation,
		GroundAssault,
		Reserve
	}

	/// <summary>
	/// <para>
	/// Time-on-target scheduling for a multi-component operation (reqs 234-236, 253-259).
	/// </para>
	/// <para>
	/// A wave was previously launched by sending every available domain at the target on the same
	/// tick and recording how far apart they actually arrived. That measures synchronization error
	/// but never plans for it: a slow ground column and a fast air strike ordered together arrive
	/// minutes apart, so the strike is spent before the assault is in position. This computes the
	/// launch tick for each component by working backwards from one arrival time, so components with
	/// different travel speeds converge, and expresses the doctrinal offsets - recon first, shaping
	/// and deception before the breach, reserve last - as data rather than as call order.
	/// </para>
	/// </summary>
	public sealed class OperationSchedule
	{
		public readonly record struct Entry(OperationComponent Component, int LaunchTick, int ArrivalTick, int TravelTicks);

		readonly List<Entry> entries = [];

		/// <summary>The tick at which the operation's components are intended to converge.</summary>
		public readonly int TimeOnTarget;

		public IReadOnlyList<Entry> Entries => entries;

		public OperationSchedule(int timeOnTarget)
		{
			TimeOnTarget = timeOnTarget;
		}

		/// <summary>
		/// Doctrinal offset from time-on-target for each component, in ticks. Negative means the
		/// component acts before the main assault arrives: reconnaissance confirms the objective,
		/// deception draws the defender away, and shaping fires suppress it, all before the ground
		/// force is committed. The reserve is deliberately late - it exploits or rescues, and
		/// committing it on the same tick as the breach would make it not a reserve.
		/// </summary>
		public static int DoctrinalOffset(OperationComponent component, int interval)
		{
			var step = Math.Max(1, interval);
			return component switch
			{
				OperationComponent.Reconnaissance => -4 * step,
				OperationComponent.Deception => -3 * step,
				OperationComponent.AirStrike => -step,
				OperationComponent.NavalBombardment => -step,
				OperationComponent.SpecialOperation => -2 * step,
				OperationComponent.GroundAssault => 0,
				OperationComponent.Reserve => 2 * step,
				_ => 0
			};
		}

		/// <summary>
		/// Schedules a component. <paramref name="travelTicks"/> is how long that force needs to
		/// reach the objective, so a slow column is launched earlier than a fast one to arrive at the
		/// same moment (reqs 258, 259).
		/// </summary>
		public Entry Add(OperationComponent component, int travelTicks, int interval)
		{
			var arrival = TimeOnTarget + DoctrinalOffset(component, interval);
			var entry = new Entry(component, arrival - Math.Max(0, travelTicks), arrival, Math.Max(0, travelTicks));
			entries.Add(entry);
			return entry;
		}

		/// <summary>The earliest launch tick across all components: when the operation must begin.</summary>
		public int OperationStartTick => entries.Count == 0 ? TimeOnTarget : entries.Min(e => e.LaunchTick);

		/// <summary>Arrival spread in ticks between the first and last component to reach the objective.</summary>
		public int ArrivalSpread => entries.Count < 2 ? 0
			: entries.Max(e => e.ArrivalTick) - entries.Min(e => e.ArrivalTick);

		/// <summary>True when a component is scheduled to arrive before the ground assault.</summary>
		public bool Precedes(OperationComponent component)
		{
			var ground = entries.FirstOrDefault(e => e.Component == OperationComponent.GroundAssault);
			if (ground.Component != OperationComponent.GroundAssault)
				return false;

			var other = entries.Where(e => e.Component == component).ToArray();
			return other.Length > 0 && other.All(e => e.ArrivalTick < ground.ArrivalTick);
		}

		/// <summary>
		/// Synchronization error for one component: how far its actual arrival missed its planned
		/// one. This is what <see cref="CoalitionMatchMetrics"/> records (req 260).
		/// </summary>
		public static int SynchronizationError(int plannedArrivalTick, int actualArrivalTick)
		{
			return Math.Abs(actualArrivalTick - plannedArrivalTick);
		}

		/// <summary>
		/// Whether an operation is tightly enough synchronized to launch, or whether a component
		/// would arrive so far ahead of its support that it fights alone (req 259).
		/// </summary>
		public bool IsSynchronized(int toleranceTicks)
		{
			return ArrivalSpread <= Math.Max(0, toleranceTicks);
		}
	}
}
