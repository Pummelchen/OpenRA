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

		/// <summary>Share of our mobile units that may stand idle inside the base before they are moved out.</summary>
		public float IdleInBaseFraction { get; init; } = 0.2f;

		/// <summary>Cells beyond the outermost building that idle units are pushed to.</summary>
		public int MusterMargin { get; init; } = 6;

		/// <summary>Units moved out per cycle, so a whole army is not re-ordered at once.</summary>
		public int RelocationsPerCycle { get; init; } = 6;

		/// <summary>Stances corrected per cycle.</summary>
		public int StanceCorrectionsPerCycle { get; init; } = 12;

		/// <summary>Attendant marking a unit as on covert business, and therefore exempt from attack mode.</summary>
		public const string CovertAttendant = "special-ops";

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

			var loitering = ClearTheBase(snapshot, context, database);

			// Anything of ours that will not engage on its own initiative, put right. Units on
			// covert business are exempt: they are supposed to reach somewhere without being
			// noticed, and a unit in attack mode announces itself at the first thing it passes.
			var passive = 0;
			foreach (var entry in database.NotInAttackMode())
			{
				if (entry.AttendedBy == CovertAttendant)
					continue;

				if (passive >= StanceCorrectionsPerCycle)
					break;

				context.Add(new SetAttackModeIntent
				{
					ActorId = entry.ActorId,
					CurrentStance = entry.Stance,
				});

				passive++;
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
					? $"{mine} of ours in good order, {neglected} unattended for {NeglectSeconds:F0}s+, " +
						$"{loitering} moved out of the base, {passive} put into attack mode"
					: $"{damaged.Length} buildings damaged (worst {worst:P0}), {repaired} repairs ordered, " +
						$"{neglected} of {mine} unattended for {NeglectSeconds:F0}s+, {loitering} moved out of the base, " +
						$"{passive} put into attack mode",

				ForceValue = damaged.Length,
			});
		}

		/// <summary>
		/// <para>
		/// Keeps the base clear of its own idle army. Returns how many units were moved out.
		/// </para>
		/// <para>
		/// A unit with nothing to do stops where it stands, and where it stands is the base that
		/// built it. Past a certain density that is not merely untidy: the gaps between buildings
		/// are the only roads a base has, and units parked in them make every later unit take the
		/// long way round. Freshly built armour then trickles to the front in ones and twos instead
		/// of arriving as a wave, which is the difference between an attack and a queue of
		/// casualties. This commander was measured with forty-three per cent of its army idle.
		/// </para>
		/// <para>
		/// Only genuinely idle units are moved, and they are pushed straight outward from the centre
		/// of the base rather than to a single muster point - a single point would simply relocate
		/// the traffic jam, and radial dispersal needs no agreement about where the front is.
		/// </para>
		/// </summary>
		int ClearTheBase(CommanderSnapshot snapshot, StaffContext context, WorldDatabase database)
		{
			var buildings = database.Standing(Allegiance.Self).Where(e => e.IsStructure).ToArray();
			if (buildings.Length < 3)
				return 0;

			var mobile = database.Standing(Allegiance.Self).Where(e => !e.IsStructure).ToArray();
			if (mobile.Length == 0)
				return 0;

			var centreX = (int)buildings.Average(e => e.LastKnownCell.X);
			var centreY = (int)buildings.Average(e => e.LastKnownCell.Y);
			var centre = new CPos(centreX, centreY);

			var radius = buildings.Max(e => Distance(e.LastKnownCell, centre));

			// Idle, and standing among the buildings rather than out in the field.
			var loitering = mobile
				.Where(e => e.LastAttendedTick < 0 && Distance(e.LastKnownCell, centre) <= radius)
				.OrderBy(e => e.ActorId)
				.ToArray();

			var allowed = (int)(mobile.Length * IdleInBaseFraction);
			var excess = loitering.Length - allowed;
			if (excess <= 0)
				return 0;

			var moved = 0;
			foreach (var entry in loitering)
			{
				if (moved >= Math.Min(excess, RelocationsPerCycle))
					break;

				// Straight out along the line from the centre through where it stands. A unit
				// sitting exactly on the centre is nudged off it deterministically rather than
				// randomly - this runs in a lockstep simulation.
				var dx = entry.LastKnownCell.X - centre.X;
				var dy = entry.LastKnownCell.Y - centre.Y;
				if (dx == 0 && dy == 0)
					dx = 1;

				var length = Math.Max(1, (int)Math.Sqrt((dx * dx) + (dy * dy)));
				var reach = radius + MusterMargin;

				context.Add(new RelocateIntent
				{
					ActorId = entry.ActorId,
					Destination = new CPos(
						centre.X + (dx * reach / length),
						centre.Y + (dy * reach / length)),
					Reason = $"idle inside the base; {loitering.Length} of {mobile.Length} are",
				});

				moved++;
			}

			return moved;
		}

		static int Distance(CPos a, CPos b)
		{
			var dx = a.X - b.X;
			var dy = a.Y - b.Y;
			return (int)Math.Sqrt((dx * dx) + (dy * dy));
		}
	}
}
