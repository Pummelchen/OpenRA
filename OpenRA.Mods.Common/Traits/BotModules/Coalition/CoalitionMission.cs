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
using System.Text;

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	/// <summary>The operation types the coalition commander can create.</summary>
	public enum MissionType
	{
		Attack,
		Defend,
		Recon,
		Raid,
		Feint,
		Retreat,
		Transport,
		Counterattack,
		SpecialOps
	}

	public enum MissionStatus
	{
		Ready,
		Executing,
		Succeeded,
		Aborted,
		Failed
	}

	/// <summary>
	/// One coalition operation. Missions are stable: the manager keeps executing until the objective
	/// is achieved, an abort condition is reached, or a superior mission supersedes it.
	/// </summary>
	public sealed class CoalitionMission
	{
		public readonly string Id;
		public readonly MissionType Type;
		public readonly int CreatedTick;
		public string Objective;
		public int Priority;
		public CPos? Target;
		public MissionStatus Status = MissionStatus.Ready;
		public int Phase;
		public int MinForce;

		// Telemetry.
		public int FriendlyValueCommitted;
		public int EnemyValueEngaged;
		public int FeintBaselineEnemyCount;

		public CoalitionMission(string id, MissionType type, int createdTick, int priority, CPos? target, string objective)
		{
			Id = id;
			Type = type;
			CreatedTick = createdTick;
			Priority = priority;
			Target = target;
			Objective = objective;
		}
	}

	/// <summary>
	/// Owns the coalition's missions: creates, updates (completion/abort), and converts active missions
	/// into per-bot execution directives consumed by the strategic brain.
	/// </summary>
	public sealed class MissionManager
	{
		readonly List<CoalitionMission> missions = [];
		int nextMissionId;

		public IReadOnlyList<CoalitionMission> Missions => missions;

		public CoalitionMission CreateMission(MissionType type, int priority, CPos? target, string objective, int minForce = 0, int createdTick = 0)
		{
			var mission = new CoalitionMission($"OP-{++nextMissionId}", type, createdTick, priority, target, objective)
			{
				MinForce = minForce
			};
			missions.Add(mission);
			return mission;
		}

		public void CancelMission(string id)
		{
			missions.RemoveAll(m => m.Id == id);
		}

		/// <summary>
		/// Advances mission lifecycle against the blackboard: missions complete when their target region
		/// no longer holds the objective, and abort when the coalition force is decisively weaker.
		/// </summary>
		public void Update(CoalitionBlackboard blackboard, float coalitionStrength, float enemyStrength)
		{
			foreach (var mission in missions.ToArray())
			{
				switch (mission.Status)
				{
					case MissionStatus.Ready:
						mission.Status = MissionStatus.Executing;
						mission.Phase = 1;
						break;

					case MissionStatus.Executing:
						if (mission.Type == MissionType.Attack || mission.Type == MissionType.Raid || mission.Type == MissionType.Counterattack)
						{
							// Completed when nothing of the target remains known in the target region.
							var targetRegion = mission.Target != null ? blackboard.RegionOf(mission.Target.Value) : null;
							var enemiesThere = blackboard.EnemyIntel.Count(i =>
								targetRegion != null && targetRegion.Bounds.Contains(i.LastSeenCell.X, i.LastSeenCell.Y));
							if (enemiesThere == 0)
							{
								mission.Status = MissionStatus.Succeeded;
								CoalitionTelemetry.Log(blackboard.World, $"Mission {mission.Id} ({mission.Type}) succeeded: target cleared");
								continue;
							}
						}

						// Reconnaissance completes once the enemy position is established.
						if (mission.Type == MissionType.Recon && blackboard.EnemyRegion >= 0)
						{
							mission.Status = MissionStatus.Succeeded;
							continue;
						}

						// A feint succeeds when it changes enemy behavior: enemy units redeploy toward it.
						if (mission.Type == MissionType.Feint && mission.Target != null)
						{
							var nearby = blackboard.EnemyIntel.Count(i =>
								(i.LastSeenCell - mission.Target.Value).LengthSquared <= 20 * 20);
							if (mission.FeintBaselineEnemyCount == 0)
								mission.FeintBaselineEnemyCount = nearby;
							if (nearby > mission.FeintBaselineEnemyCount && nearby >= 2)
							{
								mission.EnemyValueEngaged = nearby - mission.FeintBaselineEnemyCount;
								CoalitionTelemetry.Log(blackboard.World,
									$"Feint {mission.Id} effective: drew {mission.EnemyValueEngaged} enemy units; FEINT_EFFECTIVENESS={mission.EnemyValueEngaged * 100f / System.Math.Max(1, mission.FriendlyValueCommitted):0.0}%");
								mission.Status = MissionStatus.Succeeded;
								continue;
							}
						}

						// Abort when the coalition cannot win the fight.
						if (enemyStrength > coalitionStrength * 1.8f)
						{
							mission.Status = MissionStatus.Aborted;
							CoalitionTelemetry.Log(blackboard.World, $"Mission {mission.Id} aborted: coalition outmatched");
							continue;
						}

						mission.Phase++;
						break;

					default:
						// Terminal missions are dropped after they are reported.
						missions.Remove(mission);
						break;
				}
			}
		}

		/// <summary>
		/// Converts the highest-priority active missions into the execution directive JSON consumed by
		/// the strategic brain (strategy/attack/feint/counter/retreat/transport).
		/// </summary>
		public string BuildDirectiveJson(CoalitionBlackboard blackboard, string produceJson, bool forceRetreat,
			string rolesJson = null, string forceJson = null, int attackTick = -1)
		{
			var active = missions.Where(m => m.Status == MissionStatus.Executing || m.Status == MissionStatus.Ready)
				.OrderByDescending(m => m.Priority)
				.ToArray();

			var attack = active.FirstOrDefault(m => m.Type == MissionType.Attack || m.Type == MissionType.Counterattack || m.Type == MissionType.Raid);
			var feint = active.FirstOrDefault(m => m.Type == MissionType.Feint);
			var defend = active.FirstOrDefault(m => m.Type == MissionType.Defend);
			var recon = active.FirstOrDefault(m => m.Type == MissionType.Recon);
			var transport = active.FirstOrDefault(m => m.Type == MissionType.Transport || m.Type == MissionType.SpecialOps);
			var retreat = forceRetreat || active.Any(m => m.Type == MissionType.Retreat);

			var sb = new StringBuilder();
			var strategy = attack != null ? "attack" : defend != null ? "defend" : "build";
			sb.Append("{\"strategy\":\"").Append(strategy).Append('"');
			if (attack != null && attack.Target != null)
				AppendTarget(sb, "attack", attack.Target.Value);
			if (feint != null && feint.Target != null)
				AppendTarget(sb, "feint", feint.Target.Value);
			if (recon != null && recon.Target != null)
				AppendTarget(sb, "recon", recon.Target.Value);
			if (defend != null && defend.Target != null)
				AppendTarget(sb, "counter", defend.Target.Value);
			if (transport != null && transport.Target != null)
			{
				AppendTarget(sb, "transport", transport.Target.Value);

				// The brain only runs insertions when a transport kind is named.
				sb.Append(",\"transportKind\":\"naval\"");
			}

			if (!string.IsNullOrEmpty(rolesJson))
				sb.Append(",\"roles\":").Append(rolesJson);
			if (!string.IsNullOrEmpty(produceJson))
				sb.Append(",\"produce\":").Append(produceJson);
			if (!string.IsNullOrEmpty(forceJson))
				sb.Append(",\"force\":").Append(forceJson);
			if (attackTick >= 0)
				sb.Append(",\"attackTick\":").Append(attackTick);
			if (retreat)
				sb.Append(",\"retreat\":true");
			sb.Append('}');
			return sb.ToString();
		}

		static void AppendTarget(StringBuilder sb, string key, CPos cell)
		{
			sb.Append(",\"").Append(key).Append("\":{\"x\":").Append(cell.X).Append(",\"y\":").Append(cell.Y).Append('}');
		}
	}
}
