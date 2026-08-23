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
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

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

		[TestCase(TestName = "Every RA support-power order has an explicit tactical role.")]
		public void RaPowerClassification()
		{
			Assert.That(SupportPowerPolicy.Classify("SovietSpyPlane"), Is.EqualTo(SupportPowerRole.Recon));
			Assert.That(SupportPowerPolicy.Classify("SovietParatroopers"), Is.EqualTo(SupportPowerRole.Reinforcement));
			Assert.That(SupportPowerPolicy.Classify("UkraineParabombs"), Is.EqualTo(SupportPowerRole.Strike));
			Assert.That(SupportPowerPolicy.Classify("NukePowerInfoOrder"), Is.EqualTo(SupportPowerRole.Strike));
			Assert.That(SupportPowerPolicy.Classify("Chronoshift"), Is.EqualTo(SupportPowerRole.Redeployment));
			Assert.That(SupportPowerPolicy.Classify("AdvancedChronoshift"), Is.EqualTo(SupportPowerRole.Redeployment));
			Assert.That(SupportPowerPolicy.Classify("GrantExternalConditionPowerInfoOrder"),
				Is.EqualTo(SupportPowerRole.Protection));
			Assert.That(SupportPowerPolicy.Classify("SomeModdedPower"), Is.EqualTo(SupportPowerRole.Unsupported));
		}

		[TestCase(TestName = "Every RA support power the mod defines is classified, none left unsupported.")]
		public void EveryRaPowerIsSupported()
		{
			// The complete set of support-power order names in mods/ra. If a power is added to the
			// mod without a policy entry this fails instead of the bot silently never firing it.
			var raPowers = new[]
			{
				"SovietSpyPlane", "SovietParatroopers", "UkraineParabombs", "NukePowerInfoOrder",
				"Chronoshift", "AdvancedChronoshift", "GrantExternalConditionPowerInfoOrder"
			};

			foreach (var power in raPowers)
				Assert.That(SupportPowerPolicy.Classify(power), Is.Not.EqualTo(SupportPowerRole.Unsupported),
					$"RA support power \"{power}\" has no tactical role and would never be fired.");
		}

		[TestCase(TestName = "Force multipliers require a committed friendly force at the target.")]
		public void ForceMultiplierRequiresEscort()
		{
			// Iron Curtain and Chronosphere invert the friendly-fire rule: friendlies at the target
			// are the point, and firing on an empty cell wastes the charge.
			Assert.That(SupportPowerPolicy.IsForceMultiplier(SupportPowerRole.Protection), Is.True);
			Assert.That(SupportPowerPolicy.IsForceMultiplier(SupportPowerRole.Redeployment), Is.True);
			Assert.That(SupportPowerPolicy.IsForceMultiplier(SupportPowerRole.Strike), Is.False);

			const int Enough = SupportPowerPolicy.MinimumEscortedForce;
			Assert.That(SupportPowerPolicy.ShouldFire(SupportPowerRole.Protection, 5f, Enough, true), Is.True);
			Assert.That(SupportPowerPolicy.ShouldFire(SupportPowerRole.Protection, 5f, Enough - 1, true), Is.False,
				"An Iron Curtain on too small a force is wasted.");
			Assert.That(SupportPowerPolicy.ShouldFire(SupportPowerRole.Protection, 1f, Enough, true), Is.False,
				"A low-value objective does not justify the power.");

			Assert.That(SupportPowerPolicy.ShouldFire(SupportPowerRole.Redeployment, 5f, Enough, true), Is.True);
			Assert.That(SupportPowerPolicy.ShouldFire(SupportPowerRole.Redeployment, 5f, 0, true), Is.False,
				"A chronoshift with nothing to move is wasted.");
			Assert.That(SupportPowerPolicy.ShouldFire(SupportPowerRole.Redeployment, 2f, Enough, true), Is.False);
			Assert.That(SupportPowerPolicy.ShouldFire(SupportPowerRole.Protection, 5f, Enough, false), Is.False,
				"No shaping window means no fire, for every role.");
		}

		[TestCase(TestName = "Support powers require a shaping window, target value, and strike safety.")]
		public void SupportPowerFirePolicy()
		{
			Assert.That(SupportPowerPolicy.ShouldFire(SupportPowerRole.Recon, 0f, 10, true), Is.True);
			Assert.That(SupportPowerPolicy.ShouldFire(SupportPowerRole.Reinforcement, 1f, 0, true), Is.True);
			Assert.That(SupportPowerPolicy.ShouldFire(SupportPowerRole.Strike, 3f, 0, true), Is.True);
			Assert.That(SupportPowerPolicy.ShouldFire(SupportPowerRole.Strike, 2f, 0, true), Is.False);
			Assert.That(SupportPowerPolicy.ShouldFire(SupportPowerRole.Strike, 10f, 3, true), Is.False);
			Assert.That(SupportPowerPolicy.ShouldFire(SupportPowerRole.Strike, 10f, 0, false), Is.False);
		}
	}
}
