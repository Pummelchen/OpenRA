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

using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class ExternalBrainSnapshotTest
	{
		[TestCase(TestName = "External context sections are deterministically bounded.")]
		public void ContextBounds()
		{
			Assert.That(ExternalBrainBotModule.LimitContext(Enumerable.Range(0, 100), 5), Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
			Assert.That(ExternalBrainBotModule.LimitContext(Enumerable.Range(0, 100), 5, newest: true),
				Is.EqualTo(new[] { 95, 96, 97, 98, 99 }));
			Assert.That(ExternalBrainBotModule.LimitContext(Enumerable.Range(0, 10), 0), Is.Empty);
		}

		[TestCase(TestName = "Army-group snapshots expose location and nearby known threats.")]
		public void ArmyGroupSpatialContract()
		{
			var type = typeof(ExternalBrainBotModule).GetNestedType("ArmyGroupState", BindingFlags.NonPublic);
			Assert.That(type, Is.Not.Null);
			foreach (var property in new[] { "Region", "X", "Y", "NearbyThreats", "Composition", "Strength", "Readiness", "Mission" })
				Assert.That(type.GetProperty(property), Is.Not.Null, property);
		}

		[TestCase(TestName = "Fair-fog snapshots reject actors that are not currently visible.")]
		public void FairFogGate()
		{
			Assert.That(ExternalBrainBotModule.MayExposeEnemyActor(currentlyVisible: true), Is.True);
			Assert.That(ExternalBrainBotModule.MayExposeEnemyActor(currentlyVisible: false), Is.False);
		}
	}
}
