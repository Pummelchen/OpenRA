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
using OpenRA.Traits;

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

		/// <summary>
		/// Whether this can move over water under its own power.
		/// </summary>
		/// <remarks>
		/// Read from the mod's own locomotor terrain table rather than from a list of ship names.
		/// "Naval" is not a property of an actor in this engine, it is a consequence of which
		/// terrain its locomotor can cross, and asking the terrain table gets transports, hovercraft
		/// and anything a mod invents for free.
		/// </remarks>
		public bool MovesOnWater { get; init; }

		/// <summary>A unit that fights at sea: floats, is armed, and is not simply flying over.</summary>
		public bool IsNaval => MovesOnWater && !IsAircraft && !IsStructure;

		/// <summary>How far it reveals shroud, in cells. The honest measure of a scout.</summary>
		public float Vision { get; init; }

		public IReadOnlyList<WeaponCapability> Weapons { get; init; } = [];

		/// <summary>
		/// Power this actor supplies (positive) or draws (negative).
		/// </summary>
		/// <remarks>
		/// Never read anywhere in the staff before now, which is why the commander could build its
		/// way into a brownout and then wonder why its defences had stopped firing.
		/// </remarks>
		public int Power { get; init; }

		/// <summary>What must already exist before this can be built.</summary>
		public IReadOnlyList<string> Requires { get; init; } = [];

		/// <summary>
		/// Prerequisite tokens this actor grants once built - what it UNLOCKS.
		/// </summary>
		/// <remarks>
		/// The tech graph, and the answer to the only question that makes a tech decision
		/// rational: what does paying for this building buy me access to?
		/// </remarks>
		public IReadOnlyList<string> Unlocks { get; init; } = [];

		/// <summary>Ticks to build at nominal speed, before any speed-up from extra factories.</summary>
		public int BuildTicks { get; init; }

		/// <summary>Seconds to build at nominal speed.</summary>
		public float BuildSeconds => BuildTicks / (float)AbstractState.TicksPerSecond;

		/// <summary>Which production queue builds this.</summary>
		public IReadOnlyList<string> Queues { get; init; } = [];

		public bool SuppliesPower => Power > 0;
		public bool DrawsPower => Power < 0;

		/// <summary>How much this can carry, and which passenger types fit. Zero means it carries nothing.</summary>
		public int CargoCapacity { get; init; }
		public IReadOnlyList<string> CarriesTypes { get; init; } = [];
		public bool Transports => CargoCapacity > 0;

		/// <summary>Which capture types this can take. Empty means it cannot capture.</summary>
		public IReadOnlyList<string> CapturesTypes { get; init; } = [];
		public bool Captures => CapturesTypes.Count > 0;

		/// <summary>How far it detects hidden things, in cells. Zero means it cannot.</summary>
		public float DetectionRange { get; init; }
		public bool Detects => DetectionRange > 0f;

		/// <summary>Whether it can hide - cloak, submerge, or otherwise go unseen.</summary>
		public bool CanHide { get; init; }

		/// <summary>
		/// Whether this can enter an enemy structure and do something to it other than shoot it.
		/// </summary>
		/// <remarks>
		/// Detected by trait name rather than by type, because the trait lives in a mod assembly
		/// this one cannot reference. That is uglier than a type check and considerably better than
		/// the alternative it replaces: selecting covert operatives by "can hide" alone nominated
		/// every submarine in the game, and a submarine cannot infiltrate a building.
		/// </remarks>
		public bool Infiltrates { get; init; }

		/// <summary>Whether it repairs other units brought to it.</summary>
		public bool Repairs { get; init; }

		/// <summary>Whether it gathers resources.</summary>
		public bool Harvests { get; init; }

		/// <summary>Production queues this building serves. Empty for anything that builds nothing.</summary>
		public IReadOnlyList<string> Produces { get; init; } = [];
		public bool IsProduction => Produces.Count > 0;

		/// <summary>Support powers this grants, with the seconds each takes to charge.</summary>
		public IReadOnlyList<(string Power, float ChargeSeconds)> SupportPowers { get; init; } = [];
		public bool GrantsSupportPower => SupportPowers.Count > 0;

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

		/// <summary>
		/// Builds a registry from capabilities directly, for tests and for tooling that has no mod
		/// loaded. The graph logic is the part worth testing and it does not need a Ruleset to run.
		/// </summary>
		public CapabilityRegistry(IEnumerable<ActorCapability> capabilities)
		{
			ArgumentNullException.ThrowIfNull(capabilities);

			foreach (var c in capabilities)
				byType[c.Type] = c;

			All = byType.Values.OrderBy(c => c.Type, StringComparer.Ordinal).ToArray();
			ArmourClasses = byType.Values
				.Select(c => c.Armour)
				.Where(a => !string.IsNullOrEmpty(a))
				.Distinct(StringComparer.Ordinal)
				.OrderBy(a => a, StringComparer.Ordinal)
				.ToArray();
		}

		public CapabilityRegistry(Ruleset rules, UnitCatalogue catalogue)
		{
			ArgumentNullException.ThrowIfNull(rules);
			ArgumentNullException.ThrowIfNull(catalogue);

			var armours = new SortedSet<string>(StringComparer.Ordinal);

			// Which locomotors can cross water, straight from the mod's terrain speed table. A
			// locomotor with a positive speed over water is a naval one; nothing here needs to know
			// that a destroyer is a ship.
			var water = new HashSet<string>(StringComparer.Ordinal);
			if (rules.Actors.TryGetValue("world", out var worldActor))
				foreach (var locomotor in worldActor.TraitInfos<LocomotorInfo>())
					if (locomotor.TerrainSpeeds != null
						&& locomotor.TerrainSpeeds.TryGetValue("Water", out var speed)
						&& speed.Speed > 0)
						water.Add(locomotor.Name);

			foreach (var entry in catalogue.All)
			{
				if (!rules.Actors.TryGetValue(entry.Type, out var actor))
					continue;

				var buildable = actor.TraitInfoOrDefault<BuildableInfo>();
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
					MovesOnWater = mobile != null && water.Contains(mobile.Locomotor),

					Vision = actor.TraitInfos<RevealsShroudInfo>()
						.Select(r => r.Range.Length / 1024f)
						.DefaultIfEmpty(0f)
						.Max(),

					Weapons = Armaments(actor, rules),

					Power = actor.TraitInfos<PowerInfo>().Sum(p => p.Amount),
					Requires = buildable?.Prerequisites.ToArray() ?? [],
					Queues = buildable?.Queue.ToArray() ?? [],

					// Prerequisites this actor grants. Factions are ignored on purpose: the
					// commander holds every faction's tokens, so a grant that is faction-gated in
					// the rules is still a grant it will receive.
					// An actor satisfies a prerequisite equal to its own name simply by existing -
					// that is how "requires weap" is met by owning a war factory - so its own type
					// belongs in this list alongside any token it explicitly grants. Leaving it out
					// made the tech graph unable to find a path to a Tesla coil, whose only real
					// requirement is a war factory.
					Unlocks = actor.TraitInfos<ProvidesPrerequisiteInfo>()
						.Select(p => p.Prerequisite ?? entry.Type)
						.Where(t => !string.IsNullOrEmpty(t))
						.Append(entry.Type)
						.Distinct(StringComparer.Ordinal)
						.OrderBy(t => t, StringComparer.Ordinal)
						.ToArray(),

					// BuildDuration of -1 means "however much it costs", which is the mod's own
					// default rule rather than an assumption made here.
					BuildTicks = buildable == null ? 0
						: buildable.BuildDuration >= 0 ? buildable.BuildDuration : entry.Cost,

					// The six capabilities the staff had never read. Each one is a tactic the
					// commander could not previously consider, because it had no way to learn that
					// such a thing existed.
					CargoCapacity = actor.TraitInfos<CargoInfo>().Sum(c => c.MaxWeight),
					CarriesTypes = actor.TraitInfos<CargoInfo>()
						.SelectMany(c => c.Types)
						.Distinct(StringComparer.Ordinal)
						.OrderBy(t => t, StringComparer.Ordinal)
						.ToArray(),

					CapturesTypes = actor.TraitInfos<CapturesInfo>()
						.SelectMany(c => c.CaptureTypes.Select(t => t))
						.Distinct(StringComparer.Ordinal)
						.OrderBy(t => t, StringComparer.Ordinal)
						.ToArray(),

					DetectionRange = actor.TraitInfos<DetectCloakedInfo>()
						.Select(d => d.Range.Length / 1024f)
						.DefaultIfEmpty(0f)
						.Max(),

					CanHide = actor.TraitInfos<CloakInfo>().Any(),
					Infiltrates = actor.TraitInfos<ITraitInfoInterface>()
						.Any(t => t.GetType().Name == "InfiltratesInfo"),
					Repairs = actor.TraitInfos<RepairsUnitsInfo>().Any(),
					Harvests = actor.TraitInfos<HarvesterInfo>().Any(),

					Produces = actor.TraitInfos<ProductionInfo>()
						.SelectMany(pr => pr.Produces)
						.Distinct(StringComparer.Ordinal)
						.OrderBy(t => t, StringComparer.Ordinal)
						.ToArray(),

					SupportPowers = actor.TraitInfos<SupportPowerInfo>()
						.Select(sp => (Power: sp.Name ?? entry.Type,
							ChargeSeconds: sp.ChargeInterval / (float)AbstractState.TicksPerSecond))
						.ToArray(),
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

		/// <summary>Everything that can carry passengers.</summary>
		public IEnumerable<ActorCapability> Transports() =>
			All.Where(c => c.Transports).OrderByDescending(c => c.CargoCapacity);

		/// <summary>Everything that can take a building rather than destroy it.</summary>
		public IEnumerable<ActorCapability> Capturers() => All.Where(c => c.Captures);

		/// <summary>Everything that can see hidden units.</summary>
		public IEnumerable<ActorCapability> Detectors() =>
			All.Where(c => c.Detects).OrderByDescending(c => c.DetectionRange);

		/// <summary>Everything that can hide.</summary>
		public IEnumerable<ActorCapability> Hiders() => All.Where(c => c.CanHide);

		/// <summary>
		/// Units that can run a covert operation: reach an enemy structure on foot and do something
		/// to it other than shoot it.
		/// </summary>
		/// <remarks>
		/// What a spy, a thief and a commando have in common is not that somebody wrote them down
		/// together. It is that each can reach a building an army cannot and act on it once there -
		/// and that is a question the mod's own traits answer for a unit nobody has heard of.
		/// </remarks>
		public IEnumerable<ActorCapability> Operatives() =>
			All.Where(c => !c.IsStructure && c.CanMove && !c.IsAircraft && !c.IsNaval
					&& (c.Infiltrates || c.Captures || c.CanHide))

				// Cheapest first. An operation risks the operative outright, and the point of
				// sending one is that it is worth less than what it walks into.
				.OrderBy(c => c.Cost)
				.ThenBy(c => c.Type, StringComparer.Ordinal);

		/// <summary>Armed units that fight at sea, most efficient first.</summary>
		public IEnumerable<ActorCapability> Naval() =>
			All.Where(c => c.IsNaval && c.IsArmed)
				.OrderByDescending(c => c.Cost)
				.ThenBy(c => c.Type, StringComparer.Ordinal);

		/// <summary>Everything that grants a support power.</summary>
		public IEnumerable<ActorCapability> SupportPowerSources() =>
			All.Where(c => c.GrantsSupportPower);

		/// <summary>Which buildings serve a given production queue.</summary>
		public IEnumerable<ActorCapability> ProducersOf(string queue) =>
			All.Where(c => c.Produces.Contains(queue, StringComparer.Ordinal));

		/// <summary>Everything that supplies more power than it draws.</summary>
		public IEnumerable<ActorCapability> PowerPlants() =>
			All.Where(c => c.SuppliesPower).OrderByDescending(c => c.Power);

		/// <summary>
		/// What must be built, in order, to make a target buildable - the shortest chain of
		/// structures whose unlocks satisfy its prerequisites.
		/// </summary>
		/// <remarks>
		/// Answers "how do I get to a Tesla coil from here", which the commander could not ask at
		/// all before. Prerequisites prefixed with "~" are hidden rather than optional, so they are
		/// matched the same way after stripping the marker; a "!" prefix negates and is skipped,
		/// since nothing needs to be built to satisfy a requirement that something be absent.
		/// </remarks>
		public IReadOnlyList<string> PathTo(string target, IReadOnlySet<string> alreadyHeld)
		{
			var goal = Find(target);
			if (goal == null)
				return [];

			var held = new HashSet<string>(alreadyHeld ?? new HashSet<string>(), StringComparer.Ordinal);
			var plan = new List<string>();
			var visiting = new HashSet<string>(StringComparer.Ordinal);

			// Depth-first over prerequisites, so a provider is only added once ITS OWN requirements
			// are in the plan ahead of it. Satisfying just the target's direct prerequisites is not
			// enough and produced plans that could not be executed: a missile silo needs a tech
			// centre, and the first version answered "build a tech centre" without noticing that the
			// tech centre needs a war factory and a radar dome first.
			bool Resolve(ActorCapability capability, int depth)
			{
				if (depth > 12)
					return false;

				foreach (var need in Missing(capability, held))
				{
					var provider = All
						.Where(c => c.Unlocks.Contains(need, StringComparer.Ordinal)
							&& c.Queues.Count > 0)
						.OrderBy(c => c.Cost)
						.ThenBy(c => c.Type, StringComparer.Ordinal)
						.FirstOrDefault();

					if (provider == null)
					{
						// Nothing buildable grants this, so it comes from elsewhere - faction
						// identity, the lobby's tech level, a map trigger. Those are held or they
						// are not, and no amount of construction changes it. Treating them as
						// blockers made every path through a faction-gated building report "no
						// route", which is the opposite of the truth.
						held.Add(need);
						continue;
					}

					if (plan.Contains(provider.Type, StringComparer.Ordinal))
						continue;

					// A cycle in the prerequisites would otherwise recurse until the stack gave out.
					if (!visiting.Add(provider.Type))
						return false;

					if (!Resolve(provider, depth + 1))
						return false;

					visiting.Remove(provider.Type);

					plan.Add(provider.Type);
					foreach (var token in provider.Unlocks)
						held.Add(token);
				}

				return true;
			}

			return Resolve(goal, 0) ? plan : [];
		}

		/// <summary>Prerequisites of this actor that are not yet satisfied.</summary>
		public IReadOnlyList<string> Missing(ActorCapability capability, IReadOnlySet<string> held)
		{
			ArgumentNullException.ThrowIfNull(capability);

			var missing = new List<string>();
			foreach (var raw in capability.Requires)
			{
				var token = raw.TrimStart('~');
				if (token.StartsWith('!'))
					continue;

				if (held == null || !held.Contains(token))
					missing.Add(token);
			}

			return missing;
		}

		/// <summary>A one-line account for telemetry.</summary>
		public string Summary()
		{
			var armed = All.Count(c => c.IsArmed);
			var aa = All.Count(c => c.CanHitAir);
			var mobile = All.Count(c => c.CanMove);
			var plants = All.Count(c => c.SuppliesPower);
			// Actors granting a token OTHER than their own name. Counting every actor that
			// "grants a prerequisite" reported 94 of 94, which is true and tells nobody anything.
			var unlockers = All.Count(c =>
				c.Unlocks.Any(u => !string.Equals(u, c.Type, StringComparison.Ordinal)));
			return $"capabilities: {Count} actors, {armed} armed, {aa} anti-air, {mobile} mobile, " +
				$"{plants} power plants, {unlockers} grant prerequisites, " +
				$"{All.Count(c => c.Transports)} transports, {All.Count(c => c.Captures)} capturers, " +
				$"{All.Count(c => c.Detects)} detectors, {All.Count(c => c.CanHide)} can hide, " +
				$"{All.Count(c => c.GrantsSupportPower)} support powers, " +
				$"armour classes [{string.Join(" ", ArmourClasses)}]";
		}
	}
}
