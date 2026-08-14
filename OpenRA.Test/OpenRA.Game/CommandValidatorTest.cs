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

		[TestCase(TestName = "A null, empty, or known posture is accepted.")]
		public void ValidPosture()
		{
			Assert.That(CommandValidator.ValidatePosture(null), Is.Null);
			Assert.That(CommandValidator.ValidatePosture(""), Is.Null);
			Assert.That(CommandValidator.ValidatePosture("attack"), Is.Null);
			Assert.That(CommandValidator.ValidatePosture("defend"), Is.Null);
			Assert.That(CommandValidator.ValidatePosture("build"), Is.Null);
			Assert.That(CommandValidator.ValidatePosture("TURTLE"), Is.Null, "Postures are case-insensitive.");
		}

		[TestCase(TestName = "An unknown posture is rejected with a machine-readable reason.")]
		public void UnknownPosture()
		{
			Assert.That(CommandValidator.ValidatePosture("blitzkrieg"), Does.Contain("REJECTED_UNKNOWN_POSTURE"));
			Assert.That(CommandValidator.ValidatePosture("PRESSURE"), Does.Contain("REJECTED_UNKNOWN_POSTURE"),
				"Internal enum names are not part of the intent vocabulary.");
		}

		[TestCase(TestName = "A null or well-formed production list is accepted.")]
		public void ValidProduce()
		{
			Assert.That(CommandValidator.ValidateProduce(null), Is.Empty);
			Assert.That(CommandValidator.ValidateProduce([]), Is.Empty);
			Assert.That(CommandValidator.ValidateProduce(new[] { "2tnk", "mig" }), Is.Empty);
		}

		[TestCase(TestName = "A blank production entry is rejected with its index.")]
		public void BlankProduceEntry()
		{
			var rejections = CommandValidator.ValidateProduce(new[] { "2tnk", "", "mig" }).ToArray();

			Assert.That(rejections, Has.Length.EqualTo(1));
			Assert.That(rejections[0].Index, Is.EqualTo(1));
			Assert.That(rejections[0].Reason, Does.Contain("REJECTED_INVALID_PRODUCE"));
		}

		[TestCase(TestName = "An oversized production list is rejected.")]
		public void OversizedProduce()
		{
			var produce = new string[CommandValidator.MaxProduceEntries + 1];
			for (var i = 0; i < produce.Length; i++)
				produce[i] = "2tnk";

			Assert.That(CommandValidator.ValidateProduce(produce).Any(r => r.Reason.Contains("REJECTED_INVALID_PRODUCE")), Is.True);
		}

		[TestCase(TestName = "Production boosts merge into the list, deduplicated case-insensitively.")]
		public void MergeProduce()
		{
			// A null boost leaves the existing list unchanged.
			Assert.That(CommandValidator.MergeProduce(new[] { "2tnk", "mig" }, null),
				Is.EqualTo(new[] { "2tnk", "mig" }));

			// A null existing list just becomes the boost.
			Assert.That(CommandValidator.MergeProduce(null, new[] { "arty", "jeep" }),
				Is.EqualTo(new[] { "arty", "jeep" }));

			// Boosts append and deduplicate case-insensitively; blank entries are ignored.
			var merged = CommandValidator.MergeProduce(new[] { "2tnk", "mig" }, new[] { "MIG", "", "arty" }).ToArray();
			Assert.That(merged, Is.EqualTo(new[] { "2tnk", "mig", "arty" }));
		}
	}
}
