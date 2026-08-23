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

using NUnit.Framework;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

namespace OpenRA.Test
{
	/// <summary>
	/// The combined-arms production floor (reqs 198, 231, 232). Names the rule whose absence makes
	/// this fork field zero air and zero naval units across a full match: aircraft cost several times
	/// a tank and sit late in the priority list, so a coalition affording one unit per cycle takes
	/// the tank every cycle.
	/// </summary>
	[TestFixture]
	sealed class ProductionBalanceTest
	{
		const int Aircraft = 2000;
		const int Tank = 700;

		[TestCase(TestName = "An arm with infrastructure but no units is diagnosed as missing.")]
		public void MissingArmIsDetected()
		{
			Assert.That(ProductionBalance.ArmIsMissing(infrastructureExists: true, ownedOfArm: 0), Is.True);
			Assert.That(ProductionBalance.ArmIsMissing(infrastructureExists: false, ownedOfArm: 0), Is.False,
				"Without an airfield, having no aircraft is a consequence, not a choice.");
			Assert.That(ProductionBalance.ArmIsMissing(infrastructureExists: true, ownedOfArm: 5), Is.False);
		}

		[TestCase(TestName = "A missing arm is bought out of order once it can be afforded.")]
		public void MissingArmIsBoughtOutOfOrder()
		{
			// The value of the first aircraft is not its combat power; it is that it unlocks a
			// doctrine the coalition otherwise cannot execute at all.
			Assert.That(ProductionBalance.ShouldBuyArm(infrastructureExists: true, ownedOfArm: 0,
				cash: 6000, unitCost: Aircraft), Is.True);
		}

		[TestCase(TestName = "Buying the arm must not empty the treasury.")]
		public void PurchaseKeepsAWorkingReserve()
		{
			// Spending the last 2000 credits on one aircraft leaves nothing to replace losses with,
			// which is how a single trade turns into losing the match.
			Assert.That(ProductionBalance.ShouldBuyArm(infrastructureExists: true, ownedOfArm: 0,
				cash: Aircraft, unitCost: Aircraft), Is.False);
			Assert.That(ProductionBalance.ShouldBuyArm(infrastructureExists: true, ownedOfArm: 0,
				cash: Aircraft * 2, unitCost: Aircraft), Is.True);
		}

		[TestCase(TestName = "The floor stops applying once the arm is actually fielded.")]
		public void FloorIsNotAQuota()
		{
			Assert.That(ProductionBalance.ShouldBuyArm(true, ProductionBalance.ArmFloor, 100000, Aircraft), Is.False,
				"Past the minimum the normal priority order resumes; this is a floor, not a quota.");
		}

		[TestCase(TestName = "Nothing is bought without the infrastructure to build it.")]
		public void InfrastructureIsRequired()
		{
			Assert.That(ProductionBalance.ShouldBuyArm(infrastructureExists: false, ownedOfArm: 0,
				cash: 100000, unitCost: Aircraft), Is.False);
		}

		[TestCase(TestName = "Affordability explains why an arm never appears, rather than merely observing it.")]
		public void CyclesToAffordExplainsTheGap()
		{
			// The measured picture: an income that comfortably buys a tank each cycle needs several
			// cycles to buy one aircraft, and the priority list hands every cycle to the tank.
			var tankCycles = ProductionBalance.CyclesToAfford(Tank, incomePerCycle: 800);
			var aircraftCycles = ProductionBalance.CyclesToAfford(Aircraft, incomePerCycle: 800);

			Assert.That(tankCycles, Is.EqualTo(1));
			Assert.That(aircraftCycles, Is.EqualTo(3));
			Assert.That(aircraftCycles, Is.GreaterThan(tankCycles));

			Assert.That(ProductionBalance.CyclesToAfford(Aircraft, incomePerCycle: 0), Is.EqualTo(int.MaxValue),
				"With no income the aircraft is unreachable by construction, not merely unlikely.");
			Assert.That(ProductionBalance.CyclesToAfford(0, 800), Is.Zero);
		}
	}
}
