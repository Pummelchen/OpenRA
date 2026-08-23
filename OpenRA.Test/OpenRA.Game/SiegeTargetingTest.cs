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
	/// What an assault attacks once it reaches the enemy base (handbook §7). The assault previously
	/// attack-moved to a cell, which engages whatever it meets — on a defended base, the perimeter
	/// pillbox. It then grinds against static defence while the economy replacing it keeps running:
	/// high exchange ratio, nothing killed that matters, time-limit draw.
	/// </summary>
	[TestFixture]
	sealed class SiegeTargetingTest
	{
		static SiegeCandidate At(string type, int x, int y, int distance, bool defence = false)
		{
			return new SiegeCandidate(type, new CPos(x, y), distance, defence);
		}

		[TestCase(TestName = "The main force goes for the economy, not the nearest building.")]
		public void EconomyOutranksProximity()
		{
			// The pillbox is closest. Attacking it is how the assault dies on the perimeter.
			var target = SiegeTargeting.SelectMainForceTarget(
			[
				At("pbox", 50, 50, 2, defence: true),
				At("weap", 60, 60, 12),
				At("proc", 62, 62, 14)
			]);

			Assert.That(target, Is.Not.Null);
			Assert.That(target.Value.Type, Is.EqualTo("proc"),
				"An opponent with no income cannot replace what it loses.");
		}

		[TestCase(TestName = "Defences are never chosen as the main force's objective.")]
		public void DefencesAreNotObjectives()
		{
			var onlyDefences = SiegeTargeting.SelectMainForceTarget(
			[
				At("pbox", 50, 50, 2, defence: true),
				At("tsla", 51, 51, 3, defence: true)
			]);

			Assert.That(onlyDefences, Is.Null,
				"Killing defences changes nothing about the opponent's ability to fight back.");
		}

		[TestCase(TestName = "Production outranks technology, and both outrank a bare structure.")]
		public void ObjectiveValueOrdering()
		{
			Assert.That(SiegeTargeting.ObjectiveValue("proc"),
				Is.GreaterThan(SiegeTargeting.ObjectiveValue("weap")));
			Assert.That(SiegeTargeting.ObjectiveValue("weap"),
				Is.GreaterThan(SiegeTargeting.ObjectiveValue("powr")));
		}

		[TestCase(TestName = "Artillery reduces the nearest defence, which is what out-ranging it is for.")]
		public void ArtilleryTakesTheNearestDefence()
		{
			var target = SiegeTargeting.SelectArtilleryTarget(
			[
				At("tsla", 80, 80, 20, defence: true),
				At("pbox", 50, 50, 3, defence: true),
				At("proc", 55, 55, 5)
			]);

			Assert.That(target, Is.Not.Null);
			Assert.That(target.Value.Type, Is.EqualTo("pbox"));
		}

		[TestCase(TestName = "Artillery has nothing to reduce when no defence is visible.")]
		public void NoDefenceNoArtilleryTarget()
		{
			Assert.That(SiegeTargeting.SelectArtilleryTarget([At("proc", 55, 55, 5)]), Is.Null);
			Assert.That(SiegeTargeting.SelectArtilleryTarget([]), Is.Null);
		}

		[TestCase(TestName = "Waiting to reduce defences requires artillery that can do the reducing.")]
		public void ReduceOnlyWithArtillery()
		{
			Assert.That(SiegeTargeting.ShouldReduceBeforeEntering(visibleDefences: 3, artilleryAvailable: 2), Is.True);
			Assert.That(SiegeTargeting.ShouldReduceBeforeEntering(visibleDefences: 3, artilleryAvailable: 0), Is.False,
				"With no artillery, waiting achieves nothing and the assault goes in regardless.");
			Assert.That(SiegeTargeting.ShouldReduceBeforeEntering(visibleDefences: 0, artilleryAvailable: 4), Is.False);
		}

		[TestCase(TestName = "Commitment needs local superiority, not global parity.")]
		public void LocalSuperiorityGatesTheAssault()
		{
			// The measured failure: waves launched at global parity achieved local superiority in
			// 0 of 9 engagements and took no ground.
			Assert.That(SiegeTargeting.HasLocalSuperiority(150f, 100f), Is.True);
			Assert.That(SiegeTargeting.HasLocalSuperiority(100f, 100f), Is.False,
				"Attacking at 1:1 trades evenly, which does not take ground.");
			Assert.That(SiegeTargeting.HasLocalSuperiority(10f, 0f), Is.True,
				"An uncontested objective needs no ratio.");
			Assert.That(SiegeTargeting.HasLocalSuperiority(0f, 10f), Is.False);
		}

		[TestCase(TestName = "Selection is deterministic so allied bots converge on one objective.")]
		public void SelectionIsDeterministic()
		{
			static SiegeCandidate[] Candidates() =>
			[
				At("proc", 60, 60, 10),
				At("proc", 70, 70, 10)
			];

			var first = SiegeTargeting.SelectMainForceTarget(Candidates());
			var second = SiegeTargeting.SelectMainForceTarget([.. Candidates()]);

			Assert.That(first, Is.EqualTo(second),
				"Two allies attacking different refineries is two half-assaults.");
		}
	}
}
