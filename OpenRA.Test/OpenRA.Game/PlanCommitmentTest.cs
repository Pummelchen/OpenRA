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
using OpenRA.Mods.Common.Commander.Model;
using OpenRA.Mods.Common.Commander.Search;

namespace OpenRA.Test
{
	/// <summary>
	/// <para>
	/// Plan commitment - the direct fix for the defect that produced thirty-eight draws.
	/// </para>
	/// <para>
	/// The previous commander recomputed its posture every review from the instantaneous army ratio.
	/// Every successful attack makes that ratio worse before it makes it better: you lose units to
	/// the defences before you kill the production that wins the game. So it recalled the assault at
	/// exactly the moment assaults start working. These tests exist to make sure that cannot happen
	/// again, and the load-bearing one is <see cref="AFallingRatioIsNotAnAbortCondition"/>.
	/// </para>
	/// </summary>
	[TestFixture]
	sealed class PlanCommitmentTest
	{
		static Plan Assault(int start = 1000, int until = 2500, float strength = 5000f, float value = 0.62f) =>
			new()
			{
				Objective = new MacroAction(MacroVerb.Attack, 7),
				StartTick = start,
				CommittedUntilTick = until,
				LaunchStrength = strength,
				LaunchHomeIntegrity = 8000f,
				ExpectedValue = value,
			};

		[TestCase(TestName = "A falling army ratio is not an abort condition.")]
		public void AFallingRatioIsNotAnAbortCondition()
		{
			var plan = Assault();

			// The assault is halfway in and has paid forty per cent of its strength to get there.
			// The old commander read exactly this position as a reason to turn around, which is why
            // it never destroyed a structure in thirty-eight matches.
			var status = plan.Review(tick: 1800, currentStrength: 3000f, currentHomeIntegrity: 8000f,
				objectiveStillExists: true);

			Assert.That(status, Is.EqualTo(PlanStatus.Active));
			Assert.That(plan.IsActive, Is.True,
				"Losing units on the way to the objective is the price of the plan, not a reason to abandon it.");
		}

		[TestCase(TestName = "A better idea cannot interrupt a committed plan.")]
		public void CommitmentOutranksOpportunity()
		{
			var plan = Assault(value: 0.62f);

			// Something scoring much higher appears mid-assault. It still may not interrupt, because
			// re-deciding every review is the failure mode this type exists to prevent.
			var status = plan.Review(tick: 1500, currentStrength: 4500f, currentHomeIntegrity: 8000f,
				objectiveStillExists: true, bestAlternative: 0.95f);

			Assert.That(status, Is.EqualTo(PlanStatus.Active));

			// Once the commitment has run out, the same alternative is free to take over.
			var after = plan.Review(tick: 2500, currentStrength: 4500f, currentHomeIntegrity: 8000f,
				objectiveStillExists: true, bestAlternative: 0.95f);
			Assert.That(after, Is.EqualTo(PlanStatus.Superseded));
		}

		[TestCase(TestName = "A marginally better idea does not supersede even after expiry.")]
		public void SupersedingRequiresAMargin()
		{
			var plan = Assault(value: 0.62f);

			// Without a margin two nearly equal plans alternate forever and neither is carried out.
			var status = plan.Review(tick: 2500, currentStrength: 4500f, currentHomeIntegrity: 8000f,
				objectiveStillExists: true, bestAlternative: 0.66f);

			Assert.That(status, Is.EqualTo(PlanStatus.Expired), "Expired on its own terms, not displaced.");
			Assert.That(plan.WouldSupersede(0.66f), Is.False);
			Assert.That(plan.WouldSupersede(0.80f), Is.True);
		}

		[TestCase(TestName = "A force that no longer exists ends the plan.")]
		public void ForceSpentEndsThePlan()
		{
			var plan = Assault(strength: 5000f);

			// The distinction that matters: not "the exchange looks bad" but "the army is gone".
			Assert.That(plan.Review(1500, 2100f, 8000f, true), Is.EqualTo(PlanStatus.Active),
				"Forty-two per cent left is still an assault.");

			var spent = Assault(strength: 5000f);
			Assert.That(spent.Review(1500, 1500f, 8000f, true), Is.EqualTo(PlanStatus.ForceSpent));
		}

		[TestCase(TestName = "Home falling apart outranks any objective.")]
		public void HomeThreatEndsThePlan()
		{
			var plan = Assault();
			var status = plan.Review(tick: 1500, currentStrength: 5000f, currentHomeIntegrity: 4000f,
				objectiveStillExists: true);

			Assert.That(status, Is.EqualTo(PlanStatus.HomeThreatened),
				"There is no objective worth trading the base for.");
		}

		[TestCase(TestName = "An objective that is already gone ends the plan.")]
		public void VanishedObjectiveEndsThePlan()
		{
			var plan = Assault();
			Assert.That(plan.Review(1500, 5000f, 8000f, objectiveStillExists: false),
				Is.EqualTo(PlanStatus.ObjectiveGone),
				"Someone else killed it, or it was never there; either way this is now a march on an empty field.");
		}

		[TestCase(TestName = "Danger is judged before expiry.")]
		public void DangerIsJudgedBeforeExpiry()
		{
			// A plan that is both out of time and out of army must report the army, because that is
			// what the commander needs to act on - the difference between "choose the next plan" and
			// "those units are not coming back".
			var plan = Assault();
			Assert.That(plan.Review(9999, 100f, 8000f, true), Is.EqualTo(PlanStatus.ForceSpent));
		}

		[TestCase(TestName = "A concluded plan stays concluded.")]
		public void StatusIsSticky()
		{
			var plan = Assault();
			Assert.That(plan.Review(1500, 100f, 8000f, true), Is.EqualTo(PlanStatus.ForceSpent));

			// Reviewing again with the force somehow restored must not resurrect it: the plan ended,
			// and a new decision needs a new plan.
			Assert.That(plan.Review(1600, 5000f, 8000f, true), Is.EqualTo(PlanStatus.ForceSpent));
			Assert.That(plan.IsActive, Is.False);
		}

		[TestCase(TestName = "Remaining commitment is reported in seconds.")]
		public void RemainingSeconds()
		{
			var plan = Assault(start: 1000, until: 2500);
			Assert.That(plan.RemainingSeconds(1000), Is.EqualTo(60f).Within(0.01f));
			Assert.That(plan.RemainingSeconds(2500), Is.EqualTo(0f));
			Assert.That(plan.RemainingSeconds(9999), Is.EqualTo(0f), "Never negative.");
		}
	}
}
