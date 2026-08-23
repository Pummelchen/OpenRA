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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Commander.Model;

namespace OpenRA.Test
{
	/// <summary>
	/// The training set the evaluation function is fitted on. Its correctness is load-bearing:
	/// mislabelled or badly split data produces a model that scores well and has learned nothing,
	/// which is worse than no model at all because it looks like progress.
	/// </summary>
	[TestFixture]
	sealed class SelfPlayLogTest
	{
		string path;

		[SetUp]
		public void SetUp()
		{
			path = Path.Combine(Path.GetTempPath(), $"openra-selfplay-{Guid.NewGuid():N}.csv");
		}

		[TearDown]
		public void TearDown()
		{
			if (File.Exists(path))
				File.Delete(path);
		}

		static float[] Features(float value)
		{
			var f = new float[StateFeatures.Count];
			f[(int)StateFeatures.Feature.ArmyAdvantage] = value;
			f[(int)StateFeatures.Feature.Bias] = 1f;
			return f;
		}

		[TestCase(TestName = "Rows survive a round trip exactly.")]
		public void RoundTrip()
		{
			var samples = new List<(int, float[])> { (100, Features(0.25f)), (200, Features(-0.5f)) };
			SelfPlayLog.Append(path, samples, won: true);

			var rows = SelfPlayLog.Read(path);
			Assert.That(rows, Has.Count.EqualTo(2));
			Assert.That(rows[0].Tick, Is.EqualTo(100));
			Assert.That(rows[0].Won, Is.True);
			Assert.That(rows[0].Features[(int)StateFeatures.Feature.ArmyAdvantage], Is.EqualTo(0.25f));
			Assert.That(rows[1].Features[(int)StateFeatures.Feature.ArmyAdvantage], Is.EqualTo(-0.5f));
		}

		[TestCase(TestName = "Appending accumulates across matches.")]
		public void AppendAccumulates()
		{
			// The training set is meant to grow across runs; that accumulation is the mechanism by
			// which the commander gets stronger without anyone editing a threshold.
			SelfPlayLog.Append(path, [(10, Features(0.1f))], won: true);
			SelfPlayLog.Append(path, [(10, Features(0.2f))], won: false);

			var rows = SelfPlayLog.Read(path);
			Assert.That(rows, Has.Count.EqualTo(2));
			Assert.That(rows.Select(r => r.Won), Is.EqualTo(new[] { true, false }));
		}

		[TestCase(TestName = "Malformed lines are skipped, not thrown on.")]
		public void MalformedLinesAreSkipped()
		{
			SelfPlayLog.Append(path, [(10, Features(0.1f))], won: true);
			File.AppendAllLines(path,
			[
				"# a comment",
				"",
				"not,a,row",
				"10,2," + string.Join(",", Enumerable.Repeat("0", StateFeatures.Count)),
				"abc,1," + string.Join(",", Enumerable.Repeat("0", StateFeatures.Count)),
			]);

			// A half-written line from an interrupted run must cost that line and nothing else.
			var rows = SelfPlayLog.Read(path);
			Assert.That(rows, Has.Count.EqualTo(1));
		}

		[TestCase(TestName = "A missing file reads as empty.")]
		public void MissingFileIsEmpty()
		{
			Assert.That(SelfPlayLog.Read(path), Is.Empty);
			Assert.That(SelfPlayLog.Read(null), Is.Empty);
		}

		[TestCase(TestName = "The split keeps every sample of a game on one side.")]
		public void SplitIsByGame()
		{
			// Splitting at random would put near-identical consecutive samples in both sets. The
			// model would score beautifully on a holdout that is really a copy of its training
			// data, and would have learned nothing that generalises.
			var rows = new List<(SelfPlayLog.Row, int)>();
			for (var game = 0; game < 8; game++)
				for (var sample = 0; sample < 10; sample++)
					rows.Add((new SelfPlayLog.Row(sample, Features(game / 8f), game % 2 == 0), game));

			var (train, holdout) = SelfPlayLog.Split(rows, holdoutEvery: 4);

			Assert.That(train.Count + holdout.Count, Is.EqualTo(80));
			Assert.That(holdout.Count, Is.EqualTo(20), "Games 0 and 4 are held out, ten samples each.");

			// Every feature value identifies its game, so an overlap would show up as a shared value.
			var trainValues = train.Select(s => s.Features[(int)StateFeatures.Feature.ArmyAdvantage]).ToHashSet();
			var holdoutValues = holdout.Select(s => s.Features[(int)StateFeatures.Feature.ArmyAdvantage]).ToHashSet();
			Assert.That(trainValues.Overlaps(holdoutValues), Is.False,
				"No game may appear on both sides of the split.");
		}

		[TestCase(TestName = "A model fitted on split data is graded on games it never saw.")]
		public void SplitSupportsAnHonestGrade()
		{
			// End to end: eight games where the army advantage decided the result. Fitting on six
			// and grading on the other two must still find the rule.
			var rows = new List<(SelfPlayLog.Row, int)>();
			for (var game = 0; game < 8; game++)
			{
				var winning = game % 2 == 0;
				for (var sample = 0; sample < 25; sample++)
					rows.Add((new SelfPlayLog.Row(sample, Features(winning ? 0.6f : -0.6f), winning), game));
			}

			var (train, holdout) = SelfPlayLog.Split(rows, holdoutEvery: 4);
			var fit = LogisticFit.Fit(train);
			var graded = LogisticFit.Score(fit.Model, holdout, 0);

			Assert.That(graded.Accuracy, Is.EqualTo(1f));
			Assert.That(graded.BrierScore, Is.LessThan(0.25f),
				"0.25 is what a coin flip scores; the gate is beating it on unseen games.");
		}
	}
}
