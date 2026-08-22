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
	sealed class CounterattackAssessmentTest
	{
		[TestCase(TestName = "Depleted attackers create a favorable immediate counterattack window.")]
		public void DepletedAttackers()
		{
			var decision = CounterattackAssessment.Evaluate(20, 12, 7, 3, false, 10);
			Assert.That(decision.ShouldLaunch, Is.True);
			Assert.That(decision.EnemyDepleted, Is.True);
			Assert.That(decision.Reason, Is.EqualTo("attackers depleted"));
		}

		[TestCase(TestName = "An exposed production origin creates a counterattack window.")]
		public void ExposedProduction()
		{
			var decision = CounterattackAssessment.Evaluate(20, 8, 9, 2, true, 10);
			Assert.That(decision.ShouldLaunch, Is.True);
			Assert.That(decision.OriginExposed, Is.True);
			Assert.That(decision.ProductionWindow, Is.True);
		}

		[TestCase(TestName = "Unknown or unfavorable origin strength blocks the counterattack.")]
		public void UnfavorableOrUnknown()
		{
			Assert.That(CounterattackAssessment.Evaluate(20, 0, 0, 0, true, 10).ShouldLaunch, Is.False);
			Assert.That(CounterattackAssessment.Evaluate(10, 12, 7, 8, true, 10).ShouldLaunch, Is.False);
		}
	}
}
