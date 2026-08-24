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

namespace OpenRA.Mods.Common.Commander.Staff
{
	/// <summary>
	/// <para>
	/// One specialist on the commander's staff. Each owns a single domain and nothing else - who
	/// scouts, what gets built, where the army goes - so that a defect has an address.
	/// </para>
	/// <para>
	/// The rebuild that preceded this was a stack of layers where the decision logic lived in two
	/// modules of 2,700 and 2,200 lines. When the vehicle queue turned out to be building 600-credit
	/// flak trucks while 200,000 credits sat unspent, no single component was responsible for that
	/// and no single component could be fixed. A staff of specialists is the answer to that: each
	/// manager is small enough to reason about, and the question "whose job was this" always has an
	/// answer.
	/// </para>
	/// </summary>
	public interface ICommanderManager
	{
		/// <summary>Name used in telemetry and profiling.</summary>
		string Name { get; }

		/// <summary>
		/// Application order. Intents are applied in ascending order, and the order is fixed rather
		/// than derived from how quickly a manager finished thinking - see
		/// <see cref="CommanderStaff"/> for why that distinction decides whether replays desync.
		/// </summary>
		int Order { get; }

		/// <summary>
		/// Ticks between this manager's reviews. A map analyser has no reason to run as often as a
		/// tactical controller, and the whole point of separating them is that they need not.
		/// </summary>
		int Interval { get; }

		/// <summary>
		/// Whether this manager may think on a worker thread. Managers that only read the snapshot
		/// and write intents can; anything that must touch live world state cannot.
		/// </summary>
		bool CanThinkInParallel { get; }

		/// <summary>
		/// Whether this manager is the tactical chief. Chiefs run last, after every specialist has
		/// filed, and are the only managers permitted to issue a <see cref="Directive"/>.
		/// </summary>
		bool IsChief => false;

		/// <summary>
		/// <para>
		/// Decide. Reads an immutable snapshot and appends intents; must not touch the live world.
		/// </para>
		/// <para>
		/// This may run on a worker thread, so it must not read mutable engine state, must not
		/// consult a shared random source, and must not depend on wall-clock time - all three would
		/// make the result depend on when the thread happened to run.
		/// </para>
		/// <para>
		/// A manager should also file a <see cref="ManagerReport"/>: a digest of its domain that the
		/// tactical chief can decide on. Reports are conclusions rather than measurements, and a
		/// specialist that reports raw numbers is making its chief do its job.
		/// </para>
		/// </summary>
		void Think(CommanderSnapshot snapshot, StaffContext context);
	}

	/// <summary>
	/// Something a manager wants done, applied on the game thread in a deterministic order. Intents
	/// exist so that thinking and acting are separable: thinking can be parallel and unordered,
	/// acting cannot be either.
	/// </summary>
	public interface IManagerIntent
	{
		/// <summary>Short description for telemetry.</summary>
		string Describe();
	}
}
