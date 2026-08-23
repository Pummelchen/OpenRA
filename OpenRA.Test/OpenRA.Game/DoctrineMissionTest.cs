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

using System;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

namespace OpenRA.Test
{
	/// <summary>
	/// Covers the three doctrinal mission types added to close audit requirements 187, 202 and 204:
	/// Exploitation (follow-on force through a breach), EmergencyReinforcement (relief of a
	/// threatened asset) and Interception (cutting off a force in transit).
	/// </summary>
	[TestFixture]
	sealed class DoctrineMissionTest
	{
		[TestCase(TestName = "Exploitation, emergency reinforcement and interception are first-class mission types.")]
		public void DoctrineTypesExist()
		{
			var types = Enum.GetNames<MissionType>();
			Assert.That(types, Does.Contain(nameof(MissionType.Exploitation)));
			Assert.That(types, Does.Contain(nameof(MissionType.EmergencyReinforcement)));
			Assert.That(types, Does.Contain(nameof(MissionType.Interception)));
		}

		[TestCase(TestName = "The validator accepts the three doctrine types by name.")]
		public void ValidatorKnowsDoctrineTypes()
		{
			foreach (var name in new[] { "exploitation", "emergencyreinforcement", "interception" })
				Assert.That(CommandValidator.KnownMissionTypes, Does.Contain(name));
		}

		[TestCase(TestName = "Every mission type is known to the validator, so none is unreachable by the commander.")]
		public void EveryMissionTypeIsAddressable()
		{
			foreach (var type in Enum.GetValues<MissionType>())
				Assert.That(CommandValidator.KnownMissionTypes, Does.Contain(type.ToString().ToLowerInvariant()),
					$"MissionType.{type} cannot be requested: the validator would reject it as unknown.");
		}

		[TestCase(TestName = "Exploitation is offensive; relief and interception are defensive.")]
		public void DoctrineFamilies()
		{
			Assert.That(MissionManager.IsOffensive(MissionType.Exploitation), Is.True);
			Assert.That(MissionManager.IsDefensive(MissionType.EmergencyReinforcement), Is.True);
			Assert.That(MissionManager.IsDefensive(MissionType.Interception), Is.True);
			Assert.That(MissionManager.IsOffensive(MissionType.EmergencyReinforcement), Is.False);
		}

		[TestCase(TestName = "Exploitation starts in the exploitation phase; time-critical types skip recon and staging.")]
		public void DoctrineInitialPhases()
		{
			var manager = new MissionManager();
			var exploit = manager.CreateMission(MissionType.Exploitation, 88, new CPos(20, 20), "push through");
			var relief = manager.CreateMission(MissionType.EmergencyReinforcement, 95, new CPos(5, 5), "relieve");
			var intercept = manager.CreateMission(MissionType.Interception, 86, new CPos(9, 9), "cut off");

			Assert.That(exploit.Phase, Is.EqualTo(MissionPhase.Exploitation),
				"The breach already exists, so the follow-on force does not re-run recon or staging.");
			Assert.That(relief.Phase, Is.EqualTo(MissionPhase.Breach));
			Assert.That(intercept.Phase, Is.EqualTo(MissionPhase.Breach));
		}

		[TestCase(TestName = "Doctrine missions carry objectives, launch conditions and contingencies.")]
		public void DoctrineMissionsAreFullySpecified()
		{
			var manager = new MissionManager();
			var doctrineTypes = new[]
			{
				MissionType.Exploitation, MissionType.EmergencyReinforcement, MissionType.Interception
			};

			foreach (var type in doctrineTypes)
			{
				var mission = manager.CreateMission(type, 80, new CPos(12, 12), "objective");
				Assert.That(mission.DesiredEffects, Is.Not.Empty, $"{type} has no desired effect.");
				Assert.That(mission.LaunchConditions, Is.Not.Empty, $"{type} has no launch condition.");
				Assert.That(mission.Contingencies, Is.Not.Empty, $"{type} has no contingency.");
			}
		}

		[TestCase(TestName = "Relief is requested only when the attackers outnumber the defenders already there.")]
		public void EmergencyReliefThreshold()
		{
			// Proportional response: a raid the local garrison can already handle must not pull a
			// dedicated relief mission off the main effort (req 211/212 interaction).
			Assert.That(CoalitionCommandCenterBotModule.NeedsEmergencyRelief(5, 2), Is.True);
			Assert.That(CoalitionCommandCenterBotModule.NeedsEmergencyRelief(2, 5), Is.False);
			Assert.That(CoalitionCommandCenterBotModule.NeedsEmergencyRelief(3, 3), Is.False,
				"Parity is not distress.");
			Assert.That(CoalitionCommandCenterBotModule.NeedsEmergencyRelief(0, 0), Is.False,
				"No attackers means no relief mission.");
		}

		[TestCase(TestName = "An interception point sits between the contact and the asset, not on the enemy.")]
		public void InterceptionGeometry()
		{
			var contact = new CPos(40, 40);
			var home = new CPos(10, 10);
			var intercept = CoalitionCommandCenterBotModule.InterceptionCell(contact, home);

			Assert.That(intercept, Is.EqualTo(new CPos(25, 25)));
			Assert.That((intercept - contact).LengthSquared, Is.LessThan((home - contact).LengthSquared),
				"The interception point must be closer to the contact than the base is.");
			Assert.That((intercept - home).LengthSquared, Is.GreaterThan(0),
				"Intercepting at the base is base defense, not interception.");
		}

		[TestCase(TestName = "Controller replan requests are debounced so one blocked mission cannot re-plan every tick.")]
		public void ReplanRequestsAreDebounced()
		{
			// TacticalController.Unable(reason, requestReplan: true) fires from eight sites and can
			// repeat every tick while a mission stays blocked. Without this rule the coalition would
			// re-plan continuously instead of once per blackboard interval (reqs 548, 549).
			const int Interval = 40;

			Assert.That(CoalitionCommandCenterBotModule.MayReplan(100, int.MinValue, Interval), Is.True,
				"The first inability must always reach the commander.");
			Assert.That(CoalitionCommandCenterBotModule.MayReplan(101, 100, Interval), Is.False,
				"The same inability one tick later must not re-plan again.");
			Assert.That(CoalitionCommandCenterBotModule.MayReplan(139, 100, Interval), Is.False);
			Assert.That(CoalitionCommandCenterBotModule.MayReplan(140, 100, Interval), Is.True,
				"Once the interval has elapsed a still-blocked mission may re-plan.");

			// A zero or negative interval must not turn into a divide-by-zero or an always-open gate
			// that reintroduces per-tick replanning.
			Assert.That(CoalitionCommandCenterBotModule.MayReplan(101, 100, 0), Is.True);
			Assert.That(CoalitionCommandCenterBotModule.MayReplan(100, 100, 0), Is.False);
		}

		[TestCase(TestName = "Doctrine missions map to a defense kind the executor can act on.")]
		public void DoctrineDirectives()
		{
			var manager = new MissionManager();
			manager.CreateMission(MissionType.EmergencyReinforcement, 95, new CPos(5, 5), "relieve");
			var json = manager.BuildDirectiveJson(null, null, false);
			Assert.That(json, Does.Contain("\"defenseKind\":\"relief\""),
				"The relief mission must reach the executor as its own defense kind.");

			var interceptManager = new MissionManager();
			interceptManager.CreateMission(MissionType.Interception, 86, new CPos(9, 9), "cut off");
			Assert.That(interceptManager.BuildDirectiveJson(null, null, false), Does.Contain("\"defenseKind\":\"intercept\""));
		}
	}
}
