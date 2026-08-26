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
	/// Whether the army's composition follows from the opponent's, rather than from a list.
	/// </summary>
	[TestFixture]
	sealed class CompositionPlanTest
	{
		static WeaponCapability Weapon(float dps, float range, params (string Armour, float Versus)[] versus) =>
			new()
			{
				Weapon = "w",
				Range = range,
				DamagePerSecond = dps,
				HitsGround = true,
				Versus = versus.ToDictionary(v => v.Armour, v => v.Versus),
			};

		static ActorCapability Unit(string type, int cost, string armour, string queue,
			params WeaponCapability[] weapons) =>
			new()
			{
				Type = type,
				Cost = cost,
				Armour = armour,
				Queues = [queue],
				Weapons = weapons,
			};

		// A rifleman: cheap, hurts flesh, barely scratches armour. A tank: dear, and the reverse.
		// These are the shapes the real mod has, reduced to the two numbers the decision turns on.
		static ActorCapability Rifle => Unit("e1", 100, "None", "Infantry",
			Weapon(20f, 3f, ("None", 1f), ("Heavy", 0.15f)));

		static ActorCapability Tank => Unit("3tnk", 900, "Heavy", "Vehicle",
			Weapon(120f, 5f, ("None", 0.4f), ("Heavy", 1f)));

		static Availability Offering(params ActorCapability[] units) =>
			new()
			{
				Options = units.Select(u => new BuildOption
				{
					Capability = u,
					Queue = u.Queues[0],
					TimeToField = 10f,
					Affordable = true,
				}).ToArray(),
			};

		static CapabilityRegistry Registry(params ActorCapability[] units) => new(units);

		[TestCase(TestName = "The armour mix is weighted by value, not by headcount.")]
		public void MixIsValueWeighted()
		{
			var registry = Registry(Rifle, Tank);

			// Four riflemen screening one heavy tank. By bodies that is 80% unarmoured and the
			// commander would buy anti-infantry; by value it is 400 credits of flesh against 900 of
			// armour, and the tank is the thing that has to be answered.
			var mix = CompositionPlan.EnemyArmourMix(registry, ["e1", "e1", "e1", "e1", "3tnk"]);

			Assert.That(mix["Heavy"], Is.GreaterThan(mix["None"]),
				"a screen of infantry should not disguise the column it is screening");
			Assert.That(mix.Values.Sum(), Is.EqualTo(1f).Within(0.001f));
		}

		[TestCase(TestName = "With nothing seen, the opening prior is assumed rather than nothing.")]
		public void UnseenEnemyFallsBackToPrior()
		{
			var mix = CompositionPlan.EnemyArmourMix(Registry(Rifle, Tank), []);

			Assert.That(mix, Is.SameAs(CompositionPlan.OpeningPrior));
			Assert.That(mix["None"], Is.GreaterThan(mix["Heavy"]),
				"an opponent's opening is infantry and light vehicles, because that is what it can afford");
		}

		[TestCase(TestName = "Structures are excluded from the mix.")]
		public void StructuresDoNotSkewTheMix()
		{
			var registry = Registry(Rifle, Tank,
				new ActorCapability { Type = "proc", Cost = 1400, Armour = "Concrete", IsStructure = true });

			var mix = CompositionPlan.EnemyArmourMix(registry, ["e1", "proc"]);

			Assert.That(mix.ContainsKey("Concrete"), Is.False,
				"a refinery is a target, not a threat to out-compose");
		}

		[TestCase(TestName = "An armoured opponent collapses the infantry share on its own.")]
		public void ArmourShiftsTheShareToVehicles()
		{
			var registry = Registry(Rifle, Tank);
			var shares = CompositionPlan.Shares(registry, Offering(Rifle, Tank), [], ["3tnk", "3tnk", "3tnk"]);

			var infantry = shares.Single(s => s.Queue == "Infantry");
			var vehicle = shares.Single(s => s.Queue == "Vehicle");

			// No percentage anywhere decided this. The mod's own damage table says a rifle does 15%
			// to heavy armour, and the share falls out of that.
			Assert.That(vehicle.Target, Is.GreaterThan(infantry.Target),
				"against tanks the army should be tanks");
			Assert.That(shares.Sum(s => s.Target), Is.EqualTo(1f).Within(0.001f));
		}

		[TestCase(TestName = "And an infantry opponent brings the infantry share back.")]
		public void FleshShiftsTheShareToInfantry()
		{
			var registry = Registry(Rifle, Tank);
			var shares = CompositionPlan.Shares(registry, Offering(Rifle, Tank), [], ["e1", "e1", "e1", "e1"]);

			var infantry = shares.Single(s => s.Queue == "Infantry");
			var vehicle = shares.Single(s => s.Queue == "Vehicle");

			// The same code, the same units, the opposite answer. This is the property a fixed
			// percentage table cannot have, and the reason every bot shipping with one plays the
			// same match every time.
			Assert.That(infantry.Target, Is.GreaterThan(vehicle.Target),
				"a hundred-credit rifleman is the efficient answer to a hundred-credit rifleman");
		}

		[TestCase(TestName = "An arm already at its share is not built further.")]
		public void OverweightArmIsSkipped()
		{
			var registry = Registry(Rifle, Tank);

			// An army that is all infantry, facing armour. Infantry is over its share and should be
			// refused even though the barracks are free - which is exactly the failure that cost
			// 0.88 -> 0.31 when every queue built its own favourite.
			var army = Enumerable.Repeat("e1", 20).ToArray();
			var shares = CompositionPlan.Shares(registry, Offering(Rifle, Tank), army, ["3tnk", "3tnk", "3tnk"]);

			var choices = CompositionPlan.Decide(shares, ["Infantry", "Vehicle"], army.Length);

			Assert.That(choices.Any(c => c.Queue == "Vehicle"), Is.True, "the underweight arm is built");
			Assert.That(choices.Any(c => c.Queue == "Infantry"), Is.False,
				"a free barracks is not a reason to make more of what is already too much of the army");
		}

		[TestCase(TestName = "In the opening every queue builds, because there is no composition yet.")]
		public void OpeningIgnoresShares()
		{
			var registry = Registry(Rifle, Tank);
			var shares = CompositionPlan.Shares(registry, Offering(Rifle, Tank), ["e1"], []);
			var choices = CompositionPlan.Decide(shares, ["Infantry", "Vehicle"], 1);

			// Refusing to build while the shares settle is how a commander loses in the first three
			// minutes. Balance is a property of an army that exists.
			Assert.That(choices.Count, Is.EqualTo(2));
		}

		[TestCase(TestName = "A queue that can build a ship gets offered one; the old scheme never did.")]
		public void NavalArmIsReachable()
		{
			var destroyer = Unit("dd", 1000, "Heavy", "Ship",
				Weapon(90f, 6f, ("None", 0.5f), ("Heavy", 1f)));
			var registry = Registry(Rifle, Tank, destroyer);

			var shares = CompositionPlan.Shares(registry, Offering(Rifle, Tank, destroyer), [], ["3tnk"]);
			var choices = CompositionPlan.Decide(shares, ["Infantry", "Vehicle", "Ship"], 0);

			// The named cost of the hack this replaces: a shipyard was never once offered anything
			// it could build, because the best unit in the game is never a ship.
			Assert.That(choices.Any(c => c.Queue == "Ship" && c.Unit == "dd"), Is.True);
		}

		[TestCase(TestName = "A unit that cannot shoot upwards is worth less against an opponent who flies.")]
		public void AirThreatDiscountsGroundOnlyUnits()
		{
			var grounded = CompositionPlan.ScoreAgainst(Tank, CompositionPlan.OpeningPrior, false);
			var versusAir = CompositionPlan.ScoreAgainst(Tank, CompositionPlan.OpeningPrior, true);

			Assert.That(versusAir, Is.LessThan(grounded),
				"some fraction of the fights it is bought for, it cannot join at all");
		}

		static ActorCapability Flak => new()
		{
			Type = "ftrk",
			Cost = 600,
			Armour = "Light",
			Queues = ["Vehicle"],
			Weapons = [new WeaponCapability
			{
				Weapon = "flak", Range = 4f, DamagePerSecond = 40f, HitsGround = true, HitsAir = true,
			}],
		};

		static ActorCapability Fighter => new()
		{
			Type = "yak", Cost = 675, Armour = "Light", IsAircraft = true, Queues = ["Aircraft"],
			Weapons = [new WeaponCapability
			{
				Weapon = "mg", Range = 4f, DamagePerSecond = 60f, HitsGround = true,
			}],
		};

		[TestCase(TestName = "Anti-air is bought when the enemy flies, which efficiency alone never does.")]
		public void AirDefenceFloorFires()
		{
			var registry = Registry(Rifle, Tank, Flak, Fighter);
			var army = Enumerable.Repeat("3tnk", 10).ToArray();

			// Sixty-five armed units and one that can shoot upwards is what the live survey actually
			// found. Scoring will never fix that on its own: per credit, the ground unit wins every
			// comparison right up until the aircraft arrive.
			var floor = CompositionPlan.AirDefence(registry, Offering(Rifle, Tank, Flak),
				army, ["yak", "yak"], ["Infantry", "Vehicle"]);

			Assert.That(floor, Is.Not.Null);
			Assert.That(floor.Unit, Is.EqualTo("ftrk"));
		}

		[TestCase(TestName = "And is not bought against an opponent who never flies.")]
		public void AirDefenceFloorStaysQuiet()
		{
			var registry = Registry(Rifle, Tank, Flak);
			var floor = CompositionPlan.AirDefence(registry, Offering(Rifle, Tank, Flak),
				Enumerable.Repeat("3tnk", 10).ToArray(), ["3tnk"], ["Vehicle"]);

			Assert.That(floor, Is.Null,
				"anti-air against an opponent with no aircraft is exactly the waste scoring avoids");
		}

		[TestCase(TestName = "An army already covered against aircraft is left alone.")]
		public void AirDefenceFloorRespectsExistingCover()
		{
			var registry = Registry(Rifle, Tank, Flak, Fighter);

			// Half the army's value can shoot upwards, well past the floor.
			var army = new[] { "3tnk", "ftrk", "ftrk" };
			var floor = CompositionPlan.AirDefence(registry, Offering(Rifle, Tank, Flak),
				army, ["yak"], ["Vehicle"]);

			Assert.That(floor, Is.Null);
		}

		[TestCase(TestName = "The floor takes the cheapest answer, not the best one.")]
		public void AirDefenceFloorBuysCheap()
		{
			var expensive = new ActorCapability
			{
				Type = "sam.mobile", Cost = 2400, Armour = "Light", Queues = ["Vehicle"],
				Weapons = [new WeaponCapability
				{
					Weapon = "sam", Range = 9f, DamagePerSecond = 200f, HitsAir = true,
				}],
			};

			var registry = Registry(Rifle, Tank, Flak, Fighter, expensive);
			var floor = CompositionPlan.AirDefence(registry, Offering(Rifle, Tank, Flak, expensive),
				Enumerable.Repeat("3tnk", 10).ToArray(), ["yak"], ["Vehicle"]);

			// Having an answer at all is the point. Buying the finest anti-air in the game to reach
			// the floor spends the army's budget on the fights it is least likely to have.
			Assert.That(floor.Unit, Is.EqualTo("ftrk"));
		}

		[TestCase(TestName = "Nothing buildable means nothing decided, not an exception.")]
		public void EmptyAvailabilityIsSafe()
		{
			var shares = CompositionPlan.Shares(Registry(Rifle), new Availability(), ["e1"], ["3tnk"]);

			Assert.That(shares, Is.Empty);
			Assert.That(CompositionPlan.Decide(shares, ["Infantry"], 1), Is.Empty);
		}
	}
}
