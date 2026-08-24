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
	/// Owns the map: which ground is open, which is narrow, and what that implies.
	/// </para>
	/// <para>
	/// Runs rarely, because terrain does not move. What it produces is doctrine rather than orders -
	/// a map whose region graph has one path between the bases is a map where feints are worthless
	/// and siege is everything; one with four is a map where the main force should never be the only
	/// force.
	/// </para>
	/// </summary>
	public sealed class MapAnalysisManager : ICommanderManager
	{
		public string Name => "map-analysis";
		public int Order => 1;
		public int Interval => 2500;
		public bool CanThinkInParallel => true;

		bool reported;

		public void Think(CommanderSnapshot snapshot, StaffContext context)
		{
			var graph = snapshot.Graph;
			if (graph == null || graph.Regions.Length == 0 || reported)
				return;

			reported = true;

			// The narrowest link between the two ends of the map is the defensive line, and its
			// width says whether this map rewards holding ground or manoeuvring around it.
			var ordered = graph.Regions.OrderByDescending(r => r.CellCount).ToArray();
			if (ordered.Length < 2)
			{
				context.Add(new AssessmentIntent
				{
					Topic = "terrain",
					Finding = "one open region: nothing to hold, everything decided in the field",
				});
				return;
			}

			var cut = graph.MinCutBetween(ordered[0].Id, ordered[^1].Id);
			var verdict = cut.Value == 0 ? "no ground route at all"
				: cut.CutEdges.Length == 1 ? $"a single choke of width {cut.Value} decides the map"
				: $"{cut.CutEdges.Length} routes totalling {cut.Value}: too many to hold, manoeuvre instead";

			context.Add(new AssessmentIntent
			{
				Topic = "terrain",
				Finding = $"{graph.Regions.Length} regions, {graph.Chokepoints.Length} chokepoints - {verdict}",
			});

			context.Report(new ManagerReport
			{
				Manager = Name,
				Readiness = Readiness.Healthy,
				Headline = verdict,
			});
		}
	}

	/// <summary>
	/// <para>
	/// Owns what is known about the enemy, and how sure of it we are.
	/// </para>
	/// <para>
	/// It exists because being wrong here is fatal and silent. A commander that had built no
	/// anti-air was destroyed by an opponent whose units it could see perfectly well and could not
	/// name - the classification lists covered tanks but not jeeps, flak trucks or V2 launchers, so
	/// an entire enemy army was sighted, classified as nothing, and reported as "armor=False
	/// air=False infantry=False" for the whole match.
	/// </para>
	/// </summary>
	public sealed class IntelligenceManager : ICommanderManager
	{
		public string Name => "intelligence";
		public int Order => 2;
		public int Interval => 250;
		public bool CanThinkInParallel => true;

		/// <summary>Confidence above which the opponent model is worth acting on rather than hedging against.</summary>
		public float ActionableConfidence { get; init; } = 0.15f;

		public void Think(CommanderSnapshot snapshot, StaffContext context)
		{
			var posterior = snapshot.Opponent;
			if (posterior == null)
				return;

			var (strategy, probability) = posterior.Best();
			var confidence = posterior.Confidence();

			context.Report(new ManagerReport
			{
				Manager = Name,
				Readiness = confidence < ActionableConfidence ? Readiness.Strained : Readiness.Healthy,
				Headline = confidence < ActionableConfidence
					? $"no read yet - best guess {strategy.ToString().ToLowerInvariant()} at {probability:P0}"
					: $"{strategy.ToString().ToLowerInvariant()} at {probability:P0}",

				// The chief weighs this: the same words at 20% confidence and at 90% are different
				// inputs, and committing a counter on the strength of the former is superstition.
				Confidence = confidence,
			});

			if (confidence < ActionableConfidence)
			{
				context.Add(new AssessmentIntent
				{
					Topic = "opponent",
					Finding = $"no read yet (best guess {strategy.ToString().ToLowerInvariant()} " +
						$"at {probability:P0}, confidence {confidence:P0}) - hedge rather than commit",
				});
				return;
			}

			context.Add(new AssessmentIntent
			{
				Topic = "opponent",
				Finding = $"{strategy.ToString().ToLowerInvariant()} at {probability:P0} " +
					$"(confidence {confidence:P0})",
			});

			// Counters are ordered before the aircraft arrive, not after. Anti-air takes time to
			// build, so waiting for the first sighting means waiting until it is overhead.
			if (strategy == OpponentStrategy.Air)
			{
				context.Add(new ProduceUnitIntent
				{
					Unit = "mig",
					Count = 2,
					Reason = $"air expected at {probability:P0} before it is seen",
				});

				context.Add(new ConstructIntent { Structure = "agun", Reason = "air expected" });
			}

			if (strategy == OpponentStrategy.Rush)
				context.Add(new ConstructIntent { Structure = "pbox", Reason = "rush expected: hold the early push" });
		}
	}

	/// <summary>
	/// <para>
	/// Owns reconnaissance: where to look, and what it is worth.
	/// </para>
	/// <para>
	/// Directed by the belief state rather than by geometry, which is a correction of something
	/// measured. The previous sweep sent forty scouts a match to map edges and the top row - 22,2 /
	/// 23,2 / 97,2 / 98,2 on a 127x127 map - and located the enemy base exactly zero times, so every
	/// assault that followed took empty ground.
	/// </para>
	/// </summary>
	public sealed class ScoutingManager : ICommanderManager
	{
		public string Name => "scouting";
		public int Order => 3;
		public int Interval => 375;
		public bool CanThinkInParallel => true;

		/// <summary>Scouts to keep out at once.</summary>
		public int ConcurrentScouts { get; init; } = 4;

		/// <summary>Cheapest thing that can carry an eye across a map.</summary>
		public string ScoutUnit { get; init; } = "dog";

		public void Think(CommanderSnapshot snapshot, StaffContext context)
		{
			var belief = snapshot.Belief;
			if (belief == null)
				return;

			var target = belief.MostUncertainRegion(snapshot.Tick);
			if (target < 0)
				return;

			context.Add(new ScoutIntent
			{
				Region = target,
				Reason = "most believed enemy mass in a place nobody has looked",
			});

			// Scouting is only free if the scouts exist. Dogs are the cheapest eye in the mod, and
			// finding the enemy base is worth more to this commander than almost anything else it
			// could spend the same credits on.
			var scouts = snapshot.Units.GetValueOrDefault(ScoutUnit);
			if (scouts < ConcurrentScouts)
			{
				context.Add(new ProduceUnitIntent
				{
					Unit = ScoutUnit,
					Count = ConcurrentScouts - scouts,
					Reason = $"only {scouts} scouts out",
				});
			}

			// Reported as a region of interest rather than as an order: whether looking there is
			// worth the detour is the chief's call, not this manager's.
			context.Report(new ManagerReport
			{
				Manager = Name,
				Readiness = scouts == 0 ? Readiness.Critical : Readiness.Healthy,
				Headline = scouts == 0
					? "blind: no scouts in the field"
					: $"{scouts} scouts out, looking at R{target}",
				RegionOfInterest = target,
				ReadyInSeconds = scouts == 0 ? 30 : 0,
			});
		}
	}
}
