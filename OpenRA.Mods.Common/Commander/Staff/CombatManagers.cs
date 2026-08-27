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
	/// Reads the battlefield and tells the chief what is worth taking and what is worth fearing.
	/// It decides nothing itself - it is the staff officer who says "their base is at R7 and it is
	/// thinly held", and the chief decides whether that matters this minute.
	/// </para>
	/// </summary>
	public sealed class TacticalAnalysisManager : ICommanderManager
	{
		public string Name => "tactical-analysis";
		public int Order => 40;
		public int Interval => 250;
		public bool CanThinkInParallel => true;

		public void Think(CommanderSnapshot snapshot, StaffContext context)
		{
			var state = snapshot.State;
			if (state == null || state.RegionCount == 0)
				return;

			var best = -1;
			var bestValue = 0f;
			for (var region = 0; region < state.RegionCount; region++)
			{
				// What is worth taking: their base, discounted by what is standing on it. Discounted
				// rather than excluded - a defended base is still the thing that ends the match, and
				// a commander that only ever attacks undefended ground never wins.
				var prize = state.Enemy.StructuresIn(region) + (state.Enemy.ArmyValueIn(region) * 0.25f);
				if (prize <= 0f)
					continue;

				var value = prize / (1f + (state.Enemy.ForceValue(region, CombatRole.Defense) * 0.002f));
				if (value > bestValue)
				{
					bestValue = value;
					best = region;
				}
			}

			var ourArmy = state.Self.ArmyValue();

			// What can actually contest the objective, not what the enemy owns everywhere.
			//
			// Comparing against the global believed total looks reasonable and is not: most of that
			// total is an ASSUMPTION about forces nobody has seen, anchored to our own peak strength,
			// so the comparison is between our army and a mirror of our army. Measured, the reported
			// ratio was 1.00 on every single review of a thirty-thousand tick match - never above the
			// assault threshold, never below the retreat one - while the commander out-earned its
			// opponent seven to one and destroyed nothing. A number that cannot move cannot inform a
			// decision.
			//
			// The objective's own garrison plus whatever can reinforce it in the time an assault
			// takes is the quantity the assault actually has to beat.
			var theirArmy = 0f;
			if (best >= 0)
			{
				theirArmy = state.Enemy.ArmyValueIn(best);
				if (snapshot.Graph != null)
					foreach (var neighbour in snapshot.Graph.Neighbours(best))
						theirArmy += state.Enemy.ArmyValueIn(neighbour);
			}

			var ratio = theirArmy <= 0f ? 999f : ourArmy / theirArmy;

			context.Report(new ManagerReport
			{
				Manager = Name,
				Readiness =
					best < 0 ? Readiness.Strained
					: ratio >= 1.5f ? Readiness.Surplus
					: ratio >= 0.8f ? Readiness.Healthy
					: Readiness.Strained,
				Headline = best < 0
					? "no objective identified yet"
					: $"objective R{best}, force ratio {ratio:F2} against believed {theirArmy:F0}",
				RegionOfInterest = best >= 0 ? best : null,
				ForceValue = ourArmy,
				Confidence = best >= 0 ? 0.8f : 0.2f,
			});
		}
	}

	/// <summary>
	/// Owns the home ground. Reports pressure rather than acting on it, so the chief can weigh a
	/// threat at home against an opportunity away - the judgement that decides whether an assault
	/// launches or is recalled.
	/// </summary>
	public sealed class DefenceManager : ICommanderManager
	{
		public string Name => "defence";
		public int Order => 45;
		public int Interval => 125;
		public bool CanThinkInParallel => true;

		/// <summary>Enemy force in our own regions above which the chief should hear about it.</summary>
		public float ThreatCredits { get; init; } = 1500f;

		public void Think(CommanderSnapshot snapshot, StaffContext context)
		{
			var state = snapshot.State;
			if (state == null || state.RegionCount == 0)
				return;

			var worst = -1;
			var worstThreat = 0f;
			for (var region = 0; region < state.RegionCount; region++)
			{
				// Somewhere of ours with something of theirs standing on it.
				if (state.Self.StructuresIn(region) <= 0f)
					continue;

				var threat = state.Enemy.ArmyValueIn(region);
				if (threat > worstThreat)
				{
					worstThreat = threat;
					worst = region;
				}
			}

			var integrity = state.Self.PeakBaseIntegrity <= 0f
				? 1f
				: Math.Clamp(state.Self.BaseIntegrity / state.Self.PeakBaseIntegrity, 0f, 1f);

			context.Report(new ManagerReport
			{
				Manager = Name,
				Readiness =
					integrity < 0.4f ? Readiness.Critical
					: worstThreat > ThreatCredits || integrity < 0.75f ? Readiness.Strained
					: Readiness.Healthy,
				Headline = worst < 0
					? $"base intact at {integrity:P0}, nothing in our ground"
					: $"{worstThreat:F0} credits of enemy in R{worst}, base at {integrity:P0} of peak",
				RegionOfInterest = worst >= 0 ? worst : null,
			});

			if (worstThreat > ThreatCredits && worst >= 0)
			{
				context.Add(new DefendIntent
				{
					Region = worst,
					Urgency = Math.Clamp(worstThreat / (ThreatCredits * 4f), 0f, 1f),
					Reason = $"{worstThreat:F0} credits of enemy standing on our structures",
				});
			}
		}
	}

	/// <summary>
	/// Carries out whatever the chief's directive says to do with the field army. It does not decide
	/// whether to attack - that judgement needs the whole staff's reports and it only has its own.
	/// </summary>
	public sealed class AttackCoordinationManager : ICommanderManager
	{
		public string Name => "attack-coordination";
		public int Order => 50;
		public int Interval => 250;
		public bool CanThinkInParallel => true;

		public void Think(CommanderSnapshot snapshot, StaffContext context)
		{
			var directive = context.Directive;
			var state = snapshot.State;
			if (state == null)
				return;

			switch (directive.Stance)
			{
				case Stance.Assault when directive.MainEffortRegion.HasValue:
					context.Add(new AttackIntent
					{
						Region = directive.MainEffortRegion.Value,
						Verb = MacroVerb.Attack,
						Confidence = 1f - directive.ReserveFraction,
						Reason = directive.Rationale,
					});

					// The feint goes in alongside the main effort, not instead of it. Its whole
					// purpose is to move the defence off the place the real attack is going.
					if (directive.FeintRegion.HasValue)
					{
						context.Add(new AttackIntent
						{
							Region = directive.FeintRegion.Value,
							Verb = MacroVerb.Feint,
							Confidence = 0.2f,
							Reason = "draw the defence off the main effort",
						});
					}

					break;

				case Stance.Pressure when directive.MainEffortRegion.HasValue:
					context.Add(new AttackIntent
					{
						Region = directive.MainEffortRegion.Value,
						Verb = MacroVerb.Harass,
						Confidence = 0.3f,
						Reason = "squeeze without committing while the army finishes",
					});
					break;

				case Stance.Defend:
					if (directive.MainEffortRegion.HasValue)
						context.Add(new DefendIntent
						{
							Region = directive.MainEffortRegion.Value,
							Urgency = 1f,
							Reason = directive.Rationale,
						});
					break;
			}

			context.Report(new ManagerReport
			{
				Manager = Name,
				Readiness = Readiness.Healthy,
				Headline = $"executing {directive.Stance}",
				ForceValue = state.Self.ArmyValue(),
			});
		}
	}

	/// <summary>
	/// <para>
	/// Owns infiltration, stealth insertion and everything else that wins by not being seen.
	/// </para>
	/// <para>
	/// Only acts when the chief authorises it, and the chief only authorises it when the opponent
	/// model is confident. A spy sent at a base nobody has identified is a spy spent on a guess, and
	/// the units involved are expensive enough that guessing with them is a real cost.
	/// </para>
	/// </summary>
	public sealed class SpecialOperationsManager : ICommanderManager
	{
		public string Name => "special-operations";
		public int Order => 60;
		public int Interval => 750;
		public bool CanThinkInParallel => true;

		/// <summary>
		/// Fallback infiltrators, used only when no capability registry is available.
		/// </summary>
		public IReadOnlyList<string> FallbackOperatives { get; init; } = ["spy", "thf", "e6"];

		/// <summary>Who can run a covert operation, asked of the registry rather than listed.</summary>
		static IReadOnlyList<string> OperativesFor(CommanderSnapshot snapshot) =>
			snapshot.Database?.Capabilities?.Operatives().Select(c => c.Type).ToArray();

		public void Think(CommanderSnapshot snapshot, StaffContext context)
		{
			var operatives = OperativesFor(snapshot);
			if (operatives == null || operatives.Count == 0)
				operatives = FallbackOperatives;

			var directive = context.Directive;
			if (!directive.AuthoriseSpecialOperations)
			{
				// Standing down returns the operatives to normal behaviour. Without this they would
				// hold fire for the rest of the match, having been told to be quiet once.
				//
				// Only for types actually held. Ordering every operative type in the game to stand
				// down whether or not one exists issued 162 orders in a single match that the game
				// could not act on, because the executor walks OUR units looking for that type and
				// finds none. It cost nothing but it made a real channel look like a dead one in
				// the audit, and a diagnostic that cries wolf is worse than no diagnostic.
				foreach (var type in operatives.Where(o => snapshot.Units.GetValueOrDefault(o) > 0))
					context.Add(new CovertTransitIntent
					{
						OperativeType = type,
						InTransit = false,
						Reason = "operation not authorised",
					});

				context.Report(new ManagerReport
				{
					Manager = Name,
					Readiness = Readiness.Healthy,
					Headline = "standing down: not authorised",
				});

				return;
			}

			var operative = operatives.FirstOrDefault(o => snapshot.Units.GetValueOrDefault(o) > 0);
			if (operative == null)
			{
				context.Request(new ProductionRequest
				{
					Requester = Name,
					Item = operatives[0],
					Priority = RequestPriority.Wanted,
					Reason = "authorised for infiltration with nobody to send",
				});

				context.Report(new ManagerReport
				{
					Manager = Name,
					Readiness = Readiness.Strained,
					Headline = "authorised but no operative available",
					ReadyInSeconds = 45,
				});

				return;
			}

			if (directive.MainEffortRegion.HasValue)
			{
				// Quiet first, then sent. A unit that shoots on the way announces both its position
				// and that something is coming.
				context.Add(new CovertTransitIntent
				{
					OperativeType = operative,
					InTransit = true,
					Reason = $"infiltrating region {directive.MainEffortRegion.Value}",
				});

				context.Add(new AttackIntent
				{
					Region = directive.MainEffortRegion.Value,
					Verb = MacroVerb.Harass,
					Confidence = 0.1f,
					Reason = $"infiltrate with {operative} ahead of the main effort",
				});
			}

			context.Report(new ManagerReport
			{
				Manager = Name,
				Readiness = Readiness.Healthy,
				Headline = $"{operative} available and committed",
				ReadyInSeconds = 0,
			});
		}
	}

	/// <summary>
	/// One arm of the army. Ground, air and naval differ only in what they command and where they
	/// can go, so they share an implementation - and the naval arm reporting "there is no water on
	/// this map" is genuinely useful to a chief deciding where to spend.
	/// </summary>
	public sealed class ForceArmManager : ICommanderManager
	{
		public string Name { get; init; } = "ground-force";
		public int Order { get; init; } = 70;
		public int Interval => 250;
		public bool CanThinkInParallel => true;

		/// <summary>Which role this arm commands.</summary>
		public CombatRole Role { get; init; } = CombatRole.Armor;

		/// <summary>Extra roles counted as part of this arm.</summary>
		public IReadOnlyList<CombatRole> AlsoCounts { get; init; } = [];

		public void Think(CommanderSnapshot snapshot, StaffContext context)
		{
			var state = snapshot.State;
			if (state == null)
				return;

			var value = 0f;
			for (var region = 0; region < state.RegionCount; region++)
			{
				value += state.Self.ForceValue(region, Role);
				foreach (var extra in AlsoCounts)
					value += state.Self.ForceValue(region, extra);
			}

			// An arm that does not exist is not an arm that is failing. A commander with no navy on
			// a landlocked map is in no trouble whatsoever, and reporting otherwise pinned the chief
			// in Recover for entire matches.
			context.Report(new ManagerReport
			{
				Manager = Name,
				Readiness = value <= 0f ? Readiness.NotApplicable : Readiness.Healthy,
				Headline = value <= 0f
					? "this arm is not fielded"
					: $"{value:F0} credits under command",
				ForceValue = value > 0f ? value : null,
			});
		}
	}
}
