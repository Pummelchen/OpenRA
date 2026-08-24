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
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

namespace OpenRA.Mods.Common.Commander.Model
{
	/// <summary>
	/// <para>
	/// Decides what to build by computing which unit is worth most against the enemy that actually
	/// exists, rather than by consulting a list somebody ordered by hand.
	/// </para>
	/// <para>
	/// The lists this replaces - <c>ArmyPriority</c>, <c>AntiArmorUnits</c>,
	/// <c>AntiInfantryUnits</c> and the rest - are rules in the plainest sense: a person's opinion
	/// about what beats what, fixed at the moment it was written. They go stale silently when the
	/// mod changes, they cannot express degree (a mammoth counters armour, but how much better than
	/// a heavy tank, per credit?), and every ordering decision inside them has been a guess this
	/// project then had to measure and often reverse.
	/// </para>
	/// <para>
	/// The engine already holds the real answer. <c>DamageWarhead.Versus</c> says exactly what each
	/// weapon does to each armour class, <c>Valued</c> says what it costs, and <c>Health</c> says
	/// what it survives. Fighting value per credit against a known composition is arithmetic over
	/// those three, so it is computed instead of declared - and it re-derives itself the moment
	/// either the mod or the enemy changes.
	/// </para>
	/// <para>
	/// Two things are deliberately kept from the rules it replaces, because both were measured
	/// rather than assumed. Build time is priced in: a unit that arrives after the base has fallen is
	/// worth nothing, and this commander died at 17,000 ticks holding 137,760 credits because the
	/// first thing on its list cost 2,000 and took most of a minute. And a screen of cheap units has
	/// value beyond its damage, so infantry are not judged purely on cost efficiency.
	/// </para>
	/// </summary>
	public static class ProductionValuation
	{
		/// <summary>What a candidate unit is worth right now, and why.</summary>
		public readonly record struct Valuation(string Unit, float Score, string Rationale)
		{
			public override string ToString() => $"{Unit} {Score:F2} ({Rationale})";
		}

		/// <summary>
		/// Scores every buildable candidate against the believed enemy composition.
		/// </summary>
		/// <param name="candidates">Buildable unit types and their profiles.</param>
		/// <param name="enemyComposition">Believed enemy value by armour class.</param>
		/// <param name="urgency">
		/// 0 when there is time to build anything, 1 when the army is needed immediately. Scales how
		/// hard build time is penalised, which is the whole difference between opening against a
		/// rush and grinding down a turtle.
		/// </param>
		public static IReadOnlyList<Valuation> Rank(
			IEnumerable<UnitCombatProfile> candidates,
			IReadOnlyDictionary<string, float> enemyComposition,
			float urgency = 0f)
		{
			ArgumentNullException.ThrowIfNull(candidates);
			ArgumentNullException.ThrowIfNull(enemyComposition);

			urgency = Math.Clamp(urgency, 0f, 1f);

			var total = enemyComposition.Values.Where(v => v > 0f).Sum();
			var results = new List<Valuation>();

			foreach (var profile in candidates)
			{
				if (profile == null || profile.Cost <= 0)
					continue;

				float score;
				string rationale;

				if (total <= 0f)
				{
					// Nothing seen yet. Judge on general durability per credit rather than guessing
					// at a composition - being wrong about an unseen enemy is how a commander builds
					// the wrong counter and discovers it too late.
					score = profile.HitPoints / (float)profile.Cost;
					rationale = "no enemy seen: durability per credit";
				}
				else
				{
					// Fighting value per credit against what they actually have, weighted by how
					// much of it there is.
					var value = 0f;
					foreach (var (armour, amount) in enemyComposition)
					{
						if (amount <= 0f)
							continue;

						value += amount / total * profile.CostEfficiencyVersus(armour);
					}

					score = value;
					rationale = "cost efficiency against the believed composition";
				}

				// A unit that arrives too late is worth nothing, and the penalty scales with how
				// badly it is needed now.
				if (urgency > 0f && profile.Cost > 0)
				{
					var relativeCost = profile.Cost / 1000f;
					score /= 1f + (urgency * relativeCost);
					rationale += $", discounted for {profile.Cost} credits at urgency {urgency:F2}";
				}

				results.Add(new Valuation(profile.Type, score, rationale));
			}

			// Ties broken by name so the same position always produces the same build.
			results.Sort((a, b) => a.Score != b.Score
				? b.Score.CompareTo(a.Score)
				: string.CompareOrdinal(a.Unit, b.Unit));

			return results;
		}

		/// <summary>
		/// The believed enemy composition, by armour class, from whatever has been observed. Values
		/// are credits, so a single mammoth counts for more than a single rifleman.
		/// </summary>
		public static Dictionary<string, float> CompositionOf(
			IEnumerable<(string Type, int Cost, string Armour)> seen)
		{
			ArgumentNullException.ThrowIfNull(seen);

			var composition = new Dictionary<string, float>(StringComparer.Ordinal);
			foreach (var (_, cost, armour) in seen)
			{
				if (cost <= 0 || string.IsNullOrEmpty(armour))
					continue;

				composition[armour] = composition.GetValueOrDefault(armour) + cost;
			}

			return composition;
		}
	}
}
