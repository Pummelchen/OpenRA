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
using System.IO;

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	/// <summary>
	/// Appends strategic decisions to a human-readable replay-style log in the support directory,
	/// so the coalition's reasoning can be audited after a match.
	/// </summary>
	public static class CoalitionTelemetry
	{
		static readonly System.Threading.Lock Sync = new();

		public static void Log(World world, string message)
		{
			var line = $"[{world.WorldTick * world.Timestep / 1000f:000.0}] {message}";
			Console.WriteLine($"AI: {line}");
			try
			{
				lock (Sync)
				{
					var path = Path.Combine(Platform.SupportDir, "ai-telemetry.log");
					File.AppendAllText(path, line + Environment.NewLine);
				}
			}
			catch (IOException)
			{
				// Telemetry is best-effort; the console line above still records the decision.
			}
		}
	}
}
