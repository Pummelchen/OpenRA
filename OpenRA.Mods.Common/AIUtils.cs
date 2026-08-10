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
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common
{
	public enum BuildingType { Building, Defense, Refinery }

	public enum WaterCheck { NotChecked, EnoughWater, NotEnoughWater, DontCheck }

	public static class AIUtils
	{
		/// <summary>
		/// Returns the size of the largest 8-connected region of cells satisfying the predicate.
		/// </summary>
		public static int LargestConnectedRegion(int width, int height, Func<int, int, bool> isCell)
		{
			var visited = new bool[width * height];
			var largest = 0;
			var stack = new Stack<(int X, int Y)>();
			for (var y = 0; y < height; y++)
			{
				for (var x = 0; x < width; x++)
				{
					var index = y * width + x;
					if (visited[index] || !isCell(x, y))
						continue;

					visited[index] = true;
					stack.Push((x, y));
					var count = 0;
					while (stack.Count > 0)
					{
						var (cx, cy) = stack.Pop();
						count++;
						for (var dy = -1; dy <= 1; dy++)
							for (var dx = -1; dx <= 1; dx++)
							{
								if (dx == 0 && dy == 0)
									continue;

								var nx = cx + dx;
								var ny = cy + dy;
								if (nx < 0 || nx >= width || ny < 0 || ny >= height)
									continue;

								var nIndex = ny * width + nx;
								if (visited[nIndex] || !isCell(nx, ny))
									continue;

								visited[nIndex] = true;
								stack.Push((nx, ny));
							}
					}

					if (count > largest)
						largest = count;
				}
			}

			return largest;
		}

		/// <summary>
		/// True when the explored cells contain a contiguous water body of at least the requested size.
		/// Only explored water counts, so the bot never acts on water it cannot see.
		/// </summary>
		public static bool HasLargeWaterBody(Map map, Func<CPos, bool> isExplored, FrozenSet<string> terrainTypes, int minimumCells)
		{
			if (minimumCells <= 0)
				return true;

			var largest = LargestConnectedRegion(map.MapSize.Width, map.MapSize.Height, (x, y) =>
			{
				var cell = new CPos(x, y);
				return map.Contains(cell) && isExplored(cell) && terrainTypes.Contains(map.GetTerrainInfo(cell).Type);
			});

			return largest >= minimumCells;
		}

		public static bool IsAreaAvailable<T>(World world, Player player, Map map, int radius, FrozenSet<string> terrainTypes)
		{
			var cells = world.ActorsHavingTrait<T>().Where(a => a.Owner == player);

			// TODO: Properly check building foundation rather than 3x3 area.
			return cells.Select(a => map.FindTilesInCircle(a.Location, radius)
				.Count(c => map.Contains(c) && terrainTypes.Contains(map.GetTerrainInfo(c).Type) &&
					Util.AdjacentCells(world, Target.FromCell(world, c))
						.All(ac => map.Contains(ac) && terrainTypes.Contains(map.GetTerrainInfo(ac).Type))))
							.Any(availableCells => availableCells > 0);
		}

		public static ILookup<string, ProductionQueue> FindQueuesByCategory(Player player)
		{
			return player.World.ActorsWithTrait<ProductionQueue>()
				.Where(a => a.Actor.Owner == player && a.Trait.Enabled)
				.Select(a => a.Trait)
				.ToLookup(pq => pq.Info.Type);
		}

		public static int CountActorsWithNameAndTrait<T>(string actorName, Player owner)
		{
			return owner.World.ActorsHavingTrait<T>().Count(a => a.Owner == owner && a.Info.Name == actorName);
		}

		public static int CountActorByCommonName<TTraitInfo>(
			ActorIndex.OwnerAndNamesAndTrait<TTraitInfo> actorIndex) where TTraitInfo : ITraitInfoInterface
		{
			return actorIndex.Actors.Count(a => !a.IsDead);
		}

		public static void BotDebug(string format, params object[] args)
		{
			if (Game.Settings.Debug.BotDebug)
				TextNotificationsManager.Debug(format, args);
		}

		public static IEnumerable<Order> ClearBlockersOrders(List<CPos> tiles, Player owner, Actor ignoreActor = null)
		{
			var world = owner.World;
			var adjacentTiles = Util.ExpandFootprint(tiles, true).Except(tiles)
				.Where(world.Map.Contains).ToList();

			var blockers = tiles.SelectMany(world.ActorMap.GetActorsAt)
				.Where(a => a.Owner == owner && a.IsIdle && (ignoreActor == null || a != ignoreActor))
				.Select(a => new TraitPair<IMove>(a, a.TraitOrDefault<IMove>()))
				.Where(x => x.Trait != null);

			foreach (var blocker in blockers)
			{
				CPos moveCell;
				if (blocker.Trait is Mobile mobile)
				{
					var availableCells = adjacentTiles.Where(t => mobile.CanEnterCell(t)).ToList();
					if (availableCells.Count == 0)
						continue;

					moveCell = blocker.Actor.ClosestCell(availableCells);
				}
				else if (blocker.Trait is Aircraft)
					moveCell = blocker.Actor.Location;
				else
					continue;

				yield return new Order("Move", blocker.Actor, Target.FromCell(world, moveCell), false)
				{
					SuppressVisualFeedback = true
				};
			}
		}
	}
}
