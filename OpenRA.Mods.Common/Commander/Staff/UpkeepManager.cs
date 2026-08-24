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

using System.Linq;
using OpenRA.Mods.Common.Commander.Model;

namespace OpenRA.Mods.Common.Commander.Staff
{
	/// <summary>
	/// <para>
	/// Looks after what the commander already owns. Every other manager on this staff asks what to
	/// do next; this one asks what has been left alone.
	/// </para>
	/// <para>
	/// The two questions have different answers and only the second is ever neglected, because
	/// neglect produces no event to react to. Measured in a single match: a hundred and thirty
	/// thousand credits sitting unspent, forty-three per cent of the army standing idle, and damaged
	/// buildings that were never repaired at all - the engine's repair module runs only from an
	/// attack notification, so a structure damaged in a raid that ends stays broken for the rest of
	/// the match. Nothing was failing loudly enough for anyone to notice.
	/// </para>
	/// <para>
	/// Repair is the clearest case and the cheapest. A building repairs from the same credits that
	/// are otherwise idle, cannot be replaced while under fire, and supplies less - less power, less
	/// production, fewer guns - for every second it is left damaged.
	/// </para>
	/// </summary>
	public sealed class UpkeepManager : ICommanderManager
	{
		public string Name => "upkeep";
		public int Order => 18;
		public int Interval => 250;
		public bool CanThinkInParallel => true;

		/// <summary>Health below which one of our buildings is worth spending credits on.</summary>
		public float RepairBelow { get; init; } = 0.99f;

		/// <summary>Seconds without a repair order before the same building is offered one again.</summary>
		public float RepairRetrySeconds { get; init; } = 20f;

		/// <summary>Buildings repaired per cycle. Repair is per-building and the queue is the player's cash.</summary>
		public int RepairsPerCycle { get; init; } = 8;

		/// <summary>Seconds of nobody doing anything about one of ours before it counts as neglected.</summary>
		public float NeglectSeconds { get; init; } = 60f;

		public void Think(CommanderSnapshot snapshot, StaffContext context)
		{
			var database = snapshot.Database;
			if (database == null)
				return;

			var damaged = database.Damaged(RepairBelow).ToArray();

			var repaired = 0;
			foreach (var entry in damaged)
			{
				if (repaired >= RepairsPerCycle)
					break;

				// Do not re-order a repair that was only just ordered. The staff cycles far faster
				// than a building repairs, and repeating the order every cycle would toggle the
				// repair off again on engines where the order is a toggle.
				if (entry.SecondsSinceAttended(snapshot.Tick) < RepairRetrySeconds)
					continue;

				context.Add(new RepairIntent
				{
					ActorId = entry.ActorId,
					Structure = entry.Type,
					HealthFraction = entry.HealthFraction,
					Reason = $"left at {entry.HealthFraction:P0} for {entry.SecondsSinceAttended(snapshot.Tick):F0}s",
				});

				repaired++;
			}

			var neglected = database.Neglected(NeglectSeconds).Count();
			var mine = database.Standing(Allegiance.Self).Count();
			var worst = damaged.Length == 0 ? 1f : damaged[0].HealthFraction;

			context.Report(new ManagerReport
			{
				Manager = Name,

				// Damage to our own base is a strained position, not a crisis: it is Critical only
				// when there is nothing left to look after.
				Readiness =
					mine == 0 ? Readiness.Critical
					: damaged.Length > 0 ? Readiness.Strained
					: Readiness.Healthy,

				Headline = damaged.Length == 0
					? $"{mine} of ours in good order, {neglected} unattended for {NeglectSeconds:F0}s+"
					: $"{damaged.Length} buildings damaged (worst {worst:P0}), {repaired} repairs ordered, " +
						$"{neglected} of {mine} unattended for {NeglectSeconds:F0}s+",

				ForceValue = damaged.Length,
			});
		}
	}
}
