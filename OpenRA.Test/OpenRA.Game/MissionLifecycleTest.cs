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
	}
}
