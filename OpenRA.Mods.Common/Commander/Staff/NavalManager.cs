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
using System.Linq;
using OpenRA.Mods.Common.Commander.Model;

namespace OpenRA.Mods.Common.Commander.Staff
{
	/// <summary>
	/// <para>
	/// Builds the navy as balanced groups rather than as whatever the shipyard felt like, and sends
	/// a group only once it is whole.
	/// </para>
	/// <para>
	/// A fleet is not a number of ships. Submarines cannot shoot aircraft, destroyers exist largely
	/// to find submarines, and cruisers out-range everything and die to anything that reaches them.
	/// Any one type sent alone is a counter waiting to be applied - which is why the navy is
	/// counted here per type and a group is not considered ready until it has a minimum of each.
	/// </para>
	/// <para>
	/// Sending a whole group together is the other half. Ships trickling to a target one at a time
	/// are destroyed one at a time by a force none of them could have beaten alone but all of them
	/// together could.
	/// </para>
	/// </summary>
	public sealed class NavalManager : ICommanderManager
	{
		public string Name => "naval";
		public int Order => 73;
		public int Interval => 250;
		public bool CanThinkInParallel => true;

		/// <summary>Naval types this commander fields, in the order it prefers to fill a group.</summary>
		public string[] GroupTypes { get; init; } = ["ss", "msub", "dd", "ca", "pt"];

		/// <summary>How many of each type make one whole group.</summary>
		public int PerType { get; init; } = 3;

		public void Think(CommanderSnapshot snapshot, StaffContext context)
		{
			var database = snapshot.Database;
			if (database == null)
				return;

			var counts = GroupTypes.ToDictionary(t => t, database.CountOf, StringComparer.Ordinal);
			var total = counts.Values.Sum();

			if (total == 0)
			{
				// No navy at all is not a failure - most maps do not want one, and reporting
				// otherwise pinned the chief in Recover for whole matches when it had no shipyard
				// and no water to put one on.
				context.Report(new ManagerReport
				{
					Manager = Name,
					Readiness = Readiness.NotApplicable,
					Headline = "no navy fielded",
				});

				return;
			}

			// Whole groups are what can be sent; the remainder is what is still forming.
			var groups = counts.Values.Min() / Math.Max(1, PerType);

			// What the group is short of, worst shortfall first, so the shipyard fills the gap
			// rather than adding to whatever it already has most of.
			var shortfalls = GroupTypes
				.Select(t => (Type: t, Missing: ((groups + 1) * PerType) - counts[t]))
				.Where(x => x.Missing > 0)
				.OrderByDescending(x => x.Missing)
				.ThenBy(x => x.Type, StringComparer.Ordinal)
				.ToArray();

			foreach (var (type, missing) in shortfalls.Take(2))
				context.Request(new ProductionRequest
				{
					Requester = Name,
					Item = type,
					Count = Math.Min(missing, PerType),
					Priority = groups == 0 ? RequestPriority.Wanted : RequestPriority.Needed,
					Reason = $"group {groups + 1} is {missing} {type} short of {PerType} of each",
				});

			var composition = string.Join(" ", GroupTypes.Where(t => counts[t] > 0).Select(t => $"{t}x{counts[t]}"));

			context.Report(new ManagerReport
			{
				Manager = Name,

				// A part-formed group is a strained position, not a healthy one: it is the state in
				// which ships get sent piecemeal.
				Readiness = groups > 0 ? Readiness.Healthy : Readiness.Strained,
				Headline = $"{groups} whole group(s) of {PerType} of each type; have {composition}",
				ForceValue = total,
				Assessment = new Assessment
				{
					Present = $"{total} ships: {composition}",
					Target = groups > 0
						? $"commit {groups} whole group(s) together rather than ship by ship"
						: $"complete one group: {string.Join(", ", shortfalls.Select(x => $"{x.Missing} more {x.Type}"))}",
					Action = shortfalls.Length == 0 ? "group complete" : $"asked for {shortfalls[0].Type}",
					Progress = PerType <= 0 ? null : Math.Clamp(counts.Values.Min() / (float)PerType, 0f, 1f),
				},
			});
		}
	}
}
