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

using NUnit.Framework;
using OpenRA.Mods.Common.Commander.Model;

namespace OpenRA.Test
{
	/// <summary>
	/// Role classification from ruleset facts. The point of deriving it is that a unit added to the
	/// mod tomorrow is classified tomorrow, rather than being silently counted as generic strength
	/// until somebody notices.
	/// </summary>
	[TestFixture]
	sealed class RoleClassifierTest
	{
		static RoleClassifier.Traits Unit(string armor, int range = 4, bool air = false, bool ground = true,
			bool aircraft = false, bool building = false, bool mobile = true, bool armed = true) =>
			new(aircraft, building, mobile, armor, range, air, ground, armed);

		[TestCase(TestName = "Domain is decided before armament.")]
		public void DomainWinsOverArmament()
		{
			// A gunboat that only shoots at aircraft is Naval, not AntiAir: what it floats on limits
			// where it can fight far more than what it can shoot at.
			Assert.That(RoleClassifier.Classify(Unit("Ship", air: true, ground: false)), Is.EqualTo(CombatRole.Naval));

			// Likewise a helicopter gunship is Aircraft, whatever it attacks.
			Assert.That(RoleClassifier.Classify(Unit("Light", aircraft: true)), Is.EqualTo(CombatRole.Aircraft));
		}

		[TestCase(TestName = "Structures are defences, mobile units are not.")]
		public void StructuresAreDefences()
		{
			Assert.That(RoleClassifier.Classify(Unit("Concrete", building: true)), Is.EqualTo(CombatRole.Defense));
			Assert.That(RoleClassifier.Classify(Unit("Heavy", mobile: false)), Is.EqualTo(CombatRole.Defense),
				"Anything that cannot march is a defence for planning purposes.");
		}

		[TestCase(TestName = "Anti-air means it can only reach aircraft.")]
		public void AntiAirIsExclusive()
		{
			Assert.That(RoleClassifier.Classify(Unit("Light", air: true, ground: false)), Is.EqualTo(CombatRole.AntiAir));

			// A unit that handles both is counted for the harder job it does on the ground; calling
			// it anti-air would leave the ground column looking weaker than it is.
			Assert.That(RoleClassifier.Classify(Unit("Heavy", air: true, ground: true)), Is.EqualTo(CombatRole.Armor));
		}

		[TestCase(TestName = "Long reach with no answer to aircraft is artillery.")]
		public void ArtilleryIsRangeWithoutAir()
		{
			Assert.That(RoleClassifier.Classify(Unit("Light", range: RoleClassifier.ArtilleryRangeCells)),
				Is.EqualTo(CombatRole.Artillery));

			Assert.That(RoleClassifier.Classify(Unit("Light", range: RoleClassifier.ArtilleryRangeCells - 1)),
				Is.EqualTo(CombatRole.Armor), "Short reach is not artillery, whatever else is true.");

			Assert.That(RoleClassifier.Classify(Unit("Light", range: 20, air: true)),
				Is.EqualTo(CombatRole.Armor), "Something that defends itself from the air is not artillery.");
		}

		[TestCase(TestName = "Unarmoured foot troops are infantry.")]
		public void InfantryIsUnarmoured()
		{
			Assert.That(RoleClassifier.Classify(Unit("None")), Is.EqualTo(CombatRole.Infantry));
			Assert.That(RoleClassifier.Classify(Unit("none")), Is.EqualTo(CombatRole.Infantry),
				"Armour class comparison must not depend on how the rules happen to be capitalised.");

			// Unarmed infantry - engineers, spies - are still infantry, and still cost credits that
			// belong somewhere in the force vector.
			Assert.That(RoleClassifier.Classify(Unit("None", armed: false, ground: false)),
				Is.EqualTo(CombatRole.Infantry));
		}

		[TestCase(TestName = "Armour is the remainder, not a guess.")]
		public void ArmorIsTheDefault()
		{
			Assert.That(RoleClassifier.Classify(Unit("Heavy")), Is.EqualTo(CombatRole.Armor));
			Assert.That(RoleClassifier.Classify(Unit("Light")), Is.EqualTo(CombatRole.Armor));
			Assert.That(RoleClassifier.Classify(Unit("Wood")), Is.EqualTo(CombatRole.Armor));
		}
	}
}
