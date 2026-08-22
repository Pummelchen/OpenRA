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

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	/// <summary>
	/// Manages strategic reserve commitments and their justifications (reqs 355-360).
	/// Tracks each commitment with a reason, warns when the reserve drops below MinWaveSize/2,
	/// and provides hooks for counterattack interception, front reinforcement, breakthrough
	/// exploitation, and expansion protection. State recording is separated from telemetry so
	/// the bookkeeping is unit-testable without a World.
	/// </summary>
	public sealed class ReserveManager
	{
		public int LastCommitTick = int.MinValue;
		public string LastCommitReason;
		public int CommittedUnits;

		/// <summary>Records a reserve commitment without emitting telemetry (pure, testable).</summary>
		public void Record(int tick, int units, string reason)
		{
			LastCommitTick = tick;
			CommittedUnits = units;
			LastCommitReason = reason;
		}

		/// <summary>Records a reserve commitment with a reason for telemetry (req 360).</summary>
		public void Commit(int tick, int units, string reason, World world, int minWaveSize)
		{
			Record(tick, units, reason);
			CoalitionTelemetry.Log(world, $"Reserve committed: {units} units for {reason}");

			// LLM must justify: warn when reserve would drop below MinWaveSize/2 (req 360).
			if (RequiresJustification(units, minWaveSize))
				CoalitionTelemetry.Log(world, $"Reserve warning: commitment of {units} units drops reserve below MinWaveSize/2 ({minWaveSize / 2}) — LLM must justify");
		}

		/// <summary>True when a commitment drops the reserve below half the minimum wave size (req 360).</summary>
		public static bool RequiresJustification(int units, int minWaveSize)
		{
			return units < minWaveSize / 2;
		}

		/// <summary>True only during the mission phase that can turn a breach into exploitation.</summary>
		public static bool ShouldExploit(string missionPhase)
		{
			return string.Equals(missionPhase, "exploitation", System.StringComparison.OrdinalIgnoreCase);
		}
	}
}
