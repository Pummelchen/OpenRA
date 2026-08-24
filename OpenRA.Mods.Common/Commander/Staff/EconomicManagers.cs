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
	/// Owns money: how much is coming in, how much is going out, and whether the gap between them is
	/// a problem.
	/// </para>
	/// <para>
	/// It exists because that gap was this commander's largest defect and nothing owned it. Measured
	/// over a full match it earned 289,500 credits, spent 76,000 and finished sitting on 213,413 -
	/// out-earning its opponent 1.6 to 1 while out-building it nowhere. Income was never the
	/// constraint; the ability to convert income into anything was, and no component was responsible
	/// for noticing.
	/// </para>
	/// </summary>
	public sealed class EconomyManager : ICommanderManager
	{
		public string Name => "economy";
		public int Order => 10;
		public int Interval => 250;
		public bool CanThinkInParallel => true;

		/// <summary>Banked fraction above which the surplus is treated as a fault to be corrected.</summary>
		public float SurplusThreshold { get; init; } = 0.35f;

		/// <summary>Harvesters below which the economy is the priority whatever else is happening.</summary>
		public int MinimumHarvesters { get; init; } = 4;

		/// <summary>
		/// Seconds during which a domain that does not exist yet is an opening rather than a crisis.
		/// </summary>
		public float OpeningSeconds { get; init; } = 180f;

		public void Think(CommanderSnapshot snapshot, StaffContext context)
		{
			var harvesters = snapshot.Units.GetValueOrDefault("harv");
			var refineries = snapshot.Structures.GetValueOrDefault("proc");

			// A harvester with nowhere to unload earns nothing, so the pair is judged together
			// rather than separately.
			if (refineries == 0)
			{
				context.Request(new ProductionRequest
				{
					Requester = Name,
					Item = "proc",
					Priority = RequestPriority.Urgent,
					Reason = "no refinery: income is zero",
				});

				return;
			}

			if (harvesters < MinimumHarvesters)
			{
				context.Request(new ProductionRequest
				{
					Requester = Name,
					Item = "harv",
					Count = MinimumHarvesters - harvesters,
					Priority = harvesters == 0 ? RequestPriority.Urgent : RequestPriority.Needed,
					Reason = $"only {harvesters} harvesters",
				});
			}

			// The surplus test. Reported as an assessment rather than acted on directly, because the
			// cure belongs to whoever owns production - this manager's job is to notice, and it is
			// the noticing that was missing.
			if (snapshot.BankedFraction > SurplusThreshold && snapshot.Earned > 20000)
			{
				context.Add(new AssessmentIntent
				{
					Topic = "surplus",
					Finding = $"{snapshot.BankedFraction:P0} of {snapshot.Earned} earned is unspent " +
						$"({snapshot.Cash} banked) - production cannot absorb the income",
				});
			}

			// Expanding is only worth it while the income can still be spent. Past that point another
			// refinery buys nothing, which is exactly the trap this commander fell into with thirty
			// per cent of its base given over to economy.
			// Expanding while the army is committed is how a staff of specialists loses a won
			// position: the chief's stance governs, not this manager's local optimum.
			var expanding = context.Directive.Stance is Stance.Build or Stance.Probe or Stance.Pressure;

			if (expanding && snapshot.BankedFraction < SurplusThreshold && harvesters >= refineries * 2)
			{
				context.Request(new ProductionRequest
				{
					Requester = Name,
					Item = "proc",
					Priority = RequestPriority.Wanted,
					Reason = $"{harvesters} harvesters on {refineries} refineries and the income is being spent",
				});
			}

			Report(snapshot, context, harvesters, refineries);
		}

		void Report(CommanderSnapshot snapshot, StaffContext context, int harvesters, int refineries)
		{
			var banked = snapshot.BankedFraction;

			// The chief needs to know whether money is a reason to wait, a reason to move, or not a
			// factor. Surplus is deliberately reported as a *fault* rather than as strength: credits
			// in the bank have never won anything.
			// Before the opening is finished, an empty domain is an opening rather than an emergency.
			var opening = snapshot.Seconds < OpeningSeconds;

			var readiness =
				(refineries == 0 || harvesters == 0) && !opening ? Readiness.Critical
				: refineries == 0 || harvesters == 0 ? Readiness.Strained
				: banked > SurplusThreshold ? Readiness.Surplus
				: harvesters < MinimumHarvesters ? Readiness.Strained
				: Readiness.Healthy;

			var headline = readiness switch
			{
				Readiness.Critical => "no working economy",
				Readiness.Surplus => $"{banked:P0} of income unspent - can fund anything asked of it",
				Readiness.Strained => $"only {harvesters} harvesters on {refineries} refineries",
				_ => $"{harvesters} harvesters, {refineries} refineries, {banked:P0} banked",
			};

			context.Report(new ManagerReport
			{
				Manager = Name,
				Readiness = readiness,
				Headline = headline,
				NetCreditsPerSecond = snapshot.Seconds > 0 ? snapshot.Earned / snapshot.Seconds : 0f,

				// Money is never the thing an assault waits for once there is a surplus.
				ReadyInSeconds = readiness == Readiness.Surplus ? 0 : null,
			});
		}
	}

	/// <summary>
	/// <para>
	/// Owns what the base is made of.
	/// </para>
	/// <para>
	/// The composition matters more than the count, and this commander proved it twice. Building
	/// fractions had been rewritten from the upstream 30/35% for refineries and power down to 1%
	/// each, which told the builder to keep both at one per cent of the base; restoring them took a
	/// match from 14 structures to 30 and the exchange ratio from 1.19 to 1.82. Later, raising
	/// static defence from 3% to 7% flipped the building trade from 41 destroyed against 58 lost to
	/// 41 against 37 - and raising it again to 12% collapsed the whole thing.
	/// </para>
	/// </summary>
	public sealed class BuildingProductionManager : ICommanderManager
	{
		public string Name => "building-production";
		public int Order => 20;
		public int Interval => 250;
		public bool CanThinkInParallel => true;

		/// <summary>Power headroom below which production slows and everything suffers at once.</summary>
		public int MinimumPowerPlants { get; init; } = 2;

		/// <summary>
		/// Serves the structures the rest of the staff has asked for, arbitrating rather than simply
		/// obeying - and the chief's stance decides the arbitration. Somebody has to weigh scouting's
		/// dogs against intelligence's anti-air, and it cannot be either of them.
		/// </summary>
		void ServeRequests(CommanderSnapshot snapshot, StaffContext context)
		{
			// Under assault everything that is not the assault waits. Expanding the economy while
			// the army is committed is how a staff of specialists loses a won position.
			var committed = context.Directive.Stance == Stance.Assault;

			foreach (var request in context.Requests.OrderByDescending(r => r.Priority))
			{
				if (!IsStructure(request.Item))
					continue;

				if (committed && request.Priority < RequestPriority.Urgent)
					continue;

				context.Add(new ConstructIntent
				{
					Structure = request.Item,
					Reason = $"{request.Requester}: {request.Reason}",
				});
			}
		}

		static bool IsStructure(string item) => item is "proc" or "powr" or "apwr" or "weap" or "barr"
			or "tent" or "kenn" or "dome" or "agun" or "sam" or "pbox" or "gun" or "ftur" or "tsla"
			or "atek" or "stek" or "fix" or "hpad" or "afld" or "spen" or "syrd" or "silo";

		public void Think(CommanderSnapshot snapshot, StaffContext context)
		{
			var power = snapshot.Structures.GetValueOrDefault("powr") + snapshot.Structures.GetValueOrDefault("apwr");
			if (power < MinimumPowerPlants)
			{
				context.Add(new ConstructIntent
				{
					Structure = "powr",
					Reason = $"only {power} power plants: low power slows every queue at once",
				});
			}

			// Production capacity is the thing the surplus is waiting on. One war factory converts
			// roughly 66 credits a second into armour against an income near 250, so a base with one
			// factory cannot spend what it earns however many refineries it has.
			var factories = snapshot.Structures.GetValueOrDefault("weap");
			if (snapshot.BankedFraction > 0.35f && factories < 4 && snapshot.Cash > 5000)
			{
				context.Add(new ConstructIntent
				{
					Structure = "weap",
					Reason = $"{factories} war factories cannot absorb {snapshot.Cash} banked",
				});
			}

			ServeRequests(snapshot, context);

			var total = snapshot.Structures.Values.Sum();
			var opening = snapshot.Seconds < 180f;
			var radar = snapshot.Structures.GetValueOrDefault("dome");
			var airfields = snapshot.Structures.GetValueOrDefault("hpad")
				+ snapshot.Structures.GetValueOrDefault("afld")
				+ snapshot.Structures.GetValueOrDefault("afld.ukraine");

			context.Report(new ManagerReport
			{
				Manager = Name,
				Readiness =
					power == 0 && !opening ? Readiness.Critical
					: power == 0 || factories == 0 ? Readiness.Strained
					: factories >= 4 ? Readiness.Healthy
					: Readiness.Strained,
				// Radar and airfields are named explicitly because their absence is invisible in a
				// structure count. An air arm is gated behind a DOME and reports only "this arm is
				// not fielded" when it has none, which reads as irrelevance rather than as the
				// blockage it is.
				Headline = $"{total} structures, {factories} war factories, {power} power plants, " +
					$"{radar} radar, {airfields} airfields",

				// What the chief actually wants from this domain: can the base support a push.
				ReadyInSeconds = factories >= 2 ? 0 : 60,
			});
		}
	}

	/// <summary>
	/// <para>
	/// Owns what comes out of the queues.
	/// </para>
	/// <para>
	/// Instrumenting production found the defect this manager exists to prevent: the vehicle queue,
	/// the only consistent spender, was producing 600-credit flak trucks while 200,000 credits sat
	/// in the bank, because the counter lists it drew from were ordered cheapest-first. Ordering the
	/// same lists heaviest-first took the worst matchup in the benchmark from a 0.17 exchange ratio
	/// to 1.13 - expensive units both spend the surplus and win the fight.
	/// </para>
	/// </summary>
	public sealed class UnitProductionManager : ICommanderManager
	{
		public string Name => "unit-production";
		public int Order => 30;
		public int Interval => 100;
		public bool CanThinkInParallel => true;

		/// <summary>Units this commander may build, heaviest first. Every faction, since it holds every prerequisite.</summary>
		public IReadOnlyList<string> Preference { get; init; } =
			["qtnk", "4tnk", "ttnk", "ctnk", "3tnk", "dtrk", "shok", "2tnk", "v2rl", "arty", "1tnk", "e3", "e1"];

		/// <summary>Cash above which cheap units are skipped entirely in favour of waiting for heavy ones.</summary>
		public int HeavyOnlyCashThreshold { get; init; } = 15000;

		static bool IsStructureItem(string item) => item is "proc" or "powr" or "apwr" or "weap"
			or "barr" or "tent" or "kenn" or "dome" or "agun" or "sam" or "pbox" or "gun" or "ftur"
			or "tsla" or "atek" or "stek" or "fix" or "hpad" or "afld" or "spen" or "syrd" or "silo";

		public void Think(CommanderSnapshot snapshot, StaffContext context)
		{
			var idle = snapshot.Queues.Where(q => q.IsIdle).ToArray();

			// The rest of the staff's requests come before this manager's own preference: a scouting
			// manager reporting that the commander is blind outranks another tank.
			//
			// Urgent is served unconditionally. Anything less is served while there is capacity to
			// serve it - an earlier version dropped everything below Urgent outright, which meant a
			// request for the scouts that find the enemy simply vanished, and arbitration that
			// silently discards information is not arbitration.
			var served = 0;
			foreach (var request in context.Requests
				.Where(r => !IsStructureItem(r.Item))
				.OrderByDescending(r => r.Priority))
			{
				if (request.Priority < RequestPriority.Urgent && served >= Math.Max(1, idle.Length))
					break;

				context.Add(new ProduceUnitIntent
				{
					Unit = request.Item,
					Count = request.Count,
					Reason = $"{request.Requester} ({request.Priority.ToString().ToLowerInvariant()}): {request.Reason}",
				});

				served++;
			}

			if (idle.Length == 0)
				return;

			// With money to burn, an idle queue taking the cheapest thing it can build is the
			// failure this commander spent a whole match performing. Prefer the heaviest.
			var wealthy = snapshot.Cash >= HeavyOnlyCashThreshold;

			foreach (var queue in idle)
			{
				var choice = Preference.FirstOrDefault();
				if (choice == null)
					continue;

				context.Add(new ProduceUnitIntent
				{
					Unit = choice,
					Count = wealthy ? 2 : 1,
					Reason = wealthy
						? $"{queue.Type} idle with {snapshot.Cash} banked"
						: $"{queue.Type} idle",
				});
			}

			ReportForce(snapshot, context, idle.Length);
		}

		void ReportForce(CommanderSnapshot snapshot, StaffContext context, int idleQueues)
		{
			var army = snapshot.State?.Self.ArmyValue() ?? 0f;

			// "When will the army be ready" is the single question the chief asks this manager, and
			// it is answered in seconds rather than in queue entries. Below the threshold the answer
			// is an estimate from what the queues are actually turning over.
			var target = AssaultReadyArmyValue;
			var shortfall = Math.Max(0f, target - army);
			var rate = snapshot.Seconds > 0f ? Math.Max(1f, snapshot.Spent / snapshot.Seconds) : 1f;
			var readyIn = shortfall <= 0f ? 0 : (int)(shortfall / rate);

			// An army that has not been built yet is not an army that has been lost.
			var opening = snapshot.Seconds < 180f;

			context.Report(new ManagerReport
			{
				Manager = Name,
				Readiness =
					army <= 0f && !opening ? Readiness.Critical
					: army <= 0f ? Readiness.Strained
					: army >= target ? Readiness.Surplus
					: army >= target * 0.5f ? Readiness.Healthy
					: Readiness.Strained,
				Headline = army >= target
					? $"{army:F0} credits of army - ready to commit"
					: $"{army:F0} of {target:F0} credits of army, {idleQueues} queues idle",
				ForceValue = army,
				ReadyInSeconds = readyIn,
			});
		}

		/// <summary>Army value at which the chief may treat an assault as supportable.</summary>
		public float AssaultReadyArmyValue { get; init; } = 12000f;
	}
}
