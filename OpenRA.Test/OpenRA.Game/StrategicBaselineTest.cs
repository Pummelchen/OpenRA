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
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace OpenRA.Test
{
	/// <summary>
	/// <para>
	/// Guards the recorded strategic baseline in <c>ai/baselines/fixed-seed-matrix.json</c>
	/// (reqs 643, 644, 714).
	/// </para>
	/// <para>
	/// Running the 12-match matrix takes far too long for a unit suite, so the measurement lives in a
	/// checked-in record and this asserts the record stays honest and complete: every opponent
	/// present, totals consistent with the per-opponent rows, and the comparison against the scripted
	/// baseline explicit. That matters because the previous audit's headline claim - "136% above
	/// baseline" - came from quoting an exchange ratio for one matchup while the baseline was the
	/// side actually winning games. A structured record makes that failure mode visible.
	/// </para>
	/// </summary>
	[TestFixture]
	sealed class StrategicBaselineTest
	{
		static JsonElement LoadBaseline()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 6 && dir != null; i++)
			{
				var path = Path.Combine(dir.FullName, "ai", "baselines", "fixed-seed-matrix.json");
				if (File.Exists(path))
					return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();

				dir = dir.Parent;
			}

			Assert.Ignore("Baseline record not found from the test working directory.");
			return default;
		}

		[Test(Description = "643/644: the baseline records the coalition against every scripted opponent.")]
		public void EveryOpponentIsMeasured()
		{
			var baseline = LoadBaseline();
			var ai = baseline.GetProperty("results").GetProperty("ai");

			foreach (var opponent in new[] { "rush", "normal", "turtle", "naval" })
				Assert.That(ai.TryGetProperty(opponent, out _), Is.True,
					$"The matrix has no recorded result against '{opponent}'.");
		}

		[Test(Description = "The recorded totals agree with the per-opponent rows.")]
		public void TotalsAreConsistent()
		{
			var baseline = LoadBaseline();

			foreach (var side in new[] { "ai", "normal" })
			{
				var rows = baseline.GetProperty("results").GetProperty(side);
				var wins = rows.EnumerateObject().Sum(p => p.Value.GetProperty("wins").GetInt32());
				var losses = rows.EnumerateObject().Sum(p => p.Value.GetProperty("losses").GetInt32());
				var draws = rows.EnumerateObject().Sum(p => p.Value.GetProperty("draws").GetInt32());

				var totals = baseline.GetProperty("totals").GetProperty(side);
				Assert.That(wins, Is.EqualTo(totals.GetProperty("wins").GetInt32()), $"{side} win total");
				Assert.That(losses, Is.EqualTo(totals.GetProperty("losses").GetInt32()), $"{side} loss total");
				Assert.That(draws, Is.EqualTo(totals.GetProperty("draws").GetInt32()), $"{side} draw total");
			}
		}

		[Test(Description = "714: every recorded match is accounted for, so no seed is quietly dropped.")]
		public void EveryMatchIsAccountedFor()
		{
			var baseline = LoadBaseline();
			var seeds = baseline.GetProperty("seeds").GetArrayLength();

			foreach (var side in new[] { "ai", "normal" })
				foreach (var row in baseline.GetProperty("results").GetProperty(side).EnumerateObject())
				{
					var played = row.Value.GetProperty("wins").GetInt32()
						+ row.Value.GetProperty("losses").GetInt32()
						+ row.Value.GetProperty("draws").GetInt32();

					Assert.That(played, Is.EqualTo(seeds),
						$"{side} vs {row.Name} records {played} matches for {seeds} seeds; a run was dropped.");
				}
		}

		[Test(Description = "804: the record states plainly whether the coalition beats the scripted baseline.")]
		public void BaselineComparisonIsExplicit()
		{
			var baseline = LoadBaseline();
			var ai = baseline.GetProperty("totals").GetProperty("ai");
			var normal = baseline.GetProperty("totals").GetProperty("normal");

			var aiWins = ai.GetProperty("wins").GetInt32();
			var normalWins = normal.GetProperty("wins").GetInt32();

			// This is the honest state of requirement 804 as measured. The assertion is written to
			// fail the day the coalition overtakes the baseline, which is exactly when the record and
			// AUDIT_REPORT.md need updating rather than silently going stale.
			Assert.That(aiWins, Is.LessThanOrEqualTo(normalWins),
				$"The coalition now wins {aiWins} matches against the baseline's {normalWins}. "
				+ "Requirement 804 may be met - re-measure and update the baseline and AUDIT_REPORT.md.");
		}

		[Test(Description = "The known air/naval production gap is recorded rather than forgotten.")]
		public void KnownGapsAreRecorded()
		{
			var baseline = LoadBaseline();
			Assert.That(baseline.TryGetProperty("known_gaps", out var gaps), Is.True);
			Assert.That(gaps.GetProperty("air_units_fielded").GetInt32(), Is.Zero);
			Assert.That(gaps.GetProperty("note").GetString(), Is.Not.Empty);
		}
	}
}
