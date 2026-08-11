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
	sealed class DeceptionTest
	{
		[TestCase(TestName = "Creating feint or bait missions counts as a deception attempt; other missions do not.")]
		public void AttemptsCountedAtCreation()
		{
			var manager = new MissionManager();
			manager.CreateMission(MissionType.Feint, 60, null, "Test");
			manager.CreateMission(MissionType.Bait, 55, null, "Test");
			manager.CreateMission(MissionType.Attack, 90, null, "Test");
			manager.CreateMission(MissionType.Raid, 90, null, "Test");

			Assert.That(manager.DeceptionAttempts, Is.EqualTo(2));
		}

		[TestCase(TestName = "A deception draws a response only when enemy presence surges above the baseline.")]
		public void ResponseMeasurement()
		{
			// Baseline of two units and no surge: no response.
			var (drew, engaged) = MissionManager.MeasureDeceptionResponse(2, 2);
			Assert.That(drew, Is.False);
			Assert.That(engaged, Is.Zero);

			// Five units near the target versus a baseline of two: three units were pulled in.
			(drew, engaged) = MissionManager.MeasureDeceptionResponse(2, 5);
			Assert.That(drew, Is.True);
			Assert.That(engaged, Is.EqualTo(3));
		}

		[TestCase(TestName = "A lone unit near the target is not a measurable response.")]
		public void LoneUnitNotResponse()
		{
			var (drew, engaged) = MissionManager.MeasureDeceptionResponse(0, 1);
			Assert.That(drew, Is.False);
			Assert.That(engaged, Is.Zero);

			(drew, engaged) = MissionManager.MeasureDeceptionResponse(0, 2);
			Assert.That(drew, Is.True);
			Assert.That(engaged, Is.EqualTo(2));
		}

		[TestCase(TestName = "Effectiveness is the success rate; zero attempts is unknown, not a failure.")]
		public void EffectivenessFormula()
		{
			Assert.That(CoalitionBlackboard.Effectiveness(0, 0), Is.EqualTo(0f));
			Assert.That(CoalitionBlackboard.Effectiveness(3, 1), Is.EqualTo(1f / 3f).Within(0.001f));
			Assert.That(CoalitionBlackboard.Effectiveness(3, 3), Is.EqualTo(1f));
		}
	}
}
