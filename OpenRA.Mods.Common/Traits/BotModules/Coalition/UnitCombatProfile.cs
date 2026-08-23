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
using OpenRA.Mods.Common.Warheads;

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	/// <summary>
	/// Everything the rules say about one actor type's ability to fight, read from the loaded
	/// ruleset rather than from a hand-maintained list.
	/// </summary>
	public sealed class UnitCombatProfile
	{
		public readonly string Type;
		public readonly int Cost;
		public readonly int HitPoints;

		/// <summary>Armour class: None, Light, Heavy, Wood, Concrete, Ship, Defense.</summary>
		public readonly string Armor;

		/// <summary>Raw damage per second against each armour class, keyed by armour name.</summary>
		public readonly Dictionary<string, float> DamagePerSecondVersus = [];

		/// <summary>Longest weapon range in cells; 0 for an unarmed actor.</summary>
		public readonly int RangeCells;

		public readonly bool IsArmed;
		public readonly bool CanTargetAir;
		public readonly bool CanTargetGround;

		/// <summary>True for aircraft, read from the Aircraft trait rather than inferred from armour.</summary>
		public readonly bool IsAircraft;

		public UnitCombatProfile(string type, int cost, int hitPoints, string armor,
			Dictionary<string, float> damagePerSecondVersus, int rangeCells,
			bool canTargetAir, bool canTargetGround, bool isAircraft = false)
		{
			Type = type;
			Cost = Math.Max(1, cost);
			HitPoints = Math.Max(1, hitPoints);
			Armor = string.IsNullOrEmpty(armor) ? "None" : armor;
			DamagePerSecondVersus = damagePerSecondVersus ?? [];
			RangeCells = Math.Max(0, rangeCells);
			IsArmed = DamagePerSecondVersus.Values.Any(d => d > 0f);
			CanTargetAir = canTargetAir;
			CanTargetGround = canTargetGround;
			IsAircraft = isAircraft;
		}

		/// <summary>Damage per second this profile deals to the given armour class.</summary>
		public float DamageVersus(string armor)
		{
			return DamagePerSecondVersus.TryGetValue(armor ?? "None", out var dps) ? dps : 0f;
		}

		/// <summary>
		/// Seconds to destroy the target, or <see cref="float.PositiveInfinity"/> when this profile
		/// cannot hurt it at all - which is the case that matters, because "cannot hurt" is invisible
		/// in a simple damage comparison.
		/// </summary>
		public float TimeToKill(UnitCombatProfile target)
		{
			if (target == null)
				return float.PositiveInfinity;

			var dps = DamageVersus(target.Armor);
			return dps <= 0f ? float.PositiveInfinity : target.HitPoints / dps;
		}

		/// <summary>
		/// <para>
		/// Combat value per credit against the given armour class: damage output times durability,
		/// divided by the square of cost.
		/// </para>
		/// <para>
		/// Cost appears squared because both factors scale with money - a fixed budget buys
		/// <c>budget/cost</c> units, and each contributes both its damage and its hit points. This is
		/// what makes the handbook's worked example come out right on the shipped numbers: against
		/// Heavy armour a heavy tank scores 174 and a mammoth 86, so three heavy tanks (3450 credits)
		/// beat two mammoths (4000) even though a mammoth wins the one-on-one duel. A duel comparison
		/// would recommend the mammoth and lose the match.
		/// </para>
		/// </summary>
		public float CostEfficiencyVersus(string armor)
		{
			var dps = DamageVersus(armor);
			return dps <= 0f ? 0f : dps * HitPoints / ((float)Cost * Cost);
		}

		/// <summary>
		/// How good a purchase this profile is against the target. Zero when it cannot engage the
		/// target's domain or cannot hurt its armour at all.
		/// </summary>
		public float CounterScore(UnitCombatProfile target)
		{
			if (target == null || !IsArmed || !CounterMatrix.CanEngage(this, target))
				return 0f;

			return CostEfficiencyVersus(target.Armor);
		}
	}

	/// <summary>
	/// <para>
	/// The counter matrix, derived from the mod's own weapon and armour data (handbook §3).
	/// </para>
	/// <para>
	/// The coalition previously answered enemy composition from four hand-maintained lists in
	/// ai.yaml - AntiArmorUnits, AntiAirUnits and so on. Those encode one person's reading of the
	/// balance at one point in time, go stale silently when the mod changes, and cannot express
	/// degree: they say 4tnk counters armour, not that it counters armour twice as cost-effectively
	/// as 3tnk does. The engine already holds the real answer in DamageWarhead.Versus, so the matrix
	/// is computed instead of declared.
	/// </para>
	/// </summary>
	public static class CounterMatrix
	{
		/// <summary>Builds a combat profile for one actor type from the ruleset.</summary>
		public static UnitCombatProfile Profile(ActorInfo actorInfo, Ruleset rules)
		{
			if (actorInfo == null || rules == null)
				return null;

			var cost = actorInfo.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0;
			var health = actorInfo.TraitInfoOrDefault<HealthInfo>()?.HP ?? 0;
			var armor = actorInfo.TraitInfoOrDefault<ArmorInfo>()?.Type;

			var damage = new Dictionary<string, float>(StringComparer.Ordinal);
			var range = 0;
			var canTargetAir = false;
			var canTargetGround = false;

			foreach (var armament in actorInfo.TraitInfos<ArmamentInfo>())
			{
				if (string.IsNullOrEmpty(armament.Weapon)
					|| !rules.Weapons.TryGetValue(armament.Weapon.ToLowerInvariant(), out var weapon))
					continue;

				range = Math.Max(range, weapon.Range.Length / 1024);

				// A weapon that cannot target a domain contributes nothing against it, however much
				// damage it does - the classic case being rifle infantry "countering" aircraft.
				var hitsAir = weapon.ValidTargets.Contains("Air") && !weapon.InvalidTargets.Contains("Air");
				var hitsGround = (weapon.ValidTargets.Contains("Ground") || weapon.ValidTargets.Contains("Water"))
					&& !weapon.InvalidTargets.Contains("Ground");

				canTargetAir |= hitsAir;
				canTargetGround |= hitsGround;

				// InvalidTargets is not decoration: a heavy tank's 120mm declares InvalidTargets:
				// Infantry, so counting its damage against None armour would have the coalition
				// believe tanks answer massed infantry when the shell cannot be fired at them.
				var cannotHitInfantry = weapon.InvalidTargets.Contains("Infantry");

				// Reload delay is in ticks; the engine runs at 25 ticks per second.
				var shotsPerSecond = 25f / Math.Max(1, weapon.ReloadDelay) * Math.Max(1, weapon.Burst);

				foreach (var warhead in weapon.Warheads.OfType<DamageWarhead>())
				{
					if (warhead.Damage <= 0)
						continue;

					foreach (var armorClass in ArmorClasses)
					{
						// "None" armour is what infantry carry, so a weapon barred from firing at
						// infantry contributes nothing there.
						if (cannotHitInfantry && armorClass == "None")
							continue;

						// Versus is a percentage; an armour class absent from the table takes full damage.
						var versus = warhead.Versus.TryGetValue(armorClass, out var percent) ? percent : 100;
						var dps = warhead.Damage * shotsPerSecond * versus / 100f;
						damage[armorClass] = damage.GetValueOrDefault(armorClass) + dps;
					}
				}
			}

			return new UnitCombatProfile(actorInfo.Name, cost, health, armor, damage, range,
				canTargetAir, canTargetGround, actorInfo.HasTraitInfo<AircraftInfo>());
		}

		/// <summary>The armour classes RA defines. Anything not listed takes full damage.</summary>
		public static readonly string[] ArmorClasses =
			["None", "Wood", "Light", "Heavy", "Concrete", "Ship", "Defense"];

		/// <summary>
		/// Ranks buildable units by how well they answer the observed enemy composition, weighted by
		/// how much of that composition each enemy type represents. Returns best counter first.
		/// </summary>
		public static IReadOnlyList<(string Unit, float Score)> RankCounters(
			IEnumerable<UnitCombatProfile> buildable,
			IReadOnlyDictionary<UnitCombatProfile, int> enemyComposition)
		{
			if (buildable == null || enemyComposition == null || enemyComposition.Count == 0)
				return [];

			var totalEnemies = enemyComposition.Values.Sum();
			if (totalEnemies <= 0)
				return [];

			return buildable
				.Where(p => p != null && p.IsArmed)
				.Select(p =>
				{
					var score = 0f;
					foreach (var (enemy, count) in enemyComposition)
					{
						// An answer to the enemy's most numerous units is worth more than an answer
						// to one outlier, so each matchup is weighted by its share of what was seen.
						var share = count / (float)totalEnemies;

						// A unit that cannot engage the target's domain scores zero against it,
						// however good its damage numbers look.
						if (!CanEngage(p, enemy))
							continue;

						score += p.CounterScore(enemy) * share;
					}

					return (p.Type, score);
				})
				.Where(x => x.score > 0f)
				.OrderByDescending(x => x.score)
				.ThenBy(x => x.Type, StringComparer.Ordinal)
				.ToArray();
		}

		/// <summary>Whether the attacker can engage the target's domain at all.</summary>
		public static bool CanEngage(UnitCombatProfile attacker, UnitCombatProfile target)
		{
			if (attacker == null || target == null)
				return false;

			// Aircraft are the case worth being explicit about: a weapon with no air targeting is
			// not a partial answer to an air force, it is no answer.
			return target.IsAircraft ? attacker.CanTargetAir : attacker.CanTargetGround;
		}
	}
}
