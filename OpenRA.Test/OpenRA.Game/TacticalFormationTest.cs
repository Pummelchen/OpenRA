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

using NUnit.Framework;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class TacticalFormationTest
	{
		[TestCase(TestName = "Artillery pullback moves the firing point back toward the base.")]
		public void ArtilleryPullbackMovesTargetTowardBase()
		{
			var target = new WPos(10240, 0, 0);
			var baseCenter = new WPos(0, 0, 0);
			var pulled = TacticalFormation.ArtilleryPullbackTarget(target, baseCenter, 8 * 1024);
			Assert.That(pulled, Is.EqualTo(new WPos(2048, 0, 0)));
		}

		[TestCase(TestName = "Artillery pullback inside the offset distance returns the target unchanged.")]
		public void ArtilleryPullbackCloserThanOffsetReturnsTarget()
		{
			var target = new WPos(4096, 0, 0);
			var baseCenter = new WPos(0, 0, 0);
			Assert.That(TacticalFormation.ArtilleryPullbackTarget(target, baseCenter, 8 * 1024), Is.EqualTo(target));
		}

		[TestCase(TestName = "Artillery pullback with the base at the target returns the target unchanged.")]
		public void ArtilleryPullbackZeroAxisReturnsTarget()
		{
			var target = new WPos(512, 0, 0);
			var baseCenter = new WPos(512, 0, 0);
			Assert.That(TacticalFormation.ArtilleryPullbackTarget(target, baseCenter, 8 * 1024), Is.EqualTo(target));
		}

		[TestCase(TestName = "Speed coordination flags only units far ahead of the group center.")]
		public void IsAheadFlagsPositionsFarAheadOfCenter()
		{
			var target = new WPos(40960, 0, 0);
			var center = new WPos(10240, 0, 0);
			var far = new WPos(35000, 0, 0);
			var near = new WPos(5000, 0, 0);
			const long SpreadSquared = (long)(15 * 1024) * (15 * 1024);
			Assert.That(TacticalFormation.IsAheadOfCenter(far, target, center, SpreadSquared), Is.True);
			Assert.That(TacticalFormation.IsAheadOfCenter(near, target, center, SpreadSquared), Is.False);
		}

		[TestCase(TestName = "Tactical executor prioritizes armed production over a generic structure.")]
		public void TacticalTargetScorePrioritizesCombatProduction()
		{
			var production = new TacticalTargetProfile(1000, 100, armed: true, structure: true, production: true);
			var generic = new TacticalTargetProfile(1000, 100, armed: false, structure: true, production: false);
			Assert.That(TacticalEngagement.TargetScore(production, 0),
				Is.GreaterThan(TacticalEngagement.TargetScore(generic, 0)));
		}

		[TestCase(TestName = "Tactical executor finishes damaged targets and discounts distant contacts.")]
		public void TacticalTargetScoreRewardsFinishAndLocality()
		{
			var healthy = new TacticalTargetProfile(800, 100, armed: true, structure: false, production: false);
			var damaged = new TacticalTargetProfile(800, 20, armed: true, structure: false, production: false);
			Assert.That(TacticalEngagement.TargetScore(damaged, 0),
				Is.GreaterThan(TacticalEngagement.TargetScore(healthy, 0)));
			Assert.That(TacticalEngagement.TargetScore(healthy, 0),
				Is.GreaterThan(TacticalEngagement.TargetScore(healthy, 20L * 1024 * 20 * 1024)));
		}

		[TestCase(TestName = "Focus-fire budget avoids overkill and scales for valuable structures.")]
		public void TacticalFocusSlotsAreBoundedByTargetValueAndHealth()
		{
			var woundedInfantry = new TacticalTargetProfile(100, 10, armed: true, structure: false, production: false);
			var production = new TacticalTargetProfile(2000, 100, armed: true, structure: true, production: true);
			Assert.That(TacticalEngagement.FocusSlots(woundedInfantry), Is.EqualTo(1));
			Assert.That(TacticalEngagement.FocusSlots(production), Is.GreaterThan(1).And.LessThanOrEqualTo(10));
		}

		[TestCase(TestName = "Movement directives refresh only when idle or stale.")]
		public void TacticalOrdersUseBoundedRefreshCadence()
		{
			Assert.That(TacticalEngagement.ShouldRefreshOrder(true, 100, 90, 75), Is.True);
			Assert.That(TacticalEngagement.ShouldRefreshOrder(false, 100, 90, 75), Is.False);
			Assert.That(TacticalEngagement.ShouldRefreshOrder(false, 166, 90, 75), Is.True);
		}

		[TestCase(TestName = "Asset-defense commitment is proportional and bounded.")]
		public void TacticalDefenseCommitmentIsProportionalAndBounded()
		{
			Assert.That(TacticalEngagement.DefenseCommitment(1, 30, 6, 6), Is.EqualTo(6));
			Assert.That(TacticalEngagement.DefenseCommitment(3, 30, 6, 6), Is.EqualTo(18));
			Assert.That(TacticalEngagement.DefenseCommitment(10, 30, 6, 6), Is.EqualTo(30));
			Assert.That(TacticalEngagement.DefenseCommitment(0, 4, 6, 6), Is.EqualTo(4));
			Assert.That(TacticalEngagement.DefenseCommitment(3, 0, 6, 6), Is.Zero);
		}

		[TestCase(TestName = "Counter pursuit projects beyond contact without hidden positions.")]
		public void TacticalCounterPursuitProjectsAlongObservedApproach()
		{
			var home = new WPos(0, 0, 0);
			var contact = new WPos(10 * 1024, 0, 0);
			Assert.That(TacticalFormation.ProjectBeyondContact(contact, home, 30 * 1024),
				Is.EqualTo(new WPos(40 * 1024, 0, 0)));
			Assert.That(TacticalFormation.ProjectBeyondContact(home, home, 30 * 1024), Is.EqualTo(home));
		}
	}
}
