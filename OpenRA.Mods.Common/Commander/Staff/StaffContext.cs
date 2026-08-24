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
	/// What a manager is given when it thinks: the standing directive, somewhere to put its intents,
	/// and somewhere to file its report.
	/// </summary>
	public sealed class StaffContext
	{
		readonly List<IManagerIntent> intents;
		readonly List<ManagerReport> reports;

		public StaffContext(Directive directive, List<IManagerIntent> intents, List<ManagerReport> reports)
		{
			Directive = directive ?? Directive.Initial;
			this.intents = intents ?? [];
			this.reports = reports ?? [];
		}

		/// <summary>
		/// The chief's standing orders. Specialists work within these rather than deciding for
		/// themselves what the whole army should be doing - which is the difference between a staff
		/// and a committee.
		/// </summary>
		public Directive Directive { get; }

		/// <summary>Reports filed this cycle. Only the chief reads these.</summary>
		public IReadOnlyList<ManagerReport> Reports => reports;

		public void Add(IManagerIntent intent)
		{
			if (intent != null)
				intents.Add(intent);
		}

		/// <summary>The directive issued this cycle, if a chief issued one.</summary>
		public Directive IssuedDirective { get; private set; }

		/// <summary>
		/// Issue standing orders for the coming period. Only the tactical chief calls this, and it
		/// governs what every specialist does on the next cycle rather than this one.
		/// </summary>
		public void Issue(Directive directive)
		{
			if (directive != null)
				IssuedDirective = directive;
		}

		public void Report(ManagerReport report)
		{
			if (report != null)
				reports.Add(report);
		}

		/// <summary>One specialist's most recent report, for the chief to consult by name.</summary>
		public ManagerReport From(string manager) =>
			reports.FirstOrDefault(r => string.Equals(r.Manager, manager, StringComparison.Ordinal));

		/// <summary>Whether any domain is failing outright.</summary>
		public bool AnyCritical => reports.Any(r => r.Readiness == Readiness.Critical);

		/// <summary>
		/// The longest wait any domain reports before it can support a commitment. An assault is
		/// ready when the slowest necessary part of it is, not when the fastest is.
		/// </summary>
		public int? LongestWait
		{
			get
			{
				var waits = reports.Where(r => r.ReadyInSeconds.HasValue).Select(r => r.ReadyInSeconds.Value).ToArray();
				return waits.Length == 0 ? null : waits.Max();
			}
		}
	}
}
