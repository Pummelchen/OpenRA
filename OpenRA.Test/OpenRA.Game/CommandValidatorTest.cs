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
using NUnit.Framework;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class CommandValidatorTest
	{
		[TestCase(TestName = "Valid mission requests produce no rejections.")]
		public void ValidMissions()
		{
			var requests = new[] { ("attack", 10, 20, 90), ("feint", 5, 6, 60) };

			Assert.That(CommandValidator.ValidateMissions(requests, 128, 128), Is.Empty);
		}

		[TestCase(TestName = "Unknown mission types are rejected with a machine-readable reason.")]
		public void UnknownType()
		{
			var rejections = CommandValidator.ValidateMissions(new[] { ("teleport", 1, 1, 50) }, 128, 128).ToArray();

			Assert.That(rejections, Has.Length.EqualTo(1));
			Assert.That(rejections[0].Index, Is.EqualTo(0));
			Assert.That(rejections[0].Reason, Does.Contain("REJECTED_UNKNOWN_TYPE"));
		}

		[TestCase(TestName = "Out-of-bounds targets are rejected.")]
		public void OutOfBounds()
		{
			var rejections = CommandValidator.ValidateMissions(new[] { ("attack", 999, 999, 50) }, 128, 128).ToArray();

			Assert.That(rejections[0].Reason, Does.Contain("REJECTED_OUT_OF_BOUNDS"));
		}

		[TestCase(TestName = "Negative priorities are rejected.")]
		public void NegativePriority()
		{
			var rejections = CommandValidator.ValidateMissions(new[] { ("raid", 5, 5, -1) }, 128, 128).ToArray();

			Assert.That(rejections[0].Reason, Does.Contain("REJECTED_INVALID_PRIORITY"));
		}

		[TestCase(TestName = "Duplicate missions at the same target are a conflict.")]
		public void DuplicateConflict()
		{
			var requests = new[] { ("attack", 10, 20, 90), ("attack", 10, 20, 80) };
			var rejections = CommandValidator.ValidateMissions(requests, 128, 128).ToArray();

			Assert.That(rejections, Has.Length.EqualTo(1));
			Assert.That(rejections[0].Index, Is.EqualTo(1));
			Assert.That(rejections[0].Reason, Does.Contain("REJECTED_CONFLICT"));
		}

		[TestCase(TestName = "A stale reply round is detected.")]
		public void StaleRound()
		{
			Assert.That(CommandValidator.IsStale(3, 5), Is.True);
			Assert.That(CommandValidator.IsStale(5, 3), Is.False);
			Assert.That(CommandValidator.IsStale(-1, 5), Is.False, "No round means no staleness check.");
		}
	}
}
