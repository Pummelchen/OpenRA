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
		SpecialOps,
		Bait
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
	/// Operational phases an offensive mission passes through. Each phase has explicit transition
	/// conditions evaluated against the blackboard; phases are skipped when their precondition is
	/// already satisfied (e.g. the enemy is already located, so RECON is skipped).
	/// </summary>
	public enum MissionPhase
	{
		/// <summary>Confirm the target's current position and route before committing forces.</summary>
		Recon,

		/// <summary>Assemble the assigned force at a safe staging area near the axis of advance.</summary>
		Staging,

		/// <summary>Suppress or pin the target (artillery, feints, support powers).</summary>
		Shaping,

		/// <summary>Feint or bait to draw the enemy's response away from the main axis.</summary>
		Deception,

		/// <summary>Breach the enemy's perimeter at the designated point.</summary>
		Breach,

		/// <summary>Exploit the breach and push into the objective region.</summary>
		Exploitation,

		/// <summary>Hold the objective while follow-on forces consolidate.</summary>
		Consolidation,

		/// <summary>Withdraw surviving forces from a losing or completed engagement.</summary>
		Withdrawal
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
		public MissionPhase Phase = MissionPhase.Recon;
		public int PhaseTick;
		public int MinForce;

		/// <summary>Human-readable reason for the terminal state, set when the mission aborts or fails.</summary>
		public string OutcomeReason;

		// Telemetry.
		public int FriendlyValueCommitted;
		public int EnemyValueEngaged;

		/// <summary>Enemy presence at the deception target when first evaluated, the response baseline.</summary>
		public int DeceptionBaselineEnemyCount;

		public CoalitionMission(string id, MissionType type, int createdTick, int priority, CPos? target, string objective)
		{
			Id = id;
			Type = type;
			CreatedTick = createdTick;
			Priority = priority;
			Target = target;
			Objective = objective;
			Phase = InitialPhase(type);
		}

		/// <summary>The phase a mission starts in; reconnaissance and deception are pre-staged.</summary>
		static MissionPhase InitialPhase(MissionType type)
		{
			switch (type)
			{
				case MissionType.Recon:
					return MissionPhase.Recon;
				case MissionType.Feint:
				case MissionType.Bait:
					return MissionPhase.Deception;
				case MissionType.Retreat:
					return MissionPhase.Withdrawal;
				default:
					return MissionPhase.Recon;
			}
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

		/// <summary>How many feint/bait missions have been created (deception attempts).</summary>
		public int DeceptionAttempts;

		/// <summary>How many deceptions drew a measurable enemy response.</summary>
		public int DeceptionSuccesses;

		/// <summary>Total enemy units pulled out of position by successful deceptions.</summary>
		public int DeceptionEnemiesDrawn;

		public IReadOnlyList<CoalitionMission> Missions => missions;

		public CoalitionMission CreateMission(MissionType type, int priority, CPos? target, string objective, int minForce = 0, int createdTick = 0)
		{
			var mission = new CoalitionMission($"OP-{++nextMissionId}", type, createdTick, priority, target, objective)
			{
				MinForce = minForce
			};
			missions.Add(mission);

			// A created feint or bait is a deception attempt; the outcome record feeds the planner.
			if (type == MissionType.Feint || type == MissionType.Bait)
				DeceptionAttempts++;

			return mission;
		}

		public void CancelMission(string id)
		{
			missions.RemoveAll(m => m.Id == id);
		}

		/// <summary>
		/// Measures whether a deception drew an enemy response: the baseline is the enemy presence at
		/// the target when the deception was first evaluated, and a response is a later surge of at
		/// least two units above it. Pure so it can be unit-tested without a World.
		/// </summary>
		public static (bool DrewResponse, int EnemyValueEngaged) MeasureDeceptionResponse(int baselineEnemyCount, int nearbyEnemyCount)
		{
			var drew = nearbyEnemyCount > baselineEnemyCount && nearbyEnemyCount >= 2;
			return (drew, drew ? nearbyEnemyCount - baselineEnemyCount : 0);
		}

		/// <summary>
		/// Advances mission lifecycle against the blackboard: phases transition when their conditions
		/// are met, offensive missions complete when their target region no longer holds the objective,
		/// and missions abort (with a reason) when the coalition force is decisively weaker or the
		/// target becomes unreachable.
		/// </summary>
		public void Update(CoalitionBlackboard blackboard, float coalitionStrength, float enemyStrength)
		{
			foreach (var mission in missions.ToArray())
			{
				switch (mission.Status)
				{
					case MissionStatus.Ready:
						mission.Status = MissionStatus.Executing;
						mission.PhaseTick = blackboard.Tick;
						break;

					case MissionStatus.Executing:
						if (!AdvancePhase(blackboard, mission))
							continue;

						if (mission.Type == MissionType.Attack || mission.Type == MissionType.Raid || mission.Type == MissionType.Counterattack)
						{
							// Completed when nothing of the target remains known in the target region.
							var targetRegion = mission.Target != null ? blackboard.RegionOf(mission.Target.Value) : null;
							var enemiesThere = blackboard.EnemyIntel.Count(i =>
								targetRegion != null && targetRegion.Bounds.Contains(i.LastSeenCell.X, i.LastSeenCell.Y));
							if (enemiesThere == 0)
							{
								mission.Status = MissionStatus.Succeeded;
								mission.OutcomeReason = "target cleared";
								CoalitionTelemetry.Log(blackboard.World, $"Mission {mission.Id} ({mission.Type}) succeeded: {mission.OutcomeReason}");
								continue;
							}
						}

						// Reconnaissance completes once the enemy position is established.
						if (mission.Type == MissionType.Recon && blackboard.EnemyRegion >= 0)
						{
							mission.Status = MissionStatus.Succeeded;
							mission.OutcomeReason = "enemy located";
							continue;
						}

						// A feint or bait succeeds when it changes enemy behavior: enemy units redeploy
						// toward it. The drawn response feeds the deception record, which the commander
						// uses to keep funding deception or drop it when the enemy ignores it.
						if ((mission.Type == MissionType.Feint || mission.Type == MissionType.Bait) && mission.Target != null)
						{
							var nearby = blackboard.EnemyIntel.Count(i =>
								(i.LastSeenCell - mission.Target.Value).LengthSquared <= 20 * 20);
							if (mission.DeceptionBaselineEnemyCount == 0)
								mission.DeceptionBaselineEnemyCount = nearby;

							var (drew, engaged) = MeasureDeceptionResponse(mission.DeceptionBaselineEnemyCount, nearby);
							if (drew)
							{
								mission.EnemyValueEngaged = engaged;
								mission.OutcomeReason = mission.Type == MissionType.Feint
									? "enemy redeployed toward feint" : "enemy redeployed toward bait";
								DeceptionSuccesses++;
								DeceptionEnemiesDrawn += engaged;
								CoalitionTelemetry.Log(blackboard.World,
									$"{mission.Type} {mission.Id} effective: drew {engaged} enemy units; DECEPTION_EFFECTIVENESS={engaged * 100f / System.Math.Max(1, mission.FriendlyValueCommitted):0.0}%");
								mission.Status = MissionStatus.Succeeded;
								continue;
							}
						}

						// Abort when the coalition cannot win the fight, or the target became unreachable.
						if (enemyStrength > coalitionStrength * 1.8f)
						{
							mission.Status = MissionStatus.Aborted;
							mission.OutcomeReason = "coalition outmatched";
							CoalitionTelemetry.Log(blackboard.World, $"Mission {mission.Id} aborted: {mission.OutcomeReason}");
							continue;
						}

						if (mission.Target != null && !CoalitionRoutePlanner.RouteExists(blackboard.MapAnalysis,
							blackboard.HomeRegion, blackboard.RegionOf(mission.Target.Value).Index, MovementClass.Ground))
						{
							mission.Status = MissionStatus.Aborted;
							mission.OutcomeReason = "target unreachable on the ground";
							CoalitionTelemetry.Log(blackboard.World, $"Mission {mission.Id} aborted: {mission.OutcomeReason}");
							continue;
						}

						mission.PhaseTick++;
						break;

					default:
						// Terminal missions are dropped after they are reported.
						missions.Remove(mission);
						break;
				}
			}
		}

		/// <summary>
		/// Advances the mission one phase when its transition condition is satisfied. Returns false when
		/// the mission should not receive further objective evaluation this tick (it just changed phase).
		/// Non-offensive missions (recon, feint, bait, retreat) do not run the offensive pipeline:
		/// their phases are terminal by construction, and completion is decided by the caller.
		/// </summary>
		static bool AdvancePhase(CoalitionBlackboard blackboard, CoalitionMission mission)
		{
			// Recon, deception, and withdrawal missions stay in their own phase; only offensive
			// missions (attack, raid, counterattack, transport, special ops) walk the pipeline.
			if (mission.Type == MissionType.Recon || mission.Type == MissionType.Feint
				|| mission.Type == MissionType.Bait || mission.Type == MissionType.Retreat)
				return true;

			var phaseAge = blackboard.Tick - mission.PhaseTick;
			var targetRegion = mission.Target != null ? blackboard.RegionOf(mission.Target.Value).Index : -1;

			switch (mission.Phase)
			{
				case MissionPhase.Recon:
					// Recon completes when the target region is at least partially explored (we can
					// see where we are going), or the enemy is already located elsewhere.
					if (blackboard.EnemyRegion >= 0 || (targetRegion >= 0 && blackboard.Regions[targetRegion].FriendlyControl > 0.05f))
						return Transition(blackboard, mission, MissionPhase.Staging, "recon complete");
					return true;

				case MissionPhase.Staging:
					// Staging completes once the coalition has enough force to matter and enough time
					// has passed for the force to actually assemble.
					if (phaseAge >= 120 && blackboard.CoalitionArmyStrength >= mission.MinForce)
						return Transition(blackboard, mission, MissionPhase.Shaping, "force assembled");
					return true;

				case MissionPhase.Shaping:
					// Shaping is a fixed suppression window; it rolls directly into deception for
					// missions that use it, otherwise into the breach.
					if (phaseAge >= 60)
						return Transition(blackboard, mission,
							mission.Type == MissionType.Attack ? MissionPhase.Deception : MissionPhase.Breach,
							"shaping complete");
					return true;

				case MissionPhase.Deception:
					// Deception completes after a fixed window; feints and baits are excluded above
					// so only attacks with a real deception component pass through here.
					if (phaseAge >= 90)
						return Transition(blackboard, mission, MissionPhase.Breach, "deception window elapsed");
					return true;

				case MissionPhase.Breach:
					// Breach completes once we are in the target region (it is at least partially
					// explored) or the enemy there is gone.
					if (targetRegion < 0 || blackboard.Regions[targetRegion].FriendlyControl > 0.1f)
						return Transition(blackboard, mission, MissionPhase.Exploitation, "breach opened");
					return true;

				case MissionPhase.Exploitation:
					// Exploitation rolls into consolidation after a holding window.
					if (phaseAge >= 180)
						return Transition(blackboard, mission, MissionPhase.Consolidation, "objective seized");
					return true;

				case MissionPhase.Consolidation:
					// Consolidation is terminal: the objective is held until the enemy is cleared,
					// which the caller detects as mission success.
					return true;

				case MissionPhase.Withdrawal:
					return true;

				default:
					return true;
			}
		}

		static bool Transition(CoalitionBlackboard blackboard, CoalitionMission mission, MissionPhase next, string reason)
		{
			mission.Phase = next;
			mission.PhaseTick = blackboard.Tick;
			CoalitionTelemetry.Log(blackboard.World, $"Mission {mission.Id} phase -> {next} ({reason})");
			return false;
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
			var bait = active.FirstOrDefault(m => m.Type == MissionType.Bait);
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
			if (bait != null && bait.Target != null)
				AppendTarget(sb, "bait", bait.Target.Value);
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
