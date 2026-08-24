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

namespace OpenRA.Mods.Common.Commander.Staff
{
	/// <summary>
	/// <para>
	/// Runs the commander's specialists and applies what they decide.
	/// </para>
	/// <para>
	/// <b>The parallelism here is real but carefully bounded, and the boundary is the whole point.</b>
	/// OpenRA is a lockstep simulation with sync hashing: every client must produce byte-identical
	/// state from the same orders. Issuing orders from a worker thread breaks that immediately, and
	/// it breaks it in the worst way - intermittently, under load, in a manner that reproduces on
	/// one machine and not another.
	/// </para>
	/// <para>
	/// So thinking and acting are split. <b>Thinking</b> reads an immutable snapshot and may run on
	/// as many threads as there are managers. <b>Acting</b> happens on the game thread, in a fixed
	/// order defined by <see cref="ICommanderManager.Order"/> rather than by which manager finished
	/// first. That single rule is what makes the parallelism safe: the schedule of *effects* is
	/// identical whether the machine has one core or thirty-two.
	/// </para>
	/// <para>
	/// Note what is deliberately <i>not</i> done: results are never consumed "if ready". A manager
	/// that has not finished is waited for. Waiting is deterministic; skipping is not, and a
	/// commander whose decisions depend on how busy the CPU was cannot be benchmarked, replayed or
	/// debugged - which would cost this project every measurement it has.
	/// </para>
	/// </summary>
	public sealed class CommanderStaff
	{
		readonly List<ICommanderManager> managers = [];
		readonly List<IManagerIntent> intents = [];
		readonly List<ManagerReport> reports = [];
		readonly List<ProductionRequest> outgoingRequests = [];
		IReadOnlyList<ProductionRequest> standingRequests = [];
		readonly Dictionary<string, int> lastRunTick = [];

		/// <summary>
		/// Whether managers may think on worker threads. Off runs everything inline, which is
		/// useful for isolating whether a defect is in a manager or in the scheduling of them.
		/// </summary>
		public bool ThinkInParallel { get; set; } = true;

		/// <summary>Managers on the staff, in application order.</summary>
		public IReadOnlyList<ICommanderManager> Managers => managers;

		/// <summary>Intents produced by the most recent cycle, in application order.</summary>
		public IReadOnlyList<IManagerIntent> LastIntents => intents;

		public void Add(ICommanderManager manager)
		{
			ArgumentNullException.ThrowIfNull(manager);

			managers.Add(manager);

			// Sorted once on insert so the application order never depends on registration order or
			// on anything that happens at run time.
			managers.Sort((a, b) => a.Order != b.Order
				? a.Order.CompareTo(b.Order)
				: string.CompareOrdinal(a.Name, b.Name));
		}

		/// <summary>
		/// Runs one cycle: every manager whose interval has elapsed thinks, then their intents are
		/// returned in application order for the caller to apply on the game thread.
		/// </summary>
		/// <summary>
		/// <para>
		/// Runs one cycle in two phases: the specialists think, then the chief decides on what they
		/// reported.
		/// </para>
		/// <para>
		/// The two phases are not an implementation detail. Specialists optimise their own domain
		/// and, left alone, each will do so at the worst possible moment - the economy expanding
		/// while the base burns, production building harvesters while an assault waits for tanks.
		/// The chief is the only party that sees every report at once, so it runs last and its
		/// directive governs the next period rather than this one.
		/// </para>
		/// </summary>
		public IReadOnlyList<IManagerIntent> Think(CommanderSnapshot snapshot)
		{
			ArgumentNullException.ThrowIfNull(snapshot);

			intents.Clear();
			reports.Clear();

			// Requests filed last cycle become this cycle's input, for the same reason the directive
			// does: managers think in parallel, so nothing one writes can be read by another in the
			// same cycle.
			//
			// KNOWN DEFECT, measured and deliberately left in place. A request survives exactly one
			// cycle while managers run on their own intervals - a hundred ticks for production, two
			// and a half thousand for map analysis - and the staff cycles more often than any of
			// them, so a request is almost always cleared before its consumer is next due. Across a
			// whole match, not one request filed by any manager reached the manager meant to serve
			// it; the only construction that happens comes from the building manager's own directly
			// issued intents.
			//
			// Holding requests open until their consumer can see them was implemented and measured,
			// and it is worse than the bug. Delivering everything cost the exchange ratio 1.74 -> 0.43
			// across twelve matches, worse in all four matchups, by re-creating the several-managers-
			// producing-at-once failure this staff exists to end. Delivering only structure requests
			// still cost 1.74 -> 1.23. The delivery mechanism is not what is missing: the building
			// manager's arbitration is not yet able to weigh requests against what the base actually
			// needs, and until it can, delivering them reliably makes the commander worse.
			standingRequests = outgoingRequests.ToList();
			outgoingRequests.Clear();

			var due = new List<ICommanderManager>();
			foreach (var manager in managers)
			{
				var interval = Math.Max(1, manager.Interval);
				if (lastRunTick.TryGetValue(manager.Name, out var last) && snapshot.Tick - last < interval)
					continue;

				lastRunTick[manager.Name] = snapshot.Tick;
				due.Add(manager);
			}

			if (due.Count == 0)
				return intents;

			// The chief is separated out and run last, on the whole staff's reports.
			var specialists = due.Where(m => !m.IsChief).ToList();
			var chiefs = due.Where(m => m.IsChief).ToList();

			// Each specialist writes to its own lists, so none can observe another's partial work
			// and no lock is needed on the hot path.
			var specialistIntents = new List<IManagerIntent>[specialists.Count];
			var specialistReports = new List<ManagerReport>[specialists.Count];
			var specialistRequests = new List<ProductionRequest>[specialists.Count];
			for (var i = 0; i < specialists.Count; i++)
			{
				specialistIntents[i] = [];
				specialistReports[i] = [];
				specialistRequests[i] = [];
			}

			var parallelCandidates = specialists.Count(m => m.CanThinkInParallel);

			// Below two eligible managers the scheduling costs more than it saves.
			if (ThinkInParallel && parallelCandidates >= 2)
			{
				System.Threading.Tasks.Parallel.For(0, specialists.Count, i =>
				{
					if (specialists[i].CanThinkInParallel)
						specialists[i].Think(snapshot, new StaffContext(Directive, specialistIntents[i], specialistReports[i], standingRequests, specialistRequests[i]));
				});

				for (var i = 0; i < specialists.Count; i++)
					if (!specialists[i].CanThinkInParallel)
						specialists[i].Think(snapshot, new StaffContext(Directive, specialistIntents[i], specialistReports[i], standingRequests, specialistRequests[i]));
			}
			else
			{
				for (var i = 0; i < specialists.Count; i++)
					specialists[i].Think(snapshot, new StaffContext(Directive, specialistIntents[i], specialistReports[i], standingRequests, specialistRequests[i]));
			}

			// Collected strictly by manager order, never by completion order. This is the line that
			// keeps a parallel staff deterministic.
			for (var i = 0; i < specialists.Count; i++)
			{
				intents.AddRange(specialistIntents[i]);
				reports.AddRange(specialistReports[i]);
				outgoingRequests.AddRange(specialistRequests[i]);
			}

			// Now the chief, reading everything the staff filed this cycle.
			foreach (var chief in chiefs)
			{
				var context = new StaffContext(Directive, intents, reports, standingRequests, outgoingRequests);
				chief.Think(snapshot, context);

				if (context.IssuedDirective != null)
					Directive = context.IssuedDirective;
			}

			return intents;
		}

		/// <summary>The chief's standing orders. Read by every specialist on the next cycle.</summary>
		public Directive Directive { get; private set; } = Directive.Initial;

		/// <summary>Reports filed during the most recent cycle.</summary>
		public IReadOnlyList<ManagerReport> LastReports => reports;

		/// <summary>Production the staff has asked for, awaiting the production managers' judgement.</summary>
		public IReadOnlyList<ProductionRequest> PendingRequests => outgoingRequests;

		/// <summary>Resets the cadence, so a new match does not inherit the previous one's timings.</summary>
		public void Reset()
		{
			lastRunTick.Clear();
			Directive = Directive.Initial;
			outgoingRequests.Clear();
			standingRequests = [];
		}
	}
}
