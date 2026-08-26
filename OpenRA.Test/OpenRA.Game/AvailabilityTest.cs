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
using OpenRA.Mods.Common.Commander.Model;

namespace OpenRA.Test
{
	/// <summary>
	/// What the commander can do this second, as distinct from what the rules permit.
	/// </summary>
	[TestFixture]
	sealed class AvailabilityTest
	{
		static BuildOption Option(string type, string queue, float seconds,
			bool affordable = true, bool brownout = false) =>
			new()
			{
				Capability = new ActorCapability { Type = type },
				Queue = queue,
				TimeToField = seconds,
				Affordable = affordable,
				CausesBrownout = brownout,
			};

		[TestCase(TestName = "Options are ordered by when they would actually arrive.")]
		public void OrderedByTimeToField()
		{
			// The comparison this class exists for. "Cheap now" and "strong in ninety seconds" are
			// not comparable until the ninety seconds is on the table, and a commander that cannot
			// compare them takes the cheap thing every time.
			var available = new Availability
			{
				Options =
				[
					Option("jeep", "Vehicle", 11f),
					Option("4tnk", "Vehicle", 46f),
					Option("1tnk", "Vehicle", 14f),
				],
			};

			Assert.That(available.On("Vehicle").Select(o => o.Type),
				Is.EqualTo(new[] { "jeep", "1tnk", "4tnk" }));
		}

		[TestCase(TestName = "Affordability and power cost are separate from buildability.")]
		public void AffordabilityIsSeparate()
		{
			// A thing can be permitted, unaffordable, and ruinous to the power grid all at once, and
			// a manager needs to tell those apart rather than seeing one boolean.
			var available = new Availability
			{
                Cash = 400,
				PowerProvided = 400,
				PowerDrained = 250,
				Options = [Option("atek", "Building", 30f, affordable: false, brownout: true)],
			};

			var atek = available.Find("atek");
			Assert.That(atek, Is.Not.Null, "It is buildable - the rules allow it.");
			Assert.That(atek.Affordable, Is.False);
			Assert.That(atek.CausesBrownout, Is.True);
			Assert.That(available.ExcessPower, Is.EqualTo(150));
		}

		[TestCase(TestName = "What we own is counted by capability, not by unit name.")]
		public void OwnedIsCountedByVerb()
		{
			// "Forty units" tells a commander nothing. "One anti-air" tells it what to build next,
			// and it is the kind of gap that stayed invisible while nothing counted capabilities.
			var available = new Availability
			{
				OwnedByVerb = new Dictionary<string, int>
				{
					["armed"] = 62, ["antiair"] = 1, ["detector"] = 56, ["harvester"] = 4,
				},
			};

			Assert.That(available.Owned("antiair"), Is.EqualTo(1));
			Assert.That(available.Owned("transport"), Is.EqualTo(0),
				"A capability we hold none of reads as zero, not as missing data.");
		}

		[TestCase(TestName = "A capability satisfies every verb it qualifies for.")]
		public void VerbsAreDerivedFromCapability()
		{
			var mammoth = new ActorCapability
			{
				Type = "4tnk",
				Weapons =
				[
					new WeaponCapability { DamagePerSecond = 1000f, HitsGround = true },
					new WeaponCapability { DamagePerSecond = 400f, HitsAir = true },
				],
			};

			Assert.That(Availability.VerbsOf(mammoth), Is.EquivalentTo(new[] { "armed", "antiair" }));

			var apc = new ActorCapability { Type = "apc", CargoCapacity = 5 };
			Assert.That(Availability.VerbsOf(apc), Is.EquivalentTo(new[] { "transport" }));
		}

		[TestCase(TestName = "Support powers separate ready from charging.")]
		public void ReadyPowersAreSeparable()
		{
			var available = new Availability
			{
				SupportPowers =
				[
					new SupportPowerState("nuke", "Atom Bomb", false, 412f),
					new SupportPowerState("iron", "Iron Curtain", true, 0f),
				],
			};

			Assert.That(available.ReadyPowers().Select(p => p.Name), Is.EqualTo(new[] { "Iron Curtain" }));
		}
	}
}
