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

using System.Collections.Frozen;
using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class ForceRegistryTest
	{
		static readonly FrozenSet<string> Artillery = new[] { "arty", "v2rl" }.ToFrozenSet();
		static readonly FrozenSet<string> Submarines = new[] { "ss", "msub" }.ToFrozenSet();
		static readonly FrozenSet<string> Detection = new[] { "dog", "rdr" }.ToFrozenSet();
		static readonly FrozenSet<string> Transports = new[] { "lst", "heli" }.ToFrozenSet();
		static readonly FrozenSet<string> Scouts = new[] { "e1" }.ToFrozenSet();
		static readonly FrozenSet<string> AntiAir = new[] { "v2rl", "e3", "mig" }.ToFrozenSet();
		static readonly FrozenSet<string> Special = new[] { "e7", "spy" }.ToFrozenSet();

		static IReadOnlyList<FriendlyCapability> CapabilitiesOf(UnitClass unitClass, string type)
		{
			return CoalitionForceRegistry.FriendlyCapabilitiesFor(unitClass, type,
				Artillery, Submarines, Detection, Transports, Scouts, AntiAir, Special);
		}

		[TestCase(TestName = "A fighter contributes air and anti-air capability.")]
		public void AirCapabilities()
		{
			Assert.That(CapabilitiesOf(UnitClass.Air, "mig"),
				Is.EqualTo(new[]
				{
					FriendlyCapability.Air, FriendlyCapability.AntiAir,
					FriendlyCapability.AirSuperiority, FriendlyCapability.Mobility,
					FriendlyCapability.BaseDefense
				}));
		}

		[TestCase(TestName = "Armor contributes anti-armor; infantry contributes anti-infantry.")]
		public void ClassCapabilities()
		{
			Assert.That(CapabilitiesOf(UnitClass.Armor, "4tnk"),
				Is.EqualTo(new[] { FriendlyCapability.AntiArmor, FriendlyCapability.Mobility }));
			Assert.That(CapabilitiesOf(UnitClass.Infantry, "e7"),
				Is.EqualTo(new[] { FriendlyCapability.AntiInfantry, FriendlyCapability.SpecialOperations }));
		}

		[TestCase(TestName = "Artillery types also contribute siege/anti-structure capability, deduplicated.")]
		public void ArtilleryCapabilities()
		{
			// v2rl is armor, artillery, and listed anti-air: three distinct capabilities, one of each.
			Assert.That(CapabilitiesOf(UnitClass.Armor, "v2rl"),
				Is.EqualTo(new[]
				{
					FriendlyCapability.AntiArmor, FriendlyCapability.Mobility,
					FriendlyCapability.Artillery, FriendlyCapability.AntiStructure,
					FriendlyCapability.AntiAir, FriendlyCapability.BaseDefense
				}));
		}

		[TestCase(TestName = "A transport and a scout contribute their functional capabilities.")]
		public void TransportAndRecon()
		{
			Assert.That(CapabilitiesOf(UnitClass.Naval, "lst"),
				Is.EqualTo(new[] { FriendlyCapability.Naval, FriendlyCapability.Transport }));
			Assert.That(CapabilitiesOf(UnitClass.Infantry, "e1"),
				Is.EqualTo(new[]
				{
					FriendlyCapability.AntiInfantry, FriendlyCapability.Recon,
					FriendlyCapability.FastRaiding
				}));
		}

		[TestCase(TestName = "A detector contributes detection on top of its class capability.")]
		public void DetectionCapability()
		{
			Assert.That(CapabilitiesOf(UnitClass.Infantry, "dog"),
				Is.EqualTo(new[] { FriendlyCapability.AntiInfantry, FriendlyCapability.Detection }));
		}

		[TestCase(TestName = "Recording a capability sets its profile entry to 1.")]
		public void RecordCapability()
		{
			var profile = new float[System.Enum.GetValues<FriendlyCapability>().Length];
			CoalitionForceRegistry.Record(FriendlyCapability.Transport, profile);

			Assert.That(profile[(int)FriendlyCapability.Transport], Is.EqualTo(1f));
			Assert.That(profile[(int)FriendlyCapability.AntiAir], Is.EqualTo(0f));
		}

		[TestCase(TestName = "A force group reports per-type composition and defaults to idle and unassigned.")]
		public void ForceGroupDefaults()
		{
			var group = new ForceGroup("Multi0");

			Assert.That(group.ByType, Is.Empty);
			Assert.That(group.ActivityCounts, Is.Empty);
			Assert.That(group.Capabilities, Has.Length.EqualTo(System.Enum.GetValues<FriendlyCapability>().Length));
			Assert.That(group.Status, Is.EqualTo(ForceStatus.Idle));
			Assert.That(group.MissionId, Is.Null);
			Assert.That(group.CasualtyFraction, Is.EqualTo(0f));
		}

		[TestCase(TestName = "Cohesion is high for a tight force and falls as it scatters.")]
		public void Cohesion()
		{
			var tight = new List<WPos> { new(0, 0, 0), new(1024, 0, 0), new(0, 1024, 0) };
			var loose = new List<WPos> { new(0, 0, 0), new(61440, 0, 0), new(0, 61440, 0) };

			Assert.That(CoalitionBlackboard.ComputeCohesion(tight), Is.GreaterThan(0.9f));
			Assert.That(CoalitionBlackboard.ComputeCohesion(loose), Is.LessThan(0.6f));
			Assert.That(CoalitionBlackboard.ComputeCohesion([]), Is.EqualTo(1f));
		}

		[TestCase(TestName = "Coalition roles specialize main, naval, and expansion allies without overlap.")]
		public void ProductionSpecializations()
		{
			var forces = new[]
			{
				new ForceGroup("A") { TotalUnits = 20 },
				new ForceGroup("B") { TotalUnits = 10 },
				new ForceGroup("C") { TotalUnits = 5 }
			};
			forces[1].Counts[(int)UnitClass.Naval] = 4;
			var cash = new Dictionary<string, int> { ["A"] = 1000, ["B"] = 2000, ["C"] = 5000 };

			var roles = CoalitionForceRegistry.AssignRoles(forces, cash, hasBigWater: true);
			Assert.That(roles["A"], Is.EqualTo("main"));
			Assert.That(roles["B"], Is.EqualTo("naval"));
			Assert.That(roles["C"], Is.EqualTo("expansion"));

			roles = CoalitionForceRegistry.AssignRoles(forces, cash, hasBigWater: false);
			Assert.That(roles.Values, Does.Not.Contain("naval"));
			Assert.That(roles["C"], Is.EqualTo("expansion"));
		}
	}
}
