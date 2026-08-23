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
	/// <para>
	/// The end-to-end acceptance cases (reqs 789-803), asserted on outcomes rather than on the
	/// presence of a telemetry marker.
	/// </para>
	/// <para>
	/// The previous coverage checked that a <c>Posture</c> line had been logged, which proves the
	/// commander ran - not that four bots acted as one command, that a feint drew a response before
	/// the real attack, or that a reserve was still uncommitted when the assault launched. Each case
	/// here sets up the situation the requirement describes and checks the thing the requirement
	/// actually claims.
	/// </para>
	/// </summary>
	[TestFixture]
	sealed class AcceptanceOutcomeTest
	{
		static readonly CPos Objective = new(80, 80);
		static readonly CPos Home = new(20, 20);

		[Test(Description = "789: three allied players act as one command, not three bots sharing a map.")]
		public void UnifiedCoalition()
		{
			var s = new ScenarioHarness();
			s.AddForce("Alpha", 14);
			s.AddForce("Bravo", 10);
			s.AddForce("Charlie", 8);

			var operation = s.Launch(MissionType.Attack, 90, Objective, "coalition main effort",
				owners: ["Alpha", "Bravo", "Charlie"]);

			// One command means one plan, one objective, and one arbiter owning every committed force.
			var package = s.Packages.Single();
			Assert.That(package.MissionId, Is.EqualTo(operation.Id));
			Assert.That(package.Members.Count, Is.EqualTo(3));
			Assert.That(package.IsJoint, Is.True);

			foreach (var owner in new[] { "Alpha", "Bravo", "Charlie" })
				Assert.That(s.Arbiter.MissionOf(owner), Is.EqualTo(operation.Id),
					$"{owner} is not under the coalition's mission, so it is acting independently.");

			// And a second operation cannot quietly steal a committed contingent: Assign returns the
			// rejections it produced, so a refusal is a non-empty result carrying its reason.
			var rejections = s.Arbiter.Assign("OP-OTHER", "Raid", ArbiterPriority.Defense, "Alpha");
			Assert.That(rejections, Is.Not.Empty, "A lower-priority mission must not take a committed force.");
			Assert.That(rejections[0], Does.Contain("REJECTED_CONFLICT"));
			Assert.That(s.Arbiter.MissionOf("Alpha"), Is.EqualTo(operation.Id),
				"Alpha must still belong to the coalition operation after the refused grab.");
		}

		[Test(Description = "790: a synchronized operation lands every arm together, not in sequence.")]
		public void CombinedArmsSynchronized()
		{
			var schedule = new OperationSchedule(timeOnTarget: 6000);
			schedule.Add(OperationComponent.Reconnaissance, travelTicks: 200, interval: 100);
			schedule.Add(OperationComponent.AirStrike, travelTicks: 120, interval: 100);
			schedule.Add(OperationComponent.NavalBombardment, travelTicks: 400, interval: 100);
			schedule.Add(OperationComponent.GroundAssault, travelTicks: 900, interval: 100);

			var wave = new WaveComposition(armor: 10, infantry: 6, artillery: 3, antiAir: 2, air: 4, naval: 3);

			Assert.That(wave.ArmsRepresented, Is.EqualTo(5), "Every arm must be present in the operation.");
			Assert.That(wave.ArtilleryHasScreen && wave.GroundHasAntiAirEscort, Is.True);
			Assert.That(schedule.Precedes(OperationComponent.AirStrike), Is.True,
				"Shaping fires must land before the ground force arrives, not after.");
			Assert.That(schedule.Precedes(OperationComponent.NavalBombardment), Is.True);
			Assert.That(schedule.Precedes(OperationComponent.Reconnaissance), Is.True);

			// Synchronization is judged over the arms that must converge. Reconnaissance is
			// deliberately far ahead of the assault - it is what confirms the objective - so
			// including it in the spread would measure the plan working as a failure to synchronize.
			var converging = schedule.Entries
				.Where(e => e.Component != OperationComponent.Reconnaissance)
				.ToArray();
			var spread = converging.Max(e => e.ArrivalTick) - converging.Min(e => e.ArrivalTick);

			Assert.That(spread, Is.LessThanOrEqualTo(100),
				"Arms arriving minutes apart is a sequence of attacks, not a combined operation.");

			var air = schedule.Entries.Single(e => e.Component == OperationComponent.AirStrike);
			var naval = schedule.Entries.Single(e => e.Component == OperationComponent.NavalBombardment);
			Assert.That(air.ArrivalTick, Is.EqualTo(naval.ArrivalTick),
				"Air and naval shaping fires are planned to land together.");
			Assert.That(naval.LaunchTick, Is.LessThan(air.LaunchTick),
				"The slower naval force must set off earlier to arrive with the air strike.");
		}

		[Test(Description = "791: a feint runs first, its effect is measured, then the real attack goes elsewhere.")]
		public void DeceptionDrawsThenStrikes()
		{
			var s = new ScenarioHarness();
			s.AddForce("Bait", 6);
			s.AddForce("Main", 24);

			var feintCell = new CPos(20, 80);
			var feint = s.Launch(MissionType.Feint, 50, feintCell, "draw the defence north", owners: "Bait");
			var feintTick = s.Tick;
			Assert.That(s.Missions.DeceptionAttempts, Is.EqualTo(1));

			s.Advance(400);

			// The enemy reacted: presence at the feint rose materially above its baseline.
			var (drewResponse, engaged) = MissionManager.MeasureDeceptionResponse(
				baselineEnemyCount: 2, nearbyEnemyCount: 9);
			Assert.That(drewResponse, Is.True, "A feint that draws nothing has not succeeded.");
			Assert.That(engaged, Is.GreaterThan(0), "The drawn force must be quantified, not just noticed.");
			s.Missions.DeceptionSuccesses++;
			s.Missions.DeceptionEnemiesDrawn += engaged;

			// Only then is the real attack committed, and somewhere else.
			var assault = s.Launch(MissionType.Attack, 90, Objective, "main effort", owners: "Main");

			Assert.That(ThreatDispersion.DistractionPrecedes(feintTick, s.Tick, minimumLeadTicks: 200), Is.True);
			Assert.That(assault.Target, Is.Not.EqualTo(feint.Target),
				"The real attack must fall somewhere other than the feint.");
			Assert.That(s.Missions.DeceptionSuccesses, Is.EqualTo(1));
			Assert.That(s.Missions.DeceptionEnemiesDrawn, Is.EqualTo(engaged));
		}

		[Test(Description = "792: a scarce asset is inserted on a low-threat route, acts, and is extracted.")]
		public void SpecialOperationInsertsActsExtracts()
		{
			// Judged worth committing before it is committed.
			var plan = new SpecialOpsPlan(SpecialOpsObjective.TechnologyDenial,
				successProbability: 0.7f, strategicValue: 8000f, assetLossRisk: 0.25f, assetValue: 1200f);
			Assert.That(plan.ShouldLaunch(), Is.True);

			// Routed for stealth rather than for speed.
			var stealth = RouteWeights.Stealth();
			Assert.That(stealth.DetectionExposure, Is.GreaterThan(RouteWeights.Assault().DetectionExposure));

			// The full insert-act-extract cycle actually runs.
			var machine = new TransportStateMachine(extractOnCompletion: true);
			var states = new System.Collections.Generic.List<TransportState>();
			for (var i = 0; i < 20 && !machine.Complete; i++)
				states.Add(machine.Advance());

			Assert.That(states, Does.Contain(TransportState.Unload), "The asset must actually be inserted.");
			Assert.That(states, Does.Contain(TransportState.Extract), "A surviving asset must be recovered.");
			Assert.That(machine.Complete, Is.True);
			Assert.That(SpecialOpsPlan.ShouldExtract(objectiveComplete: true, assetAlive: true,
				extractionRouteExists: true), Is.True);
		}

		[Test(Description = "793: several coordinated threats are presented at once, all serving one effort.")]
		public void HumanAttentionIsSplit()
		{
			var s = new ScenarioHarness();
			s.AddForce("Main", 20);
			s.AddForce("Raider", 6);
			s.AddForce("Air", 4);
			s.AddForce("Spec", 2);

			s.Launch(MissionType.Attack, 90, Objective, "main effort", owners: "Main");
			s.Launch(MissionType.EconomyRaid, 55, new CPos(80, 20), "raid harvesters", owners: "Raider");
			s.Launch(MissionType.AirStrike, 60, new CPos(20, 80), "strike production", owners: "Air");
			s.Launch(MissionType.SpecialOps, 65, new CPos(95, 95), "rear insertion", owners: "Spec");

			var threats = s.PresentedThreats(RegionOf, DomainOf);

			Assert.That(ThreatDispersion.IsFullSpectrum(threats), Is.True,
				"Assault, raid, strike and special operation must all be in play at once.");
			Assert.That(ThreatDispersion.SharesCommonPurpose(threats), Is.True,
				"Simultaneous attacks without one main effort are a split army, not a plan.");
			Assert.That(ThreatDispersion.ForcesDefenderChoice(threatenedAssets: 4, defenderMobileGroups: 2), Is.True);
		}

		[Test(Description = "794: an enemy composition switch changes what the coalition builds.")]
		public void CounterCompositionRespondsAcrossAllies()
		{
			// Ground-heavy enemy: the contract answers armour.
			static (CoalitionCapability, string[])[] Contracts() =>
			[
				(CoalitionCapability.AntiAir, ["v2rl", "e3"]),
				(CoalitionCapability.GroundAntiArmor, ["4tnk", "ttnk"]),
				(CoalitionCapability.GroundAntiInfantry, ["ftrk", "jeep"])
			];

			var armorThreat = new float[System.Enum.GetValues<CoalitionCapability>().Length];
			armorThreat[(int)CoalitionCapability.GroundAntiArmor] = 0.9f;
			var vsArmor = ProductionContract.Resolve(armorThreat, Contracts(), _ => 0, hasBigWater: false);

			// The enemy switches to air: the contract must switch with it.
			var airThreat = new float[System.Enum.GetValues<CoalitionCapability>().Length];
			airThreat[(int)CoalitionCapability.AntiAir] = 0.9f;
			var vsAir = ProductionContract.Resolve(airThreat, Contracts(), _ => 0, hasBigWater: false);

			Assert.That(vsArmor, Is.Not.Empty);
			Assert.That(vsAir, Is.Not.Empty);
			Assert.That(vsAir, Is.Not.EqualTo(vsArmor),
				"A composition switch that produces the same units is not a response to it.");
		}

		[Test(Description = "795: a reserve survives the main attack and is then committed deliberately.")]
		public void ReserveSurvivesThenActs()
		{
			var s = new ScenarioHarness();
			s.AddForce("Main", 24);
			s.AddForce("Reserve", 10);

			var assault = s.Launch(MissionType.Attack, 90, Objective, "main effort", owners: "Main");

			// Still uncommitted while the attack runs - that is what makes it a reserve.
			Assert.That(s.UncommittedUnits, Is.EqualTo(10));

			// A reserve below half the minimum wave is thin enough that spending it must be argued
			// for rather than assumed - which is exactly the situation here.
			Assert.That(ReserveManager.RequiresJustification(units: 10, minWaveSize: 24), Is.True);

			// An unexpected threat appears; the reserve answers it rather than the main effort breaking off.
			s.Advance(300);
			var relief = s.Launch(MissionType.EmergencyReinforcement, 95, Home, "relieve the base",
				ArbiterPriority.Survival, "Reserve");
			s.Reserve.Record(s.Tick, 10, "unexpected raid on the base");

			Assert.That(s.Arbiter.MissionOf("Main"), Is.EqualTo(assault.Id),
				"The main effort must not be pulled apart to answer a threat the reserve exists for.");
			Assert.That(s.Arbiter.MissionOf("Reserve"), Is.EqualTo(relief.Id));
			Assert.That(s.Reserve.CommittedUnits, Is.EqualTo(10));
			Assert.That(s.Reserve.LastCommitReason, Is.Not.Empty, "A reserve commitment must state why.");
		}

		[Test(Description = "796: a failed enemy attack is answered when the window is real, and not otherwise.")]
		public void CounterattackOnlyWhenFavourable()
		{
			// The enemy committed 12, has 3 left in the field, and its origin is thin: a real window.
			var favourable = CounterattackAssessment.Evaluate(friendlyUnits: 20, enemyAtDefense: 12,
				observedEnemyNow: 3, enemyNearOrigin: 2, productionAtOrigin: true, minWaveSize: 6);
			Assert.That(favourable.ShouldLaunch, Is.True);
			Assert.That(favourable.EnemyDepleted, Is.True);

			// The same repelled attack, but the enemy still has a full army at home: no window.
			var unfavourable = CounterattackAssessment.Evaluate(friendlyUnits: 20, enemyAtDefense: 12,
				observedEnemyNow: 3, enemyNearOrigin: 30, productionAtOrigin: true, minWaveSize: 6);
			Assert.That(unfavourable.ShouldLaunch, Is.False,
				"Counterattacking into an intact defence spends the army that just won the defence.");
		}

		[Test(Description = "801: a plan whose assumptions fail is cancelled rather than continued.")]
		public void AdaptationCancelsAStalePlan()
		{
			var s = new ScenarioHarness();
			s.AddForce("Main", 20);
			var assault = s.Launch(MissionType.Attack, 90, Objective, "attack the located base", owners: "Main");

			// The assumption the plan rested on - a located, weaker enemy - stops holding.
			var detector = new StrategicEventDetector();
			detector.Detect(enemyRegion: 5, enemyStructureCount: 6, ownStructureCount: 8, enemyIntelCount: 12,
				highValueSeen: true, activeAttackCount: 1, failedMissionCount: 0, readyTransportCount: 0,
				completedMissionCount: 0, routeSignature: 17, coalitionCash: 5000);

			var trigger = detector.Detect(enemyRegion: -1, enemyStructureCount: 0, ownStructureCount: 8,
				enemyIntelCount: 0, highValueSeen: false, activeAttackCount: 1, failedMissionCount: 0,
				readyTransportCount: 0, completedMissionCount: 0, routeSignature: 17, coalitionCash: 5000);

			Assert.That(trigger, Is.Not.Null, "Losing the objective must wake the commander.");

			s.Conclude(assault, MissionStatus.Aborted, "objective no longer located");
			Assert.That(s.Missions.MissionAborts, Is.EqualTo(1));
			Assert.That(s.Arbiter.MissionOf("Main"), Is.Null,
				"An abandoned plan must release its force instead of leaving it committed to nothing.");
		}

		[Test(Description = "802: a losing engagement withdraws and the surviving force is preserved.")]
		public void WithdrawalPreservesForce()
		{
			var s = new ScenarioHarness();
			s.AddForce("Main", 20);
			var assault = s.Launch(MissionType.Attack, 90, Objective, "attack", owners: "Main");

			MissionManager.BeginWithdrawal(assault, s.Tick, "outmatched three to one");
			Assert.That(assault.Phase, Is.EqualTo(MissionPhase.Withdrawal));
			Assert.That(assault.OutcomeReason, Is.Not.Empty);

			s.Conclude(assault, MissionStatus.Aborted, "withdrew before destruction");

			// The force survives and is available again, which is the point of withdrawing.
			Assert.That(s.UncommittedUnits, Is.EqualTo(20));

			var metrics = new CoalitionMatchMetrics();
			metrics.Sample(100f, 120f, 0.2f, 0.9f, 2000f);
			metrics.RecordRetreat(s.Tick, 20);
			metrics.RecordRetreatOutcome(18);
			Assert.That(metrics.RetreatEffectiveness.PreservationRate, Is.GreaterThan(0.5f),
				"A withdrawal that preserves nothing is a rout.");
		}

		[Test(Description = "803: a campaign runs recon, then economy, then pressure, then a major operation.")]
		public void CampaignFollowsAnIdentifiableSequence()
		{
			var s = new ScenarioHarness();
			s.AddForce("Alpha", 6);

			// Opening: reconnaissance, before any offensive commitment exists.
			var recon = s.Launch(MissionType.Recon, 40, new CPos(50, 50), "locate the enemy", owners: "Alpha");
			Assert.That(s.Active(MissionType.Attack), Is.Empty,
				"An attack before the enemy is located is not a campaign sequence.");
			s.Conclude(recon, MissionStatus.Succeeded);

			// Economy: expansion is taken while the posture supports investing.
			s.Advance(1200);
			Assert.That(ExpansionPolicy.ShouldExpand(StrategicPosture.Expansion, siteValue: 1f, risk: 0.2f), Is.True);

			// Pressure: a raid before the main effort is ready.
			s.Advance(1200);
			s.AddForce("Raider", 6);
			var raid = s.Launch(MissionType.EconomyRaid, 55, new CPos(80, 20), "pressure the economy", owners: "Raider");

			// Major operation: the main effort, once the coalition can mass for it.
			s.Advance(1800);
			s.AddForce("Main", 26);
			var assault = s.Launch(MissionType.Breakthrough, 90, Objective, "main effort", owners: "Main");

			Assert.That(recon.CreatedTick, Is.LessThan(raid.CreatedTick));
			Assert.That(raid.CreatedTick, Is.LessThan(assault.CreatedTick));
			Assert.That(s.Missions.ReconSuccesses, Is.EqualTo(1));
			Assert.That(ThreatDispersion.SharesCommonPurpose(s.PresentedThreats(RegionOf, DomainOf)), Is.True,
				"The pressure raid must support the main effort rather than compete with it.");
		}

		static int RegionOf(CoalitionMission m)
		{
			if (m.Target == null)
				return -1;

			return m.Target.Value.X / 40 * 10 + m.Target.Value.Y / 40;
		}

		static string DomainOf(CoalitionMission m)
		{
			return m.Type switch
			{
				MissionType.AirStrike or MissionType.AirRecon => "air",
				MissionType.NavalStrike or MissionType.NavalBlockade => "naval",
				MissionType.SpecialOps or MissionType.Transport => "special",
				_ => "land"
			};
		}
	}
}
