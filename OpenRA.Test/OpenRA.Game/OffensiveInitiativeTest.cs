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
	/// <para>
	/// Covers the offensive-initiative fix behind requirement 804.
	/// </para>
	/// <para>
	/// The coalition could out-trade a scripted opponent for a whole match and still never threaten
	/// it: the deliberate-assault gate needs a 33% strength edge, but the enemy estimate carries a fog
	/// floor proportional to the unexplored map, so an army that never advanced could never earn that
	/// edge - it assumed a large hidden enemy precisely because it had not looked. With no observed
	/// enemy structure there was also no objective to name, so every mission created was reactive.
	/// </para>
	/// </summary>
	[TestFixture]
	sealed class OffensiveInitiativeTest
	{
		static readonly CPos Home = new(10, 10);

		[TestCase(TestName = "An observed enemy base means the normal assault gate applies, not an advance.")]
		public void ObservedBaseSuppressesReconInForce()
		{
			Assert.That(CoalitionCommandCenterBotModule.ShouldAdvanceToFindEnemy(
				observedEnemyRegion: 4, coalitionArmy: 24 * CoalitionCommandCenterBotModule.AdvanceForceMultiple, coordinatedMinimum: 24,
				currentTick: 100000, searchStartTick: 0, commandInterval: 100), Is.False,
				"With the base located there is nothing to search for.");
		}

		[TestCase(TestName = "An army at only the coordinated minimum does not advance on an unconfirmed objective.")]
		public void SmallArmyDoesNotAdvance()
		{
			Assert.That(CoalitionCommandCenterBotModule.ShouldAdvanceToFindEnemy(
				observedEnemyRegion: -1, coalitionArmy: 24, coordinatedMinimum: 24,
				currentTick: 100000, searchStartTick: 0, commandInterval: 100), Is.False);
		}

		[TestCase(TestName = "Reconnaissance gets a fair chance before the army is committed to find contact.")]
		public void ScoutingIsGivenTimeFirst()
		{
			// Immediately after the force is ready, scouting may still locate the base; advancing then
			// would abandon reconnaissance for a costly probe.
			Assert.That(CoalitionCommandCenterBotModule.ShouldAdvanceToFindEnemy(
				observedEnemyRegion: -1, coalitionArmy: 24 * CoalitionCommandCenterBotModule.AdvanceForceMultiple, coordinatedMinimum: 24,
				currentTick: 500, searchStartTick: 0, commandInterval: 100), Is.False);

			// Ten command intervals later, scouting has demonstrably failed.
			Assert.That(CoalitionCommandCenterBotModule.ShouldAdvanceToFindEnemy(
				observedEnemyRegion: -1, coalitionArmy: 24 * CoalitionCommandCenterBotModule.AdvanceForceMultiple, coordinatedMinimum: 24,
				currentTick: 1000, searchStartTick: 0, commandInterval: 100), Is.True);
		}

		[TestCase(TestName = "An unstarted search clock never triggers an advance.")]
		public void UnstartedClockDoesNotAdvance()
		{
			Assert.That(CoalitionCommandCenterBotModule.ShouldAdvanceToFindEnemy(
				observedEnemyRegion: -1, coalitionArmy: 24 * CoalitionCommandCenterBotModule.AdvanceForceMultiple, coordinatedMinimum: 24,
				currentTick: 100000, searchStartTick: -1, commandInterval: 100), Is.False);
		}

		[TestCase(TestName = "The inferred base is an unexplored starting location, never an explored one.")]
		public void ExploredSpawnsAreRuledOut()
		{
			var spawns = new[] { Home, new CPos(90, 90), new CPos(90, 10) };

			// The coalition has looked at (90,90) and found no base, so it must not keep aiming there.
			var inferred = CoalitionBlackboard.InferEnemyBaseCell(
				spawns, Home, approach: null, isExplored: c => c == new CPos(90, 90));

			Assert.That(inferred, Is.EqualTo(new CPos(90, 10)));
		}

		[TestCase(TestName = "Own starting location is never inferred as the enemy base.")]
		public void HomeIsNeverTheEnemyBase()
		{
			var inferred = CoalitionBlackboard.InferEnemyBaseCell(
				[Home], Home, approach: null, isExplored: _ => false);

			Assert.That(inferred, Is.Null, "With only our own spawn left there is nothing to infer.");
		}

		[TestCase(TestName = "With contact, the spawn nearest the approach axis is preferred.")]
		public void ApproachAxisSelectsTheLikelySpawn()
		{
			var north = new CPos(10, 90);
			var east = new CPos(90, 10);
			var spawns = new[] { Home, north, east };

			// Enemy forces keep arriving from the east, so the eastern spawn is the likely origin
			// even though the northern one is equally distant from home.
			var inferred = CoalitionBlackboard.InferEnemyBaseCell(
				spawns, Home, approach: new CPos(70, 12), isExplored: _ => false);

			Assert.That(inferred, Is.EqualTo(east));
		}

		[TestCase(TestName = "Without contact, the most distant unexplored spawn is assumed.")]
		public void NoContactFallsBackToTheFurthestSpawn()
		{
			var near = new CPos(20, 20);
			var far = new CPos(120, 120);

			var inferred = CoalitionBlackboard.InferEnemyBaseCell(
				[Home, near, far], Home, approach: null, isExplored: _ => false);

			Assert.That(inferred, Is.EqualTo(far),
				"An opponent is far more likely to start across the map than next door.");
		}

		[TestCase(TestName = "Inference is deterministic, so every allied bot names the same objective.")]
		public void InferenceIsDeterministic()
		{
			var a = new CPos(90, 10);
			var b = new CPos(10, 90);

			// Equidistant candidates must resolve identically regardless of declaration order,
			// otherwise allied bots would attack different objectives.
			var first = CoalitionBlackboard.InferEnemyBaseCell([Home, a, b], Home, null, _ => false);
			var second = CoalitionBlackboard.InferEnemyBaseCell([Home, b, a], Home, null, _ => false);

			Assert.That(first, Is.EqualTo(second));
		}

		[TestCase(TestName = "No starting locations yields no inference rather than a fabricated target.")]
		public void EmptySpawnsYieldNoInference()
		{
			Assert.That(CoalitionBlackboard.InferEnemyBaseCell([], Home, null, _ => false), Is.Null);
		}
	}
}
