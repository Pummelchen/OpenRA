#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of the
 * License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

namespace OpenRA.Test
{
	/// <summary>
	/// The derived counter matrix (handbook §3). These use hand-built profiles carrying the shipped
	/// mod's real numbers, so the arithmetic is checked without needing a loaded ruleset. The
	/// ruleset-reading path itself runs in every headless match.
	/// </summary>
	[TestFixture]
	sealed class CounterMatrixTest
	{
		static UnitCombatProfile Unit(string type, int cost, int hp, string armor,
			(string Armor, float Dps)[] damage = null, bool air = false, bool canHitAir = false,
			bool canHitGround = true, int range = 4)
		{
			var table = new Dictionary<string, float>();
			foreach (var (a, d) in damage ?? [])
				table[a] = d;

			return new UnitCombatProfile(type, cost, hp, armor, table, range, canHitAir, canHitGround, air);
		}

		// Damage figures below are computed from the shipped mod: damage x (25 / ReloadDelay) x burst
		// x Versus%. M1Carbine 1000 dmg / 20 reload, Versus None 150 Heavy 10; Dragon 5000 / 50,
		// Versus Heavy 100 None 10; 120mm 6000 / 90 burst 2, Versus Heavy 115, InvalidTargets
		// Infantry. A rebalance that invalidates these shows up here.
		static UnitCombatProfile Rifleman() => Unit("e1", 100, 5000, "None",
			[("None", 1875f), ("Light", 500f), ("Heavy", 125f)]);

		static UnitCombatProfile RocketInfantry() => Unit("e3", 300, 4500, "None",
			[("None", 250f), ("Light", 850f), ("Heavy", 2500f)], canHitAir: true);

		// The 120mm cannot fire at infantry at all, so it has no entry against None armour.
		static UnitCombatProfile HeavyTank() => Unit("3tnk", 1150, 60000, "Heavy",
			[("Light", 3333f), ("Heavy", 3833f)]);

		static UnitCombatProfile Mammoth() => Unit("4tnk", 2000, 90000, "Heavy",
			[("Light", 3333f), ("Heavy", 3833f)], canHitAir: true);

		static UnitCombatProfile Artillery() => Unit("arty", 850, 10000, "Light",
			[("None", 2200f), ("Light", 1400f), ("Heavy", 700f)], range: 9);

		static UnitCombatProfile Mig() => Unit("mig", 2000, 8000, "Light",
			[("Heavy", 1400f), ("Light", 900f)], air: true, canHitGround: true);

		[TestCase(TestName = "A weapon that cannot target air is no answer to air, whatever its damage.")]
		public void GroundOnlyWeaponsCannotAnswerAircraft()
		{
			// The failure this prevents: rifle infantry ranking as an anti-air counter because their
			// damage-versus-Light number looks acceptable on paper.
			Assert.That(CounterMatrix.CanEngage(Rifleman(), Mig()), Is.False);
			Assert.That(CounterMatrix.CanEngage(RocketInfantry(), Mig()), Is.True);
			Assert.That(Rifleman().CounterScore(Mig()), Is.Zero,
				"A ground-only weapon scores zero against aircraft, not a reduced amount.");
			Assert.That(CounterMatrix.RankCounters([Rifleman()],
				new Dictionary<UnitCombatProfile, int> { [Mig()] = 6 }), Is.Empty);
		}

		[TestCase(TestName = "Time to kill is infinite when a unit cannot hurt the target at all.")]
		public void CannotHurtIsInfiniteNotZero()
		{
			var harmless = Unit("harv", 1100, 60000, "Heavy");
			Assert.That(harmless.TimeToKill(HeavyTank()), Is.EqualTo(float.PositiveInfinity),
				"\"Cannot hurt\" must be distinguishable from \"kills slowly\".");
			Assert.That(harmless.IsArmed, Is.False);
		}

		[TestCase(TestName = "Cost efficiency prefers heavy tanks over mammoths against armour.")]
		public void ThreeHeavyTanksBeatTwoMammoths()
		{
			// The handbook's worked example, on the shipped numbers: against Heavy armour a heavy
			// tank scores ~174 per credit and a mammoth ~86, so three heavy tanks (3450 credits)
			// beat two mammoths (4000) - even though a mammoth wins the one-on-one duel. A duel
			// comparison would recommend the mammoth and lose the match.
			var heavy = HeavyTank().CostEfficiencyVersus("Heavy");
			var mammoth = Mammoth().CostEfficiencyVersus("Heavy");

			Assert.That(heavy, Is.GreaterThan(mammoth * 1.5f),
				$"Heavy tank {heavy:0} vs mammoth {mammoth:0} per credit against Heavy armour.");

			// And the duel genuinely goes the other way, which is why the distinction matters.
			Assert.That(Mammoth().TimeToKill(HeavyTank()),
				Is.LessThan(HeavyTank().TimeToKill(Mammoth())),
				"The mammoth still wins one-on-one; cost efficiency is a different question.");
		}

		[TestCase(TestName = "A heavy tank is not credited with answering infantry it cannot fire at.")]
		public void InvalidTargetsAreHonoured()
		{
			// The 120mm declares InvalidTargets: Infantry. Counting its damage against None armour
			// would have the coalition believe tanks answer massed infantry.
			Assert.That(HeavyTank().DamageVersus("None"), Is.Zero);
			Assert.That(HeavyTank().CostEfficiencyVersus("None"), Is.Zero);

			var vsInfantry = CounterMatrix.RankCounters([Rifleman(), HeavyTank()],
				new Dictionary<UnitCombatProfile, int> { [Rifleman()] = 20 });

			Assert.That(vsInfantry.Count, Is.EqualTo(1));
			Assert.That(vsInfantry[0].Unit, Is.EqualTo("e1"),
				"Only the unit that can actually shoot infantry may be ranked against them.");
		}

		[TestCase(TestName = "Rocket infantry out-counter riflemen against armour, and the reverse against infantry.")]
		public void CountersInvertWithArmourClass()
		{
			var enemyArmour = new Dictionary<UnitCombatProfile, int> { [HeavyTank()] = 8 };
			var enemyInfantry = new Dictionary<UnitCombatProfile, int> { [Rifleman()] = 20 };

			var vsArmour = CounterMatrix.RankCounters([Rifleman(), RocketInfantry()], enemyArmour);
			var vsInfantry = CounterMatrix.RankCounters([Rifleman(), RocketInfantry()], enemyInfantry);

			Assert.That(vsArmour[0].Unit, Is.EqualTo("e3"), "Rockets answer armour.");
			Assert.That(vsInfantry[0].Unit, Is.EqualTo("e1"), "Rifles answer infantry.");
		}

		[TestCase(TestName = "Counters are weighted by how much of the enemy force each type represents.")]
		public void RankingIsWeightedByComposition()
		{
			// One outlier tank among twenty infantry must not make anti-tank the top production
			// priority - that is how a coalition ends up answering a threat it barely faces.
			var mostlyInfantry = new Dictionary<UnitCombatProfile, int>
			{
				[Rifleman()] = 20,
				[HeavyTank()] = 1
			};

			var ranked = CounterMatrix.RankCounters([Rifleman(), RocketInfantry()], mostlyInfantry);
			Assert.That(ranked[0].Unit, Is.EqualTo("e1"));
		}

		[TestCase(TestName = "An unarmed or empty force yields no ranking rather than a fabricated one.")]
		public void DegenerateInputYieldsNothing()
		{
			Assert.That(CounterMatrix.RankCounters([], new Dictionary<UnitCombatProfile, int>()), Is.Empty);
			Assert.That(CounterMatrix.RankCounters(null, null), Is.Empty);
			Assert.That(CounterMatrix.RankCounters([Unit("harv", 1100, 60000, "Heavy")],
				new Dictionary<UnitCombatProfile, int> { [HeavyTank()] = 4 }), Is.Empty,
				"An unarmed unit is never a counter.");
		}

		[TestCase(TestName = "Artillery out-ranges tanks, which is why it leads nothing and follows everything.")]
		public void ArtilleryIsLongRangedAndFragile()
		{
			var artillery = Artillery();
			var tank = HeavyTank();

			Assert.That(artillery.RangeCells, Is.GreaterThan(tank.RangeCells),
				"Out-ranging static defence is the entire purpose of artillery.");
			Assert.That(artillery.HitPoints, Is.LessThan(tank.HitPoints / 4),
				"And it evaporates to anything that closes, so it never leads an advance.");
			Assert.That(artillery.Armor, Is.EqualTo("Light"));
		}
	}
}
