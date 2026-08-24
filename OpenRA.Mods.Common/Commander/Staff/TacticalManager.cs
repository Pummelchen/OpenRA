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
using OpenRA.Mods.Common.Commander.Model;

namespace OpenRA.Mods.Common.Commander.Staff
{
	/// <summary>
	/// <para>
	/// The tactical chief. Reads every specialist's report and decides what the whole staff does for
	/// the next few minutes.
	/// </para>
	/// <para>
	/// It is the only manager that sees the whole picture, and that is the entire reason it exists.
	/// Each specialist optimises its own domain and, left to itself, will do so at the worst
	/// possible moment: the economy expanding while the base burns, production building harvesters
	/// while an assault waits for tanks, scouts dispatched from the army that is about to attack.
	/// Somebody has to decide which domain gets its way, and it has to be somebody holding all the
	/// reports at once.
	/// </para>
	/// <para>
	/// Three rules govern it, and each is a correction of something this commander measurably did
	/// wrong:
	/// </para>
	/// <para>
	/// <b>It commits for a period.</b> A directive is binding until it expires. The previous
	/// commander re-derived its posture every review from the instantaneous army ratio, and since
	/// every attack makes that ratio worse before it makes it better, it cancelled assaults at
	/// exactly the moment they began to work - thirty-eight draws, zero structures destroyed.
	/// </para>
	/// <para>
	/// <b>It attacks on a timing, not on a feeling.</b> Specialists report when they will be ready
	/// in seconds; the chief waits for the slowest necessary one and then commits. An assault is
	/// ready when its slowest part is, not when its fastest is.
	/// </para>
	/// <para>
	/// <b>It treats a surplus as a fault.</b> Credits in the bank have never won anything. When the
	/// economy reports more income than production can absorb, that is a reason to commit, not a
	/// reason to feel comfortable.
	/// </para>
	/// </summary>
	public sealed class TacticalManager : ICommanderManager
	{
		public string Name => "tactical";
		public int Order => 1000;
		public int Interval => 250;

		/// <summary>Runs on the game thread, after everybody, so it sees a complete set of reports.</summary>
		public bool CanThinkInParallel => false;

		public bool IsChief => true;

		/// <summary>How long a directive binds the staff, in ticks. 1500 is one minute.</summary>
		public int DirectiveTicks { get; init; } = 1500;

		/// <summary>Seconds of waiting the chief will accept before committing anyway.</summary>
		public int MaximumWaitSeconds { get; init; } = 90;

		/// <summary>Opponent-model confidence above which deception and infiltration are worth funding.</summary>
		public float DeceptionConfidence { get; init; } = 0.25f;

		public void Think(CommanderSnapshot snapshot, StaffContext context)
		{
			// A standing directive is not reconsidered until it expires. This is the rule that stops
			// the commander cancelling its own attacks.
			if (context.Directive.IsValid(snapshot.Tick) && !MustIntervene(context))
				return;

			var directive = Decide(snapshot, context);
			context.Issue(directive);

			context.Add(new AssessmentIntent
			{
				Topic = "directive",
				Finding = directive.ToString(),
			});
		}

		/// <summary>
		/// The only grounds for tearing up a standing directive early: something is failing outright.
		/// Deliberately narrow - "a better idea appeared" is not on the list, because acting on that
		/// every cycle is the failure mode this design exists to prevent.
		/// </summary>
		static bool MustIntervene(StaffContext context) => context.AnyCritical;

		Directive Decide(CommanderSnapshot snapshot, StaffContext context)
		{
			var economy = context.From("economy");
			var production = context.From("unit-production");
			var intel = context.From("intelligence");
			var defence = context.From("defence");
			var tactics = context.From("tactical-analysis");

			var until = snapshot.Tick + DirectiveTicks;

			// 1. Anything failing outright takes precedence over everything else. There is no
			//    objective worth trading the base for, and no attack worth launching with no economy
			//    behind it.
			var critical = context.Reports.FirstOrDefault(r => r.Readiness == Readiness.Critical);
			if (critical != null)
			{
				return new Directive
				{
					Stance = critical.Manager == "defence" ? Stance.Defend : Stance.Recover,
					MainEffortRegion = critical.RegionOfInterest,
					ReserveFraction = 0.6f,
					IssuedTick = snapshot.Tick,
					ValidUntilTick = until,
					Rationale = $"{critical.Manager} critical: {critical.Headline}",
				};
			}

			// 2. Under real pressure at home, hold. Reserve rises so the counterattack has something
			//    to counter with.
			if (defence?.Readiness == Readiness.Strained)
			{
				return new Directive
				{
					Stance = Stance.Defend,
					MainEffortRegion = defence.RegionOfInterest,
					ReserveFraction = 0.5f,
					IssuedTick = snapshot.Tick,
					ValidUntilTick = until,
					Rationale = $"defence strained: {defence.Headline}",
				};
			}

			// 3. Not knowing where they are is itself a decision the chief must make, not a state to
			//    drift in. A commander that attacks without finding the base first takes empty
			//    ground, which this one did for entire matches.
			var target = tactics?.RegionOfInterest ?? intel?.RegionOfInterest;
			if (target == null)
			{
				return new Directive
				{
					Stance = Stance.Probe,
					ReserveFraction = 0.3f,
					IssuedTick = snapshot.Tick,
					ValidUntilTick = until,
					Rationale = "no objective identified: find them before committing anything",
				};
			}

			// 4. The timing. Everyone who reported a wait is waited for - an assault is ready when
			//    its slowest necessary part is - but not indefinitely, because a commander that
			//    waits for perfect readiness never attacks at all.
			var wait = context.LongestWait ?? 0;
			var ready = wait <= 0;
			var waitedTooLong = wait > MaximumWaitSeconds;

			// A surplus is a fault, not a comfort. If the economy has more than production can
			// absorb, the extra should be in the field rather than in the bank.
			var surplus = economy?.Readiness == Readiness.Surplus;

			if (ready || waitedTooLong || (surplus && production?.Readiness != Readiness.Critical))
			{
				var confident = (intel?.Confidence ?? 0f) >= DeceptionConfidence;

				return new Directive
				{
					Stance = Stance.Assault,
					MainEffortRegion = target,

					// A feint only pays once we know enough about the opponent to predict what it
					// will move; before that it is a detachment thrown away for nothing.
					FeintRegion = confident ? PickFeint(snapshot, target.Value) : null,

					// Likewise infiltration: a spy sent at a base we have not identified is a spy
					// spent on a guess.
					AuthoriseSpecialOperations = confident,
					ReserveFraction = 0.2f,
					IssuedTick = snapshot.Tick,
					ValidUntilTick = until,
					Rationale = ready
						? $"army ready, objective R{target}"
						: waitedTooLong
							? $"waited {wait}s for readiness; committing rather than drifting"
							: $"economy surplus with nothing to spend it on - commit to R{target}",
				};
			}

			// 5. Not ready, not threatened, objective known. Squeeze without committing: raids cost
			//    little and buy time for the thing that will decide the match.
			return new Directive
			{
				Stance = Stance.Pressure,
				MainEffortRegion = target,
				AuthoriseSpecialOperations = (intel?.Confidence ?? 0f) >= DeceptionConfidence,
				ReserveFraction = 0.35f,
				IssuedTick = snapshot.Tick,
				ValidUntilTick = until,
				Rationale = $"ready in {wait}s: pressure R{target} until then",
			};
		}

		/// <summary>
		/// Somewhere worth the enemy's attention that is not where the main effort is going. A feint
		/// that lands on the same region as the assault is not a feint.
		/// </summary>
		static int? PickFeint(CommanderSnapshot snapshot, int mainEffort)
		{
			var state = snapshot.State;
			if (state == null || state.RegionCount <= 1)
				return null;

			var best = -1;
			var bestValue = 0f;
			for (var region = 0; region < state.RegionCount; region++)
			{
				if (region == mainEffort)
					continue;

				// Where they have something to lose and little to lose it with: the enemy must
				// believe the threat, and must be able to move to meet it.
				var value = state.Enemy.StructuresIn(region) / (1f + state.Enemy.ArmyValueIn(region));
				if (value > bestValue)
				{
					bestValue = value;
					best = region;
				}
			}

			return best >= 0 ? best : null;
		}
	}
}
