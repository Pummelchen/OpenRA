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
			var spreadSquared = (long)(15 * 1024) * (15 * 1024);
			Assert.That(TacticalFormation.IsAheadOfCenter(far, target, center, spreadSquared), Is.True);
			Assert.That(TacticalFormation.IsAheadOfCenter(near, target, center, spreadSquared), Is.False);
		}
	}
}
