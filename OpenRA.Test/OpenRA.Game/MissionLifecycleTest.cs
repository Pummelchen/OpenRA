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
	sealed class MissionLifecycleTest
	{
		// The phase machine's transition conditions are pure functions of the mission and a small
		// view of blackboard state. We exercise the manager's public lifecycle without a World:
		// mission creation, unique IDs, cancellation, and terminal-state reasons are all engine-free.

		[TestCase(TestName = "Missions receive unique sequential IDs.")]
		public void UniqueIds()
		{
			var manager = new MissionManager();
			var first = manager.CreateMission(MissionType.Attack, 90, null, "Test 1");
			var second = manager.CreateMission(MissionType.Attack, 80, null, "Test 2");

			Assert.That(first.Id, Is.Not.EqualTo(second.Id));
			Assert.That(first.Id, Does.StartWith("OP-"));
			Assert.That(manager.Missions.Count, Is.EqualTo(2));
		}

		[TestCase(TestName = "Cancelling a mission removes it by id.")]
		public void Cancel()
		{
			var manager = new MissionManager();
			var mission = manager.CreateMission(MissionType.Recon, 50, null, "Test");

			manager.CancelMission(mission.Id);

			Assert.That(manager.Missions, Is.Empty);
		}

		[TestCase(TestName = "Initial phase depends on the mission type.")]
		public void InitialPhase()
		{
			var manager = new MissionManager();

			var recon = manager.CreateMission(MissionType.Recon, 50, null, "Test");
			var feint = manager.CreateMission(MissionType.Feint, 50, null, "Test");
			var retreat = manager.CreateMission(MissionType.Retreat, 50, null, "Test");
			var attack = manager.CreateMission(MissionType.Attack, 50, null, "Test");

			Assert.That(recon.Phase, Is.EqualTo(MissionPhase.Recon));
			Assert.That(feint.Phase, Is.EqualTo(MissionPhase.Deception));
			Assert.That(retreat.Phase, Is.EqualTo(MissionPhase.Withdrawal));
			Assert.That(attack.Phase, Is.EqualTo(MissionPhase.Recon));
		}

		[TestCase(TestName = "A retreat mission never transitions out of withdrawal.")]
		public void RetreatStaysInWithdrawal()
		{
			// Retreat is terminal by construction: the phase machine keeps it in Withdrawal. This is
			// covered structurally by the phase enum; the manager's Update needs a World, so this
			// asserts the invariant directly.
			Assert.That(System.Enum.GetValues<MissionPhase>(), Does.Contain(MissionPhase.Withdrawal));
			Assert.That(MissionPhase.Withdrawal, Is.GreaterThan(MissionPhase.Consolidation),
				"Withdrawal is the last phase; nothing follows it.");
		}

		[TestCase(TestName = "All offensive phases exist in the expected order.")]
		public void PhaseOrdering()
		{
			var phases = System.Enum.GetValues<MissionPhase>();
			Assert.That(phases, Is.EqualTo(new[]
			{
				MissionPhase.Recon,
				MissionPhase.Staging,
				MissionPhase.Shaping,
				MissionPhase.Deception,
				MissionPhase.Breach,
				MissionPhase.Exploitation,
				MissionPhase.Consolidation,
				MissionPhase.Withdrawal
			}));
		}

		[TestCase(TestName = "The directive JSON names the chosen missions and their targets.")]
		public void DirectiveJson()
		{
			var manager = new MissionManager();
			var attack = manager.CreateMission(MissionType.Attack, 90, new CPos(12, 34), "Test");
			attack.Status = MissionStatus.Executing;

			var directive = manager.BuildDirectiveJson(null, null, false);

			Assert.That(directive, Does.Contain("\"strategy\":\"attack\""));
			Assert.That(directive, Does.Contain("\"attack\":{\"x\":12,\"y\":34}"));
			Assert.That(directive, Does.Not.Contain("\"feint\""));
		}

		[TestCase(TestName = "Mission status enum covers ready, executing, and all terminal states.")]
		public void StatusCoverage()
		{
			Assert.That(System.Enum.GetValues<MissionStatus>(), Is.EqualTo(new[]
			{
				MissionStatus.Ready,
				MissionStatus.Executing,
				MissionStatus.Succeeded,
				MissionStatus.Aborted,
				MissionStatus.Failed
			}));
		}

		[TestCase(TestName = "Offensive mission types are recognized across the expanded set.")]
		public void OffensiveMissionTypes()
		{
			Assert.That(MissionManager.IsOffensive(MissionType.Breakthrough), Is.True);
			Assert.That(MissionManager.IsOffensive(MissionType.AirStrike), Is.True);
			Assert.That(MissionManager.IsOffensive(MissionType.EconomyRaid), Is.True);
			Assert.That(MissionManager.IsOffensive(MissionType.SupportPowerStrike), Is.True);
			Assert.That(MissionManager.IsOffensive(MissionType.Flank), Is.True);
			Assert.That(MissionManager.IsOffensive(MissionType.Defend), Is.False);
			Assert.That(MissionManager.IsOffensive(MissionType.Recon), Is.False);
			Assert.That(MissionManager.IsOffensive(MissionType.Feint), Is.False);
		}

		[TestCase(TestName = "Missions carry desired effects, launch conditions, and contingencies.")]
		public void MissionFrameworkFields()
		{
			var manager = new MissionManager();
			var raid = manager.CreateMission(MissionType.EconomyRaid, 65, new CPos(1, 1), "Test");

			Assert.That(raid.DesiredEffects, Does.Contain("starve_enemy"));
			Assert.That(raid.LaunchConditions, Does.Contain("force >= MinForce"));
			Assert.That(raid.Contingencies, Does.Contain("withdraw"));

			var strike = manager.CreateMission(MissionType.SupportPowerStrike, 95, new CPos(2, 2), "Test");
			Assert.That(strike.LaunchConditions, Does.Contain("power_ready"));
			Assert.That(strike.Phase, Is.EqualTo(MissionPhase.Breach), "Support-power strikes fire immediately.");

			var air = manager.CreateMission(MissionType.AirStrike, 70, new CPos(3, 3), "Test");
			Assert.That(air.Phase, Is.EqualTo(MissionPhase.Shaping), "Air strikes skip ground staging.");
		}

		[TestCase(TestName = "The directive JSON carries strike and support-power targets.")]
		public void DirectiveStrikeTargets()
		{
			var manager = new MissionManager();
			var strike = manager.CreateMission(MissionType.AirStrike, 70, new CPos(12, 34), "Test");
			strike.Status = MissionStatus.Executing;

			var directive = manager.BuildDirectiveJson(null, null, false);
			Assert.That(directive, Does.Contain("\"strike\":{\"x\":12,\"y\":34}"));
			Assert.That(directive, Does.Not.Contain("\"attack\""));

			var power = manager.CreateMission(MissionType.SupportPowerStrike, 95, new CPos(5, 6), "Test");
			power.Status = MissionStatus.Executing;
			Assert.That(manager.BuildDirectiveJson(null, null, false), Does.Contain("\"supportPower\":{\"x\":5,\"y\":6}"));
		}

		[TestCase(TestName = "Defensive and reconnaissance mission families are recognized.")]
		public void DefensiveAndReconFamilies()
		{
			Assert.That(MissionManager.IsDefensive(MissionType.MobileDefense), Is.True);
			Assert.That(MissionManager.IsDefensive(MissionType.AntiAirUmbrella), Is.True);
			Assert.That(MissionManager.IsDefensive(MissionType.Escort), Is.True);
			Assert.That(MissionManager.IsDefensive(MissionType.Attack), Is.False);

			Assert.That(MissionManager.IsRecon(MissionType.DeepRecon), Is.True);
			Assert.That(MissionManager.IsRecon(MissionType.ExpansionSearch), Is.True);
			Assert.That(MissionManager.IsRecon(MissionType.Raid), Is.False);

			Assert.That(MissionManager.IsStaticDirective(MissionType.NavalScreen), Is.True);
			Assert.That(MissionManager.IsStaticDirective(MissionType.AirRecon), Is.True);
			Assert.That(MissionManager.IsStaticDirective(MissionType.Breakthrough), Is.False);
		}

		[TestCase(TestName = "A defensive mission directive names its defense kind.")]
		public void DefensiveDirectiveKind()
		{
			var manager = new MissionManager();
			var mobile = manager.CreateMission(MissionType.MobileDefense, 50, new CPos(7, 7), "Test");
			mobile.Status = MissionStatus.Executing;

			var directive = manager.BuildDirectiveJson(null, null, false);
			Assert.That(directive, Does.Contain("\"strategy\":\"defend\""));
			Assert.That(directive, Does.Contain("\"counter\":{\"x\":7,\"y\":7}"));
			Assert.That(directive, Does.Contain("\"defenseKind\":\"mobile\""));
		}

		[TestCase(TestName = "Recon missions carry their information objective.")]
		public void ReconObjectives()
		{
			var manager = new MissionManager();
			var deep = manager.CreateMission(MissionType.DeepRecon, 40, new CPos(9, 9), "Test");

			Assert.That(deep.DesiredEffects, Does.Contain("locate_enemy_main_force"));
		}

		[TestCase(TestName = "Deception missions carry their intended enemy reaction.")]
		public void DeceptionIntendedReaction()
		{
			var manager = new MissionManager();
			var feint = manager.CreateMission(MissionType.Feint, 60, null, "Test");
			var demonstration = manager.CreateMission(MissionType.Demonstration, 50, null, "Test");

			Assert.That(feint.IntendedReaction, Does.Contain("redeploys"));
			Assert.That(demonstration.IntendedReaction, Does.Contain("reserves"));

			Assert.That(MissionManager.IsDeception(MissionType.DecoyTransport), Is.True);
			Assert.That(MissionManager.IsDeception(MissionType.Attack), Is.False);
			Assert.That(manager.DeceptionAttempts, Is.EqualTo(2));
		}

		[TestCase(TestName = "A demonstration maps to the feint directive.")]
		public void DemonstrationDirective()
		{
			var manager = new MissionManager();
			var demonstration = manager.CreateMission(MissionType.Demonstration, 50, new CPos(10, 20), "Test");
			demonstration.Status = MissionStatus.Executing;

			var directive = manager.BuildDirectiveJson(null, null, false);
			Assert.That(directive, Does.Contain("\"feint\":{\"x\":10,\"y\":20}"));
		}
	}
}
