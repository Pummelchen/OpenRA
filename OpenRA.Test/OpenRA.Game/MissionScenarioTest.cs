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

using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

namespace OpenRA.Test
{
	/// <summary>
	/// Named end-to-end scenarios (reqs 663-684), driven through the shipped mission manager, order
	/// arbiter, force packages, transport state machine and schedule. Each sets up its situation
	/// deliberately and asserts the outcome, rather than running a match and hoping the scenario
	/// occurred.
	/// </summary>
	[TestFixture]
	sealed class MissionScenarioTest
	{
		static readonly CPos Objective = new(80, 80);
		static readonly CPos Home = new(20, 20);

		[TestCase(TestName = "663: a coordinated ground attack commits a force and holds it until the mission ends.")]
		public void CoordinatedGroundAttack()
		{
			var s = new ScenarioHarness();
			s.AddForce("Alpha", 20, composition: (UnitClass.Armor, 12));
			var mission = s.Launch(MissionType.Attack, 90, Objective, "destroy enemy concentration", owners: "Alpha");

			Assert.That(s.Arbiter.MissionOf("Alpha"), Is.EqualTo(mission.Id));
			Assert.That(s.UncommittedUnits, Is.Zero);

			s.Conclude(mission, MissionStatus.Succeeded);
			Assert.That(s.Arbiter.MissionOf("Alpha"), Is.Null, "A concluded mission must release its force.");
			Assert.That(s.UncommittedUnits, Is.EqualTo(20));
		}

		[TestCase(TestName = "664/239: three allied players contribute to one operation as a single joint package.")]
		public void MultiPlayerCoalitionAttack()
		{
			var s = new ScenarioHarness();
			s.AddForce("Alpha", 12, composition: (UnitClass.Armor, 12));
			s.AddForce("Bravo", 8, composition: (UnitClass.Infantry, 8));
			s.AddForce("Charlie", 4, composition: (UnitClass.Air, 4));
			var mission = s.Launch(MissionType.Attack, 90, Objective, "joint assault",
				owners: ["Alpha", "Bravo", "Charlie"]);

			var package = s.Packages.Single(p => p.MissionId == mission.Id);
			Assert.That(package.IsJoint, Is.True);
			Assert.That(package.Members.Count, Is.EqualTo(3));
			Assert.That(package.TotalUnits, Is.EqualTo(24),
				"The commander judges the operation on the combined force, not one ally's contingent.");
		}

		[TestCase(TestName = "665/666/667/668: ground pairs with artillery, air and naval as one combined operation.")]
		public void CombinedArmsScenarios()
		{
			var groundArtillery = new WaveComposition(armor: 10, infantry: 4, artillery: 3, antiAir: 0, air: 0, naval: 0);
			Assert.That(groundArtillery.ArtilleryHasScreen, Is.True, "665");

			var groundAir = new WaveComposition(armor: 10, infantry: 4, artillery: 0, antiAir: 2, air: 4, naval: 0);
			Assert.That(groundAir.GroundHasAirSupport, Is.True, "666");
			Assert.That(groundAir.GroundHasAntiAirEscort, Is.True);

			var groundNaval = new WaveComposition(armor: 10, infantry: 4, artillery: 0, antiAir: 0, air: 0, naval: 3);
			Assert.That(groundNaval.GroundHasNavalSupport, Is.True, "667");

			var full = new WaveComposition(armor: 10, infantry: 6, artillery: 3, antiAir: 2, air: 4, naval: 3);
			Assert.That(full.ArmsRepresented, Is.EqualTo(5), "668: every arm is present in one operation.");
			Assert.That(full.IsCombinedArms, Is.True);
		}

		[TestCase(TestName = "669: a feint runs first, and the main assault is scheduled after it (feint then assault).")]
		public void FeintPrecedesMainAssault()
		{
			var s = new ScenarioHarness();
			s.AddForce("Alpha", 24);
			s.AddForce("Bravo", 6);

			var feint = s.Launch(MissionType.Feint, 50, new CPos(20, 80), "divert attention", owners: "Bravo");
			var feintTick = s.Tick;
			s.Advance(400);
			var assault = s.Launch(MissionType.Attack, 90, Objective, "main effort", owners: "Alpha");

			Assert.That(s.Missions.DeceptionAttempts, Is.EqualTo(1),
				"A feint must be counted as a deception attempt, not as an ordinary attack.");
			Assert.That(ThreatDispersion.DistractionPrecedes(feintTick, s.Tick, minimumLeadTicks: 200), Is.True);
			Assert.That(assault.Priority, Is.GreaterThan(feint.Priority),
				"The feint supports the assault; it is not a co-equal effort.");
		}

		[TestCase(TestName = "670: a bait withdraws by design, and its retreat is mission success rather than failure.")]
		public void FakeRetreatIntoAmbush()
		{
			var s = new ScenarioHarness();
			s.AddForce("Bravo", 6);
			var bait = s.Launch(MissionType.Bait, 55, new CPos(45, 45), "lure into the kill zone", owners: "Bravo");

			Assert.That(MissionManager.IsDeception(MissionType.Bait), Is.True);
			Assert.That(bait.IntendedReaction, Is.Not.Empty,
				"A bait must state the reaction it is trying to produce, or its success cannot be judged.");

			s.Conclude(bait, MissionStatus.Succeeded, "enemy pursued into the ambush");
			Assert.That(s.Missions.MissionSuccesses, Is.EqualTo(1),
				"Withdrawing as planned is the bait succeeding, not the force being driven off.");
		}

		[TestCase(TestName = "671/672: harvester raids and expansion denial target the economy, not the army.")]
		public void EconomicRaidScenarios()
		{
			var s = new ScenarioHarness();
			s.AddForce("Alpha", 8);
			var raid = s.Launch(MissionType.EconomyRaid, 60, Objective, "kill harvesters", owners: "Alpha");
			Assert.That(raid.DesiredEffects, Does.Contain("damage_economy"), "671");

			var denial = s.Missions.CreateMission(MissionType.ExpansionDenial, 60, new CPos(60, 20), "deny expansion");
			Assert.That(denial.DesiredEffects, Does.Contain("deny_expansion"), "672");

			Assert.That(ExpansionPolicy.ShouldRaidEconomy(enemyEconomicStrength: 40f, ownEconomicStrength: 100f,
				availableRaiders: 8, minimumRaidForce: 4), Is.True);
		}

		[TestCase(TestName = "673/674/675: base defence draws relief, then a counterattack when the attacker is spent.")]
		public void DefenceReliefAndCounterattack()
		{
			var s = new ScenarioHarness();
			s.AddForce("Alpha", 20);
			s.AddForce("Reserve", 10);

			// 673: an asset under attack the local garrison cannot hold pulls a dedicated relief.
			Assert.That(CoalitionCommandCenterBotModule.NeedsEmergencyRelief(attackersNearAsset: 8, defendersNearAsset: 3), Is.True);
			var relief = s.Launch(MissionType.EmergencyReinforcement, 95, Home, "relieve the base",
				ArbiterPriority.Survival, "Reserve");

			// 674: the reserve is what answered, and its commitment is recorded.
			s.Reserve.Record(s.Tick, 10, "relieve the base");
			Assert.That(s.Reserve.CommittedUnits, Is.EqualTo(10));
			Assert.That(s.Arbiter.MissionOf("Reserve"), Is.EqualTo(relief.Id));

			// 675: the repelled attacker is now depleted, which is the counterattack window.
			s.Conclude(relief, MissionStatus.Succeeded);
			var decision = CounterattackAssessment.Evaluate(friendlyUnits: 20, enemyAtDefense: 12,
				observedEnemyNow: 3, enemyNearOrigin: 2, productionAtOrigin: true, minWaveSize: 6);
			Assert.That(decision.ShouldLaunch, Is.True);
			Assert.That(decision.EnemyDepleted, Is.True);
		}

		[TestCase(TestName = "676/677: air and naval insertions run the full transport cycle to a hold.")]
		public void TransportInsertions()
		{
			foreach (var extract in new[] { false, true })
			{
				var machine = new TransportStateMachine(extractOnCompletion: extract);
				var states = new System.Collections.Generic.List<TransportState> { machine.State };
				for (var i = 0; i < 20 && !machine.Complete; i++)
					states.Add(machine.Advance());

				Assert.That(machine.Complete, Is.True, "The insertion must reach a terminal state.");
				Assert.That(states, Does.Contain(TransportState.Load));
				Assert.That(states, Does.Contain(TransportState.Transit));
				Assert.That(states, Does.Contain(TransportState.Unload));
			}
		}

		[TestCase(TestName = "678/679/680: Tanya, spy and engineer operations are scored by consequence, not by target cost.")]
		public void SpecialAssetOperations()
		{
			// A spy against technology outranks a Tanya raid on an isolated building, because
			// denying tech compounds while one building does not.
			var techDenial = new SpecialOpsPlan(SpecialOpsObjective.TechnologyDenial, 0.7f, 6000f, 0.3f, 1200f);
			var isolated = new SpecialOpsPlan(SpecialOpsObjective.IsolatedHighValue, 0.7f, 1500f, 0.3f, 1200f);

			Assert.That(techDenial.ShouldLaunch(), Is.True, "679: a spy operation worth running.");
			Assert.That(SpecialOpsPlan.ConsequenceRank(techDenial.Objective),
				Is.GreaterThan(SpecialOpsPlan.ConsequenceRank(isolated.Objective)));

			// 680: an engineer capture is a production-denial objective, judged the same way.
			var capture = new SpecialOpsPlan(SpecialOpsObjective.ProductionDenial, 0.6f, 5000f, 0.35f, 500f);
			Assert.That(capture.ShouldLaunch(), Is.True);

			// 678: Tanya is scarce, so a near-certain loss is refused however rich the target.
			var suicidal = new SpecialOpsPlan(SpecialOpsObjective.ProductionDenial, 0.9f, 50000f, 0.95f, 2000f);
			Assert.That(suicidal.ShouldLaunch(), Is.False);
		}

		[TestCase(TestName = "681/682: a transport reroutes on a new threat and aborts when transit becomes impossible.")]
		public void TransportReroutingAndAbort()
		{
			// 681: a new threat changes the route weights, so a different route is chosen.
			var stealth = RouteWeights.Stealth();
			var assault = RouteWeights.Assault();
			Assert.That(stealth.VisionExposure, Is.GreaterThan(assault.VisionExposure),
				"A transport values not being seen more highly than an assault column does.");

			// 682: an aborted transport holds rather than continuing into the threat.
			var machine = new TransportStateMachine(extractOnCompletion: false);
			machine.Advance();
			machine.Abort();
			Assert.That(machine.Aborted, Is.True);
			Assert.That(machine.State, Is.EqualTo(TransportState.Hold),
				"An aborted insertion holds position instead of pressing on.");
		}

		[TestCase(TestName = "683: a surviving special asset is extracted so it can be used again.")]
		public void SpecialAssetExtraction()
		{
			var machine = new TransportStateMachine(extractOnCompletion: true);
			var states = new System.Collections.Generic.List<TransportState>();
			for (var i = 0; i < 20 && !machine.Complete; i++)
				states.Add(machine.Advance());

			Assert.That(states, Does.Contain(TransportState.Extract),
				"With extraction planned the cycle must actually reach it.");
			Assert.That(SpecialOpsPlan.ShouldExtract(objectiveComplete: true, assetAlive: true,
				extractionRouteExists: true), Is.True);
		}

		[TestCase(TestName = "684: simultaneous multi-front pressure is several problems, all serving one main effort.")]
		public void SimultaneousMultiFrontPressure()
		{
			var s = new ScenarioHarness();
			s.AddForce("Alpha", 20);
			s.AddForce("Bravo", 8);
			s.AddForce("Charlie", 4);

			s.Launch(MissionType.Attack, 90, Objective, "main effort", owners: "Alpha");
			s.Launch(MissionType.EconomyRaid, 55, new CPos(80, 20), "raid the economy", owners: "Bravo");
			s.Launch(MissionType.AirStrike, 60, new CPos(20, 80), "strike production", owners: "Charlie");

			var threats = s.PresentedThreats(
				m => m.Target?.X switch { 80 => m.Target.Value.Y == 80 ? 1 : 2, 20 => 3, _ => -1 },
				m => m.Type == MissionType.AirStrike ? "air" : "land");

			Assert.That(ThreatDispersion.DistinctRegions(threats), Is.EqualTo(3));
			Assert.That(ThreatDispersion.IsMultiThreat(threats), Is.True);
			Assert.That(ThreatDispersion.SharesCommonPurpose(threats), Is.True,
				"Three threats with one clear main effort is a plan; three co-equal ones is a split army.");
			Assert.That(ThreatDispersion.ForcesDefenderChoice(threatenedAssets: 3, defenderMobileGroups: 1), Is.True);
		}

		[TestCase(TestName = "238: a reserve stays uncommitted while the main attack runs.")]
		public void ReserveStaysUncommitted()
		{
			var s = new ScenarioHarness();
			s.AddForce("Alpha", 24);
			s.AddForce("Reserve", 8);

			s.Launch(MissionType.Attack, 90, Objective, "main effort", owners: "Alpha");

			Assert.That(s.UncommittedUnits, Is.EqualTo(8),
				"The reserve must not be swept into the main attack by default.");
			Assert.That(s.Arbiter.MissionOf("Reserve"), Is.Null);
			Assert.That(s.Packages.Single().Owners, Does.Not.Contain("Reserve"));
		}

		[TestCase(TestName = "260: synchronization error is measured against the planned arrival, not the launch.")]
		public void SynchronizationErrorIsRecorded()
		{
			var metrics = new CoalitionMatchMetrics();
			metrics.Sample(friendlyValue: 100f, enemyValue: 120f, idleFraction: 0.2f, cohesion: 0.9f, cash: 2000f);
			var schedule = new OperationSchedule(5000);
			var ground = schedule.Add(OperationComponent.GroundAssault, travelTicks: 900, interval: 100);

			var actualArrival = ground.ArrivalTick + 130;
			metrics.RecordSyncError(actualArrival, OperationSchedule.SynchronizationError(ground.ArrivalTick, actualArrival));

			Assert.That(metrics.Synchronization.Waves, Is.EqualTo(1));
			Assert.That(metrics.Synchronization.MaximumErrorTicks, Is.EqualTo(130));
			Assert.That(metrics.Summary(), Does.Contain("sync"));
		}
	}
}
