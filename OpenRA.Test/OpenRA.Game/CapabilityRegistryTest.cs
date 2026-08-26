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

		[TestCase(TestName = "Reach is the longest weapon, not the average.")]
		public void ReachIsTheLongestWeapon()
		{
			var unit = Actor("ca", 2400, 80000, "Heavy",
				Weapon(500f, range: 20f), Weapon(300f, range: 4f));

			Assert.That(unit.Reach, Is.EqualTo(20f),
				"What a unit can reach decides whether it can strike without being struck.");
		}
	}
}
