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
	sealed class PostureSelectionTest
	{
		[TestCase(TestName = "A small army is in the opening posture.")]
		public void Opening()
		{
			Assert.That(PostureSelection.Select(1f, 0f, 4, false), Is.EqualTo(StrategicPosture.Opening));
		}

		[TestCase(TestName = "A crushing enemy forces the desperation posture.")]
		public void Desperation()
		{
			Assert.That(PostureSelection.Select(3f, 0f, 20, false), Is.EqualTo(StrategicPosture.Desperation));
		}

		[TestCase(TestName = "An outnumbered coalition stands on the defensive.")]
		public void Defensive()
		{
			Assert.That(PostureSelection.Select(1.5f, 0f, 20, false), Is.EqualTo(StrategicPosture.Defensive));
		}

		[TestCase(TestName = "Heavy enemy fortifications demand a siege.")]
		public void Siege()
		{
			Assert.That(PostureSelection.Select(0.5f, 0.8f, 20, false), Is.EqualTo(StrategicPosture.Siege));
		}

		[TestCase(TestName = "A decisive edge turns into a breakthrough.")]
		public void Breakthrough()
		{
			Assert.That(PostureSelection.Select(0.3f, 0f, 20, false), Is.EqualTo(StrategicPosture.Breakthrough));
		}

		[TestCase(TestName = "An overwhelming edge is an all-in push.")]
		public void AllIn()
		{
			Assert.That(PostureSelection.Select(0.1f, 0f, 20, false), Is.EqualTo(StrategicPosture.AllIn));
		}

		[TestCase(TestName = "Posture selects the target-scoring profile.")]
		public void TargetWeightsFor()
		{
			Assert.That(PostureSelection.TargetWeightsFor(StrategicPosture.Breakthrough).PositionalValue, Is.EqualTo(TargetWeights.Breakthrough().PositionalValue));
			Assert.That(PostureSelection.TargetWeightsFor(StrategicPosture.Raiding).EconomicDamage, Is.EqualTo(TargetWeights.Raiding().EconomicDamage));
			Assert.That(PostureSelection.TargetWeightsFor(StrategicPosture.Defensive).StrategicValue, Is.EqualTo(TargetWeights.Balanced().StrategicValue));
		}

		[TestCase(TestName = "All-in and desperation commit the reserve.")]
		public void CommitsReserve()
		{
			Assert.That(PostureSelection.CommitsReserve(StrategicPosture.AllIn), Is.True);
			Assert.That(PostureSelection.CommitsReserve(StrategicPosture.Desperation), Is.True);
			Assert.That(PostureSelection.CommitsReserve(StrategicPosture.Breakthrough), Is.False);
		}
	}
}
