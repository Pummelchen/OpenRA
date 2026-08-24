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
		readonly List<ProductionRequest> outgoing;

		public StaffContext(Directive directive, List<IManagerIntent> intents, List<ManagerReport> reports,
			IReadOnlyList<ProductionRequest> standing = null, List<ProductionRequest> outgoing = null)
		{
			Directive = directive ?? Directive.Initial;
			this.intents = intents ?? [];
			this.reports = reports ?? [];
			this.outgoing = outgoing ?? [];
			Requests = standing ?? [];
		}

		/// <summary>
		/// <para>
		/// Production asked for by the rest of the staff, filed on the previous cycle.
		/// </para>
		/// <para>
		/// Production is one domain and needs one owner. An audit of this staff found SIX managers
		/// queueing items independently - economy, building-production, unit-production,
		/// intelligence, scouting and special-operations - which is the same "nobody is responsible"
		/// failure the staff was created to end. Specialists now describe what they need and the
		/// production managers decide what is actually built.
		/// </para>
		/// <para>
		/// Requests are consumed a cycle after they are filed, for the same reason directives are:
		/// managers think in parallel, so anything one manager writes cannot be safely read by
		/// another in the same cycle.
		/// </para>
		/// </summary>
		public IReadOnlyList<ProductionRequest> Requests { get; }

		/// <summary>Ask the production managers for something. They decide whether it is built.</summary>
		public void Request(ProductionRequest request)
		{
			if (request != null)
				outgoing.Add(request);
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
		/// <para>
		/// The longest wait among the domains an assault actually depends on.
		/// </para>
		/// <para>
		/// Deliberately not the maximum over every report. Special operations answering "forty-five
		/// seconds to build a spy" would otherwise delay a committed assault for an infiltrator
		/// nobody asked for, and a naval arm on a landlocked map would delay it forever. An assault
		/// waits for the slowest part <i>of itself</i>.
		/// </para>
		/// </summary>
		public int? LongestWait
		{
			get
			{
				var waits = reports
					.Where(r => AssaultCritical.Contains(r.Manager) && r.ReadyInSeconds.HasValue)
					.Select(r => r.ReadyInSeconds.Value)
					.ToArray();

				return waits.Length == 0 ? null : waits.Max();
			}
		}

		/// <summary>The domains without which an assault is not an assault.</summary>
		static readonly string[] AssaultCritical = ["unit-production", "economy", "building-production"];
	}
}
