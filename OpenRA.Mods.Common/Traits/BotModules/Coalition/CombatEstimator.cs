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
