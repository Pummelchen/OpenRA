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
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OpenRA.FileFormats;

namespace OpenRA.Mods.Common.UtilityCommands
{
	/// <summary>
	/// Reads an OpenRA replay (.orarep) and reports the match, so a recorded game - including one
	/// played by a human - can be evaluated the same way a headless batch is (reqs 645, 707).
	/// Optionally aligns the AI telemetry log against the replay timeline so a strategic decision can
	/// be read next to the replay tick it was taken at (req 708).
	/// </summary>
	public sealed class AnalyzeReplayCommand : IUtilityCommand
	{
		string IUtilityCommand.Name => "--analyze-replay";

		bool IUtilityCommand.ValidateArguments(string[] args)
		{
			return args.Length >= 2;
		}

		[Desc("REPLAY=<path to .orarep> [TELEMETRY=<path to ai-telemetry.log>] [DECISIONS=n]",
			  "Reports a recorded match: players, factions, teams, outcomes, duration and final tick. " +
			  "This accepts any OpenRA replay, so games played against a human can be evaluated with " +
			  "the same tooling as headless self-play. TELEMETRY aligns AI strategic decisions to the " +
			  "replay's tick timeline; DECISIONS caps how many are printed (default 40, 0 = all).")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			var replayPath = ParseArg(args, "REPLAY", null);
			if (string.IsNullOrEmpty(replayPath))
				throw new InvalidDataException("--analyze-replay requires REPLAY=<path to .orarep>.");

			if (!File.Exists(replayPath))
				throw new FileNotFoundException($"Replay not found: {replayPath}");

			var metadata = ReplayMetadata.Read(replayPath);
			if (metadata == null)
				throw new InvalidDataException(
					$"'{replayPath}' carries no replay metadata. Replays recorded by an older engine, "
					+ "or truncated by a crash, cannot be analyzed.");

			var info = metadata.GameInfo;
			Console.WriteLine($"Replay: {Path.GetFileName(replayPath)}");
			Console.WriteLine($"Mod: {info.Mod} {info.Version}");
			Console.WriteLine($"Map: {info.MapTitle} ({info.MapUid})");
			Console.WriteLine($"Duration: {info.Duration} over {info.FinalGameTick} ticks");
			Console.WriteLine($"Started: {info.StartTimeUtc:u}");

			var humans = info.Players.Count(p => p.IsHuman);
			var bots = info.Players.Count(p => p.IsBot);
			Console.WriteLine($"Participants: {humans} human, {bots} bot");
			Console.WriteLine();

			foreach (var player in info.Players.OrderBy(p => p.Team).ThenBy(p => p.ClientIndex))
			{
				var kind = player.IsBot ? $"bot:{player.BotType}" : player.IsHuman ? "human" : "other";
				Console.WriteLine(
					$"  team {player.Team}  {kind,-14} faction {player.FactionId,-10} " +
					$"spawn {player.SpawnPoint,-3} outcome {player.Outcome,-11} {player.Name}");
			}

			Console.WriteLine();

			// A human-versus-AI verdict is the point of analyzing a human replay (req 645): report
			// which side actually won rather than leaving the caller to read the table.
			var humanWon = info.Players.Any(p => p.IsHuman && p.Outcome == WinState.Won);
			var botWon = info.Players.Any(p => p.IsBot && p.Outcome == WinState.Won);
			if (humans > 0 && bots > 0)
				Console.WriteLine($"Human-vs-AI result: {(humanWon ? "human won" : botWon ? "AI won" : "no decision")}");

			Console.WriteLine($"Winners: {string.Join(", ", info.Players.Where(p => p.Outcome == WinState.Won).Select(p => p.Name))}");

			var telemetryPath = ParseArg(args, "TELEMETRY", null);
			if (string.IsNullOrEmpty(telemetryPath))
				return;

			if (!File.Exists(telemetryPath))
			{
				Console.WriteLine($"\nTelemetry not found: {telemetryPath}");
				return;
			}

			var limit = int.TryParse(ParseArg(args, "DECISIONS", "40"), NumberStyles.Integer,
				CultureInfo.InvariantCulture, out var parsed) ? parsed : 40;

			Console.WriteLine($"\nAI decisions aligned to the replay timeline (final tick {info.FinalGameTick}):");
			var decisions = AlignDecisions(File.ReadAllLines(telemetryPath), info.FinalGameTick).ToArray();
			if (decisions.Length == 0)
			{
				Console.WriteLine("  (no tick-stamped strategic decisions in this telemetry log)");
				return;
			}

			foreach (var (tick, percent, message) in limit > 0 ? decisions.Take(limit) : decisions)
				Console.WriteLine($"  tick {tick,-7} ({percent,3:0}% of match)  {message}");

			if (limit > 0 && decisions.Length > limit)
				Console.WriteLine($"  ... {decisions.Length - limit} further decisions (raise DECISIONS= to show them)");
		}

		static readonly Regex TickRegex = new(@"\btick[= ](\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		/// <summary>
		/// Extracts tick-stamped decisions from a telemetry log and expresses each as a position in
		/// the replay's timeline, so a decision can be located in the recorded game (req 708).
		/// </summary>
		public static IEnumerable<(int Tick, float Percent, string Message)> AlignDecisions(
			IReadOnlyList<string> telemetryLines, int finalTick)
		{
			foreach (var line in telemetryLines ?? [])
			{
				var match = TickRegex.Match(line ?? string.Empty);
				if (!match.Success)
					continue;

				if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tick))
					continue;

				// A decision past the replay's end belongs to a different match sharing the log file.
				if (finalTick > 0 && tick > finalTick)
					continue;

				var percent = finalTick > 0 ? tick * 100f / finalTick : 0f;
				yield return (tick, percent, line.Trim());
			}
		}

		static string ParseArg(string[] args, string name, string fallback)
		{
			var prefix = name + "=";
			var arg = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
			return arg == null ? fallback : arg[prefix.Length..];
		}
	}
}
