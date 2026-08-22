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

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class OrderArbiterTest
	{
		[TestCase(TestName = "Directive ownership is explicit once an assignment map is present.")]
		public void DirectiveOwnership()
		{
			Assert.That(CoalitionOrderArbiter.IsAssigned(null, "attack", "Multi0"), Is.True,
				"legacy plans remain broadcast-compatible");
			var assignments = new Dictionary<string, string[]>
			{
				["attack"] = new[] { "Multi1" },
				["recon"] = System.Array.Empty<string>()
			};
			Assert.That(CoalitionOrderArbiter.IsAssigned(assignments, "attack", "Multi1"), Is.True);
			Assert.That(CoalitionOrderArbiter.IsAssigned(assignments, "attack", "Multi0"), Is.False);
			Assert.That(CoalitionOrderArbiter.IsAssigned(assignments, "recon", "Multi1"), Is.False);
			Assert.That(CoalitionOrderArbiter.IsAssigned(assignments, "feint", "Multi1"), Is.False);
		}

		[TestCase(TestName = "An assignment records mission and role ownership.")]
		public void AssignOwnsForce()
		{
			var arbiter = new CoalitionOrderArbiter();

			Assert.That(arbiter.Assign("OP-1", "main", ArbiterPriority.ActiveCombat, "Multi0"), Is.Empty);

			Assert.That(arbiter.MissionOf("Multi0"), Is.EqualTo("OP-1"));
			Assert.That(arbiter.RoleOf("Multi0"), Is.EqualTo("main"));
			Assert.That(arbiter.Commitments.Count, Is.EqualTo(1));
		}

		[TestCase(TestName = "Re-assigning the same mission and force is a no-op.")]
		public void AssignIsIdempotent()
		{
			var arbiter = new CoalitionOrderArbiter();
			arbiter.Assign("OP-1", "main", ArbiterPriority.ActiveCombat, "Multi0");

			Assert.That(arbiter.Assign("OP-1", "main", ArbiterPriority.ActiveCombat, "Multi0"), Is.Empty);
			Assert.That(arbiter.Commitments.Count, Is.EqualTo(1));
		}

		[TestCase(TestName = "A conflicting assignment of equal or lower priority is rejected with a reason.")]
		public void ConflictRejected()
		{
			var arbiter = new CoalitionOrderArbiter();
			arbiter.Assign("OP-1", "main", ArbiterPriority.ActiveCombat, "Multi0");

			var rejections = arbiter.Assign("OP-2", "feint", ArbiterPriority.ActiveCombat, "Multi0").ToArray();
			Assert.That(rejections, Has.Length.EqualTo(1));
			Assert.That(rejections[0], Does.Contain("REJECTED_CONFLICT"));
			Assert.That(rejections[0], Does.Contain("\"Multi0\""));

			// The original commitment survives.
			Assert.That(arbiter.MissionOf("Multi0"), Is.EqualTo("OP-1"));
		}

		[TestCase(TestName = "A higher-priority assignment supersedes a lower-priority one.")]
		public void SupersedeLowerPriority()
		{
			var arbiter = new CoalitionOrderArbiter();
			arbiter.Assign("OP-1", "feint", ArbiterPriority.Recon, "Multi0");

			Assert.That(arbiter.Assign("OP-2", "special", ArbiterPriority.SpecialMission, "Multi0"), Is.Empty);
			Assert.That(arbiter.MissionOf("Multi0"), Is.EqualTo("OP-2"));
		}

		[TestCase(TestName = "Releasing a mission frees every force it still holds.")]
		public void ReleaseMission()
		{
			var arbiter = new CoalitionOrderArbiter();
			arbiter.Assign("OP-1", "main", ArbiterPriority.ActiveCombat, "Multi0");
			arbiter.Assign("OP-1", "escort", ArbiterPriority.ActiveCombat, "Multi1");

			arbiter.ReleaseMission("OP-1");

			Assert.That(arbiter.MissionOf("Multi0"), Is.Null);
			Assert.That(arbiter.MissionOf("Multi1"), Is.Null);
		}

		[TestCase(TestName = "Releasing a specific force frees it for reassignment.")]
		public void ReleaseForce()
		{
			var arbiter = new CoalitionOrderArbiter();
			arbiter.Assign("OP-1", "main", ArbiterPriority.ActiveCombat, "Multi0");
			arbiter.ReleaseForce("Multi0");

			Assert.That(arbiter.MissionOf("Multi0"), Is.Null);
			Assert.That(arbiter.Assign("OP-2", "main", ArbiterPriority.ActiveCombat, "Multi0"), Is.Empty);
		}

		[TestCase(TestName = "Priority levels order emergency survival above active combat above staging.")]
		public void PriorityOrdering()
		{
			// The enum is the contract: an emergency (survival) must outrank a combat mission, which
			// outranks routine defense and staging. Kept strictly increasing so a higher value always wins.
			Assert.That(ArbiterPriority.Survival, Is.GreaterThan(ArbiterPriority.SpecialMission));
			Assert.That(ArbiterPriority.SpecialMission, Is.GreaterThan(ArbiterPriority.ActiveCombat));
			Assert.That(ArbiterPriority.ActiveCombat, Is.GreaterThan(ArbiterPriority.Defense));
			Assert.That(ArbiterPriority.Defense, Is.GreaterThan(ArbiterPriority.Reserve));
			Assert.That(ArbiterPriority.Reserve, Is.GreaterThan(ArbiterPriority.Recon));
			Assert.That(ArbiterPriority.Recon, Is.GreaterThan(ArbiterPriority.Staging));
			Assert.That(ArbiterPriority.Staging, Is.GreaterThan(ArbiterPriority.Idle));
		}

		[TestCase(TestName = "An emergency survival commitment overrides active combat.")]
		public void SurvivalOverridesCombat()
		{
			var arbiter = new CoalitionOrderArbiter();
			arbiter.Assign("OP-1", "main", ArbiterPriority.ActiveCombat, "Multi0");

			Assert.That(arbiter.Assign("RETREAT-1", "withdraw", ArbiterPriority.Survival, "Multi0"), Is.Empty,
				"An emergency withdrawal must override a combat commitment.");
			Assert.That(arbiter.MissionOf("Multi0"), Is.EqualTo("RETREAT-1"));
		}

		[TestCase(TestName = "ForcesOf lists every force still committed to a mission.")]
		public void ForcesOfListsCommittedForces()
		{
			var arbiter = new CoalitionOrderArbiter();
			arbiter.Assign("OP-1", "main", ArbiterPriority.ActiveCombat, "Multi0");
			arbiter.Assign("OP-1", "escort", ArbiterPriority.ActiveCombat, "Multi1");

			Assert.That(arbiter.ForcesOf("OP-1").ToArray(), Is.EquivalentTo(new[] { "Multi0", "Multi1" }));
		}

		[TestCase(TestName = "A released force no longer appears in ForcesOf.")]
		public void ForcesOfExcludesReleasedForces()
		{
			var arbiter = new CoalitionOrderArbiter();
			arbiter.Assign("OP-1", "main", ArbiterPriority.ActiveCombat, "Multi0");
			arbiter.Assign("OP-1", "escort", ArbiterPriority.ActiveCombat, "Multi1");
			arbiter.ReleaseForce("Multi0");

			Assert.That(arbiter.ForcesOf("OP-1").ToArray(), Is.EqualTo(new[] { "Multi1" }));
		}

		[TestCase(TestName = "Unknown force and mission references resolve to null/empty.")]
		public void UnknownReferencesResolveEmpty()
		{
			var arbiter = new CoalitionOrderArbiter();
			Assert.That(arbiter.MissionOf("Ghost"), Is.Null);
			Assert.That(arbiter.RoleOf("Ghost"), Is.Null);
			Assert.That(arbiter.ForcesOf("OP-9"), Is.Empty);
		}

		[TestCase(TestName = "An LLM special-mission assignment supersedes a routine recon commitment.")]
		public void LlmSpecialMissionSupersedesRecon()
		{
			var arbiter = new CoalitionOrderArbiter();
			arbiter.Assign("OP-1", "recon", ArbiterPriority.Recon, "Multi0");

			// assign_force at SpecialMission (as ApplyLlmForceDirectives commits it) wins.
			Assert.That(arbiter.Assign("OP-2", "attack", ArbiterPriority.SpecialMission, "Multi0"), Is.Empty);
			Assert.That(arbiter.MissionOf("Multi0"), Is.EqualTo("OP-2"));
		}

		[TestCase(TestName = "An emergency survival commitment is not overridden by an LLM special mission.")]
		public void SurvivalNotOverriddenBySpecialMission()
		{
			var arbiter = new CoalitionOrderArbiter();
			arbiter.Assign("RETREAT-1", "withdraw", ArbiterPriority.Survival, "Multi0");

			var rejections = arbiter.Assign("OP-2", "attack", ArbiterPriority.SpecialMission, "Multi0").ToArray();
			Assert.That(rejections, Has.Length.EqualTo(1));
			Assert.That(rejections[0], Does.Contain("REJECTED_CONFLICT"));
			Assert.That(arbiter.MissionOf("Multi0"), Is.EqualTo("RETREAT-1"));
		}
	}
}
