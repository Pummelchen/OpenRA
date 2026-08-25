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

using System.Collections.Generic;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>One killing, as it happened: who died, and who did it.</summary>
	public readonly record struct CombatOutcome(
		uint VictimActorId,
		string VictimType,
		Player VictimOwner,
		uint KillerActorId,
		string KillerType,
		Player KillerOwner,
		int Tick)
	{
		/// <summary>Whether anybody can be credited. Falling off a cliff has no killer.</summary>
		public bool HasKiller => KillerOwner != null;
	}

	[TraitLocation(SystemActors.World)]
	[Desc("Collects every kill in the match so that observers can attribute them to the unit type",
		"that did the killing. Records only; changes nothing about the game.")]
	public class CombatRecordRegistryInfo : TraitInfo
	{
		[Desc("Kills held before the oldest are dropped. Consumers drain this; the cap only bounds",
			"memory if nothing ever does.")]
		public readonly int Capacity = 4096;

		public override object Create(ActorInitializer init) { return new CombatRecordRegistry(this); }
	}

	/// <summary>
	/// <para>
	/// The match's kill log, in order, for anything that cares to read it.
	/// </para>
	/// <para>
	/// The engine already counts kills per player, which answers "am I winning" and nothing else.
	/// What is missing is which TYPE did the killing and which type died, and that is the question
	/// worth asking: a commander that knows a heavy tank trades at four to one and a rifleman at one
	/// to three can work out what to build, whereas one that knows only its own total kill count
	/// cannot. Neither can be inferred from the rules - it depends on the map, the opponent, and
	/// what the opponent happens to be fielding.
	/// </para>
	/// <para>
	/// Deliberately a passive log. It is written from <see cref="RecordsCombatOutcome"/> on the
	/// dying actor and read by whoever wants it; it takes no decisions and belongs to no player, so
	/// it is equally available to every bot in the match and gives none of them information they
	/// could not have obtained by watching.
	/// </para>
	/// </summary>
	public class CombatRecordRegistry
	{
		readonly CombatRecordRegistryInfo info;
		readonly Queue<CombatOutcome> outcomes = new();

		/// <summary>Total kills recorded this match, including any already drained.</summary>
		public int TotalRecorded { get; private set; }

		public CombatRecordRegistry(CombatRecordRegistryInfo info)
		{
			this.info = info;
		}

		public void Record(in CombatOutcome outcome)
		{
			outcomes.Enqueue(outcome);
			TotalRecorded++;

			while (outcomes.Count > info.Capacity)
				outcomes.Dequeue();
		}

		/// <summary>
		/// Takes everything logged since the last drain, oldest first, and empties the log.
		/// </summary>
		/// <remarks>
		/// Draining rather than reading is what keeps several consumers from each re-counting the
		/// same kill on every tick. The order is the order the kills happened, which is deterministic
		/// - this is a lockstep simulation and a consumer that saw them in a different order on a
		/// different machine would desync.
		/// </remarks>
		public List<CombatOutcome> Drain()
		{
			var drained = new List<CombatOutcome>(outcomes);
			outcomes.Clear();
			return drained;
		}
	}

	[Desc("Reports this actor's death, and who caused it, to the world's combat record.",
		"Bookkeeping only: attaches to anything that can die and changes nothing about it.")]
	public class RecordsCombatOutcomeInfo : TraitInfo<RecordsCombatOutcome> { }

	public class RecordsCombatOutcome : INotifyKilled
	{
		void INotifyKilled.Killed(Actor self, AttackInfo e)
		{
			var registry = self.World.WorldActor.TraitOrDefault<CombatRecordRegistry>();
			if (registry == null)
				return;

			// A unit killed by something with no owner - terrain, a crate, its own demolition - is
			// still a death worth recording. Only the credit is missing, and pretending it belongs
			// to somebody would be worse than leaving it unattributed.
			var killer = e?.Attacker;
			var validKiller = killer != null && killer != self && killer.Owner != null;

			registry.Record(new CombatOutcome(
				self.ActorID,
				self.Info.Name,
				self.Owner,
				validKiller ? killer.ActorID : 0,
				validKiller ? killer.Info.Name : "",
				validKiller ? killer.Owner : null,
				self.World.WorldTick));
		}
	}
}
