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
	/// <summary>
	/// The chief says "hold back a fifth"; the brain wants a divisor. Getting this backwards made
	/// the chief's reserve decision unable to change anything at all.
	/// </summary>
	[TestFixture]
	sealed class ReserveFractionTest
	{
		[TestCase(0.5f, 2, TestName = "Half the army held back is a divisor of two.")]
		[TestCase(0.25f, 4, TestName = "A quarter is four.")]
		[TestCase(0.2f, 5, TestName = "A fifth is five.")]
		[TestCase(0.1f, 10, TestName = "A tenth is ten.")]
		public void FractionBecomesDivisor(float fraction, int expected)
		{
			Assert.That(CoalitionCommandCenterBotModule.DivisorFor(fraction), Is.EqualTo(expected));
		}

		[TestCase(TestName = "The old conversion collapsed every fraction onto one value.")]
		public void TheDefectItReplaces()
		{
			// What the code did: multiply by a hundred and hand that over as the divisor, which the
			// brain then clamped to ten. A fifth arrived as 20 and clamped to 10; two fifths arrived
			// as 40 and clamped to 10. Twenty-four benchmark matches at each setting returned a
			// byte-identical result, because the two settings were the same setting.
			static int Old(float fraction) => System.Math.Clamp((int)System.Math.Round(fraction * 100f), 0, 10);

			Assert.That(Old(0.2f), Is.EqualTo(Old(0.4f)), "the defect, stated as a test");
			Assert.That(CoalitionCommandCenterBotModule.DivisorFor(0.2f),
				Is.Not.EqualTo(CoalitionCommandCenterBotModule.DivisorFor(0.4f)),
				"and the fix: the chief's decision now reaches the army");
		}

		[TestCase(TestName = "Holding back nothing means no override, not a divide by zero.")]
		public void ZeroMeansNoOverride()
		{
			Assert.That(CoalitionCommandCenterBotModule.DivisorFor(0f), Is.EqualTo(0));
			Assert.That(CoalitionCommandCenterBotModule.DivisorFor(-1f), Is.EqualTo(0));
		}

		[TestCase(TestName = "An absurd fraction is clamped rather than trusted.")]
		public void ExtremesAreClamped()
		{
			// A reserve of nine tenths would send a tenth of the army at the objective.
			Assert.That(CoalitionCommandCenterBotModule.DivisorFor(0.9f), Is.EqualTo(2));
			Assert.That(CoalitionCommandCenterBotModule.DivisorFor(0.01f), Is.EqualTo(10));
		}
	}
}
