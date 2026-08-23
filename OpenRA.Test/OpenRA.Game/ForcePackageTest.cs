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
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

namespace OpenRA.Test
{
	/// <summary>
	/// Covers multi-player force packages (req 26). OpenRA forbids ordering another player's actors,
	/// so a ForceGroup stays per-owner; a package is the joint object the commander assigns and
	/// scores, spanning every ally committed to one mission.
	/// </summary>
	[TestFixture]
	sealed class ForcePackageTest
	{
		static ForceGroup Group(string owner, string mission, int units, float strength,
			float readiness = 1f, FriendlyCapability? capability = null)
		{
			var group = new ForceGroup(owner)
			{
				MissionId = mission,
				TotalUnits = units,
				Strength = strength,
				Readiness = readiness,
				Center = new CPos(units, units)
			};

			if (capability != null)
				group.Capabilities[(int)capability.Value] = 1f;

			return group;
		}

		[TestCase(TestName = "A package spans several allied players committed to one mission.")]
		public void PackageIsJointAcrossOwners()
		{
			var packages = CoalitionForcePackage.Build(
			[
				Group("Alpha", "m1", 10, 100f),
				Group("Bravo", "m1", 6, 60f),
				Group("Charlie", "m2", 4, 40f)
			]);

			Assert.That(packages.Count, Is.EqualTo(2));

			var joint = packages.First(p => p.MissionId == "m1");
			Assert.That(joint.IsJoint, Is.True, "Two allied players contribute, so the package is joint.");
			Assert.That(joint.Owners, Is.EquivalentTo(new[] { "Alpha", "Bravo" }));
			Assert.That(joint.TotalUnits, Is.EqualTo(16));
			Assert.That(joint.Strength, Is.EqualTo(160f));

			var single = packages.First(p => p.MissionId == "m2");
			Assert.That(single.IsJoint, Is.False, "One contributor is not a joint force.");
		}

		[TestCase(TestName = "Uncommitted forces are not packaged.")]
		public void UncommittedForcesAreExcluded()
		{
			var packages = CoalitionForcePackage.Build(
			[
				Group("Alpha", null, 10, 100f),
				Group("Bravo", string.Empty, 5, 50f),
				Group("Charlie", "m1", 4, 40f)
			]);

			Assert.That(packages.Count, Is.EqualTo(1));
			Assert.That(packages[0].MissionId, Is.EqualTo("m1"));
		}

		[TestCase(TestName = "Capabilities combine, so a coalition gap is judged coalition-wide.")]
		public void CapabilitiesCombineAcrossAllies()
		{
			// The point of the package: one ally's anti-air covers the whole operation, so the
			// commander must not conclude the force is short on AA because one contingent lacks it.
			var packages = CoalitionForcePackage.Build(
			[
				Group("Alpha", "m1", 10, 100f, capability: FriendlyCapability.AntiArmor),
				Group("Bravo", "m1", 4, 40f, capability: FriendlyCapability.AntiAir)
			]);

			var package = packages[0];
			Assert.That(package.Has(FriendlyCapability.AntiAir), Is.True);
			Assert.That(package.Has(FriendlyCapability.AntiArmor), Is.True);
			Assert.That(package.Has(FriendlyCapability.Naval), Is.False);
		}

		[TestCase(TestName = "Readiness is unit-weighted, so a small ready contingent cannot mask a large unready one.")]
		public void ReadinessIsUnitWeighted()
		{
			var packages = CoalitionForcePackage.Build(
			[
				Group("Alpha", "m1", 90, 900f, readiness: 0.0f),
				Group("Bravo", "m1", 10, 100f, readiness: 1.0f)
			]);

			// A naive average would report 0.50 and call this force half ready; it is 10% ready.
			Assert.That(packages[0].Readiness, Is.EqualTo(0.10f).Within(0.001f));
		}

		[TestCase(TestName = "Packaging is deterministic, so every allied bot builds the identical view.")]
		public void PackagingIsDeterministic()
		{
			ForceGroup[] Forces() =>
			[
				Group("Charlie", "m2", 4, 40f),
				Group("Alpha", "m1", 10, 100f),
				Group("Bravo", "m1", 6, 60f)
			];

			var first = CoalitionForcePackage.Build(Forces());
			var second = CoalitionForcePackage.Build(Forces().Reverse().ToArray());

			Assert.That(first.Select(p => p.MissionId), Is.EqualTo(second.Select(p => p.MissionId)));
			Assert.That(first[0].Members.Select(m => m.Owner),
				Is.EqualTo(second[0].Members.Select(m => m.Owner)),
				"Member order must not depend on the input order.");
		}

		[TestCase(TestName = "An empty package reports zero rather than dividing by zero.")]
		public void EmptyPackageIsSafe()
		{
			var package = new CoalitionForcePackage("m1", []);
			Assert.That(package.TotalUnits, Is.Zero);
			Assert.That(package.Readiness, Is.Zero);
			Assert.That(package.Cohesion, Is.Zero);
			Assert.That(package.IsJoint, Is.False);
		}
	}
}
