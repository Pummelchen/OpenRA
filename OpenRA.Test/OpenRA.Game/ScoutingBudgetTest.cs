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
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// <para>
	/// The reconnaissance budget (reqs 215-226, and the direct cause of 804 failing).
	/// </para>
	/// <para>
	/// <c>scoutsDeployed</c> counts every scout ever dispatched. Comparing it to the concurrent squad
	/// size meant the coalition stopped scouting permanently after four probes - dead or alive,
	/// successful or not. Scouts probing a defended base usually die, so the enemy base was never
	/// located, no offensive objective could be named, and the coalition spent whole matches
	/// reacting. Measured on Shattered Mountain: 4 scouts and 0 main efforts across 30,000 ticks
	/// before the fix; 40 scouts and 2 main efforts after it.
	/// </para>
	/// </summary>
	[TestFixture]
	sealed class ScoutingBudgetTest
	{
		[TestCase(TestName = "The concurrent cap limits how many scouts are out at once.")]
		public void ConcurrentCapIsRespected()
		{
			Assert.That(StrategicBrainBotModule.ShouldScout(
				enemyBaseLocated: false, activeScouts: 4, maximumScouts: 4,
				scoutsDeployed: 4, lifetimeBudget: 40), Is.False,
				"Reconnaissance must not eat the field army.");

			Assert.That(StrategicBrainBotModule.ShouldScout(
				enemyBaseLocated: false, activeScouts: 3, maximumScouts: 4,
				scoutsDeployed: 4, lifetimeBudget: 40), Is.True);
		}

		[TestCase(TestName = "Losing scouts does not end the search while budget remains.")]
		public void LostScoutsDoNotEndTheSearch()
		{
			// The regression: with the two bounds conflated, this returned false and the coalition
			// went blind for the rest of the match after four losses.
			Assert.That(StrategicBrainBotModule.ShouldScout(
				enemyBaseLocated: false, activeScouts: 0, maximumScouts: 4,
				scoutsDeployed: 12, lifetimeBudget: 40), Is.True,
				"Twelve scouts lost is a reason to keep looking, not to stop.");
		}

		[TestCase(TestName = "The lifetime budget still bounds the total search.")]
		public void LifetimeBudgetStillBounds()
		{
			Assert.That(StrategicBrainBotModule.ShouldScout(
				enemyBaseLocated: false, activeScouts: 0, maximumScouts: 4,
				scoutsDeployed: 40, lifetimeBudget: 40), Is.False,
				"Reconnaissance is bounded; an unbounded search would bleed the army indefinitely.");
		}

		[TestCase(TestName = "Scouting stops once the enemy base is located, whatever the budget.")]
		public void LocatingTheBaseEndsTheSearch()
		{
			Assert.That(StrategicBrainBotModule.ShouldScout(
				enemyBaseLocated: true, activeScouts: 0, maximumScouts: 4,
				scoutsDeployed: 0, lifetimeBudget: 40), Is.False);
		}

		[TestCase(TestName = "An unset budget preserves the previous behaviour for callers that pass none.")]
		public void UnsetBudgetFallsBackToTheConcurrentCap()
		{
			Assert.That(StrategicBrainBotModule.ShouldScout(
				enemyBaseLocated: false, activeScouts: 0, maximumScouts: 4, scoutsDeployed: 4), Is.False);
			Assert.That(StrategicBrainBotModule.ShouldScout(
				enemyBaseLocated: false, activeScouts: 0, maximumScouts: 4, scoutsDeployed: 3), Is.True);
		}

		[TestCase(TestName = "A disabled squad size disables scouting entirely.")]
		public void ZeroSquadSizeDisablesScouting()
		{
			Assert.That(StrategicBrainBotModule.ShouldScout(
				enemyBaseLocated: false, activeScouts: 0, maximumScouts: 0,
				scoutsDeployed: 0, lifetimeBudget: 40), Is.False);
		}
	}
}
