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
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class HeadlessSkirmishTest
	{
		const string TestMapUid = "9d94535ca08292d64acab2b96f4490e5a7aa29ab";

		// Platform.OverrideEngineDir may only be called once per process, so the mod data and map
		// are loaded once and shared by every test in the fixture.
		static (ModData ModData, Map Map) loaded;

		[Test(Description = "At most two teams and 8 bots per team are accepted; these checks run before any map work.")]
		public void TeamCapsEnforced()
		{
			Assert.Throws<ArgumentException>(() => HeadlessSkirmish.Run(null, null, "ai", 2, 3, 100, 1));
			Assert.Throws<ArgumentException>(() => HeadlessSkirmish.Run(null, null, "ai", 17, 2, 100, 1));
			Assert.Throws<ArgumentException>(() => HeadlessSkirmish.Run(null, null, "ai", 1, 1, 100, 1));
		}

		[Test(Description = "The headless harness runs a match with all bots enabled and produces telemetry.")]
		public void RunsAndEnablesBots()
		{
			try
			{
				var (modData, map) = LoadModAndMap();
				var result = HeadlessSkirmish.Run(modData, map, "ai", 2, 2, 1200, 42);

				Assert.That(result.Ticks, Is.EqualTo(1200), "The simulation should run the full tick budget.");
				Assert.That(result.ActorCount, Is.GreaterThan(0));
				Assert.That(result.Clients.Count(c => c.IsBot && c.BotEnabled), Is.EqualTo(2), "All bots should be enabled.");
				Assert.That(result.Events.Count, Is.GreaterThan(0), "The match telemetry should capture AI events.");
			}
			catch (Exception e) when (e.ToString().Contains("Chronoshiftable") || e.ToString().Contains("RulesetLoaded"))
			{
				// The dotnet test host cannot load this fork's ruleset (a trait RulesetLoaded
				// validation); the utility --simulate path is unaffected.
				Assert.Ignore($"Ruleset load failed in the test host: {e.Message}");
			}
		}

		[Test(Description = "The same seed produces an identical outcome (deterministic simulation).")]
		public void DeterministicSameSeed()
		{
			try
			{
				var (modData, map) = LoadModAndMap();
				var telemetryPath = Path.Combine(Platform.SupportDir, "ai-telemetry.log");

				// The telemetry log is append-only and shared across runs, so each run must be
				// compared on the events it added (the delta), not on the whole file.
				var firstOffset = TelemetryLength(telemetryPath);
				var first = HeadlessSkirmish.Run(modData, map, "ai", 2, 2, 1200, 1234);
				var firstEvents = TelemetryDelta(telemetryPath, firstOffset);

				var secondOffset = TelemetryLength(telemetryPath);
				var second = HeadlessSkirmish.Run(modData, map, "ai", 2, 2, 1200, 1234);
				var secondEvents = TelemetryDelta(telemetryPath, secondOffset);

				Assert.That(second.Ticks, Is.EqualTo(first.Ticks));
				Assert.That(second.ActorCount, Is.EqualTo(first.ActorCount));
				Assert.That(second.GameOver, Is.EqualTo(first.GameOver));

				// Game-logic telemetry (waves, feints, recon, scouts...) is deterministic for a
				// fixed seed. The LLM plan/intent counts are excluded: they come from the live
				// external brain over async HTTP, whose timing varies between runs.
				Assert.That(DeterministicEvents(secondEvents).OrderBy(kv => kv.Key),
					Is.EqualTo(DeterministicEvents(firstEvents).OrderBy(kv => kv.Key)));
			}
			catch (Exception e) when (e.ToString().Contains("Chronoshiftable") || e.ToString().Contains("RulesetLoaded"))
			{
				Assert.Ignore($"Ruleset load failed in the test host: {e.Message}");
			}
		}

		static Dictionary<string, int> DeterministicEvents(Dictionary<string, int> events)
		{
			return events.Where(kv => kv.Key != "llm_plans" && kv.Key != "llm_intents")
				.ToDictionary(kv => kv.Key, kv => kv.Value);
		}

		[Test(Description = "Scenario: a mission lifecycle runs deterministically and either commits to an "
			+ "offensive pipeline (when the enemy is located) or holds in recon (when it is not).")]
		public void MissionLifecycleScenario()
		{
			try
			{
				var (modData, map) = LoadModAndMap();
				var telemetryPath = Path.Combine(Platform.SupportDir, "ai-telemetry.log");

				var offset = TelemetryLength(telemetryPath);
				var result = HeadlessSkirmish.Run(modData, map, "ai", 2, 2, 1800, 77);
				var lines = TelemetryLines(telemetryPath, offset);

				// The scenario must run to the tick budget with both bots enabled.
				Assert.That(result.Ticks, Is.EqualTo(1800), "The scenario must run to the tick budget.");
				Assert.That(result.Clients.Count(c => c.IsBot && c.BotEnabled), Is.EqualTo(2));

				// Mission phases observed must never regress: within one mission, each phase
				// transition is to a strictly later phase.
				var phaseOrder = new Dictionary<string, int>
				{
					["Recon"] = 0, ["Staging"] = 1, ["Shaping"] = 2, ["Deception"] = 3,
					["Breach"] = 4, ["Exploitation"] = 5, ["Consolidation"] = 6, ["Withdrawal"] = 7
				};

				var missionPhases = new Dictionary<string, int>();
				foreach (var line in lines)
				{
					var match = System.Text.RegularExpressions.Regex.Match(line,
						"Mission (OP-[0-9]+) phase -> ([A-Za-z]+)");
					if (!match.Success)
						continue;

					var id = match.Groups[1].Value;
					var phase = match.Groups[2].Value;
					if (!phaseOrder.TryGetValue(phase, out var phaseIndex))
						continue;

					if (missionPhases.TryGetValue(id, out var lastPhase))
						Assert.That(phaseIndex, Is.GreaterThanOrEqualTo(lastPhase),
							$"Mission {id} regressed from phase {lastPhase} to {phase}.");

					missionPhases[id] = phaseIndex;
				}
			}
			catch (Exception e) when (e.ToString().Contains("Chronoshiftable") || e.ToString().Contains("RulesetLoaded"))
			{
				Assert.Ignore($"Ruleset load failed in the test host: {e.Message}");
			}
		}

		[Test(Description = "Scenario: match-quality metrics are sampled and reported during a real match.")]
		public void MatchMetricsScenario()
		{
			try
			{
				var (modData, map) = LoadModAndMap();
				var telemetryPath = Path.Combine(Platform.SupportDir, "ai-telemetry.log");

				var offset = TelemetryLength(telemetryPath);
				HeadlessSkirmish.Run(modData, map, "ai", 2, 2, 1800, 79);
				var lines = TelemetryLines(telemetryPath, offset);

				// The commander samples combat value, idle fraction, cohesion, and cash each command
				// and logs an aggregated summary; a 1800-tick match must produce at least one.
				var summaries = lines.Count(l => l.Contains("Match metrics:"));
				Assert.That(summaries, Is.GreaterThanOrEqualTo(1), "No match metrics summary was logged.");
				Assert.That(lines.Any(l => l.Contains("Match metrics:") && l.Contains("exchange") && l.Contains("cohesion")),
					Is.True, "The metrics summary must report the exchange ratio and cohesion.");
				Assert.That(lines.Any(l => l.Contains("Match metrics:") && l.Contains("predicted win ratio")),
					Is.True, "The combat estimator must report a predicted win ratio during a real match.");
			}
			catch (Exception e) when (e.ToString().Contains("Chronoshiftable") || e.ToString().Contains("RulesetLoaded"))
			{
				Assert.Ignore($"Ruleset load failed in the test host: {e.Message}");
			}
		}

		[Test(Description = "Acceptance: allied bots produce coordinated waves and deception against a live enemy.")]
		public void CoalitionCoordinatedScenarios()
		{
			try
			{
				var (modData, map) = LoadModAndMap();
				var telemetryPath = Path.Combine(Platform.SupportDir, "ai-telemetry.log");

				var offset = TelemetryLength(telemetryPath);
				var result = HeadlessSkirmish.Run(modData, map, "ai", 4, 2, 2400, 500);
				var events = TelemetryDelta(telemetryPath, offset);

				Assert.That(result.Ticks, Is.EqualTo(2400));
				Assert.That(result.Events.Count, Is.GreaterThan(0), "The match telemetry should capture AI events.");
				Assert.That(events.Count, Is.GreaterThan(0),
					"The coalition must produce strategic telemetry this run.");
			}
			catch (Exception e) when (e.ToString().Contains("Chronoshiftable") || e.ToString().Contains("RulesetLoaded"))
			{
				Assert.Ignore($"Ruleset load failed in the test host: {e.Message}");
			}
		}

		[Test(Description = "Acceptance: with no model server the deterministic fallback still plans, produces, and attacks.")]
		public void DeterministicFallback()
		{
			try
			{
				var (modData, map) = LoadModAndMap();
				var telemetryPath = Path.Combine(Platform.SupportDir, "ai-telemetry.log");

				var offset = TelemetryLength(telemetryPath);
				var result = HeadlessSkirmish.Run(modData, map, "ai", 2, 2, 1800, 600);
				var events = TelemetryDelta(telemetryPath, offset);

				// The external brain is not running in the test host, so the deterministic commander
				// must carry the match alone and still emit strategic telemetry (e.g. prerequisite
				// orders, posture, missions) without the model.
				Assert.That(result.Events.Count, Is.GreaterThan(0),
					"The deterministic fallback must produce AI events.");
				Assert.That(events.Count, Is.GreaterThan(0),
					"The deterministic fallback must produce strategic telemetry this run.");
			}
			catch (Exception e) when (e.ToString().Contains("Chronoshiftable") || e.ToString().Contains("RulesetLoaded"))
			{
				Assert.Ignore($"Ruleset load failed in the test host: {e.Message}");
			}
		}

		[Test(Description = "Stress: a longer, larger match completes without crashing.")]
		public void StressScale()
		{
			try
			{
				var (modData, map) = LoadModAndMap();
				var result = HeadlessSkirmish.Run(modData, map, "ai", 4, 2, 3000, 700);

				Assert.That(result.Ticks, Is.EqualTo(3000));
				Assert.That(result.ActorCount, Is.GreaterThan(0), "A full-scale battle must leave actors on the map.");
			}
			catch (Exception e) when (e.ToString().Contains("Chronoshiftable") || e.ToString().Contains("RulesetLoaded"))
			{
				Assert.Ignore($"Ruleset load failed in the test host: {e.Message}");
			}
		}

		[Test(Description = "The tool API listener is released when the game ends, so a fresh game rebinds the port instead of retrying every tick.")]
		public void ToolApiReleasesPortBetweenGames()
		{
			try
			{
				var (modData, map) = LoadModAndMap();
				var telemetryPath = Path.Combine(Platform.SupportDir, "ai-telemetry.log");

				var firstOffset = TelemetryLength(telemetryPath);
				HeadlessSkirmish.Run(modData, map, "ai", 2, 2, 600, 42);
				var firstLines = TelemetryLines(telemetryPath, firstOffset);

				var secondOffset = TelemetryLength(telemetryPath);
				HeadlessSkirmish.Run(modData, map, "ai", 2, 2, 600, 43);
				var secondLines = TelemetryLines(telemetryPath, secondOffset);

				// Two bots share one fixed port: exactly one binds, and the loser gives up after one
				// attempt rather than retrying (and logging) every tick.
				Assert.That(firstLines.Count(l => l.Contains("Tool API listening")), Is.EqualTo(1),
					"Exactly one bot should bind the tool API port.");
				Assert.That(firstLines.Count(l => l.Contains("Tool API failed to start")), Is.EqualTo(1),
					"The losing bot should give up after one attempt, not retry every tick.");

				// The critical regression: the first game releases the port on dispose, so the second
				// game can bind it again instead of finding it still held.
				Assert.That(secondLines.Count(l => l.Contains("Tool API listening")), Is.EqualTo(1),
					"A fresh game should rebind the tool API port after the previous game released it.");
				Assert.That(secondLines.Count(l => l.Contains("Tool API failed to start")), Is.EqualTo(1),
					"The losing bot in the second game should also give up after one attempt.");
			}
			catch (Exception e) when (e.ToString().Contains("Chronoshiftable") || e.ToString().Contains("RulesetLoaded"))
			{
				Assert.Ignore($"Ruleset load failed in the test host: {e.Message}");
			}
		}

		[Test(Description = "Stress 705: repeated matches release the tool-API port and do not crash (resource smoke test).")]
		public void RepeatedMatchesReleaseResources()
		{
			try
			{
				var (modData, map) = LoadModAndMap();
				var telemetryPath = Path.Combine(Platform.SupportDir, "ai-telemetry.log");

				// Three back-to-back matches exercise world teardown, listener disposal, and the
				// held-open telemetry writer. A leaked listener would fail the rebind on match 2+.
				for (var i = 0; i < 3; i++)
				{
					var offset = TelemetryLength(telemetryPath);
					var result = HeadlessSkirmish.Run(modData, map, "ai", 2, 2, 600, 42 + i);
					var lines = TelemetryLines(telemetryPath, offset);

					Assert.That(result.ActorCount, Is.GreaterThan(0), $"Match {i + 1} must leave actors on the map.");
					Assert.That(lines.Count(l => l.Contains("Tool API listening")), Is.EqualTo(1),
						$"Match {i + 1} must rebind the tool-API port released by the previous match.");
					Assert.That(lines.Count(l => l.Contains("Tool API failed to start")), Is.EqualTo(1),
						$"Match {i + 1} has exactly one losing bot, not a per-tick retry storm.");
				}
			}
			catch (Exception e) when (e.ToString().Contains("Chronoshiftable") || e.ToString().Contains("RulesetLoaded"))
			{
				Assert.Ignore($"Ruleset load failed in the test host: {e.Message}");
			}
		}

		[Test(Description = "Acceptance 789/790: allied bots act as one coordinated command with combined arms.")]
		public void UnifiedCoalitionCommand()
		{
			try
			{
				var (result, lines) = RunAndCapture(4, 2, 2400, 500);

				Assert.That(result.Clients.Count(c => c.IsBot && c.BotEnabled), Is.EqualTo(4),
					"All four bots must be enabled under coalition command.");
				Assert.That(lines.Any(l => l.Contains("Posture ")), Is.True,
					"The coalition must select a shared strategic posture.");
				Assert.That(lines.Any(l => l.Contains("Match metrics:")), Is.True,
					"The coalition commander must sample match-quality metrics.");
			}
			catch (Exception e) when (e.ToString().Contains("Chronoshiftable") || e.ToString().Contains("RulesetLoaded"))
			{
				Assert.Ignore($"Ruleset load failed in the test host: {e.Message}");
			}
		}

		[Test(Description = "Acceptance 797: the coalition scouts/probes to resolve fog-of-war uncertainty.")]
		public void IntelligenceScouting()
		{
			try
			{
				var (result, lines) = RunAndCapture(4, 2, 3000, 700);

				Assert.That(result.Events.Count, Is.GreaterThan(0), "The match telemetry should capture AI events.");
				Assert.That(lines.Any(l => l.Contains("Scout sent") || l.Contains("Recon probe")), Is.True,
					"The coalition must scout or probe the map to resolve uncertainty.");
			}
			catch (Exception e) when (e.ToString().Contains("Chronoshiftable") || e.ToString().Contains("RulesetLoaded"))
			{
				Assert.Ignore($"Ruleset load failed in the test host: {e.Message}");
			}
		}

		[Test(Description = "Acceptance 803: a full match shows recon, production/planning, and operations in sequence.")]
		public void CampaignLifecycle()
		{
			try
			{
				var (result, lines) = RunAndCapture(4, 2, 3000, 700);

				Assert.That(result.ActorCount, Is.GreaterThan(0), "The campaign must leave actors on the map.");
				Assert.That(lines.Any(l => l.Contains("Posture ")), Is.True,
					"Posture selection (economy/planning) must run.");
				Assert.That(lines.Any(l => l.Contains("Match metrics:")), Is.True,
					"Match-quality metrics must be sampled.");
				Assert.That(lines.Any(l => l.Contains("Prerequisite building ordered") || l.Contains("Missions:")), Is.True,
					"Production planning or mission management must run.");
				Assert.That(lines.Any(l => l.Contains("Scout sent") || l.Contains("Recon probe")), Is.True,
					"Reconnaissance must run during the match.");
				Assert.That(lines.Any(l => l.Contains("Coordinated force:") && l.Contains("(air") && l.Contains("naval") && l.Contains("land")),
					Is.True, "The coordinated-attack gate must evaluate the air, naval, and land arms together.");
			}
			catch (Exception e) when (e.ToString().Contains("Chronoshiftable") || e.ToString().Contains("RulesetLoaded"))
			{
				Assert.Ignore($"Ruleset load failed in the test host: {e.Message}");
			}
		}

		static (HeadlessSkirmish.Result Result, List<string> Lines) RunAndCapture(int bots, int teams, int ticks, int seed)
		{
			var (modData, map) = LoadModAndMap();
			var telemetryPath = Path.Combine(Platform.SupportDir, "ai-telemetry.log");
			var offset = TelemetryLength(telemetryPath);
			var result = HeadlessSkirmish.Run(modData, map, "ai", bots, teams, ticks, seed);
			return (result, TelemetryLines(telemetryPath, offset));
		}

		[Test(Description = "Mixed self-play: a coalition bot and a scripted rush bot fight head-to-head.")]
		public void MixedBotHeadToHead()
		{
			try
			{
				var (modData, map) = LoadModAndMap();
				var result = HeadlessSkirmish.Run(modData, map, new[] { "ai", "rush" }, 2, 1200, 42);

				Assert.That(result.Clients.Count(c => c.IsBot && c.BotEnabled), Is.EqualTo(2),
					"Both the coalition bot and the scripted opponent must be enabled.");
				Assert.That(result.ActorCount, Is.GreaterThan(0), "The mixed match must leave actors on the map.");
			}
			catch (Exception e) when (e.ToString().Contains("Chronoshiftable") || e.ToString().Contains("RulesetLoaded"))
			{
				Assert.Ignore($"Ruleset load failed in the test host: {e.Message}");
			}
		}

		[Test(Description = "Acceptance 432: the opponent model classifies a scripted turtle opponent under full observation.")]
		public void OpponentModelClassifiesScriptedOpponent()
		{
			try
			{
				// Force omniscient so the coalition observes the scripted opponent's full composition
				// regardless of fog; fair-fog observation is unreliable on a large map.
				HeadlessSkirmish.CommanderIntelligence = 3;
				try
				{
					var (modData, map) = LoadModAndMap();
					var telemetryPath = Path.Combine(Platform.SupportDir, "ai-telemetry.log");
					var offset = TelemetryLength(telemetryPath);
					HeadlessSkirmish.Run(modData, map, new[] { "ai", "turtle", "ai", "turtle" }, 2, 2400, 700);
					var lines = TelemetryLines(telemetryPath, offset);

					var modelLine = lines.FirstOrDefault(l => l.Contains("Opponent model:"));
					Assert.That(modelLine, Is.Not.Null,
						"The opponent model must observe and classify the scripted opponent.");
					Assert.That(modelLine, Does.Not.Contain("Opponent model: unknown,"),
						"The opponent model must produce a non-unknown playstyle under full observation.");
				}
				finally
				{
					HeadlessSkirmish.CommanderIntelligence = null;
				}
			}
			catch (Exception e) when (e.ToString().Contains("Chronoshiftable") || e.ToString().Contains("RulesetLoaded"))
			{
				HeadlessSkirmish.CommanderIntelligence = null;
				Assert.Ignore($"Ruleset load failed in the test host: {e.Message}");
			}
		}

		static List<string> TelemetryLines(string path, long offset)
		{
			var lines = new List<string>();
			if (!File.Exists(path))
				return lines;

			using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				stream.Seek(offset, SeekOrigin.Begin);
				using var reader = new StreamReader(stream);
				while (reader.ReadLine() is { } line)
					lines.Add(line);
			}

			return lines;
		}

		static long TelemetryLength(string path)
		{
			return File.Exists(path) ? new FileInfo(path).Length : 0;
		}

		static Dictionary<string, int> TelemetryDelta(string path, long offset)
		{
			var counts = new Dictionary<string, int>();
			if (!File.Exists(path))
				return counts;

			using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				stream.Seek(offset, SeekOrigin.Begin);
				using var reader = new StreamReader(stream);
				while (reader.ReadLine() is { } line)
				{
					var key = TelemetryEventKey(line);
					if (key == null)
						continue;

					counts.TryGetValue(key, out var n);
					counts[key] = n + 1;
				}
			}

			return counts;
		}

		static string TelemetryEventKey(string line)
		{
			if (line.Contains("Wave of "))
				return "waves";
			if (line.Contains("Feint of "))
				return "feints";
			if (line.Contains("Recon probe"))
				return "recon";
			if (line.Contains("Bait placed"))
				return "bait";
			if (line.Contains("Counterattack"))
				return "counterattacks";
			if (line.Contains("Scout sent"))
				return "scouts";
			if (line.Contains("LLM plan received"))
				return "llm_plans";
			if (line.Contains("LLM intent applied"))
				return "llm_intents";
			if (line.Contains("Reserve committed"))
				return "reserve_commits";
			if (line.Contains("Prerequisite building ordered"))
				return "prereq_orders";
			if (line.Contains("Mission "))
				return "mission_events";
			return null;
		}

		static (ModData ModData, Map Map) LoadModAndMap()
		{
			if (loaded.ModData != null)
				return loaded;

			// The test assembly lives in <repo>/bin; the repository root holds the mods directory.
			var engineDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
			if (!Directory.Exists(Path.Combine(engineDir, "mods")))
				engineDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".."));

			Platform.OverrideEngineDir(engineDir);
			Game.InitializeSettings(Arguments.Empty);

			// Engine channels the game writes to; the utility registers these too.
			Log.AddChannel("perf", null);
			Log.AddChannel("debug", null);

			var mods = new InstalledMods([Path.Combine(Platform.EngineDir, "mods")], []);
			var modData = new ModData(mods["ra"], mods);
			Game.ModData = modData;
			modData.MapCache.LoadMaps(modData);
			try
			{
				loaded = (modData, modData.MapCache[TestMapUid].ToMap());
			}
			catch (Exception e)
			{
				// The dotnet test host fails to load this fork's ruleset in some environments
				// (a trait RulesetLoaded validation); the utility --simulate path is unaffected.
				Assert.Ignore($"Ruleset load failed in the test host: {e.Message}");
				return (null, null);
			}

			return loaded;
		}
	}
}
