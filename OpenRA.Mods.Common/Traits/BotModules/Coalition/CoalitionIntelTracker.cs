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
	/// The durable enemy-intelligence tracker that implements the honesty ladder. The commander
	/// observes enemy actors in explored territory every blackboard rebuild; the tracker retains what
	/// was seen and ages it into the correct status:
	/// OBSERVED (visible now) → LAST_KNOWN (was seen, now hidden, position error grows) → and, for
	/// structures that cannot move, INFERRED (still believed present at low confidence). Mobile
	/// intel that outlives the memory window is dropped back to UNKNOWN. Confidence halves every
	/// 30 seconds; counts widen and position error grows as the sighting ages. Pure and deterministic
	/// so it can be unit-tested without a World.
	/// </summary>
	public sealed class CoalitionIntelTracker
	{
		sealed class Entry
		{
			public string Type;
			public UnitClass Class;
			public int Region;
			public CPos LastSeenCell;
			public int LastSeenTick;
			public int ObservedCount;
		}

		readonly Dictionary<(string Type, UnitClass Class, int Region), Entry> entries = [];
		readonly int memoryTicks;
		readonly int timestep;
		readonly float halfLifeSeconds = 30f;
		int lastAgeTick = int.MinValue;
		IReadOnlyList<EnemyIntel> cached;

		public CoalitionIntelTracker(int memoryTicks = 600, int timestep = 40)
		{
			this.memoryTicks = memoryTicks;
			this.timestep = timestep;
		}

		/// <summary>Records a fresh sighting of an enemy type in a region at the current tick.</summary>
		public void Observe(string type, UnitClass unitClass, int region, CPos cell, int tick)
		{
			var key = (type, unitClass, region);
			if (!entries.TryGetValue(key, out var entry))
				entries[key] = entry = new Entry { Type = type, Class = unitClass, Region = region };

			entry.LastSeenCell = cell;
			entry.LastSeenTick = tick;
			entry.ObservedCount++;
		}

		/// <summary>
		/// Produces the aged intel list for the given tick: observed entries (seen this tick),
		/// last-known mobile entries within memory, and inferred structures. Idempotent within a tick.
		/// </summary>
		public IReadOnlyList<EnemyIntel> Age(int tick)
		{
			if (tick == lastAgeTick)
				return cached;

			lastAgeTick = tick;
			var result = new List<EnemyIntel>();
			var expired = new List<(string Type, UnitClass Class, int Region)>();

			foreach (var entry in entries.Values)
			{
				var ageTicks = tick - entry.LastSeenTick;
				var ageSeconds = ageTicks * timestep / 1000f;
				var observed = entry.LastSeenTick == tick;
				var isStructure = entry.Class == UnitClass.Structure;
				var confidence = observed ? 1f : MathF.Pow(0.5f, ageSeconds / halfLifeSeconds);

				if (observed)
				{
					result.Add(Make(entry, ageTicks, 1f, entry.ObservedCount, entry.ObservedCount, entry.ObservedCount,
						IntelStatus.Observed, 0));
					entry.ObservedCount = 0;
				}
				else if (isStructure)
				{
					// Structures do not move: they remain inferred at a confidence floor.
					result.Add(Make(entry, ageTicks, MathF.Max(0.3f, confidence), 0, 1, 1, IntelStatus.Inferred, 0));
				}
				else if (ageTicks <= memoryTicks)
				{
					// Mobile: last known while within memory; position error and max count widen with age.
					var positionError = Math.Clamp((int)(ageSeconds / 5f), 1, 40);
					var min = ageSeconds > 60 ? 0 : 1;
					var max = 1 + (int)(ageSeconds / 20f);
					result.Add(Make(entry, ageTicks, confidence, min, 1, max, IntelStatus.LastKnown, positionError));
				}
				else
				{
					expired.Add((entry.Type, entry.Class, entry.Region));
				}
			}

			foreach (var key in expired)
				entries.Remove(key);

			cached = result;
			return cached;
		}

		static EnemyIntel Make(Entry entry, int ageTicks, float confidence, int min, int expected, int max,
			IntelStatus status, int positionErrorCells)
		{
			return new EnemyIntel(entry.Type, entry.Class)
			{
				LastSeenCell = entry.LastSeenCell,
				LastSeenTick = entry.LastSeenTick,
				Confidence = confidence,
				MinCount = min,
				ExpectedCount = expected,
				MaxCount = max,
				Status = status,
				AgeTicks = ageTicks,
				PositionErrorCells = positionErrorCells
			};
		}
	}
}
