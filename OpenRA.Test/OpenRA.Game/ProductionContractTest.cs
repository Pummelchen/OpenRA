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
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;
using OpenRA.Primitives;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class ProductionContractTest
	{
		static readonly string[] AntiAir = new[] { "mig", "v2rl" };
		static readonly string[] AntiArmor = new[] { "4tnk", "ttnk" };
		static readonly string[] AntiInfantry = new[] { "ftrk", "jeep" };
		static readonly string[] Naval = new[] { "dd", "ca" };

		static (CoalitionCapability Capability, string[] CounterUnits)[] Contracts()
		{
			return new (CoalitionCapability Capability, string[] CounterUnits)[]
			{
				(CoalitionCapability.AntiAir, AntiAir),
				(CoalitionCapability.GroundAntiArmor, AntiArmor),
				(CoalitionCapability.GroundAntiInfantry, AntiInfantry),
				(CoalitionCapability.Naval, Naval),
				(CoalitionCapability.Submarine, Naval)
			};
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

			Assert.That(ProductionContract.IsSatisfied("transport", new[] { ally }), Is.True);
			Assert.That(ProductionContract.IsSatisfied("special_operations", new[] { ally }), Is.True);
			Assert.That(ProductionContract.IsSatisfied("air_superiority", new[] { ally }), Is.False);
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
	}
}
