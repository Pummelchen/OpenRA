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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class LlmEvalTest
	{
		// These tests exercise the telemetry parsing logic that the Python eval harness
		// (ai/llm_eval.py) uses to score LLM strategic decisions. The parsers are pure
		// functions of log lines — no World, no simulation — so we feed them synthetic
		// telemetry lines and assert the resulting scores.
		static readonly Regex TimestampRegex = new(@"^\[(\d+\.\d+)\]\s*(.*)", RegexOptions.Compiled);

		static string StripTimestamp(string line)
		{
			var m = TimestampRegex.Match(line);
			return m.Success ? m.Groups[2].Value : line;
		}

		static float? TimestampSeconds(string line)
		{
			var m = TimestampRegex.Match(line);
			return m.Success ? float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;
		}

		// --- Legality scoring (req 729) ---
		static (float Score, int Rejections, int TotalCommands) ScoreLegality(string[] lines)
		{
			var rejections = 0;
			var totalCommands = 0;
			foreach (var raw in lines)
			{
				var msg = StripTimestamp(raw);
				if (msg.Contains("REJECTED_"))
					rejections++;
				if (msg.StartsWith("LLM intent applied:", StringComparison.Ordinal))
				{
					var m = Regex.Match(msg, @"missions=(\d+).*produce=(\d+)");
					if (m.Success)
						totalCommands += int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)
							+ int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
					else
						totalCommands++;
				}
			}

			var score = totalCommands == 0 ? 1f : Math.Max(0f, 1f - rejections / (float)Math.Max(1, totalCommands));
			return (score, rejections, totalCommands);
		}

		// --- Strategic oscillation (req 734) ---
		static (float Score, int Changes, float ChangesPerMinute, bool Oscillating) ScoreOscillation(string[] lines)
		{
			var postureChanges = new List<float>();
			string lastPosture = null;
			string lastStrategic = null;
			foreach (var raw in lines)
			{
				var ts = TimestampSeconds(raw);
				var msg = StripTimestamp(raw);

				var m = Regex.Match(msg, @"^Posture\s+(\w+);");
				if (m.Success)
				{
					var posture = m.Groups[1].Value;
					if (posture != lastPosture)
					{
						lastPosture = posture;
						if (ts.HasValue)
							postureChanges.Add(ts.Value);
					}

					continue;
				}

				m = Regex.Match(msg, @"^Strategic posture:\s+(\w+)");
				if (m.Success)
				{
					var posture = m.Groups[1].Value;
					if (posture != lastStrategic)
					{
						lastStrategic = posture;
						if (ts.HasValue)
							postureChanges.Add(ts.Value);
					}
				}
			}

			if (postureChanges.Count < 2)
				return (1f, postureChanges.Count, 0f, false);

			var durationSeconds = postureChanges[^1] - postureChanges[0];
			var durationMinutes = Math.Max(0.001f, durationSeconds / 60f);
			var changesPerMinute = postureChanges.Count / durationMinutes;
			var oscillating = changesPerMinute > 3f;
			float score;
			if (changesPerMinute <= 3f)
				score = 1f;
			else
				score = Math.Max(0f, 1f - (changesPerMinute - 3f) / 10f);

			return (score, postureChanges.Count, changesPerMinute, oscillating);
		}

		// --- Idle fraction parsing (req 738) ---
		static (float Score, float? AvgIdle, bool Flagged) ScoreIdleForces(string[] lines)
		{
			var idleFractions = new List<float>();
			foreach (var raw in lines)
			{
				var msg = StripTimestamp(raw);
				var m = Regex.Match(msg, @"avg idle (\d+)%");
				if (m.Success)
					idleFractions.Add(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) / 100f);
			}

			if (idleFractions.Count == 0)
				return (1f, null, false);

			var avgIdle = idleFractions.Average();
			var flagged = avgIdle > 0.5f;
			float score;
			if (avgIdle <= 0.5f)
				score = 1f;
			else
				score = Math.Max(0f, 1f - (float)(avgIdle - 0.5) / 0.5f);

			return (score, (float)avgIdle, flagged);
		}

		// --- Tests ---
		[TestCase(TestName = "A clean log with no rejections scores 1.0.")]
		public void LegalityNoRejections()
		{
			var lines = new[]
			{
				"[10.0] LLM intent applied: posture=attack missions=2 produce=3 retreat=False",
				"[20.0] LLM intent applied: posture=defend missions=1 produce=2 retreat=False",
			};

			var (score, rejections, total) = ScoreLegality(lines);
			Assert.That(score, Is.EqualTo(1.0f).Within(0.001f));
			Assert.That(rejections, Is.EqualTo(0));
			Assert.That(total, Is.EqualTo(8), "2+3 + 1+2 = 8 total commands.");
		}

		[TestCase(TestName = "Rejections reduce the legality score proportionally.")]
		public void LegalityWithRejections()
		{
			var lines = new[]
			{
				"[10.0] LLM intent applied: posture=attack missions=4 produce=2 retreat=False",
				"[11.0] Command validator: REJECTED_UNKNOWN_TYPE: unknown mission type \"blitzkrieg\"",
				"[12.0] Command validator: REJECTED_OUT_OF_BOUNDS: target (999,999) is outside the 128x128 map",
			};

			var (score, rejections, total) = ScoreLegality(lines);

			// 2 rejections out of 6 total commands => score = 1 - 2/6 = 0.6667
			Assert.That(rejections, Is.EqualTo(2));
			Assert.That(total, Is.EqualTo(6));
			Assert.That(score, Is.EqualTo(1f - 2f / 6f).Within(0.001f));
		}

		[TestCase(TestName = "An empty log scores 1.0 (no commands, no rejections).")]
		public void LegalityEmpty()
		{
			var (score, rejections, total) = ScoreLegality([]);
			Assert.That(score, Is.EqualTo(1.0f).Within(0.001f));
			Assert.That(rejections, Is.EqualTo(0));
			Assert.That(total, Is.EqualTo(0));
		}

		[TestCase(TestName = "All rejections drive the legality score to 0.")]
		public void LegalityAllRejected()
		{
			var lines = new[]
			{
				"[10.0] LLM intent applied: posture=attack missions=2 produce=0 retreat=False",
				"[11.0] Command validator: REJECTED_UNKNOWN_TYPE: unknown mission type \"foo\"",
				"[12.0] Command validator: REJECTED_INVALID_PRIORITY: priority -5 is negative",
			};

			var (score, rejections, _) = ScoreLegality(lines);
			Assert.That(rejections, Is.EqualTo(2));
			Assert.That(score, Is.EqualTo(0.0f).Within(0.001f));
		}

		[TestCase(TestName = "Few posture changes over a long span do not trigger oscillation.")]
		public void OscillationStable()
		{
			var lines = new[]
			{
				"[0.0] Posture build; coalition 100 vs enemy 100",
				"[120.0] Posture attack; coalition 200 vs enemy 100",
				"[300.0] Strategic posture: breakthrough",
			};
			var (score, changes, _, oscillating) = ScoreOscillation(lines);
			Assert.That(changes, Is.EqualTo(3));
			Assert.That(oscillating, Is.False, "3 changes over 5 minutes is 0.6/min, well under the 3/min threshold.");
			Assert.That(score, Is.EqualTo(1.0f).Within(0.001f));
		}

		[TestCase(TestName = "Frequent posture changes trigger the oscillation flag.")]
		public void OscillationFlagged()
		{
			// 10 posture changes in 60 seconds = 10/min, well above 3/min.
			var lines = new[]
			{
				"[0.0] Posture build; coalition 100 vs enemy 100",
				"[6.0] Posture attack; coalition 100 vs enemy 100",
				"[12.0] Posture defend; coalition 100 vs enemy 100",
				"[18.0] Posture attack; coalition 100 vs enemy 100",
				"[24.0] Posture build; coalition 100 vs enemy 100",
				"[30.0] Strategic posture: defensive",
				"[36.0] Posture attack; coalition 100 vs enemy 100",
				"[42.0] Posture defend; coalition 100 vs enemy 100",
				"[48.0] Posture build; coalition 100 vs enemy 100",
				"[60.0] Posture attack; coalition 100 vs enemy 100",
			};

			var (score, changes, cpm, oscillating) = ScoreOscillation(lines);
			Assert.That(changes, Is.EqualTo(10));
			Assert.That(oscillating, Is.True);
			Assert.That(cpm, Is.GreaterThan(3f));
			Assert.That(score, Is.LessThan(1.0f), "Score must degrade when oscillating.");
		}

		[TestCase(TestName = "Fewer than two posture changes cannot oscillate.")]
		public void OscillationSingleChange()
		{
			var lines = new[]
			{
				"[0.0] Posture build; coalition 100 vs enemy 100",
			};
			var (score, _, _, oscillating) = ScoreOscillation(lines);
			Assert.That(score, Is.EqualTo(1.0f).Within(0.001f));
			Assert.That(oscillating, Is.False);
		}

		[TestCase(TestName = "Repeated identical postures are not counted as changes.")]
		public void OscillationSamePosture()
		{
			var lines = new[]
			{
				"[0.0] Posture attack; coalition 100 vs enemy 100",
				"[10.0] Posture attack; coalition 100 vs enemy 100",
				"[20.0] Posture attack; coalition 100 vs enemy 100",
			};

			var (score, changes, _, oscillating) = ScoreOscillation(lines);
			Assert.That(changes, Is.EqualTo(1), "Only the first occurrence counts as a change.");
			Assert.That(oscillating, Is.False);
			Assert.That(score, Is.EqualTo(1.0f).Within(0.001f));
		}

		[TestCase(TestName = "Low idle fraction is not flagged and scores 1.0.")]
		public void IdleLowFraction()
		{
			var lines = new[]
			{
				"[60.0] Match metrics: exchange 1.50 (enemy 300 / friendly 200 lost), econ dmg (enemy refineries lost 2, friendly 1), avg idle 20%, cohesion 0.85, avg cash 5000, predicted win ratio 0.70, result ongoing, samples 10",
				"[120.0] Match metrics: exchange 1.60 (enemy 320 / friendly 200 lost), econ dmg (enemy refineries lost 2, friendly 1), avg idle 30%, cohesion 0.80, avg cash 4000, predicted win ratio 0.75, result ongoing, samples 20",
			};

			var (score, avgIdle, flagged) = ScoreIdleForces(lines);
			Assert.That(avgIdle, Is.EqualTo(0.25f).Within(0.001f), "Average of 20% and 30%.");
			Assert.That(flagged, Is.False);
			Assert.That(score, Is.EqualTo(1.0f).Within(0.001f));
		}

		[TestCase(TestName = "High idle fraction is flagged and the score degrades.")]
		public void IdleHighFraction()
		{
			var lines = new[]
			{
				"[60.0] Match metrics: exchange 0.50 (enemy 100 / friendly 200 lost), econ dmg (enemy refineries lost 0, friendly 2), avg idle 60%, cohesion 0.40, avg cash 8000, predicted win ratio 0.30, result ongoing, samples 10",
				"[120.0] Match metrics: exchange 0.50 (enemy 100 / friendly 200 lost), econ dmg (enemy refineries lost 0, friendly 2), avg idle 80%, cohesion 0.35, avg cash 9000, predicted win ratio 0.25, result ongoing, samples 20",
			};

			var (score, avgIdle, flagged) = ScoreIdleForces(lines);
			Assert.That(avgIdle, Is.EqualTo(0.70f).Within(0.001f), "Average of 60% and 80%.");
			Assert.That(flagged, Is.True, "70% average idle exceeds the 50% threshold.");
			Assert.That(score, Is.LessThan(1.0f));
			Assert.That(score, Is.GreaterThan(0.0f));
		}

		[TestCase(TestName = "No match metrics lines yield a default 1.0 score.")]
		public void IdleNoMetrics()
		{
			var lines = new[]
			{
				"[10.0] Posture build; coalition 100 vs enemy 100",
			};

			var (score, avgIdle, flagged) = ScoreIdleForces(lines);
			Assert.That(score, Is.EqualTo(1.0f).Within(0.001f));
			Assert.That(avgIdle, Is.Null);
			Assert.That(flagged, Is.False);
		}

		[TestCase(TestName = "Idle fraction exactly at the 50% boundary is not flagged.")]
		public void IdleAtBoundary()
		{
			var lines = new[]
			{
				"[60.0] Match metrics: exchange 1.00 (enemy 200 / friendly 200 lost), econ dmg (enemy refineries lost 1, friendly 1), avg idle 50%, cohesion 0.70, avg cash 5000, predicted win ratio 0.50, result ongoing, samples 10",
			};

			var (score, avgIdle, flagged) = ScoreIdleForces(lines);
			Assert.That(avgIdle, Is.EqualTo(0.50f).Within(0.001f));
			Assert.That(flagged, Is.False, "50% is the boundary; the flag is strictly greater than 50%.");
			Assert.That(score, Is.EqualTo(1.0f).Within(0.001f));
		}
	}
}
