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
using OpenRA.Mods.Common.UtilityCommands;

namespace OpenRA.Test
{
	/// <summary>
	/// Covers aligning AI strategic decisions to a replay's tick timeline (req 708), the mechanism
	/// that lets a recorded game - including one played against a human (req 645) - be inspected
	/// decision by decision.
	/// </summary>
	[TestFixture]
	sealed class ReplayAnalysisTest
	{
		[TestCase(TestName = "Tick-stamped decisions are placed on the replay timeline.")]
		public void DecisionsArePositionedInTheMatch()
		{
			var lines = new[]
			{
				"[10.5] Posture attack; tick=1000",
				"[20.5] Main effort set to (40,40) tick 5000",
				"[30.5] Match metrics: exchange 1.20 tick=9000"
			};

			var aligned = AnalyzeReplayCommand.AlignDecisions(lines, 10000).ToArray();

			Assert.That(aligned.Length, Is.EqualTo(3));
			Assert.That(aligned[0].Tick, Is.EqualTo(1000));
			Assert.That(aligned[0].Percent, Is.EqualTo(10f).Within(0.01f));
			Assert.That(aligned[1].Tick, Is.EqualTo(5000));
			Assert.That(aligned[1].Percent, Is.EqualTo(50f).Within(0.01f));
			Assert.That(aligned[2].Percent, Is.EqualTo(90f).Within(0.01f));
		}

		[TestCase(TestName = "Both 'tick=N' and 'tick N' forms are recognised.")]
		public void BothTickFormsParse()
		{
			var aligned = AnalyzeReplayCommand.AlignDecisions(
				["decision tick=42", "decision tick 84"], 100).ToArray();

			Assert.That(aligned.Select(a => a.Tick), Is.EqualTo(new[] { 42, 84 }));
		}

		[TestCase(TestName = "Lines with no tick stamp are skipped.")]
		public void UnstampedLinesAreSkipped()
		{
			var aligned = AnalyzeReplayCommand.AlignDecisions(
				["a header line", "Tool API listening on 8766", "real decision tick=7"], 100).ToArray();

			Assert.That(aligned.Length, Is.EqualTo(1));
			Assert.That(aligned[0].Tick, Is.EqualTo(7));
		}

		[TestCase(TestName = "Decisions past the replay's end belong to another match and are excluded.")]
		public void LaterMatchesAreExcluded()
		{
			// The telemetry log is appended across runs, so a shared file holds several matches.
			// Attributing a later match's decisions to this replay would be a false reading.
			var aligned = AnalyzeReplayCommand.AlignDecisions(
				["this match tick=500", "next match tick=9999"], 1000).ToArray();

			Assert.That(aligned.Length, Is.EqualTo(1));
			Assert.That(aligned[0].Tick, Is.EqualTo(500));
		}

		[TestCase(TestName = "An unknown final tick still lists decisions without inventing positions.")]
		public void UnknownDurationYieldsNoPercentage()
		{
			var aligned = AnalyzeReplayCommand.AlignDecisions(["decision tick=500"], 0).ToArray();

			Assert.That(aligned.Length, Is.EqualTo(1));
			Assert.That(aligned[0].Percent, Is.EqualTo(0f),
				"Without a known duration no position is claimed rather than a fabricated one.");
		}

		[TestCase(TestName = "Empty and null input is handled without throwing.")]
		public void EmptyInputIsSafe()
		{
			Assert.That(AnalyzeReplayCommand.AlignDecisions([], 100).ToArray(), Is.Empty);
			Assert.That(AnalyzeReplayCommand.AlignDecisions(null, 100).ToArray(), Is.Empty);
			Assert.That(AnalyzeReplayCommand.AlignDecisions([null, ""], 100).ToArray(), Is.Empty);
		}
	}
}
