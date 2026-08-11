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
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class ThreatModelTest
	{
		static readonly FrozenSet<string> Artillery = new[] { "arty", "v2rl" }.ToFrozenSet();
		static readonly FrozenSet<string> Submarines = new[] { "ss", "msub" }.ToFrozenSet();
		static readonly FrozenSet<string> Detection = new[] { "dog", "rdr" }.ToFrozenSet();
		static readonly FrozenSet<string> Superweapons = new[] { "iron", "pdox" }.ToFrozenSet();
		static readonly FrozenSet<string> Production = new[] { "weap", "afld", "hpad", "fact" }.ToFrozenSet();

		[TestCase(TestName = "A tank seeds ground anti-armor only.")]
		public void ArmorCapabilities()
		{
			var caps = CoalitionBlackboard.CapabilitiesFor(UnitClass.Armor, "2tnk",
				Artillery, Submarines, Detection, Superweapons, Production).ToArray();
			Assert.That(caps, Is.EquivalentTo(new[] { CoalitionCapability.GroundAntiArmor }));
		}

		[TestCase(TestName = "Infantry seeds ground anti-infantry only.")]
		public void InfantryCapabilities()
		{
			var caps = CoalitionBlackboard.CapabilitiesFor(UnitClass.Infantry, "e1",
				Artillery, Submarines, Detection, Superweapons, Production).ToArray();
			Assert.That(caps, Is.EquivalentTo(new[] { CoalitionCapability.GroundAntiInfantry }));
		}

		[TestCase(TestName = "A mig seeds both AA interception and air-to-air threat.")]
		public void AirCapabilities()
		{
			var caps = CoalitionBlackboard.CapabilitiesFor(UnitClass.Air, "mig",
				Artillery, Submarines, Detection, Superweapons, Production).ToArray();
			Assert.That(caps, Is.EquivalentTo(new[]
			{
				CoalitionCapability.AntiAir, CoalitionCapability.AirToAir
			}));
		}

		[TestCase(TestName = "A submarine seeds naval and submarine threat.")]
		public void SubmarineCapabilities()
		{
			var caps = CoalitionBlackboard.CapabilitiesFor(UnitClass.Naval, "msub",
				Artillery, Submarines, Detection, Superweapons, Production).ToArray();
			Assert.That(caps, Is.EquivalentTo(new[]
			{
				CoalitionCapability.Naval, CoalitionCapability.Submarine
			}));
		}

		[TestCase(TestName = "Artillery units seed both anti-armor (class) and artillery threat.")]
		public void ArtilleryCapabilities()
		{
			var caps = CoalitionBlackboard.CapabilitiesFor(UnitClass.Armor, "v2rl",
				Artillery, Submarines, Detection, Superweapons, Production).ToArray();
			Assert.That(caps, Is.EquivalentTo(new[]
			{
				CoalitionCapability.GroundAntiArmor, CoalitionCapability.Artillery
			}));
		}

		[TestCase(TestName = "A factory seeds static defense and reinforcement threat.")]
		public void ProductionStructureCapabilities()
		{
			var caps = CoalitionBlackboard.CapabilitiesFor(UnitClass.Structure, "weap",
				Artillery, Submarines, Detection, Superweapons, Production).ToArray();
			Assert.That(caps, Is.EquivalentTo(new[]
			{
				CoalitionCapability.StaticDefense, CoalitionCapability.Reinforcement
			}));
		}

		[TestCase(TestName = "A superweapon seeds static defense and support-power risk.")]
		public void SuperweaponCapabilities()
		{
			var caps = CoalitionBlackboard.CapabilitiesFor(UnitClass.Structure, "iron",
				Artillery, Submarines, Detection, Superweapons, Production).ToArray();
			Assert.That(caps, Is.EquivalentTo(new[]
			{
				CoalitionCapability.StaticDefense, CoalitionCapability.SupportPowerRisk
			}));
		}

		[TestCase(TestName = "A dog seeds infantry-class and detection threat.")]
		public void DetectionCapabilities()
		{
			var caps = CoalitionBlackboard.CapabilitiesFor(UnitClass.Infantry, "dog",
				Artillery, Submarines, Detection, Superweapons, Production).ToArray();
			Assert.That(caps, Is.EquivalentTo(new[]
			{
				CoalitionCapability.GroundAntiInfantry, CoalitionCapability.Detection
			}));
		}

		[TestCase(TestName = "A harvester (support) seeds no combat threat.")]
		public void SupportCapabilities()
		{
			var caps = CoalitionBlackboard.CapabilitiesFor(UnitClass.Support, "harv",
				Artillery, Submarines, Detection, Superweapons, Production).ToArray();
			Assert.That(caps, Is.Empty);
		}
	}
}
