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

using System;
using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;
using OpenRA.Primitives;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class ProductionContractTest
	{
		static readonly string[] AntiAir = ["mig", "v2rl"];
		static readonly string[] AntiArmor = ["4tnk", "ttnk"];
		static readonly string[] AntiInfantry = ["ftrk", "jeep"];
		static readonly string[] Naval = ["dd", "ca"];

		static (CoalitionCapability Capability, string[] CounterUnits)[] Contracts()
		{
			return
			[
				(CoalitionCapability.AntiAir, AntiAir),
				(CoalitionCapability.GroundAntiArmor, AntiArmor),
				(CoalitionCapability.GroundAntiInfantry, AntiInfantry),
				(CoalitionCapability.Naval, Naval),
				(CoalitionCapability.Submarine, Naval)
			];
		}

		static float[] Profile(params (CoalitionCapability Capability, float Threat)[] threats)
		{
			var profile = new float[Enum.GetValues<CoalitionCapability>().Length];
			foreach (var (capability, threat) in threats)
				profile[(int)capability] = threat;

			return profile;
		}

		[TestCase(TestName = "Aggregation takes the strongest threat per capability across regions.")]
		public void AggregateAcrossRegions()
		{
			var regions = new[]
			{
				new CoalitionRegion(0, new Rectangle(0, 0, 10, 10)),
				new CoalitionRegion(1, new Rectangle(10, 0, 10, 10))
			};
			regions[0].Threats[(int)CoalitionCapability.AntiAir] = 0.4f;
			regions[0].Threats[(int)CoalitionCapability.GroundAntiArmor] = 0.9f;
			regions[1].Threats[(int)CoalitionCapability.AntiAir] = 0.7f;

			var profile = ProductionContract.Aggregate(regions);

			Assert.That(profile[(int)CoalitionCapability.AntiAir], Is.EqualTo(0.7f).Within(0.001f));
			Assert.That(profile[(int)CoalitionCapability.GroundAntiArmor], Is.EqualTo(0.9f).Within(0.001f));
			Assert.That(profile[(int)CoalitionCapability.GroundAntiInfantry], Is.EqualTo(0f));
		}

		[TestCase(TestName = "Contracts answer the strongest capability threat first.")]
		public void OrderingByThreat()
		{
			var profile = Profile(
				(CoalitionCapability.GroundAntiInfantry, 0.5f),
				(CoalitionCapability.GroundAntiArmor, 1f));

			var units = ProductionContract.Resolve(profile, Contracts(), _ => 0, hasBigWater: true);

			Assert.That(units, Is.EqualTo(new[] { "4tnk", "ttnk", "ftrk", "jeep" }));
		}

		[TestCase(TestName = "Sub-material threats are not contracted against.")]
		public void Threshold()
		{
			var profile = Profile((CoalitionCapability.AntiAir, 0.1f));

			Assert.That(ProductionContract.Resolve(profile, Contracts(), _ => 0, true), Is.Null);
		}

		[TestCase(TestName = "Naval contracts require an explored water body; submarine threat uses the naval counters.")]
		public void NavalGate()
		{
			var profile = Profile(
				(CoalitionCapability.Naval, 1f),
				(CoalitionCapability.Submarine, 0.9f));

			// No usable water: no naval production at all.
			Assert.That(ProductionContract.Resolve(profile, Contracts(), _ => 0, hasBigWater: false), Is.Null);

			// With water: both naval and submarine threat contract the naval counters once.
			var units = ProductionContract.Resolve(profile, Contracts(), _ => 0, hasBigWater: true);
			Assert.That(units, Is.EqualTo(new[] { "dd", "ca" }));
		}

		[TestCase(TestName = "A contract is skipped when the coalition already fields its counters.")]
		public void FieldedGap()
		{
			var profile = Profile((CoalitionCapability.AntiAir, 1f));
			var fielded = new Dictionary<string, int> { ["mig"] = 4 };

			Assert.That(ProductionContract.Resolve(profile, Contracts(), t => fielded.GetValueOrDefault(t), true), Is.Null);

			fielded["mig"] = 2;
			Assert.That(ProductionContract.Resolve(profile, Contracts(), t => fielded.GetValueOrDefault(t), true),
				Is.EqualTo(new[] { "mig", "v2rl" }));
		}

		[TestCase(TestName = "An empty profile yields no production contract.")]
		public void EmptyProfile()
		{
			Assert.That(ProductionContract.Resolve(new float[Enum.GetValues<CoalitionCapability>().Length], Contracts(), _ => 0, true), Is.Null);
		}

		[TestCase(TestName = "The capability weight scale promotes or suppresses material threats.")]
		public void WeightScale()
		{
			var subMaterial = Profile((CoalitionCapability.AntiAir, 0.1f));

			// Default scale keeps the threat sub-material.
			Assert.That(ProductionContract.Resolve(subMaterial, Contracts(), _ => 0, true), Is.Null);

			// A scale above 1 lifts it above the material threshold.
			Assert.That(ProductionContract.Resolve(subMaterial, Contracts(), _ => 0, true, 3f),
				Is.EqualTo(new[] { "mig", "v2rl" }));

			// A scale below 1 suppresses a fully-material threat.
			var material = Profile((CoalitionCapability.AntiAir, 1f));
			Assert.That(ProductionContract.Resolve(material, Contracts(), _ => 0, true, 0.1f), Is.Null);
		}

		[TestCase(TestName = "Operational missions create explicit non-counter capability requirements.")]
		public void OperationalRequirements()
		{
			var requirements = ProductionContract.DetermineRequirements(
				enemyLocationUnknown: true, enemyAirPresent: true, longGroundRoute: true,
				raidMission: true, transportMission: true, specialOperationsMission: true,
				navalMission: true, hasBigWater: true);

			Assert.That(requirements, Is.EqualTo(new[]
			{
				"recon", "mobility", "fast_raiding", "air_superiority", "transport",
				"special_operations", "naval"
			}));
			Assert.That(ProductionContract.DetermineRequirements(false, false, false, false, false, false, true, false),
				Is.Empty, "naval capability must not be requested without usable water");
		}

		[TestCase(TestName = "An allied capability satisfies the coalition requirement and prevents duplication.")]
		public void AlliedCapabilitySatisfiesRequirement()
		{
			var ally = new ForceGroup("Multi1");
			CoalitionForceRegistry.Record(FriendlyCapability.Transport, ally.Capabilities);
			CoalitionForceRegistry.Record(FriendlyCapability.SpecialOperations, ally.Capabilities);

			Assert.That(ProductionContract.IsSatisfied("transport", [ally]), Is.True);
			Assert.That(ProductionContract.IsSatisfied("special_operations", [ally]), Is.True);
			Assert.That(ProductionContract.IsSatisfied("air_superiority", [ally]), Is.False);
		}

		[TestCase(TestName = "Destroyed production infrastructure triggers the first valid emergency replacement.")]
		public void EmergencyReplacement()
		{
			var critical = new[] { "weap", "barr", "proc" };
			var existing = new HashSet<string> { "barr" };
			var queued = new HashSet<string> { "proc" };
			var buildable = new HashSet<string> { "weap", "barr", "proc" };

			Assert.That(ProductionContract.SelectEmergencyReplacement(true, critical,
				existing.Contains, queued.Contains, buildable.Contains), Is.EqualTo("weap"));
			existing.Add("weap");
			Assert.That(ProductionContract.SelectEmergencyReplacement(true, critical,
				existing.Contains, queued.Contains, buildable.Contains), Is.Null);
			Assert.That(ProductionContract.SelectEmergencyReplacement(false, critical,
				_ => false, _ => false, _ => true), Is.Null);
		}

		[TestCase(TestName = "Technology investment waits for a rush-safe field army.")]
		public void PrerequisiteInvestmentGate()
		{
			Assert.That(StrategicBrainBotModule.MayInvestInPrerequisite(9, 10), Is.False);
			Assert.That(StrategicBrainBotModule.MayInvestInPrerequisite(10, 10), Is.True);
			Assert.That(StrategicBrainBotModule.MayInvestInPrerequisite(0, 0), Is.True);
		}

		[TestCase(TestName = "Opening reconnaissance is bounded and stops after locating the enemy base.")]
		public void ScoutingGate()
		{
			Assert.That(StrategicBrainBotModule.ShouldScout(false, 0, 4), Is.True);
			Assert.That(StrategicBrainBotModule.ShouldScout(false, 4, 4), Is.False);
			Assert.That(StrategicBrainBotModule.ShouldScout(true, 0, 4), Is.False);
			Assert.That(StrategicBrainBotModule.ShouldScout(false, 0, 0), Is.False);
			Assert.That(StrategicBrainBotModule.ShouldScout(false, 0, 4, 4), Is.False,
				"Dead scouts must not reopen an unlimited deployment slot.");
			Assert.That(StrategicBrainBotModule.ScoutSeparationScore(new CPos(9, 0), [], new CPos(0, 0)), Is.EqualTo(81));
			Assert.That(StrategicBrainBotModule.ScoutSeparationScore(new CPos(9, 0),
				[new CPos(8, 0), new CPos(0, 0)], new CPos(4, 4)), Is.EqualTo(1));
		}

		[TestCase(TestName = "Reserve commitment requires a located base and broad reconnaissance.")]
		public void ReserveCommitmentGate()
		{
			Assert.That(StrategicBrainBotModule.MayCommitObservedAdvantage(false, 1f, 3, 10, 0.6f), Is.False);
			Assert.That(StrategicBrainBotModule.MayCommitObservedAdvantage(true, 0.69f, 3, 10, 0.6f), Is.False);
			Assert.That(StrategicBrainBotModule.MayCommitObservedAdvantage(true, 0.7f, 6, 10, 0.6f), Is.True);
			Assert.That(StrategicBrainBotModule.MayCommitObservedAdvantage(true, 0.7f, 7, 10, 0.6f), Is.False);
		}

		[TestCase(TestName = "Strategic production accepts standard harvester and MCV requests.")]
		public void StandardProductionRequestIntegration()
		{
			Assert.That(typeof(IBotRequestUnitProduction).IsAssignableFrom(typeof(StrategicBrainBotModule)), Is.True);
		}

		[TestCase(TestName = "Operational roles cannot override the shared coalition strategy.")]
		public void TeamRoleDoesNotInventAttack()
		{
			Assert.That(StrategicBrainBotModule.ResolveTeamStrategy(false, "main", "build"), Is.EqualTo("build"));
			Assert.That(StrategicBrainBotModule.ResolveTeamStrategy(false, "escort", "build"), Is.EqualTo("build"));
			Assert.That(StrategicBrainBotModule.ResolveTeamStrategy(false, "main", "attack"), Is.EqualTo("attack"));
			Assert.That(StrategicBrainBotModule.ResolveTeamStrategy(false, "defend", "attack"), Is.EqualTo("defend"));
			Assert.That(StrategicBrainBotModule.ResolveTeamStrategy(true, "main", "attack"), Is.EqualTo("defend"));
		}

		[TestCase(TestName = "Fair-fog field interception requires material observed contact and parity.")]
		public void ObservedForceInterceptionGate()
		{
			Assert.That(CoalitionCommandCenterBotModule.ShouldInterceptObservedForce(1, 1f, 20, 24), Is.True);
			Assert.That(CoalitionCommandCenterBotModule.ShouldInterceptObservedForce(0, 1f, 20, 24), Is.False);
			Assert.That(CoalitionCommandCenterBotModule.ShouldInterceptObservedForce(1, 0.2f, 20, 24), Is.False,
				"A lone scout is not a material field army.");
			Assert.That(CoalitionCommandCenterBotModule.ShouldInterceptObservedForce(1, 1.01f, 20, 24), Is.False);
			Assert.That(CoalitionCommandCenterBotModule.ShouldInterceptObservedForce(1, 0f, 20, 24), Is.False,
				"Unknown enemy strength must not be treated as a free advantage.");
		}

		[TestCase(100, 100, 20, 40, 60, 70)]
		[TestCase(20, 40, 20, 40, 20, 40)]
		public void FieldInterceptionConcentratesTowardHome(int cx, int cy, int hx, int hy,
			int expectedX, int expectedY)
		{
			Assert.That(CoalitionCommandCenterBotModule.InterceptionCell(new CPos(cx, cy), new CPos(hx, hy)),
				Is.EqualTo(new CPos(expectedX, expectedY)));
		}

		[TestCase(300, 0, 300, true)]
		[TestCase(300, 20, 300, false)]
		[TestCase(320, 20, 300, true)]
		[TestCase(20, 20, 0, false)]
		public void PersistentAttackPlansDebounceWaveOrders(int currentTick, int lastWaveTick, int interval, bool expected)
		{
			Assert.That(StrategicBrainBotModule.MayIssueWave(currentTick, lastWaveTick, interval), Is.EqualTo(expected));
		}

		[TestCase(19, 109, 109, 47, 25, 103)]
		[TestCase(19, 47, 109, 47, 25, 47)]
		[TestCase(19, 47, 109, 47, 19, 47, 0)]
		public void SpawnReconUsesUnoccupiedHomeFacingApproach(int sx, int sy, int hx, int hy,
			int expectedX, int expectedY, int offset = 6)
		{
			Assert.That(StrategicBrainBotModule.SpawnApproachCell(new CPos(sx, sy), new CPos(hx, hy), offset),
				Is.EqualTo(new CPos(expectedX, expectedY)));
		}
	}
}
