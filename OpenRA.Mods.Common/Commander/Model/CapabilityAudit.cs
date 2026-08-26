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

namespace OpenRA.Mods.Common.Commander.Model
{
	/// <summary>
	/// <para>
	/// Prints what the registry derived, so a person can check it against what they know about the
	/// game.
	/// </para>
	/// <para>
	/// This exists because a derived registry fails silently. A hardcoded list that names the wrong
	/// unit is obvious on sight; a table that computed the wrong damage-per-credit looks exactly like
	/// a table that computed the right one, and the commander would go on making confident decisions
	/// from it. The check is whether the numbers agree with what a player would say: a Mammoth should
	/// come out slow, expensive and hard to kill; a Ranger fast, fragile and far-seeing; anti-air
	/// should list the units that actually shoot aircraft.
	/// </para>
	/// </summary>
	public static class CapabilityAudit
	{
		/// <summary>
		/// A readable account of the registry, as a sequence of log lines.
		/// </summary>
		public static IEnumerable<string> Report(CapabilityRegistry registry, int examples = 6)
		{
			ArgumentNullException.ThrowIfNull(registry);

			yield return $"AUDIT {registry.Summary()}";

			// The fastest and the slowest things that move, because speed is easy to sanity-check.
			var mobile = registry.All.Where(c => c.CanMove).OrderByDescending(c => c.Speed).ToArray();
			if (mobile.Length > 0)
			{
				yield return "AUDIT fastest: " + Join(mobile.Take(examples), c => $"{c.Type} {c.Speed:F1}c/s");
				yield return "AUDIT slowest: " + Join(mobile.Reverse().Take(examples), c => $"{c.Type} {c.Speed:F1}c/s");
			}

			var seers = registry.All.Where(c => c.Vision > 0f).OrderByDescending(c => c.Vision).ToArray();
			if (seers.Length > 0)
				yield return "AUDIT widest vision: " + Join(seers.Take(examples), c => $"{c.Type} {c.Vision:F1}c");

			var reach = registry.All.Where(c => c.IsArmed).OrderByDescending(c => c.Reach).ToArray();
			if (reach.Length > 0)
				yield return "AUDIT longest reach: " + Join(reach.Take(examples), c => $"{c.Type} {c.Reach:F1}c");

			var tough = registry.All.Where(c => !c.IsStructure).OrderByDescending(c => c.HitPoints).ToArray();
			if (tough.Length > 0)
				yield return "AUDIT toughest unit: " + Join(tough.Take(examples), c => $"{c.Type} {c.HitPoints}hp {c.Armour}");

			var aa = registry.AntiAir().OrderByDescending(c => c.DamageVersus("", true)).ToArray();
			yield return $"AUDIT anti-air ({aa.Length}): " + Join(aa.Take(examples), c => c.Type);

			// The counter matrix, which is the claim most worth checking: against each armour class,
			// what the mod's own damage tables say is the most efficient answer.
			foreach (var armour in registry.ArmourClasses)
			{
				var best = registry.BestAgainst(armour).Take(examples).ToArray();
				if (best.Length > 0)

					// Cost and raw damage are printed beside the ratio on purpose. A
					// damage-per-credit figure hides its own denominator, so a unit with a missing
					// or nominal price outranks everything and looks like a discovery rather than a
					// data problem. Showing all three makes that self-evident.
					yield return $"AUDIT best vs {armour}: "
						+ Join(best, x => $"{x.Actor.Type}({x.Actor.Cost}cr "
							+ $"{x.Actor.DamageVersus(armour):F0}dps)");
			}

			// The six that were read zero times. Each line is a tactic that did not exist before.
			var transports = registry.Transports().ToArray();
			yield return $"AUDIT transports ({transports.Length}): "
				+ Join(transports.Take(examples), c => $"{c.Type} holds {c.CargoCapacity}");

			var capturers = registry.Capturers().ToArray();
			yield return $"AUDIT capturers ({capturers.Length}): "
				+ Join(capturers.Take(examples), c => $"{c.Type} [{string.Join("/", c.CapturesTypes)}]");

			var detectors = registry.Detectors().ToArray();
			yield return $"AUDIT detectors ({detectors.Length}): "
				+ Join(detectors.Take(examples), c => $"{c.Type} {c.DetectionRange:F0}c");

			var hiders = registry.Hiders().ToArray();
			yield return $"AUDIT can hide ({hiders.Length}): " + Join(hiders.Take(examples), c => c.Type);

			var powers = registry.SupportPowerSources().ToArray();
			yield return $"AUDIT support powers ({powers.Length}): "
				+ Join(powers.Take(examples), c => $"{c.Type}:{string.Join("/", c.SupportPowers.Select(sp => $"{sp.Power} {sp.ChargeSeconds:F0}s"))}");

			foreach (var queue in new[] { "Vehicle", "Infantry", "Aircraft", "Ship" })
			{
				var producers = registry.ProducersOf(queue).ToArray();
				if (producers.Length > 0)
					yield return $"AUDIT builds {queue}: " + Join(producers.Take(examples), c => c.Type);
			}

			var plants = registry.PowerPlants().ToArray();
			if (plants.Length > 0)
				yield return "AUDIT power plants: "
					+ Join(plants.Take(examples), c => $"{c.Type} +{c.Power} ({c.Cost}cr)");

			var drains = registry.All.Where(c => c.DrawsPower)
				.OrderBy(c => c.Power).Take(examples).ToArray();
			if (drains.Length > 0)
				yield return "AUDIT hungriest: " + Join(drains, c => $"{c.Type} {c.Power}");

			// The tech graph, asked the way a commander would ask it: what do I have to build to
			// get to the thing I want? Answered from an empty base, so the whole chain shows.
			foreach (var goal in new[] { "tsla", "atek", "mslo", "4tnk" })
			{
				var path = registry.PathTo(goal, new HashSet<string>(StringComparer.Ordinal));
				var target = registry.Find(goal);
				if (target == null)
					continue;

				yield return $"AUDIT path to {goal}: "
					+ (path.Count == 0 ? "(none found)" : string.Join(" -> ", path))
					+ $"  [requires {string.Join(", ", target.Requires)}]";
			}

			// And a worked example, so the per-unit numbers can be read rather than inferred from a
			// ranking.
			foreach (var name in new[] { "4tnk", "jeep", "e1", "arty" })
			{
				var c = registry.Find(name);
				if (c == null)
					continue;

				yield return $"AUDIT {c}";
				foreach (var w in c.Weapons)
					yield return $"AUDIT   {w.Weapon}: {w.DamagePerSecond:F0}dps range {w.Range:F1}c "
						+ $"[{(w.HitsGround ? "G" : "")}{(w.HitsAir ? "A" : "")}{(w.HitsWater ? "W" : "")}] "
						+ (w.Versus.Count == 0
							? "versus: unmodified"
							: "versus " + Join(w.Versus.OrderBy(v => v.Key, StringComparer.Ordinal),
								v => $"{v.Key} {v.Value:P0}"));
			}
		}

		/// <summary>
		/// What the commander could do at this moment, so the live view can be checked the same way
		/// the static registry was.
		/// </summary>
		public static IEnumerable<string> Availability(Availability available, int examples = 8)
		{
			ArgumentNullException.ThrowIfNull(available);

			yield return "AVAIL " + available.Summary();

			foreach (var queue in available.Options.Select(o => o.Queue).Distinct().OrderBy(q => q, StringComparer.Ordinal))
			{
				var soonest = available.On(queue).Take(examples).ToArray();
				if (soonest.Length > 0)
					yield return $"AVAIL {queue}: "
						+ Join(soonest, o => $"{o.Type} {o.TimeToField:F0}s{(o.Affordable ? "" : "*")}");
			}

			var owned = Model.Availability.Verbs
				.Select(v => (Verb: v, Count: available.Owned(v)))
				.Where(x => x.Count > 0)
				.ToArray();

			if (owned.Length > 0)
				yield return "AVAIL owned: " + Join(owned, x => $"{x.Verb} {x.Count}");

			if (available.SupportPowers.Count > 0)
				yield return "AVAIL powers: " + Join(available.SupportPowers,
					p => $"{p.Name} {(p.Ready ? "READY" : $"{p.SecondsRemaining:F0}s")}");
		}

		static string Join<T>(IEnumerable<T> items, Func<T, string> format) =>
			string.Join(", ", items.Select(format));
	}
}
