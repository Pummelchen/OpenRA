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
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using OpenRA.Mods.Common.Commander.Terrain;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common.UtilityCommands
{
	/// <summary>
	/// Reports the commander's region decomposition of a map, so phase 1 of the rebuild can be
	/// checked against terrain a person can look at. The synthetic-grid tests prove the algorithm
	/// correct; this proves it produces something sane on the maps the benchmark actually uses.
	/// </summary>
	sealed class RegionGraphCommand : IUtilityCommand
	{
		string IUtilityCommand.Name => "--region-graph";

		bool IUtilityCommand.ValidateArguments(string[] args) => args.Length >= 2;

		[Desc("MAP=<uid or title> [LOCOMOTOR=name] [ASCII=1]",
			"Decompose a map into the commander's regions and chokepoints.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			var modData = Game.ModData = utility.ModData;
			modData.MapCache.LoadMaps(modData);

			var mapArg = Arg(args, "MAP");
			var locomotorName = Arg(args, "LOCOMOTOR") ?? "tracked";
			var ascii = Arg(args, "ASCII") == "1";

			var preview = ResolveMap(modData, mapArg);
			if (preview == null)
			{
				Console.WriteLine($"No map matching '{mapArg}'.");
				return;
			}

			var map = preview.ToMap();
			var locomotors = map.Rules.Actors[SystemActors.World].TraitInfos<LocomotorInfo>().ToArray();
			var locomotor = locomotors.FirstOrDefault(l => l.Name == locomotorName);
			if (locomotor == null)
			{
				Console.WriteLine($"No locomotor '{locomotorName}'. Available: " +
					string.Join(", ", locomotors.Select(l => l.Name)));
				return;
			}

			var timer = Stopwatch.StartNew();
			var graph = MapRegions.Build(map, locomotor);
			timer.Stop();

			var passable = 0;
			foreach (var label in graph.Labels)
				if (label >= 0)
					passable++;

			Console.WriteLine($"{preview.Title}  {map.Bounds.Width}x{map.Bounds.Height}  locomotor={locomotor.Name}");
			Console.WriteLine($"  regions={graph.Regions.Length} chokepoints={graph.Chokepoints.Length} " +
				$"passable={passable} built in {timer.Elapsed.TotalMilliseconds:F1} ms");

			foreach (var region in graph.Regions.OrderByDescending(r => r.CellCount))
			{
				var cell = MapRegions.ToCell(map, region.CentreX, region.CentreY);
				Console.WriteLine($"    R{region.Id,-3} cells={region.CellCount,-6} centre={cell} " +
					$"exits={region.Chokepoints.Length}");
			}

			foreach (var choke in graph.Chokepoints)
			{
				var cell = MapRegions.ToCell(map, choke.CentreX, choke.CentreY);
				Console.WriteLine($"    C{choke.Id,-3} R{choke.RegionA}-R{choke.RegionB} " +
					$"capacity={choke.Capacity,-4} at {cell}");
			}

			// The min-cut between the two most distant large regions approximates the defensive
			// line a commander starting in one of them would have to hold.
			if (graph.Regions.Length >= 2)
			{
				var ordered = graph.Regions.OrderByDescending(r => r.CellCount).ToArray();
				var a = ordered[0].Id;
				var b = ordered.Skip(1).OrderByDescending(r =>
					Math.Abs(r.CentreX - ordered[0].CentreX) + Math.Abs(r.CentreY - ordered[0].CentreY)).First().Id;

				var cut = graph.MinCutBetween(a, b);
				Console.WriteLine($"  min-cut R{a} to R{b}: capacity={cut.Value} across " +
					$"{cut.CutEdges.Length} chokepoint(s)");
			}

			if (ascii)
				PrintAscii(graph);
		}

		static void PrintAscii(RegionGraph graph)
		{
			const string Glyphs = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
			var stepX = Math.Max(1, graph.Width / 110);
			var stepY = Math.Max(1, graph.Height / 55);

			Console.WriteLine();
			for (var y = 0; y < graph.Height; y += stepY)
			{
				var line = new char[(graph.Width + stepX - 1) / stepX];
				var i = 0;
				for (var x = 0; x < graph.Width; x += stepX)
				{
					var region = graph.RegionAt(x, y);
					line[i++] = region < 0 ? '.' : Glyphs[region % Glyphs.Length];
				}

				Console.WriteLine(new string(line, 0, i));
			}

			Console.WriteLine();
		}

		static MapPreview ResolveMap(ModData modData, string mapArg)
		{
			if (string.IsNullOrEmpty(mapArg))
				return null;

			var available = new List<MapPreview>(modData.MapCache);

			var byUid = available.FirstOrDefault(m => m.Uid == mapArg);
			if (byUid != null)
				return byUid;

			string Normalise(string s) =>
				new(s.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c)).ToArray());

			var wanted = Normalise(mapArg);
			return available.FirstOrDefault(m => m.Title != null && Normalise(m.Title) == wanted)
				?? available.FirstOrDefault(m => m.Title != null && Normalise(m.Title).Contains(wanted));
		}

		static string Arg(string[] args, string key)
		{
			var prefix = key + "=";
			foreach (var a in args)
				if (a.StartsWith(prefix, StringComparison.Ordinal))
					return a[prefix.Length..];

			return null;
		}
	}
}
