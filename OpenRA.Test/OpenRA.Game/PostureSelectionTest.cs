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

		[TestCase(TestName = "A safe high-value opportunity selects expansion.")]
		public void Expansion()
		{
			Assert.That(PostureSelection.Select(0.9f, 0f, 20, false, expansionOpportunity: true),
				Is.EqualTo(StrategicPosture.Expansion));
		}

		[TestCase(TestName = "A recovered defensive front counterattacks.")]
		public void Counterattack()
		{
			Assert.That(PostureSelection.Select(0.9f, 0f, 20, false, recentlyDefended: true),
				Is.EqualTo(StrategicPosture.Counterattack));
		}

		[TestCase(TestName = "Heavy coalition casualties select recovery.")]
		public void Recovery()
		{
			Assert.That(PostureSelection.Select(0.9f, 0f, 20, false, casualtyFraction: 0.6f),
				Is.EqualTo(StrategicPosture.Recovery));
		}

		[TestCase(TestName = "Local theater posture ignores the global force ratio.")]
		public void LocalPostures()
		{
			Assert.That(PostureSelection.SelectLocal(0.2f, 0.8f, 0f), Is.EqualTo(StrategicPosture.Defensive));
			Assert.That(PostureSelection.SelectLocal(0.8f, 0.2f, 0f), Is.EqualTo(StrategicPosture.Breakthrough));
			Assert.That(PostureSelection.SelectLocal(0.1f, 0.1f, 0.8f), Is.EqualTo(StrategicPosture.Expansion));
			Assert.That(PostureSelection.SelectLocal(0.3f, 0.3f, 0.3f), Is.EqualTo(StrategicPosture.None));
		}

		[TestCase(TestName = "Every strategic posture has a bounded cross-system policy.")]
		public void EveryPostureHasPolicy()
		{
			foreach (var posture in System.Enum.GetValues<StrategicPosture>())
			{
				var policy = PostureSelection.PolicyFor(posture);
				Assert.That(policy, Is.Not.Null, posture.ToString());
				Assert.That(policy.AcceptableLossFraction, Is.InRange(0f, 1f), posture.ToString());
				Assert.That(policy.ReserveFraction, Is.InRange(1, 10), posture.ToString());
				Assert.That(policy.ExpansionPriority, Is.InRange(-1, 1), posture.ToString());
				Assert.That(policy.SecondaryOperationBudget, Is.InRange(0f, 1f), posture.ToString());
				Assert.That(policy.RequiredDefensiveFraction, Is.InRange(0f, 1f), posture.ToString());
			}
		}

		[TestCase(TestName = "Postures materially change production, risk, reserves, and expansion timing.")]
		public void PosturePolicyChangesOperationalConstraints()
		{
			var expansion = PostureSelection.PolicyFor(StrategicPosture.Expansion);
			var defensive = PostureSelection.PolicyFor(StrategicPosture.Defensive);
			var allIn = PostureSelection.PolicyFor(StrategicPosture.AllIn);

			Assert.That(expansion.ProductionCapabilities, Does.Contain("recon"));
			Assert.That(expansion.ExpansionPriority, Is.EqualTo(1));
			Assert.That(defensive.AcceptableLossFraction, Is.LessThan(allIn.AcceptableLossFraction));
			Assert.That(defensive.RequiredDefensiveFraction, Is.GreaterThan(allIn.RequiredDefensiveFraction));
			Assert.That(allIn.CommitReserve, Is.True);
		}

		[TestCase(TestName = "Posture selects the target-scoring profile.")]
		public void TargetWeightsFor()
		{
			Assert.That(PostureSelection.TargetWeightsFor(StrategicPosture.Breakthrough).PositionalValue, Is.EqualTo(TargetWeights.Breakthrough().PositionalValue));
			Assert.That(PostureSelection.TargetWeightsFor(StrategicPosture.Raiding).EconomicDamage, Is.EqualTo(TargetWeights.Raiding().EconomicDamage));
			Assert.That(PostureSelection.TargetWeightsFor(StrategicPosture.Defensive).StrategicValue, Is.EqualTo(TargetWeights.Balanced().StrategicValue));
		}

		[TestCase(TestName = "Target-profile sweeps materially change target scoring weights.")]
		public void TunableTargetProfiles()
		{
			var balanced = PostureSelection.TargetWeightsForProfile(StrategicPosture.Defensive, "balanced");
			var breakthrough = PostureSelection.TargetWeightsForProfile(StrategicPosture.Defensive, "breakthrough");
			var raiding = PostureSelection.TargetWeightsForProfile(StrategicPosture.Defensive, "raiding");

			Assert.That(breakthrough.PositionalValue, Is.GreaterThan(balanced.PositionalValue));
			Assert.That(raiding.EconomicDamage, Is.GreaterThan(balanced.EconomicDamage));
		}

		[TestCase(TestName = "Feint commitment and special-operations risk thresholds are tunable and fail safe.")]
		public void TunableDeceptionAndSpecialOps()
		{
			Assert.That(StrategicBrainBotModule.FeintCommitment(24, 4), Is.EqualTo(6));
			Assert.That(StrategicBrainBotModule.FeintCommitment(24, 8), Is.EqualTo(3));
			Assert.That(StrategicBrainBotModule.FeintCommitment(24, 0), Is.Zero);
			Assert.That(CoalitionCommandCenterBotModule.WithinSpecialOpsRisk(2f, 1.5f), Is.False);
			Assert.That(CoalitionCommandCenterBotModule.WithinSpecialOpsRisk(2f, 2.5f), Is.True);
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
