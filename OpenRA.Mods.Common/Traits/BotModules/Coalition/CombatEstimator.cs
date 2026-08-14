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
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	/// <summary>
	/// Combat estimation, target scoring, and route-risk planning for the coalition commander.
	/// Everything is deterministic so all allied bots reach the same conclusions.
	/// </summary>
	public static class CombatEstimator
	{
		/// <summary>Power weight of one full-health unit of a class.</summary>
		public static float ClassWeight(UnitClass unitClass)
		{
			return unitClass switch
			{
				UnitClass.Infantry => 1f,
				UnitClass.Armor => 3f,
				UnitClass.Air => 2.5f,
				UnitClass.Naval => 2f,
				UnitClass.Structure => 2f,
				_ => 0.5f
			};
		}

		/// <summary>Power of a single actor: class weight scaled by remaining health.</summary>
		public static float Power(Actor a, Func<Actor, UnitClass> classify)
		{
			var health = a.TraitOrDefault<IHealth>();
			var hp = health == null ? 1f : health.HP * 1f / health.MaxHP;
			return ClassWeight(classify(a)) * hp;
		}

		/// <summary>Combined power of a force.</summary>
		public static float ForcePower(IEnumerable<Actor> actors, Func<Actor, UnitClass> classify)
		{
			return actors.Sum(a => Power(a, classify));
		}

		/// <summary>Lanchester-style outcome estimate: win ratio and expected friendly power loss.</summary>
		public static (float WinRatio, float LossFraction) Estimate(float friendlyPower, float enemyPower)
		{
			if (enemyPower <= 0)
				return (1f, 0f);

			if (friendlyPower <= 0)
				return (0f, 1f);

			var ratio = friendlyPower / enemyPower;
			var loss = enemyPower >= friendlyPower
				? 1f - friendlyPower / enemyPower
				: 0.5f * (1f - enemyPower / friendlyPower);
			return (ratio, Math.Clamp(loss, 0f, 1f));
		}

		/// <summary>Class-matchup multiplier: how effective an attacker class is against a defender class.</summary>
		public static float MatchupFactor(UnitClass attacker, UnitClass defender)
		{
			return (attacker, defender) switch
			{
				(UnitClass.Armor, UnitClass.Infantry) => 1.25f,   // armor overruns infantry
				(UnitClass.Air, UnitClass.Naval) => 1.2f,        // air harasses ships
				(UnitClass.Infantry, UnitClass.Air) => 0.5f,     // rifles barely hurt planes
				(UnitClass.Naval, UnitClass.Armor) => 0.5f,      // ships cannot engage tanks
				(UnitClass.Armor, UnitClass.Structure) => 1.2f,  // armor cracks static defenses
				(UnitClass.Structure, UnitClass.Structure) => 0f, // static defenses do not duel each other
				_ => 1f
			};
		}

		/// <summary>
		/// Friendly power adjusted for class matchups against the enemy's dominant class. Returns the
		/// sum of each class's weight and count scaled by its matchup against the enemy composition.
		/// </summary>
		public static float MatchupPower(int[] friendlyCounts, int[] enemyCounts, float health)
		{
			var defender = DominantClass(enemyCounts);
			var power = 0f;
			for (var c = 0; c < friendlyCounts.Length; c++)
			{
				if (friendlyCounts[c] <= 0)
					continue;
				power += ClassWeight((UnitClass)c) * friendlyCounts[c] * MatchupFactor((UnitClass)c, defender);
			}

			return power * (health > 0 ? health : 1f);
		}

		static UnitClass DominantClass(int[] counts)
		{
			var best = 0;
			var bestCount = 0;
			for (var c = 0; c < counts.Length; c++)
				if (counts[c] > bestCount)
				{
					bestCount = counts[c];
					best = c;
				}

			return (UnitClass)best;
		}

		/// <summary>Air power suppressed by the opposing anti-air coverage (0..1).</summary>
		public static float SuppressAir(float airPower, float antiAirCoverage)
		{
			return airPower * (1f - Math.Clamp(antiAirCoverage, 0f, 1f));
		}

		/// <summary>Artillery contributes a pre-contact range advantage: a free fraction of its power.</summary>
		public static float RangeAdvantage(float artilleryPower)
		{
			return Math.Max(0f, artilleryPower) * 0.25f;
		}

		/// <summary>Terrain factor: defending hard or exposed ground shifts the balance.</summary>
		public static float TerrainFactor(float staticDefenseThreat, float visionExposure)
		{
			return 1f - 0.25f * Math.Clamp(staticDefenseThreat, 0f, 1f) - 0.1f * Math.Clamp(visionExposure, 0f, 1f);
		}

		/// <summary>
		/// Matchup- and terrain-adjusted estimate: air is suppressed by the opposing anti-air coverage,
		/// artillery contributes a range advantage, and hard/exposed ground penalizes the attacker.
		/// <paramref name="friendlyPower"/>/<paramref name="enemyPower"/> should already be matchup-adjusted
		/// (see <see cref="MatchupPower"/>); this composes the air, artillery, and terrain factors.
		/// </summary>
		public static (float WinRatio, float LossFraction) Estimate(
			float friendlyPower, float enemyPower,
			float friendlyAir, float enemyAir,
			float friendlyArtillery, float enemyArtillery,
			float friendlyAntiAir, float enemyAntiAir,
			float staticDefenseThreat, float visionExposure)
		{
			var terrain = TerrainFactor(staticDefenseThreat, visionExposure);
			var adjustedFriendly = (friendlyPower - friendlyAir + SuppressAir(friendlyAir, enemyAntiAir) + RangeAdvantage(friendlyArtillery)) * terrain;
			var adjustedEnemy = enemyPower - enemyAir + SuppressAir(enemyAir, friendlyAntiAir) + RangeAdvantage(enemyArtillery);
			return Estimate(adjustedFriendly, adjustedEnemy);
		}

		/// <summary>Major matchup weaknesses the engagement exposes, in human-readable form.</summary>
		public static IEnumerable<string> MajorRisks(float enemyAntiAir, float friendlyAir, float enemyArtillery,
			float friendlyArtillery, float enemyAir, float friendlyAntiAir)
		{
			if (enemyAntiAir > 0 && friendlyAir > 0)
				yield return "enemy_anti_air";
			if (enemyArtillery > 0 && friendlyArtillery <= 0)
				yield return "enemy_artillery";
			if (enemyAir > 0 && friendlyAntiAir <= 0)
				yield return "insufficient_anti_air";
			if (friendlyAir > 0 && friendlyAntiAir <= 0)
				yield return "no_air_cover";
		}

		/// <summary>Capability gaps the coalition should close before committing, derived from the matchup.</summary>
		public static IEnumerable<string> CapabilityGaps(float enemyAir, float friendlyAntiAir, float enemyArmor,
			float friendlyArtillery, float enemyAntiAir, float friendlyAir)
		{
			if (enemyAir > 0 && friendlyAntiAir <= 0)
				yield return "anti_air";
			if (enemyArmor > 0 && friendlyArtillery <= 0)
				yield return "anti_armor";
			if (enemyAntiAir > 0 && friendlyAir > 0)
				yield return "more_air";
		}

		/// <summary>Whose reinforcements are expected to shift the engagement, from the two sides' reinforcement potential.</summary>
		public static string ReinforcementAdvantage(float friendlyReinforcement, float enemyReinforcement)
		{
			var delta = friendlyReinforcement - enemyReinforcement;
			if (delta > 0.25f)
				return "friendly";
			if (delta < -0.25f)
				return "enemy";
			return "even";
		}

		/// <summary>Strategic value of an enemy target: economy and tech first, military by class weight.</summary>
		public static float TargetValue(Actor target, Func<Actor, UnitClass> classify)
		{
			if (target.Info.HasTraitInfo<BuildingInfo>())
			{
				switch (target.Info.Name)
				{
					case "proc":
					case "silo":
						return 10f;
					case "atek":
					case "stek":
					case "dome":
					case "afld":
					case "hpad":
					case "spen":
					case "syrd":
						return 8f;
					case "fact":
					case "barr":
					case "tent":
					case "weap":
					case "fix":
						return 7f;
					case "powr":
					case "apwr":
						return 6f;
					default:
						return 4f;
				}
			}

			return 2f * ClassWeight(classify(target));
		}

		/// <summary>
		/// Risk of walking a straight-line route: samples the region threat map along the path
		/// (static defenses and anti-air weigh most, vision exposure less).
		/// </summary>
		public static float RouteRisk(CoalitionBlackboard blackboard, CPos from, CPos to)
		{
			var delta = to - from;
			var steps = Math.Max(1, Math.Abs(delta.Length) / 4);
			var risk = 0f;
			for (var i = 0; i <= steps; i++)
			{
				var cell = from + delta * i / steps;
				var region = blackboard.RegionOf(cell);
				risk += 2f * region.Threats[(int)CoalitionCapability.StaticDefense]
					+ region.Threats[(int)CoalitionCapability.AntiAir]
					+ 0.5f * region.Threats[(int)CoalitionCapability.VisionExposure];
			}

			return risk / (steps + 1);
		}
	}
}
