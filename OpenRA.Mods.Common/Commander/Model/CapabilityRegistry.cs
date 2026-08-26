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
using OpenRA.GameRules;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Warheads;

namespace OpenRA.Mods.Common.Commander.Model
{
	/// <summary>
	/// What one weapon on one actor can do, reduced to the numbers a commander reasons with.
	/// </summary>
	public sealed class WeaponCapability
	{
		public string Weapon { get; init; } = "";

		/// <summary>Maximum range in cells.</summary>
		public float Range { get; init; }

		/// <summary>Damage per second against an unmodified target, before armour is considered.</summary>
		public float DamagePerSecond { get; init; }

		/// <summary>Can it shoot at things in the air, on the ground, on water.</summary>
		public bool HitsAir { get; init; }
		public bool HitsGround { get; init; }
		public bool HitsWater { get; init; }

		/// <summary>Damage multiplier against each armour class, 1.0 meaning unmodified.</summary>
		public IReadOnlyDictionary<string, float> Versus { get; init; } =
			new Dictionary<string, float>();

		public float VersusOr(string armour, float fallback = 1f) =>
			string.IsNullOrEmpty(armour) ? fallback : Versus.GetValueOrDefault(armour, fallback);
	}

	/// <summary>
	/// Everything derivable about one actor from the rules alone.
	/// </summary>
	/// <remarks>
	/// Every field here is READ from the mod, not declared by this commander. That distinction is the
	/// whole point of the class: the staff previously decided what a unit was for by consulting lists
	/// somebody typed - "jeep, apc, 1tnk, e1" for escorts, "ss, msub, dd, ca, pt" for the navy - which
	/// are opinions frozen at the moment they were written, wrong for any other mod, and silent when
	/// the mod is rebalanced. A registry read from the rules is correct for a unit nobody has heard of.
	/// </remarks>
	public sealed class ActorCapability
	{
		public string Type { get; init; } = "";
		public BuildCategory Category { get; init; }
		public bool IsStructure { get; init; }

		public int Cost { get; init; }
		public int HitPoints { get; init; }
		public string Armour { get; init; } = "";

		/// <summary>Cells per second. Zero for anything that cannot move.</summary>
		public float Speed { get; init; }
		public bool CanMove => Speed > 0f;
		public bool IsAircraft { get; init; }

		/// <summary>How far it reveals shroud, in cells. The honest measure of a scout.</summary>
		public float Vision { get; init; }

		public IReadOnlyList<WeaponCapability> Weapons { get; init; } = [];

		public bool IsArmed => Weapons.Count > 0;
		public bool CanHitAir => Weapons.Any(w => w.HitsAir);
		public bool CanHitGround => Weapons.Any(w => w.HitsGround);
		public bool CanHitWater => Weapons.Any(w => w.HitsWater);

		/// <summary>Longest reach of any weapon, in cells.</summary>
		public float Reach => Weapons.Count == 0 ? 0f : Weapons.Max(w => w.Range);

		/// <summary>Total damage per second across every weapon, before armour.</summary>
		public float DamagePerSecond => Weapons.Sum(w => w.DamagePerSecond);

		/// <summary>
		/// Damage per second this actor deals to a given armour class, taking the best weapon for the
		/// job rather than the sum - a unit does not fire every weapon at the same target.
		/// </summary>
		public float DamageVersus(string armour, bool targetIsAir = false)
		{
			var usable = Weapons.Where(w => targetIsAir ? w.HitsAir : w.HitsGround).ToArray();
			if (usable.Length == 0)
				return 0f;

			return usable.Max(w => w.DamagePerSecond * w.VersusOr(armour));
		}

		/// <summary>
		/// How much damage this actor can deal, per credit it costs, against a given armour class.
		/// </summary>
		/// <remarks>
		/// The "pro" half of pro-and-con, and the only comparison that means anything across units of
		/// different prices. A rifleman and a heavy tank are not comparable by damage; they are
		/// comparable by damage bought per credit spent.
		/// </remarks>
		public float DamagePerCreditVersus(string armour, bool targetIsAir = false) =>
			Cost <= 0 ? 0f : DamageVersus(armour, targetIsAir) / Cost;

		/// <summary>Hit points per credit - how much punishment the money buys.</summary>
		public float DurabilityPerCredit => Cost <= 0 ? 0f : HitPoints / (float)Cost;

		public override string ToString() =>
			$"{Type} {Category} {Cost}cr {HitPoints}hp {Armour} " +
			$"spd{Speed:F1} vis{Vision:F1} dps{DamagePerSecond:F0} reach{Reach:F1}" +
			(CanHitAir ? " AA" : "");
	}

	/// <summary>
	/// <para>
	/// What every buildable thing in the mod can do, derived from the rules at match start.
	/// </para>
	/// <para>
	/// This is the half the commander was missing. <see cref="UnitCatalogue"/> already recorded what
	/// things ARE - name, cost, hit points, armour - and the shared database records what is on the
	/// map. Neither could answer what a thing is FOR, so every manager answered that question from a
	/// hardcoded list, and eight whole capabilities the engine publishes - transport, capture,
	/// detection, repair, vision, power, tech unlocks, support powers - were read exactly zero times
	/// across the entire staff. A tactic the commander has never been told about is one it cannot
	/// consider.
	/// </para>
	/// <para>
	/// Nothing here is learned and nothing needs a training match. It is arithmetic over tables the
	/// mod already ships, which is why it is correct on the first game it ever plays.
	/// </para>
	/// </summary>
	public sealed class CapabilityRegistry
	{
		readonly Dictionary<string, ActorCapability> byType = [];

		/// <summary>Every capability, ordered by actor name so readers never depend on hash layout.</summary>
		public IReadOnlyList<ActorCapability> All { get; }

		/// <summary>The armour classes this mod actually uses, ordered.</summary>
		public IReadOnlyList<string> ArmourClasses { get; }

		public int Count => byType.Count;

		public ActorCapability Find(string type) =>
			string.IsNullOrEmpty(type) ? null : byType.GetValueOrDefault(type);

		public CapabilityRegistry(Ruleset rules, UnitCatalogue catalogue)
		{
			ArgumentNullException.ThrowIfNull(rules);
			ArgumentNullException.ThrowIfNull(catalogue);

			var armours = new SortedSet<string>(StringComparer.Ordinal);

			foreach (var entry in catalogue.All)
			{
				if (!rules.Actors.TryGetValue(entry.Type, out var actor))
					continue;

				var mobile = actor.TraitInfoOrDefault<MobileInfo>();
				var aircraft = actor.TraitInfoOrDefault<AircraftInfo>();
				var armour = actor.TraitInfoOrDefault<ArmorInfo>()?.Type ?? "";
				if (!string.IsNullOrEmpty(armour))
					armours.Add(armour);

				byType[entry.Type] = new ActorCapability
				{
					Type = entry.Type,
					Category = entry.Category,
					IsStructure = entry.IsStructure,
					Cost = entry.Cost,
					HitPoints = entry.HitPoints,
					Armour = armour,

					// Speed is in world units per tick; cells per second reads the way a person thinks.
					Speed = (mobile?.Speed ?? aircraft?.Speed ?? 0)
						* AbstractState.TicksPerSecond / 1024f,
					IsAircraft = aircraft != null,

					Vision = actor.TraitInfos<RevealsShroudInfo>()
						.Select(r => r.Range.Length / 1024f)
						.DefaultIfEmpty(0f)
						.Max(),

					Weapons = Armaments(actor, rules),
				};
			}

			All = byType.Values.OrderBy(c => c.Type, StringComparer.Ordinal).ToArray();
			ArmourClasses = armours.ToArray();
		}

		static IReadOnlyList<WeaponCapability> Armaments(ActorInfo actor, Ruleset rules)
		{
			var weapons = new List<WeaponCapability>();

			foreach (var armament in actor.TraitInfos<ArmamentInfo>())
			{
				if (string.IsNullOrEmpty(armament.Weapon)
					|| !rules.Weapons.TryGetValue(armament.Weapon.ToLowerInvariant(), out var weapon))
					continue;

				// Damage and armour modifiers live on the warheads, not the weapon, and a weapon may
				// carry several. Take the largest: that is the one that decides whether a target dies.
				var damage = 0;
				IReadOnlyDictionary<string, float> versus = new Dictionary<string, float>();

				foreach (var warhead in weapon.Warheads.OfType<DamageWarhead>())
				{
					if (warhead.Damage <= damage)
						continue;

					damage = warhead.Damage;
					versus = warhead.Versus.ToDictionary(v => v.Key, v => v.Value / 100f);
				}

				if (damage <= 0)
					continue;

				// ReloadDelay is ticks between bursts; Burst is shots per burst. Both live on the
				// weapon rather than the armament that mounts it.
				var perBurst = damage * Math.Max(1, weapon.Burst);
				var seconds = Math.Max(1, weapon.ReloadDelay) / (float)AbstractState.TicksPerSecond;

				weapons.Add(new WeaponCapability
				{
					Weapon = armament.Weapon,
					Range = weapon.Range.Length / 1024f,
					DamagePerSecond = perBurst / seconds,

					// What the weapon may point at, from the mod's own target-type sets. This is how
					// the commander learns which of its units are anti-air without being told.
					HitsAir = Targets(weapon, "AirborneActor", "Air"),
					HitsGround = Targets(weapon, "GroundActor", "Ground"),
					HitsWater = Targets(weapon, "WaterActor", "Water"),
					Versus = versus,
				});
			}

			return weapons;
		}

		/// <summary>
		/// Whether a weapon may fire at a kind of target.
		/// </summary>
		/// <remarks>
		/// Two token families mean the same thing in this mod's weapon definitions - the actor forms
		/// (GroundActor, AirborneActor, WaterActor) and the terrain forms (Ground, Air, Water) - and
		/// weapons use whichever the author reached for. Checking only one of them found ZERO anti-air
		/// units in a mod with SAM sites, and the registry reported that with a straight face.
		/// </remarks>
		static bool Targets(WeaponInfo weapon, params string[] types) =>
			types.Any(t => weapon.ValidTargets.Contains(t))
				&& !types.Any(t => weapon.InvalidTargets.Contains(t));

		/// <summary>
		/// The counter matrix: for one armour class, which buildable units deal the most damage per
		/// credit against it, best first.
		/// </summary>
		/// <remarks>
		/// This replaces four hand-drawn combat roles with the mod's own damage tables. Four roles
		/// cannot express that a rocket soldier is excellent against heavy armour and useless against
		/// infantry; the table it is derived from says exactly that, and always has.
		/// </remarks>
		public IEnumerable<(ActorCapability Actor, float PerCredit)> BestAgainst(
			string armour, bool targetIsAir = false, bool includeStructures = false) =>
			All.Where(c => (includeStructures || !c.IsStructure) && c.IsArmed)
				.Select(c => (Actor: c, PerCredit: c.DamagePerCreditVersus(armour, targetIsAir)))
				.Where(x => x.PerCredit > 0f)
				.OrderByDescending(x => x.PerCredit)
				.ThenBy(x => x.Actor.Type, StringComparer.Ordinal);

		/// <summary>
		/// The other half of pro-and-con: which units threaten this one most, per credit they cost.
		/// </summary>
		public IEnumerable<(ActorCapability Actor, float PerCredit)> Threats(ActorCapability target)
		{
			ArgumentNullException.ThrowIfNull(target);
			return BestAgainst(target.Armour, target.IsAircraft, includeStructures: true);
		}

		/// <summary>Everything that can shoot at aircraft. Asked by verb, never by name.</summary>
		public IEnumerable<ActorCapability> AntiAir() => All.Where(c => c.CanHitAir);

		/// <summary>A one-line account for telemetry.</summary>
		public string Summary()
		{
			var armed = All.Count(c => c.IsArmed);
			var aa = All.Count(c => c.CanHitAir);
			var mobile = All.Count(c => c.CanMove);
			return $"capabilities: {Count} actors, {armed} armed, {aa} anti-air, {mobile} mobile, " +
				$"armour classes [{string.Join(" ", ArmourClasses)}]";
		}
	}
}
