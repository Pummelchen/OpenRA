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

using System.Collections.Generic;
using System.Linq;

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	/// <summary>
	/// Priority levels for force commitments. A force can only be reassigned by a commitment of
	/// strictly higher priority; equal or lower requests are rejected with a machine-readable reason.
	/// </summary>
	public enum ArbiterPriority
	{
		Idle = 0,
		Staging = 1,
		Recon = 2,
		Reserve = 3,
		Defense = 4,
		ActiveCombat = 5,
		SpecialMission = 6,
		Survival = 7
	}

	/// <summary>One force commitment: a mission owns a force at a priority until released.</summary>
	public sealed class ForceCommitment
	{
		public readonly string MissionId;
		public readonly string Role;
		public readonly string Force;
		public readonly ArbiterPriority Priority;
		public readonly int Tick;
		public bool Released;

		public ForceCommitment(string missionId, string role, string force, ArbiterPriority priority, int tick)
		{
			MissionId = missionId;
			Role = role;
			Force = force;
			Priority = priority;
			Tick = tick;
		}
	}

	/// <summary>
	/// The coalition order arbiter: a persistent force-commitment ledger. Missions assign forces to
	/// themselves at a priority; a conflicting assignment (a force already committed to a mission of
	/// equal or higher priority) is rejected with a machine-readable reason instead of being silently
	/// honored. Missions release their forces on completion, cancellation, or supersession. Pure and
	/// engine-free so it is unit-testable without a World.
	/// </summary>
	public sealed class CoalitionOrderArbiter
	{
		readonly List<ForceCommitment> commitments = [];
		readonly Dictionary<string, ForceCommitment> byForce = [];

		public IReadOnlyList<ForceCommitment> Commitments => commitments;

		/// <summary>
		/// Assigns a force to a mission. Re-assigning the same mission+force is a no-op. Returns the
		/// machine-readable rejection when the force is already committed elsewhere at equal or higher
		/// priority, or an empty list when the assignment is accepted (or supersedes a lower one).
		/// </summary>
		public IReadOnlyList<string> Assign(string missionId, string role, ArbiterPriority priority, string force)
		{
			var rejections = new List<string>();
			if (byForce.TryGetValue(force, out var existing) && !existing.Released)
			{
				if (existing.MissionId == missionId)
					return rejections;

				if (existing.Priority >= priority)
				{
					rejections.Add($"REJECTED_CONFLICT: force \"{force}\" is already committed to \"{existing.MissionId}\" at priority {existing.Priority} (>= {priority})");
					return rejections;
				}

				// The new commitment supersedes a lower-priority one.
				existing.Released = true;
			}

			var commitment = new ForceCommitment(missionId, role, force, priority, 0);
			commitments.Add(commitment);
			byForce[force] = commitment;
			return rejections;
		}

		/// <summary>Releases every force still committed to the given mission.</summary>
		public void ReleaseMission(string missionId)
		{
			foreach (var commitment in commitments.Where(c => c.MissionId == missionId && !c.Released))
				commitment.Released = true;
		}

		/// <summary>Releases a specific force, whatever mission holds it.</summary>
		public void ReleaseForce(string force)
		{
			if (byForce.TryGetValue(force, out var commitment) && !commitment.Released)
				commitment.Released = true;
		}

		/// <summary>The mission currently holding a force, or null.</summary>
		public string MissionOf(string force)
		{
			return byForce.TryGetValue(force, out var commitment) && !commitment.Released
				? commitment.MissionId
				: null;
		}

		/// <summary>The role of the force's current commitment, or null.</summary>
		public string RoleOf(string force)
		{
			return byForce.TryGetValue(force, out var commitment) && !commitment.Released
				? commitment.Role
				: null;
		}
	}
}
