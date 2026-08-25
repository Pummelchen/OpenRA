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

		/// <summary>Tick the chief first wanted to move and could not. -1 when nothing is pending.</summary>
		int waitingSince = -1;

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

			// The chief's own overview, assembled from what the staff worked out rather than from
			// the world directly. This is the one place in the commander where past, present,
			// intent and action are visible side by side for every domain at once - which is the
			// whole justification for a chief existing, and the thing no specialist can produce
			// because none of them can see the others.
			var assessed = context.Reports
				.Where(r => r.Assessment != null && !r.Assessment.IsEmpty)
				.OrderBy(r => r.Manager, StringComparer.Ordinal)
				.ToArray();

			if (assessed.Length > 0)
				context.Add(new AssessmentIntent
				{
					Topic = "overview",
					Finding = $"{directive.Stance} | " + string.Join(" || ",
						assessed.Select(r => $"[{r.Manager}] {r.Assessment}")),
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
			// The records keeper is consulted last and matters most when the other two have nothing:
			// they read the live picture, which goes blank the moment the commander loses sight of
			// a base, while the database still holds where that base was and how long ago anyone
			// confirmed it. Without it the chief answers "no objective identified" and falls back to
			// probing ground it has already taken - which it did while holding a seven-to-one
			// economic advantage over an opponent whose base it had already found.
			var records = context.From("records");
			var target = tactics?.RegionOfInterest ?? intel?.RegionOfInterest ?? records?.RegionOfInterest;
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

			// 4. The timing. The assault waits for the slowest domain it depends on - but not
			//    indefinitely, because a commander that waits for perfect readiness never attacks.
			//
			//    Note the distinction between how long a domain says it NEEDS and how long we have
			//    actually been waiting. An earlier version conflated them: a production manager
			//    reporting "352 seconds" was read as "we have waited 352 seconds", so five seconds
			//    into a match the chief announced it was "committing rather than drifting" and threw
			//    a non-existent army at the enemy. A long estimate is a reason to wait, not to go.
			var wait = context.LongestWait ?? 0;
			var ready = wait <= 0;

			if (!ready && waitingSince < 0)
				waitingSince = snapshot.Tick;
			else if (ready)
				waitingSince = -1;

			var waitedSeconds = waitingSince < 0
				? 0
				: (snapshot.Tick - waitingSince) / AbstractState.TicksPerSecond;

			var waitedTooLong = waitedSeconds > MaximumWaitSeconds;

			// Are we strong enough for this to be an assault rather than a donation? The tactical
			// analyst reports the force ratio; Strained means outnumbered, and marching an
			// outnumbered army at a defended objective is how an army is spent rather than used.
			var outnumbered = tactics?.Readiness is Readiness.Strained or Readiness.Critical;

			// A surplus is a fault, not a comfort - but it is a PRODUCTION fault, and an earlier
			// version treated it as an attack trigger. Since this commander banks two thirds of its
			// income the economy reports Surplus almost continuously, so the chief assaulted on nine
			// cycles out of ten and fed its army in piecemeal. Exchange ratio 0.84 against 1.12 for
			// doing nothing. Money with nothing to buy is a reason to press, not to charge.
			var surplus = economy?.Readiness == Readiness.Surplus;

			if (!outnumbered && (ready || waitedTooLong))
			{
				// The clock is deliberately NOT reset here. It measures how long the chief has
				// wanted to move and could not, and committing out of impatience does not make the
				// army ready - so resetting it made the chief commit for exactly one directive
				// period, start the ninety-second clock again, and fall back to pressuring. It
				// oscillated between assaulting and waiting for the rest of the match. The clock
				// stops when the domain actually becomes ready, and not before.

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
						: $"waited {waitedSeconds}s for readiness; committing rather than drifting",
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
				Rationale = outnumbered
					? $"outnumbered: pressure R{target} rather than feed the army in"
					: surplus
						? $"money with nothing to buy: press R{target} while production catches up"
						: $"ready in {wait}s: pressure R{target} until then",
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
