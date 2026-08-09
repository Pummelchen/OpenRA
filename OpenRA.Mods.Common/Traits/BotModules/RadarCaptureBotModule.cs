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
using OpenRA.FileFormats;
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Captures a full-map HD radar image - terrain with height shading, explored-vs-unexplored shroud, and unit " +
		"dots - for the external model brain. The image is written as a PNG on the configured interval; the external " +
		"brain then sends it to a vision-capable model as the bot's strategic view.")]
	[TraitLocation(SystemActors.Player)]
	public sealed class RadarCaptureBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Output image width in pixels; the height follows the map aspect ratio.")]
		public readonly int RadarCaptureWidth = 1920;

		[Desc("File path the radar PNG is written to. Defaults to the support directory.")]
		public readonly string RadarCapturePath = null;

		public override object Create(ActorInitializer init) { return new RadarCaptureBotModule(this, init); }
	}

	public sealed class RadarCaptureBotModule : ConditionalTrait<RadarCaptureBotModuleInfo>
	{
		readonly RadarCaptureBotModuleInfo info;

		Player player;

		public RadarCaptureBotModule(RadarCaptureBotModuleInfo info, ActorInitializer init)
			: base(info)
		{
			this.info = info;
		}

		/// <summary>The path of the most recent radar capture, or null before the first capture.</summary>
		public string LastCapturePath { get; private set; }

		/// <summary>
		/// Renders the whole map into an HD image now and returns the PNG path. Called on demand by the
		/// external brain, which paces captures to one per analysis cycle.
		/// </summary>
		public string CaptureNow(IBot bot)
		{
			if (IsTraitDisabled)
				return null;

			player = bot.Player;
			LastCapturePath = CaptureRadar();
			return LastCapturePath;
		}

		/// <summary>
		/// Renders the whole map into an HD image: terrain colors with height shading, the explored
		/// shroud (unexplored cells are darkened), and dots for own units (green) and explored enemy
		/// units (red). The image is scaled up to the configured width with nearest-neighbour sampling.
		/// </summary>
		string CaptureRadar()
		{
			var map = player.World.Map;
			var width = map.MapSize.Width;
			var height = map.MapSize.Height;
			var isRectangularIsometric = map.Grid.Type == MapGridType.RectangularIsometric;
			var bitmapWidth = isRectangularIsometric ? 2 * width - 1 : width;
			var top = map.Grid.MaximumTerrainHeight > 0 ? map.GetCellSpaceBounds().Top : map.Bounds.Top;

			// Per-cell dot colors: own units are green, explored enemies are red.
			var dotColors = new Dictionary<CPos, Color>();
			foreach (var a in player.World.Actors)
			{
				if (a.IsDead || !a.IsInWorld)
					continue;

				if (a.Owner == player)
				{
					dotColors[a.Location] = Color.Green;
					continue;
				}

				if (player.RelationshipWith(a.Owner) == PlayerRelationship.Enemy && player.Shroud.IsExplored(a.CenterPosition))
					dotColors[a.Location] = Color.Red;
			}

			var baseData = new byte[bitmapWidth * height * 4];
			for (var y = 0; y < height; y++)
			{
				for (var x = 0; x < width; x++)
				{
					var uv = new MPos(x + map.Bounds.Left, y + top);
					var explored = player.Shroud.IsExplored(uv);

					Color color;
					if (dotColors.TryGetValue(uv.ToCPos(map), out var dot))
						color = dot;
					else
					{
						var (left, right) = map.GetTerrainColorPair(uv);
						color = Color.FromArgb((left.R + right.R) / 2, (left.G + right.G) / 2, (left.B + right.B) / 2);
						if (!explored)
							color = Color.FromArgb(color.R / 4, color.G / 4, color.B / 4);
					}

					if (isRectangularIsometric)
					{
						var dx = uv.V & 1;
						var xOffset = 4 * (2 * x + dx);
						if (x + dx > 0)
						{
							var z = y * bitmapWidth * 4 + xOffset - 4;
							baseData[z++] = color.R;
							baseData[z++] = color.G;
							baseData[z++] = color.B;
							baseData[z] = color.A;
						}

						if (xOffset < bitmapWidth * 4)
						{
							var z = y * bitmapWidth * 4 + xOffset;
							baseData[z++] = color.R;
							baseData[z++] = color.G;
							baseData[z++] = color.B;
							baseData[z] = color.A;
						}
					}
					else
					{
						var z = y * bitmapWidth * 4 + 4 * x;
						baseData[z++] = color.R;
						baseData[z++] = color.G;
						baseData[z++] = color.B;
						baseData[z] = color.A;
					}
				}
			}

			// Scale the per-cell image up to the requested width.
			var scale = Math.Max(1, info.RadarCaptureWidth / bitmapWidth);
			var outWidth = bitmapWidth * scale;
			var outHeight = height * scale;
			var outData = new byte[outWidth * outHeight * 4];
			for (var y = 0; y < outHeight; y++)
			{
				var sy = y / scale;
				for (var x = 0; x < outWidth; x++)
				{
					var sx = x / scale;
					var src = (sy * bitmapWidth + sx) * 4;
					var dst = (y * outWidth + x) * 4;
					outData[dst] = baseData[src];
					outData[dst + 1] = baseData[src + 1];
					outData[dst + 2] = baseData[src + 2];
					outData[dst + 3] = baseData[src + 3];
				}
			}

			var path = info.RadarCapturePath ?? Path.Combine(Platform.SupportDir, "ai-radar.png");
			new Png(outData, SpriteFrameType.Rgba32, outWidth, outHeight).Save(path);
			return path;
		}
	}
}
