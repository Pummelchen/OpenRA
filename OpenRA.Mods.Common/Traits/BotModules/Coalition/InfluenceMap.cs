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
using System.Linq;

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	/// <summary>One source of influence: a unit or structure at a cell, with a strength and a reach.</summary>
	public readonly record struct InfluenceSource(int X, int Y, float Strength, int ReachCells, bool IsOwn);

	/// <summary>
	/// <para>
	/// Spatial reasoning for the commander (handbook §15.1). A coarse grid over the map in which
	/// every unit deposits influence that falls off with distance - own forces positive, observed
	/// enemy negative.
	/// </para>
	/// <para>
	/// This exists to replace "attack the enemy base region" with a continuous answer. A region index
	/// cannot express that the enemy is thin on one flank and massed on the other, so a commander
	/// reasoning in regions cannot choose *where* to hit, only *what*. The derived layers answer the
	/// spatial questions directly: control from influence, where the fighting is from the front line,
	/// and - the one that matters most - where the enemy is weak relative to what it is protecting.
	/// </para>
	/// </summary>
	public sealed class InfluenceMap
	{
		/// <summary>Cells per grid tile. Coarse on purpose: this is planning, not pathfinding.</summary>
		public const int TileSize = 4;

		public readonly int Width;
		public readonly int Height;

		readonly float[] own;
		readonly float[] enemy;

		public InfluenceMap(int mapWidthCells, int mapHeightCells)
		{
			Width = Math.Max(1, (mapWidthCells + TileSize - 1) / TileSize);
			Height = Math.Max(1, (mapHeightCells + TileSize - 1) / TileSize);
			own = new float[Width * Height];
			enemy = new float[Width * Height];
		}

		int Index(int tx, int ty) => ty * Width + tx;

		bool InBounds(int tx, int ty) => tx >= 0 && ty >= 0 && tx < Width && ty < Height;

		/// <summary>
		/// Deposits one source. Influence falls off linearly to zero at the source's reach, which is
		/// cheap and behaves correctly at the boundary - an exponential tail would leave every cell
		/// faintly contested and blur the front line this exists to find.
		/// </summary>
		public void Add(InfluenceSource source)
		{
			if (source.Strength <= 0f)
				return;

			var tx = source.X / TileSize;
			var ty = source.Y / TileSize;
			var reach = Math.Max(1, source.ReachCells / TileSize);
			var field = source.IsOwn ? own : enemy;

			for (var dy = -reach; dy <= reach; dy++)
			{
				for (var dx = -reach; dx <= reach; dx++)
				{
					var x = tx + dx;
					var y = ty + dy;
					if (!InBounds(x, y))
						continue;

					var distance = MathF.Sqrt(dx * dx + dy * dy);
					if (distance > reach)
						continue;

					field[Index(x, y)] += source.Strength * (1f - distance / reach);
				}
			}
		}

		public void AddAll(IEnumerable<InfluenceSource> sources)
		{
			foreach (var source in sources ?? [])
				Add(source);
		}

		public float Own(int tx, int ty) => InBounds(tx, ty) ? own[Index(tx, ty)] : 0f;

		public float Enemy(int tx, int ty) => InBounds(tx, ty) ? enemy[Index(tx, ty)] : 0f;

		/// <summary>Who controls this ground: positive is ours, negative is theirs.</summary>
		public float Influence(int tx, int ty) => Own(tx, ty) - Enemy(tx, ty);

		/// <summary>How much both sides have invested here. High tension is contested ground.</summary>
		public float Tension(int tx, int ty) => Own(tx, ty) + Enemy(tx, ty);

		/// <summary>
		/// How weakly held this ground is relative to how contested it is. High vulnerability means
		/// both sides care about the cell but neither dominates it - which is where an attack
		/// achieves something and where a defence is about to fail.
		/// </summary>
		public float Vulnerability(int tx, int ty) => Tension(tx, ty) - Math.Abs(Influence(tx, ty));

		/// <summary>
		/// Front-line tiles: those bordering a sign change in influence. This is where the fighting
		/// is, and where a defensive force belongs - not on the base perimeter, which is where the
		/// fighting will be after the front has already collapsed.
		/// </summary>
		public IEnumerable<(int X, int Y)> FrontLine()
		{
			for (var y = 0; y < Height; y++)
			{
				for (var x = 0; x < Width; x++)
				{
					var here = Influence(x, y);
					if (here == 0f && Tension(x, y) <= 0f)
						continue;

					var contested = false;
					for (var dy = -1; dy <= 1 && !contested; dy++)
						for (var dx = -1; dx <= 1 && !contested; dx++)
							if ((dx != 0 || dy != 0) && InBounds(x + dx, y + dy)
								&& Math.Sign(Influence(x + dx, y + dy)) != Math.Sign(here))
								contested = true;

					if (contested)
						yield return (x, y);
				}
			}
		}

		/// <summary>
		/// The best assault objective: enemy value weighted by how weakly it is held. Value alone
		/// sends the army at the strongest point of the base; vulnerability alone sends it at empty
		/// ground. The product is the cell worth taking that can actually be taken.
		/// </summary>
		public (int X, int Y)? BestAssaultTile(Func<int, int, float> enemyValueAt)
		{
			if (enemyValueAt == null)
				return null;

			(int X, int Y)? best = null;
			var bestScore = 0f;

			for (var y = 0; y < Height; y++)
			{
				for (var x = 0; x < Width; x++)
				{
					var value = enemyValueAt(x, y);
					if (value <= 0f)
						continue;

					// Vulnerability can be zero on ground the enemy holds outright; a small floor
					// keeps a high-value target in contention rather than discarding it entirely.
					var score = value * Math.Max(0.1f, Vulnerability(x, y));
					if (score > bestScore)
					{
						bestScore = score;
						best = (x, y);
					}
				}
			}

			return best;
		}

		/// <summary>
		/// The best feint objective: where the enemy is most invested and we are least, so the
		/// response is large and what we risk is small. A feint into ground we already hold draws
		/// nothing; a feint into the enemy's strongest point is just a bad attack.
		/// </summary>
		public (int X, int Y)? BestFeintTile()
		{
			(int X, int Y)? best = null;
			var bestScore = 0f;

			for (var y = 0; y < Height; y++)
			{
				for (var x = 0; x < Width; x++)
				{
					var threat = Enemy(x, y);
					if (threat <= 0f)
						continue;

					// Drawn response per credit risked: enemy investment divided by ours.
					var score = threat / (1f + Own(x, y));
					if (score > bestScore)
					{
						bestScore = score;
						best = (x, y);
					}
				}
			}

			return best;
		}

		/// <summary>Converts a tile back to the cell at its centre.</summary>
		public CPos CellOf(int tx, int ty)
		{
			return new CPos(tx * TileSize + TileSize / 2, ty * TileSize + TileSize / 2);
		}

		/// <summary>Total own and enemy influence, for a coarse global picture.</summary>
		public (float Own, float Enemy) Totals()
		{
			return (own.Sum(), enemy.Sum());
		}
	}
}
