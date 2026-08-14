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
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class SupportPowerTest
	{
		[TestCase(TestName = "A support power is withheld when friendly units crowd the target.")]
		public void FriendlyFireGuard()
		{
			// The guard is a single pre-fire check, so it applies uniformly to every support power.
			Assert.That(StrategicBrainBotModule.ShouldWithholdSupportPower(0), Is.False);
			Assert.That(StrategicBrainBotModule.ShouldWithholdSupportPower(2), Is.False,
				"Two friendly units near the target is acceptable.");
			Assert.That(StrategicBrainBotModule.ShouldWithholdSupportPower(3), Is.True,
				"The threshold is three friendly units within the blast radius.");
			Assert.That(StrategicBrainBotModule.ShouldWithholdSupportPower(10), Is.True);
		}

		[TestCase(TestName = "The friendly-fire radius and threshold are non-trivial.")]
		public void GuardParameters()
		{
			Assert.That(StrategicBrainBotModule.SupportPowerFriendlyFireRadius, Is.GreaterThan(0),
				"The blast radius must be positive.");
			Assert.That(StrategicBrainBotModule.SupportPowerFriendlyFireThreshold, Is.GreaterThanOrEqualTo(1),
				"The threshold must be at least one friendly unit.");
		}
	}
}
