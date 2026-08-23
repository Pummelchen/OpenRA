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
using OpenRA.FileSystem;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common.UtilityCommands
{
	sealed class SimulateCommand : IUtilityCommand
	{
		string IUtilityCommand.Name => "--simulate";

		bool IUtilityCommand.ValidateArguments(string[] args)
		{
			return args.Length >= 2;
		}

		[Desc("MAP=<map uid or path> [BOTS=n] [TEAMS=n] [TICKS=n] [SEED=n] [BOT=type] [BOT_TYPES=a,b,...] [INTELLIGENCE=n] [FACTION=name] [ENABLE_LLM=1]",
			  "Run a headless skirmish simulation and report the outcome. BOT_TYPES lists one bot type " +
			  "per bot (comma-separated, in team order) for mixed self-play; otherwise BOT applies to all. " +
			  "INTELLIGENCE overrides the coalition commander's fog advantage (0 = fair fog, 3 = omniscient). " +
			  "FACTION pins every bot to one playable faction (e.g. soviet, allies) so a batch varies only the strategy. " +
			  "ENABLE_LLM=1 enables the real LLM brain (requires model server on port 8765); default is deterministic-only.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			// The engine assumes Game.ModData is set; do so before touching any map data.
			Game.ModData = utility.ModData;

			// The game initializes the map preview cache in its startup path; the utility does not.
			utility.ModData.MapCache.LoadMaps(utility.ModData);

			var mapArg = ParseArg(args, "MAP", null);
			if (string.IsNullOrEmpty(mapArg))
				throw new InvalidDataException("--simulate requires MAP=<map uid or path>.");

			var bots = ParseInt(args, "BOTS", 4);
			var teams = ParseInt(args, "TEAMS", 2);
			var ticks = ParseInt(args, "TICKS", 12000);
			var seed = ParseInt(args, "SEED", 12345);
			var botType = ParseArg(args, "BOT", "ai");
			var botTypesArg = ParseArg(args, "BOT_TYPES", null);
			var intelligenceArg = ParseArg(args, "INTELLIGENCE", null);
			if (intelligenceArg != null && int.TryParse(intelligenceArg, out var intel))
				HeadlessSkirmish.CommanderIntelligence = intel;

			var factionArg = ParseArg(args, "FACTION", null);
			if (!string.IsNullOrEmpty(factionArg))
				HeadlessSkirmish.CommanderFaction = factionArg;

			Map map;
			try
			{
				map = LoadMap(utility, mapArg);
			}
			catch (KeyNotFoundException)
			{
				Console.WriteLine($"Map '{mapArg}' is not installed. Available skirmish maps:");
				foreach (var preview in utility.ModData.MapCache.Where(p =>
					p.Status == MapStatus.Available && p.Visibility.HasFlag(MapVisibility.Lobby)))
					Console.WriteLine($"  {preview.Uid}");
				return;
			}

			HeadlessSkirmish.IsBotEnabled = p => p.PlayerActor.TraitsImplementing<ModularBot>().Any(b => b.IsEnabled);
			HeadlessSkirmish.CaptureKillCosts = p =>
			{
				var stats = p.PlayerActor.TraitOrDefault<PlayerStatistics>();
				return stats == null ? (0, 0) : (stats.KillsCost, stats.DeathsCost);
			};

			// Self-play evaluation must be replay-deterministic; the async model consultation (even a
			// timeout) introduces thread-timing nondeterminism, so the external brain is disabled by
			// default. Set ENABLE_LLM=1 to run with the real LLM brain (radar images + model server)
			// for testing the full LLM pipeline headlessly.
			HeadlessSkirmish.DisableExternalBrain = ParseArg(args, "ENABLE_LLM", null) != "1";

			var botTypes = botTypesArg?.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToArray();
			var botCount = botTypes?.Length ?? bots;

			Console.WriteLine($"Simulating {map.Title} ({map.Uid}): {botCount} bots in {teams} teams for {ticks} ticks (seed {seed})...");

			HeadlessSkirmish.Result result;
			try
			{
				result = botTypes != null
					? HeadlessSkirmish.Run(utility.ModData, map, botTypes, teams, ticks, seed)
					: HeadlessSkirmish.Run(utility.ModData, map, botType, botCount, teams, ticks, seed);
			}
			catch (Exception e)
			{
				Console.WriteLine($"Simulation failed: {e.Message}");
				throw;
			}

			Console.WriteLine($"Finished: {result.Ticks} ticks, {(result.GameOver ? "game over" : "time limit reached")}, {result.ActorCount} actors");
			foreach (var client in result.Clients.OrderBy(c => c.Index))
				Console.WriteLine($"  {client.Index,2}  {(client.IsBot ? (client.BotEnabled ? "AI enabled" : "AI disabled") : "observer")}  " +
					$"team {client.Team}  faction {client.Faction}  {client.Name}  kills_cost={client.KillsCost} deaths_cost={client.DeathsCost}");
			if (result.Winners.Count > 0)
				Console.WriteLine($"Winners: {string.Join(", ", result.Winners)}");

			if (result.Events.Count > 0)
			{
				Console.WriteLine("Match telemetry:");
				foreach (var kv in result.Events.OrderByDescending(kv => kv.Value))
					Console.WriteLine($"  {kv.Key,-16} {kv.Value}");
			}
		}

		/// <summary>Loads a map by path (registering it with the mod's map cache) or by installed map uid.</summary>
		static Map LoadMap(Utility utility, string mapArg)
		{
			if (Directory.Exists(mapArg))
			{
				var fullPath = Path.GetFullPath(mapArg);
				var folder = Path.GetFileName(fullPath);
				utility.ModData.MapCache.LoadMap(folder, new Folder(Path.GetDirectoryName(fullPath)), MapClassification.User, null);
				var uid = utility.ModData.MapCache.LastModifiedMap;
				if (uid == null)
					throw new InvalidDataException($"Failed to load map from '{mapArg}'.");
				return utility.ModData.MapCache[uid].ToMap();
			}

			return utility.ModData.MapCache[mapArg].ToMap();
		}

		static string ParseArg(string[] args, string key, string def)
		{
			var prefix = key + "=";
			var arg = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
			return arg?[prefix.Length..] ?? def;
		}

		static int ParseInt(string[] args, string key, int def)
		{
			var value = ParseArg(args, key, null);
			return value == null || !int.TryParse(value, out var result) ? def : result;
		}
	}
}
