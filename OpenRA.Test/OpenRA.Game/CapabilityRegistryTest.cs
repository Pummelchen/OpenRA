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

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Commander.Model;

namespace OpenRA.Test
{
	/// <summary>
	/// The arithmetic the registry does on the mod's tables, checked without needing a mod.
	/// </summary>
	/// <remarks>
	/// A derived registry fails silently: a table that computed the wrong number looks exactly like one
	/// that computed the right number, and the commander goes on making confident decisions from it.
	/// The first version of this class reported ZERO anti-air units in a game with SAM sites, because
	/// it checked the target token "Air" when the mod writes "AirborneActor" - and nothing complained.
	/// </remarks>
	[TestFixture]
	sealed class CapabilityRegistryTest
	{
		static WeaponCapability Weapon(float dps, float range = 5f, bool air = false,
			bool ground = true, IReadOnlyDictionary<string, float> versus = null) =>
			new()
			{
				Weapon = "test",
				Range = range,
				DamagePerSecond = dps,
				HitsAir = air,
				HitsGround = ground,
				Versus = versus ?? new Dictionary<string, float>(),
			};

		static ActorCapability Afloat(string type, int cost, params WeaponCapability[] weapons) =>
			new()
			{
				Type = type,
				Cost = cost,
				HitPoints = 500,
				Armour = "Heavy",
				Weapons = weapons,
				MovesOnWater = true,
			};

		static ActorCapability Actor(string type, int cost, int hp, string armour,
			params WeaponCapability[] weapons) =>
			new()
			{
				Type = type,
				Cost = cost,
				HitPoints = hp,
				Armour = armour,
				Weapons = weapons,
			};

		[TestCase(TestName = "Damage against an armour class uses that class's own multiplier.")]
		public void DamageUsesVersus()
		{
			var versus = new Dictionary<string, float> { ["Heavy"] = 1.15f, ["Wood"] = 0.3f };
			var tank = Actor("tank", 2000, 90000, "Heavy", Weapon(1000f, versus: versus));

			Assert.That(tank.DamageVersus("Heavy"), Is.EqualTo(1150f).Within(0.01f));
			Assert.That(tank.DamageVersus("Wood"), Is.EqualTo(300f).Within(0.01f),
				"A weapon that is poor against infantry must not report its unmodified damage.");
			Assert.That(tank.DamageVersus("Concrete"), Is.EqualTo(1000f).Within(0.01f),
				"An armour class the weapon says nothing about is unmodified, not zero.");
		}

		[TestCase(TestName = "A unit that cannot shoot air deals no damage to it.")]
		public void GroundOnlyCannotHitAir()
		{
			var artillery = Actor("arty", 600, 4000, "Light", Weapon(2000f, ground: true, air: false));

			Assert.That(artillery.CanHitAir, Is.False);
			Assert.That(artillery.DamageVersus("Light", targetIsAir: true), Is.EqualTo(0f),
				"Reporting ground damage against an aircraft is how a commander sends artillery "
				+ "to answer an air raid.");
		}

		[TestCase(TestName = "The best weapon for the target is used, not the sum of all weapons.")]
		public void BestWeaponNotTheSum()
		{
			// A Mammoth carries a cannon for armour and missiles for aircraft. It fires one of them
            // at a given target, so summing both would overstate it against everything.
			var cannon = Weapon(1000f, versus: new Dictionary<string, float> { ["Heavy"] = 1.15f });
			var missiles = Weapon(400f, air: true, ground: false);
			var mammoth = Actor("4tnk", 2000, 90000, "Heavy", cannon, missiles);

			Assert.That(mammoth.DamageVersus("Heavy"), Is.EqualTo(1150f).Within(0.01f));
			Assert.That(mammoth.DamageVersus("Heavy", targetIsAir: true), Is.EqualTo(400f).Within(0.01f));
			Assert.That(mammoth.CanHitAir, Is.True);
			Assert.That(mammoth.CanHitGround, Is.True);
		}

		[TestCase(TestName = "Value is damage per credit, so cheap and expensive units compare.")]
		public void ValueIsPerCredit()
		{
			// The comparison the whole production decision rests on. A rifleman and a heavy tank are
			// not comparable by damage; they are comparable by damage bought per credit spent.
			var rifle = Actor("e1", 100, 5000, "Wood", Weapon(200f));
			var tank = Actor("tank", 2000, 90000, "Heavy", Weapon(1000f));

			Assert.That(rifle.DamagePerCreditVersus("None"), Is.EqualTo(2f).Within(0.001f));
			Assert.That(tank.DamagePerCreditVersus("None"), Is.EqualTo(0.5f).Within(0.001f));
			Assert.That(rifle.DamagePerCreditVersus("None"),
				Is.GreaterThan(tank.DamagePerCreditVersus("None")),
				"Per credit, the cheap unit wins on raw damage - which is why durability and reach "
				+ "have to be weighed as well, rather than this number being used alone.");
		}

		[TestCase(TestName = "A free or unpriced actor scores zero rather than infinity.")]
		public void UnpricedScoresZero()
		{
			// Otherwise a missing price produces an infinite ratio, and the commander concludes that
			// the one thing nobody bothered to cost is the best buy in the game.
			var free = Actor("mystery", 0, 1000, "Wood", Weapon(500f));

			Assert.That(free.DamagePerCreditVersus("Wood"), Is.EqualTo(0f));
			Assert.That(free.DurabilityPerCredit, Is.EqualTo(0f));
		}

		[TestCase(TestName = "An unarmed actor is not offered as an answer to anything.")]
		public void UnarmedIsNotACounter()
		{
			var harvester = Actor("harv", 1100, 60000, "Heavy");

			Assert.That(harvester.IsArmed, Is.False);
			Assert.That(harvester.DamageVersus("Heavy"), Is.EqualTo(0f));
			Assert.That(harvester.Reach, Is.EqualTo(0f));
		}

		static ActorCapability Structure(string type, int cost, int power,
			IReadOnlyList<string> requires, IReadOnlyList<string> unlocks) =>
			new()
			{
				Type = type,
				Cost = cost,
				HitPoints = 40000,
				Armour = "Concrete",
				IsStructure = true,
				Power = power,
				Requires = requires,
				Unlocks = unlocks.Append(type).Distinct().ToArray(),
				Queues = ["Building"],
			};

		[TestCase(TestName = "An actor satisfies a prerequisite equal to its own name.")]
		public void ActorsProvideTheirOwnName()
		{
			// How "requires weap" is met by owning a war factory. Leaving this out made the tech
			// graph unable to find a route to a Tesla coil, whose real requirement is a war factory.
			var registry = new CapabilityRegistry(new[]
			{
				Structure("weap", 2000, -30, [], []),
				Structure("tsla", 1200, -80, ["weap"], []),
			});

			var path = registry.PathTo("tsla", new HashSet<string>());
			Assert.That(path, Is.EqualTo(new[] { "weap" }));
		}

		[TestCase(TestName = "The tech path is ordered, and stops once everything is held.")]
		public void PathIsOrderedAndStops()
		{
			var registry = new CapabilityRegistry(new[]
			{
				Structure("weap", 2000, -30, [], []),
				Structure("dome", 1400, -40, ["weap"], []),
				Structure("atek", 1500, -200, ["dome"], ["techcenter"]),
				Structure("mslo", 2500, -150, ["techcenter"], []),
			});

			// Exact order, because the order IS the answer: each step must be buildable when it
			// is reached. A loose assertion here hid a real defect - the first implementation
			// returned just "atek", a tech centre that could not yet be built.
			Assert.That(registry.PathTo("mslo", new HashSet<string>()),
				Is.EqualTo(new[] { "weap", "dome", "atek" }),
				"A build order must put each prerequisite before the thing that needs it.");

			Assert.That(registry.PathTo("mslo", new HashSet<string> { "techcenter" }), Is.Empty,
				"Holding the token already means there is nothing left to build.");
		}

		[TestCase(TestName = "Tokens nothing can build are treated as held, not as blockers.")]
		public void ExternalTokensDoNotBlock()
		{
			// Faction identity and lobby tech level are granted from outside the build queue. Treating
			// them as blockers made every path through a faction-gated building report "no route",
			// which is the opposite of the truth.
			var registry = new CapabilityRegistry(new[]
			{
				Structure("weap", 2000, -30, [], []),
				Structure("tsla", 1200, -80, ["weap", "~structures.soviet", "~techlevel.medium"], []),
			});

			Assert.That(registry.PathTo("tsla", new HashSet<string>()), Is.EqualTo(new[] { "weap" }));
		}

		[TestCase(TestName = "A negated prerequisite is never something to go and build.")]
		public void NegatedPrerequisitesAreSkipped()
		{
			var registry = new CapabilityRegistry(new[]
			{
				Structure("weap", 2000, -30, [], []),
				Structure("special", 900, -20, ["weap", "!disabled"], []),
			});

			var capability = registry.Find("special");
			Assert.That(registry.Missing(capability, new HashSet<string> { "weap" }), Is.Empty,
				"Requiring that something be ABSENT cannot be satisfied by construction.");
		}

		[TestCase(TestName = "Power is separated into what supplies and what draws.")]
		public void PowerIsSigned()
		{
			var registry = new CapabilityRegistry(new[]
			{
				Structure("apwr", 500, 200, [], []),
				Structure("atek", 1500, -200, [], []),
			});

			Assert.That(registry.Find("apwr").SuppliesPower, Is.True);
			Assert.That(registry.Find("atek").DrawsPower, Is.True);
			Assert.That(registry.PowerPlants().Select(p => p.Type), Is.EqualTo(new[] { "apwr" }));
		}

		[TestCase(TestName = "Capabilities are asked for by verb, never by unit name.")]
		public void QueriedByVerb()
		{
			// The whole point of the registry. A manager asks "who can carry passengers" and gets a
			// correct answer in a mod nobody wrote a list for - where before it asked for "apc" and
			// got nothing in any mod that spells it differently.
            var registry = new CapabilityRegistry(new[]
			{
				new ActorCapability { Type = "apc", Cost = 800, CargoCapacity = 5, Armour = "Heavy" },
				new ActorCapability { Type = "e6", Cost = 500, CapturesTypes = ["building"], Armour = "Wood" },
				new ActorCapability { Type = "ss", Cost = 950, CanHide = true, Armour = "Light" },
				new ActorCapability { Type = "dd", Cost = 1000, DetectionRange = 6f, Armour = "Light" },
				new ActorCapability { Type = "e1", Cost = 100, Armour = "Wood" },
			});

			Assert.That(registry.Transports().Select(c => c.Type), Is.EqualTo(new[] { "apc" }));
			Assert.That(registry.Capturers().Select(c => c.Type), Is.EqualTo(new[] { "e6" }));
			Assert.That(registry.Hiders().Select(c => c.Type), Is.EqualTo(new[] { "ss" }));
			Assert.That(registry.Detectors().Select(c => c.Type), Is.EqualTo(new[] { "dd" }));
		}

		[TestCase(TestName = "Transports are ranked by how much they actually carry.")]
		public void TransportsRankedByCapacity()
		{
			var registry = new CapabilityRegistry(new[]
			{
				new ActorCapability { Type = "jeep", CargoCapacity = 1, Cost = 600 },
				new ActorCapability { Type = "tran", CargoCapacity = 8, Cost = 1200 },
				new ActorCapability { Type = "apc", CargoCapacity = 5, Cost = 800 },
			});

			Assert.That(registry.Transports().Select(c => c.Type),
				Is.EqualTo(new[] { "tran", "apc", "jeep" }),
				"A manager moving eight soldiers needs the one that fits them, not the first match.");
		}

		[TestCase(TestName = "Which building serves a production queue is read, not assumed.")]
		public void ProducersAreDerived()
		{
			var registry = new CapabilityRegistry(new[]
			{
				new ActorCapability { Type = "weap", Produces = ["Vehicle"], IsStructure = true },
				new ActorCapability { Type = "barr", Produces = ["Infantry"], IsStructure = true },
				new ActorCapability { Type = "tent", Produces = ["Infantry"], IsStructure = true },
			});

			Assert.That(registry.ProducersOf("Infantry").Select(c => c.Type),
				Is.EqualTo(new[] { "barr", "tent" }));
			Assert.That(registry.ProducersOf("Ship"), Is.Empty,
				"A queue nothing serves is an empty answer, not a wrong one.");
		}

		[TestCase(TestName = "Reach is the longest weapon, not the average.")]
		public void ReachIsTheLongestWeapon()
		{
			var unit = Actor("ca", 2400, 80000, "Heavy",
				Weapon(500f, range: 20f), Weapon(300f, range: 4f));

			Assert.That(unit.Reach, Is.EqualTo(20f),
				"What a unit can reach decides whether it can strike without being struck.");
		}

		[TestCase(TestName = "Naval means it crosses water under its own power, and fights.")]
		public void NavalIsWaterMovementNotAName()
		{
			var destroyer = Afloat("dd", 1000, Weapon(90f));
			var tank = Actor("1tnk", 700, 300, "Heavy", Weapon(80f));
			var transport = Afloat("lst", 700);

			// Nothing here knows what a destroyer is. It knows which actors move over water, which
			// the real registry reads out of the mod's own locomotor terrain table rather than out
			// of a list somebody has to maintain.
			var registry = new CapabilityRegistry(new[] { destroyer, tank, transport });
			var naval = registry.Naval().Select(c => c.Type).ToArray();

			Assert.That(naval, Is.EqualTo(new[] { "dd" }),
				"armed and afloat; the tank does not float and the transport does not shoot");
		}

		[TestCase(TestName = "Aircraft are not naval merely for passing overhead.")]
		public void AircraftAreNotNaval()
		{
			var helicopter = new ActorCapability
			{
				Type = "heli",
				Cost = 1200,
				Armour = "Light",
				Weapons = [Weapon(100f)],
				MovesOnWater = true,
				IsAircraft = true,
			};

			Assert.That(new CapabilityRegistry(new[] { helicopter }).Naval(), Is.Empty);
		}
	}
}
