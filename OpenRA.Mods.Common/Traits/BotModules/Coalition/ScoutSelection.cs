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

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	/// <summary>A candidate scout: what it costs and how fast it covers ground.</summary>
	public readonly record struct ScoutCandidate(string Type, int Cost, int Speed);

	/// <summary>
	/// <para>
	/// Picks the best available reconnaissance unit from what the faction can actually build
	/// (handbook §6).
	/// </para>
	/// <para>
	/// Naming a unit directly does not survive contact with the mod: the dog is the obvious scout -
	/// 200 credits against rifle infantry's 100, but speed 100 against their 54, so it covers close
	/// to twice the ground - and its kennel requires ~structures.soviet. An Allied commander told to
	/// scout with dogs simply never scouts, which is exactly what happened here: 80 probes sent, not
	/// one of them a dog, because the faction could not build the kennel.
	/// </para>
	/// <para>
	/// So the choice is derived. Score is speed over the square root of cost, which prefers a fast
	/// unit without letting an expensive one win on speed alone - a 500-credit ranger is not four
	/// times the scout a rifleman is. Soviets get dogs, Allies get infantry, and a mod that changes
	/// either gets whatever is actually best.
	/// </para>
	/// </summary>
	public static class ScoutSelection
	{
		/// <summary>
		/// Reconnaissance value: ground covered per credit, weighted toward raw speed because a
		/// scout's job is to reveal ground early and it is expected to die doing it.
		/// </summary>
		public static float Score(ScoutCandidate candidate)
		{
			if (candidate.Cost <= 0 || candidate.Speed <= 0)
				return 0f;

			return candidate.Speed / MathF.Sqrt(candidate.Cost);
		}

		/// <summary>
		/// The best scout among those currently buildable, or null when none is. Candidates above
		/// <paramref name="maximumCost"/> are excluded: a scout is expected to be lost, so spending
		/// real money on one is spending it in the wrong place.
		/// </summary>
		public static string Best(IEnumerable<ScoutCandidate> buildable, int maximumCost = 600)
		{
			var best = (buildable ?? [])
				.Where(c => c.Cost > 0 && c.Cost <= maximumCost && c.Speed > 0)
				.OrderByDescending(Score)
				.ThenBy(c => c.Cost)
				.ThenBy(c => c.Type, StringComparer.Ordinal)
				.Select(c => c.Type)
				.FirstOrDefault();

			return best;
		}

		/// <summary>
		/// Ranks candidates best-first, so the caller can prefer the top choice and fall back
		/// through the rest as availability changes mid-match.
		/// </summary>
		public static IReadOnlyList<string> Ranked(IEnumerable<ScoutCandidate> buildable, int maximumCost = 600)
		{
			return (buildable ?? [])
				.Where(c => c.Cost > 0 && c.Cost <= maximumCost && c.Speed > 0)
				.OrderByDescending(Score)
				.ThenBy(c => c.Cost)
				.ThenBy(c => c.Type, StringComparer.Ordinal)
				.Select(c => c.Type)
				.ToArray();
		}

		/// <summary>
		/// <para>
		/// The scout to produce: the first entry of <paramref name="preferred"/> that is currently
		/// buildable, falling back to the best-scoring buildable unit when none of them is.
		/// </para>
		/// <para>
		/// Preference comes first because the scoring cannot see everything that makes a scout good.
		/// On the shipped numbers a ranger scores 7.33 against a dog's 7.07 and would be chosen - but
		/// it costs 500 against 200, so losing one (which is the expected outcome for a scout) hurts
		/// two and a half times as much, and the dog also detects infiltrators the ranger cannot.
		/// Meanwhile rifle infantry are cheapest of all and move at 54 against the dog's 100, slow
		/// enough that on a mid-size map they frequently die before arriving anywhere useful.
		/// </para>
		/// <para>
		/// The derived fallback still matters: a dog needs a Soviet kennel, so an Allied commander
		/// that only knew the preference would never scout at all.
		/// </para>
		/// </summary>
		public static string Preferred(IEnumerable<string> preferred, IEnumerable<ScoutCandidate> buildable,
			int maximumCost = 600)
		{
			var available = (buildable ?? []).Select(c => c.Type).ToHashSet(StringComparer.Ordinal);

			foreach (var type in preferred ?? [])
				if (available.Contains(type))
					return type;

			return Best(buildable, maximumCost);
		}

		/// <summary>Builds a candidate from an actor's rules, or null when it cannot scout.</summary>
		public static ScoutCandidate? Candidate(ActorInfo actorInfo)
		{
			if (actorInfo == null)
				return null;

			var cost = actorInfo.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0;
			var mobile = actorInfo.TraitInfoOrDefault<MobileInfo>();
			if (cost <= 0 || mobile == null)
				return null;

			return new ScoutCandidate(actorInfo.Name, cost, mobile.Speed);
		}
	}
}
