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

using NUnit.Framework;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class ReserveManagerTest
	{
		[TestCase(TestName = "Recording a reserve commitment stores tick, count, and reason.")]
		public void RecordStoresCommitment()
		{
			var manager = new ReserveManager();
			manager.Record(1234, 17, "breakthrough");
			Assert.That(manager.LastCommitTick, Is.EqualTo(1234));
			Assert.That(manager.CommittedUnits, Is.EqualTo(17));
			Assert.That(manager.LastCommitReason, Is.EqualTo("breakthrough"));
		}

		[TestCase(TestName = "A fresh reserve manager holds no commitment.")]
		public void Defaults()
		{
			var manager = new ReserveManager();
			Assert.That(manager.LastCommitTick, Is.EqualTo(int.MinValue));
			Assert.That(manager.CommittedUnits, Is.EqualTo(0));
			Assert.That(manager.LastCommitReason, Is.Null);
		}

		[TestCase(TestName = "Reserve justification is required only below half the minimum wave size.")]
		public void JustificationThreshold()
		{
			Assert.That(ReserveManager.RequiresJustification(4, 10), Is.True);
			Assert.That(ReserveManager.RequiresJustification(5, 10), Is.False);
			Assert.That(ReserveManager.RequiresJustification(9, 10), Is.False);
		}
	}
}
