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

namespace OpenRA.Mods.Common.Commander.Model
{
	/// <summary>Which side an entry belongs to, from this commander's point of view.</summary>
	public enum Allegiance
	{
		Self,
		Ally,
		Enemy,
	}

	/// <summary>What the commander currently believes about a thing it has seen.</summary>
	public enum RecordStatus
	{
		/// <summary>Seen now.</summary>
		Live,

		/// <summary>Seen once, not visible now. It may have moved, been repaired, or be gone.</summary>
		Stale,

		/// <summary>Watched it die. The one status that is certain.</summary>
		Destroyed,
	}

	/// <summary>
	/// One thing the commander knows about, and everything it knows about it.
	/// </summary>
	public sealed class DatabaseEntry
	{
		public uint ActorId { get; init; }
		public string Type { get; init; } = "";
		public bool IsStructure { get; init; }
		public Allegiance Side { get; init; }

		/// <summary>Where it was when last seen. For our own actors this is simply where it is.</summary>
		public CPos LastKnownCell { get; set; }

		/// <summary>
		/// Which region that cell falls in, resolved once when the sighting is recorded. Managers
		/// reason in regions and have no Map, deliberately - handing them one would let them read
		/// live world state on a worker thread, which is the one thing the snapshot design exists
		/// to prevent.
		/// </summary>
		public int Region { get; set; } = -1;

		public int FirstSeenTick { get; init; }
		public int LastSeenTick { get; set; }
		public RecordStatus Status { get; set; } = RecordStatus.Live;

		/// <summary>Health when last seen, 0-1. Damage over time is how a siege is distinguished from a stalemate.</summary>
		public float HealthFraction { get; set; } = 1f;

		/// <summary>Which manager last confirmed this entry, so a stale reading can be traced to who took it.</summary>
		public string ObservedBy { get; set; } = "";

		/// <summary>The last order issued for this actor, who issued it, and when.</summary>
		public string LastOrder { get; set; }
		public string OrderedBy { get; set; }
		public int OrderedTick { get; set; } = -1;

		/// <summary>
		/// When a manager last actually did something about this actor, and which one.
		/// </summary>
		/// <remarks>
		/// Distinct from <see cref="LastSeenTick"/> on purpose. Being looked at is not being looked
		/// after: a damaged power plant sits in plain sight of the whole staff for a whole match
		/// while nobody repairs it, and by every visibility measure it is perfectly well attended.
		/// </remarks>
		public int LastAttendedTick { get; set; } = -1;
		public string AttendedBy { get; set; } = "";

		/// <summary>Times this actor type has been seen rebuilt at this position after being destroyed.</summary>
		public int RebuildCount { get; set; }

		public int TicksSinceSeen(int now) => Math.Max(0, now - LastSeenTick);

		public float SecondsSinceSeen(int now) => TicksSinceSeen(now) / (float)AbstractState.TicksPerSecond;

		/// <summary>Seconds since any manager did anything about this actor. Never attended reads as forever.</summary>
		public float SecondsSinceAttended(int now) =>
			LastAttendedTick < 0 ? float.MaxValue
			: Math.Max(0, now - LastAttendedTick) / (float)AbstractState.TicksPerSecond;

		public float SecondsSinceOrdered(int now) =>
			OrderedTick < 0 ? float.MaxValue : Math.Max(0, now - OrderedTick) / (float)AbstractState.TicksPerSecond;

		public override string ToString() =>
			$"{Type}#{ActorId} {Side} {Status} @{LastKnownCell}" +
			(string.IsNullOrEmpty(LastOrder) ? "" : $" order={LastOrder} by={OrderedBy}");
	}

	/// <summary>
	/// <para>
	/// Everything the commander has seen this match, in memory, for this match only: every unit and
	/// every structure on either side, what state it was in, how long ago that was, who looked, and
	/// what order was last given about it.
	/// </para>
	/// <para>
	/// The staff previously had no shared memory at all. Each manager rebuilt its own view from the
	/// world every time it ran, so nothing any of them learned outlived a single cycle and no two of
	/// them could agree on what they were looking at. Several measured defects trace straight to
	/// that: an "enemy composition" read that classified an opponent from whatever happened to be on
	/// screen, an assault declared won because nothing hostile was VISIBLE at the target, and a
	/// commander that besieged the same construction yard twenty-six times without noticing that it
	/// was being rebuilt behind it.
	/// </para>
	/// <para>
	/// <b>Age is recorded rather than assumed.</b> Every entry carries the tick it was last actually
	/// observed, so a manager can tell the difference between "there is nothing there" and "nobody
	/// has looked for four minutes". Those are opposite conclusions and this commander has already
	/// been measured acting on the first when only the second was true.
	/// </para>
	/// <para>
	/// <b>Determinism.</b> Entries live in a dictionary for lookup and are always enumerated through
	/// <see cref="All"/>, which orders by actor id. Iterating dictionary order would make the
	/// commander's decisions depend on hash layout, and in a lockstep simulation with sync hashing
	/// that is a desync rather than a curiosity. Writes happen on the game thread only; managers
	/// read during the parallel phase, when nothing is writing.
	/// </para>
	/// </summary>
	public sealed class WorldDatabase
	{
		readonly Dictionary<uint, DatabaseEntry> entries = [];

		/// <summary>
		/// Enemy structures destroyed, by type, and how many of those have since been replaced.
		/// </summary>
		/// <remarks>
		/// Keyed by type rather than by the cell the destroyed one stood on, and that was measured.
		/// Same-ground rebuilding is rare - across a full match against the rushing opponent, with
		/// two hundred and eight enemy actors destroyed, it happened exactly zero times, so the
		/// counter read zero all match and told the commander nothing. What an opponent actually
		/// does is put the replacement somewhere else: that same opponent finished a long match
		/// holding a hundred and forty-two structures against eighty-nine destroyed. Replacement by
		/// type catches that; replacement by address does not.
		/// </remarks>
		readonly Dictionary<string, int> razedByType = [];
		readonly Dictionary<string, int> replacedByType = [];

		/// <summary>Tick of the most recent update, so readers can age entries without a World.</summary>
		public int Tick { get; private set; }

		/// <summary>How many enemy structures have been seen rebuilt after being destroyed.</summary>
		public int EnemyRebuilds { get; private set; }

		public int Count => entries.Count;

		/// <summary>Every entry, ordered by actor id so that iteration order never depends on hashing.</summary>
		public IEnumerable<DatabaseEntry> All => entries.Values.OrderBy(e => e.ActorId);

		public DatabaseEntry Find(uint actorId) => entries.GetValueOrDefault(actorId);

		/// <summary>Opens a new match. The database is per-match memory and holds nothing across one.</summary>
		public void Clear()
		{
			entries.Clear();
			razedByType.Clear();
			replacedByType.Clear();
			EnemyRebuilds = 0;
			Tick = 0;
		}

		/// <summary>
		/// Records a sighting. Called on the game thread for everything currently observable; an
		/// actor that is not passed this tick simply keeps the age it already had, which is the
		/// point of the database.
		/// </summary>
		public void Observe(uint actorId, string type, bool isStructure, Allegiance side,
			CPos cell, float healthFraction, int tick, string observedBy, int region = -1)
		{
			Tick = Math.Max(Tick, tick);

			if (!entries.TryGetValue(actorId, out var entry))
			{
				entry = new DatabaseEntry
				{
					ActorId = actorId,
					Type = type ?? "",
					IsStructure = isStructure,
					Side = side,
					FirstSeenTick = tick,
				};

				entries[actorId] = entry;

				// A structure standing where one was destroyed is the opponent replacing what it
				// lost. Counting that is the difference between "we are winning the siege" and "we
				// are demolishing a base as fast as it is rebuilt", which look identical in a
				// running total of structures destroyed.
				if (isStructure && side == Allegiance.Enemy
					&& razedByType.GetValueOrDefault(entry.Type) > replacedByType.GetValueOrDefault(entry.Type))
				{
					replacedByType[entry.Type] = replacedByType.GetValueOrDefault(entry.Type) + 1;
					entry.RebuildCount++;
					EnemyRebuilds++;
				}
			}

			entry.LastKnownCell = cell;
			entry.Region = region;
			entry.LastSeenTick = tick;
			entry.Status = RecordStatus.Live;
			entry.HealthFraction = healthFraction;

			if (!string.IsNullOrEmpty(observedBy))
				entry.ObservedBy = observedBy;
		}

		/// <summary>
		/// Marks everything not seen this tick as stale rather than gone. Losing sight of something
		/// is not evidence that it stopped existing, and treating it as such is how a commander
		/// convinces itself an enemy base has vanished.
		/// </summary>
		public void AgeUnseen(int tick, Func<uint, bool> seenThisTick)
		{
			ArgumentNullException.ThrowIfNull(seenThisTick);

			Tick = Math.Max(Tick, tick);

			foreach (var entry in entries.Values)
				if (entry.Status == RecordStatus.Live && !seenThisTick(entry.ActorId))
					entry.Status = RecordStatus.Stale;
		}

		/// <summary>
		/// Records that a manager has actually dealt with this actor - repaired it, given it a job,
		/// folded it into a squad. The counterpart to observing it.
		/// </summary>
		public void MarkAttended(uint actorId, string by, int tick)
		{
			if (!entries.TryGetValue(actorId, out var entry))
				return;

			entry.LastAttendedTick = tick;
			entry.AttendedBy = by ?? "";
		}

		/// <summary>
		/// Ours that nobody has done anything about for a while.
		/// </summary>
		/// <remarks>
		/// The point of keeping the record at all. A commander with two hundred units and a hundred
		/// and thirty thousand idle credits was measured with forty-three per cent of its army
		/// standing still and damaged buildings it never repaired, and nothing in the staff was in a
		/// position to notice either: every manager asked "what should I do next", none asked "what
		/// have I left alone".
		/// </remarks>
		public IEnumerable<DatabaseEntry> Neglected(float seconds, Allegiance side = Allegiance.Self) =>
			Standing(side).Where(e => NeedsAttention(e) && e.SecondsSinceAttended(Tick) >= seconds);

		/// <summary>
		/// Whether this actor wants anything doing. A building at full health and a unit already
		/// carrying out an order are not neglected, they are fine - and counting them as neglected
		/// would bury the handful that are not in a list of everything the commander owns.
		/// </summary>
		public static bool NeedsAttention(DatabaseEntry entry)
		{
			ArgumentNullException.ThrowIfNull(entry);

			if (entry.Side != Allegiance.Self || entry.Status == RecordStatus.Destroyed)
				return false;

			return entry.IsStructure ? entry.HealthFraction < 1f : entry.LastAttendedTick < 0;
		}

		/// <summary>How many of ours, of one type, are standing. The economy is counted, not assumed.</summary>
		public int CountOf(string type) =>
			Standing(Allegiance.Self).Count(e => e.Type == type);

		/// <summary>Ours that are damaged and still standing, worst first. Repair is cheap and buildings are not.</summary>
		public IEnumerable<DatabaseEntry> Damaged(float below = 1f) =>
			Standing(Allegiance.Self)
				.Where(e => e.IsStructure && e.Status == RecordStatus.Live && e.HealthFraction < below)
				.OrderBy(e => e.HealthFraction)
				.ThenBy(e => e.ActorId);

		/// <summary>Records a death actually witnessed. The only way an entry becomes Destroyed.</summary>
		public void RecordDestroyed(uint actorId, int tick)
		{
			if (!entries.TryGetValue(actorId, out var entry))
				return;

			entry.Status = RecordStatus.Destroyed;
			entry.LastSeenTick = tick;
			entry.HealthFraction = 0f;

			if (entry.IsStructure && entry.Side == Allegiance.Enemy)
				razedByType[entry.Type] = razedByType.GetValueOrDefault(entry.Type) + 1;
		}

		/// <summary>
		/// Records that an order was issued for an actor, and by which manager.
		/// </summary>
		/// <remarks>
		/// Provenance, not bookkeeping. A single match logged more than two thousand rejected order
		/// conflicts - managers countermanding each other on the same units - and without a record
		/// of who ordered what, a manager cannot tell whether a unit is idle because nobody wants it
		/// or because somebody else is already using it.
		/// </remarks>
		public void RecordOrder(uint actorId, string order, string orderedBy, int tick)
		{
			if (!entries.TryGetValue(actorId, out var entry))
				return;

			entry.LastOrder = order;
			entry.OrderedBy = orderedBy;
			entry.OrderedTick = tick;
		}

		/// <summary>Entries on one side, live or merely unseen, never ones known to be dead.</summary>
		public IEnumerable<DatabaseEntry> Standing(Allegiance side) =>
			All.Where(e => e.Side == side && e.Status != RecordStatus.Destroyed);

		/// <summary>Enemy structures believed to be standing, oldest sighting last.</summary>
		public IEnumerable<DatabaseEntry> EnemyStructures() =>
			Standing(Allegiance.Enemy).Where(e => e.IsStructure);

		/// <summary>Things nobody has looked at for a while. The commander's own list of what it does not know.</summary>
		public IEnumerable<DatabaseEntry> StaleFor(float seconds) =>
			All.Where(e => e.Status == RecordStatus.Stale && e.SecondsSinceSeen(Tick) >= seconds);

		/// <summary>
		/// How confident the commander should be about an entry, decaying with how long ago it was
		/// last actually seen. Confidence is a function of age, not of how much the commander would
		/// like the reading to be true.
		/// </summary>
		public static float Confidence(DatabaseEntry entry, int now, float halfLifeSeconds = 30f)
		{
			ArgumentNullException.ThrowIfNull(entry);

			if (entry.Status == RecordStatus.Destroyed)
				return 1f;

			if (entry.Status == RecordStatus.Live)
				return 1f;

			var age = entry.SecondsSinceSeen(now);
			return (float)Math.Pow(0.5, age / Math.Max(1f, halfLifeSeconds));
		}

		/// <summary>A one-line account of what the commander knows, for the chief and for telemetry.</summary>
		public string Summary()
		{
			var mine = Standing(Allegiance.Self).Count();
			var enemy = Standing(Allegiance.Enemy).Count();
			var enemyStructures = EnemyStructures().Count();
			var destroyed = All.Count(e => e.Status == RecordStatus.Destroyed && e.Side == Allegiance.Enemy);
			var stale = All.Count(e => e.Status == RecordStatus.Stale);

			var damaged = Damaged().Count();
			var neglected = Neglected(60f).Count();

			return $"database: {Count} tracked, mine {mine} ({CountOf("harv")} harv / {CountOf("proc")} proc), " +
				$"enemy {enemy} ({enemyStructures} structures), " +
				$"enemy destroyed {destroyed}, rebuilt {EnemyRebuilds}, stale {stale}, " +
				$"damaged {damaged}, unattended>60s {neglected}";
		}
	}
}
