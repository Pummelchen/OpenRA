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

		[Desc("MAP=<map uid or path> [BOTS=n] [TEAMS=n] [TICKS=n] [SEED=n] [BOT=type]",
			  "Run a headless skirmish simulation and report the outcome.")]
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

			Console.WriteLine($"Simulating {map.Title} ({map.Uid}): {bots} bots in {teams} teams for {ticks} ticks (seed {seed})...");

			HeadlessSkirmish.Result result;
			try
			{
				result = HeadlessSkirmish.Run(utility.ModData, map, botType, bots, teams, ticks, seed);
			}
			catch (Exception e)
			{
				Console.WriteLine($"Simulation failed: {e.Message}");
				throw;
			}

			Console.WriteLine($"Finished: {result.Ticks} ticks, {(result.GameOver ? "game over" : "time limit reached")}, {result.ActorCount} actors");
			foreach (var client in result.Clients.OrderBy(c => c.Index))
				Console.WriteLine($"  {client.Index,2}  {(client.IsBot ? (client.BotEnabled ? "AI enabled" : "AI disabled") : "observer")}  " +
					$"team {client.Team}  faction {client.Faction}  {client.Name}");
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
