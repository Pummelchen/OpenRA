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
using System.Text;
using System.Text.Json;

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
		Bait,
		Breakthrough,
		Siege,
		Harassment,
		EconomyRaid,
		ProductionRaid,
		ExpansionDenial,
		ChokepointSeizure,
		Flank,
		AirStrike,
		NavalStrike,
		SupportPowerStrike,
		MobileDefense,
		AntiAirUmbrella,
		NavalScreen,
		DelayingAction,
		Evacuation,
		Escort,
		DeepRecon,
		AirRecon,
		NavalRecon,
		RouteRecon,
		ExpansionSearch,
		DefenseProbe,
		Demonstration,
		DecoyTransport,
		Pincer,
		NavalBlockade,
		FakeBuildup
	}

	public enum MissionStatus
	{
		Ready,
		Executing,
		Succeeded,
		Aborted,
		Failed,
		Cancelled
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

		/// <summary>The strategic effects this mission aims to achieve, derived from its type.</summary>
		public List<string> DesiredEffects = [];

		/// <summary>The enemy reaction a deception mission intends to provoke (feint/bait/demonstration).</summary>
		public string IntendedReaction;

		/// <summary>The force groups (owner ids) assigned to this mission by the order arbiter.</summary>
		public List<string> AssignedForces = [];

		/// <summary>The staging region forces assemble in, or -1 when none is chosen.</summary>
		public int StagingRegion = -1;

		/// <summary>The planned region route to the target, or empty when unplanned.</summary>
		public int[] PlannedRegions = [];

		/// <summary>Explicit launch conditions (documented and enforced by the phase machine).</summary>
		public List<string> LaunchConditions = [];

		/// <summary>Fallback plans if the mission's assumptions fail.</summary>
		public List<string> Contingencies = [];

		/// <summary>0..1 readiness: coalition strength versus the required minimum force.</summary>
		public float Readiness;

		/// <summary>0..1 progress through the mission phase machine.</summary>
		public float Progress;

		/// <summary>How many times a battlefield disruption forced this mission back to reconnaissance.</summary>
		public int ReplanAttempts;

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
			DesiredEffects = DesiredEffectsFor(type);
			IntendedReaction = IntendedReactionFor(type);
			LaunchConditions = LaunchConditionsFor(type);
			Contingencies = ContingenciesFor(type);
		}

		public static string IntendedReactionFor(MissionType type)
		{
			return type switch
			{
				MissionType.Feint => "enemy redeploys toward the feint",
				MissionType.Bait => "enemy pursues the bait into the ambush",
				MissionType.Demonstration => "enemy holds or shifts reserves",
				MissionType.DecoyTransport => "enemy reacts to the apparent insertion",
				MissionType.FakeBuildup => "enemy redeploys to face the phantom buildup",
				_ => null
			};
		}

		public static List<string> DesiredEffectsFor(MissionType type)
		{
			return type switch
			{
				MissionType.Attack or MissionType.Counterattack or MissionType.Breakthrough => ["destroy_enemy_forces", "seize_objective"],
				MissionType.Siege => ["reduce_static_defense", "open_breach"],
				MissionType.Raid or MissionType.Harassment => ["damage_economy", "disrupt_production"],
				MissionType.EconomyRaid => ["damage_economy", "starve_enemy"],
				MissionType.ProductionRaid => ["destroy_production", "deny_reinforcements"],
				MissionType.ExpansionDenial => ["deny_expansion", "contain_enemy"],
				MissionType.ChokepointSeizure => ["control_chokepoint", "split_enemy"],
				MissionType.Flank => ["attack_enemy_flank", "divide_defense"],
				MissionType.Pincer => ["envelop_enemy", "crush_between_forces"],
				MissionType.NavalBlockade => ["deny_enemy_naval_movement", "isolate_enemy_coast"],
				MissionType.AirStrike or MissionType.NavalStrike => ["destroy_high_value_target", "soften_defense"],
				MissionType.SupportPowerStrike => ["strike_high_value_target"],
				MissionType.Recon => ["locate_enemy", "reveal_terrain"],
				MissionType.DeepRecon => ["locate_enemy_main_force", "reveal_rear"],
				MissionType.AirRecon => ["reveal_terrain", "find_enemy_air"],
				MissionType.NavalRecon => ["reveal_water", "find_enemy_navy"],
				MissionType.RouteRecon => ["scout_route", "find_chokepoints"],
				MissionType.ExpansionSearch => ["find_expansion_site", "reveal_resources"],
				MissionType.DefenseProbe => ["probe_defenses", "reveal_garrison"],
				MissionType.Feint or MissionType.Bait => ["draw_enemy_response", "expose_enemy_position"],
				MissionType.Demonstration => ["show_force", "draw_attention"],
				MissionType.FakeBuildup => ["feign_buildup", "draw_enemy_redeployment"],
				MissionType.DecoyTransport => ["feign_insertion", "draw_enemy_response"],
				MissionType.Transport or MissionType.SpecialOps => ["insert_asset", "destroy_rear_target"],
				MissionType.Defend => ["protect_base", "repel_attack"],
				MissionType.MobileDefense => ["intercept_enemy", "shield_advance"],
				MissionType.AntiAirUmbrella => ["protect_from_air", "deny_airspace"],
				MissionType.NavalScreen => ["shield_coast", "deny_landings"],
				MissionType.DelayingAction => ["slow_enemy", "buy_time"],
				MissionType.Evacuation => ["save_assets", "withdraw"],
				MissionType.Escort => ["protect_harvesters", "shield_economy"],
				MissionType.Retreat => ["preserve_forces"],
				_ => ["achieve_objective"]
			};
		}

		static List<string> LaunchConditionsFor(MissionType type)
		{
			return type switch
			{
				MissionType.Attack or MissionType.Raid or MissionType.Counterattack or MissionType.Breakthrough
					or MissionType.Siege or MissionType.Harassment or MissionType.EconomyRaid or MissionType.ProductionRaid
					or MissionType.ExpansionDenial or MissionType.ChokepointSeizure or MissionType.Flank
					or MissionType.Pincer => ["force >= MinForce", "route exists"],
				MissionType.NavalBlockade => ["naval_available", "enemy_coast_identified"],
				MissionType.AirStrike or MissionType.NavalStrike => ["air_or_naval_available", "target_identified"],
				MissionType.SupportPowerStrike => ["power_ready", "high_value_target"],
				MissionType.Transport or MissionType.SpecialOps => ["transport available", "timing window"],
				_ => []
			};
		}

		static List<string> ContingenciesFor(MissionType type)
		{
			return type switch
			{
				MissionType.Attack or MissionType.Breakthrough => ["convert to feint", "withdraw"],
				MissionType.Raid or MissionType.Harassment or MissionType.EconomyRaid or MissionType.ProductionRaid
					or MissionType.ExpansionDenial or MissionType.Flank or MissionType.Pincer => ["withdraw"],
				MissionType.NavalBlockade => ["withdraw_to_port"],
				MissionType.AirStrike or MissionType.NavalStrike => ["return to base"],
				MissionType.SupportPowerStrike => ["withhold power"],
				MissionType.Transport or MissionType.SpecialOps => ["abort and hold"],
				_ => []
			};
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
				case MissionType.Demonstration:
				case MissionType.DecoyTransport:
				case MissionType.FakeBuildup:
					return MissionPhase.Deception;
				case MissionType.Retreat:
					return MissionPhase.Withdrawal;
				case MissionType.AirStrike:
				case MissionType.NavalStrike:
				case MissionType.NavalBlockade:
					return MissionPhase.Shaping; // no ground staging for strike missions
				case MissionType.SupportPowerStrike:
					return MissionPhase.Breach;  // fire immediately when ready
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

		/// <summary>Number of missions that reached a successful terminal state.</summary>
		public int MissionSuccesses;

		/// <summary>Number of missions that aborted or failed.</summary>
		public int MissionAborts;

		/// <summary>Number of special-operations/transport missions that succeeded.</summary>
		public int SpecialOpsSuccesses;

		/// <summary>Number of reconnaissance missions that succeeded.</summary>
		public int ReconSuccesses;

		/// <summary>One-line mission-outcome summary for the telemetry log.</summary>
		public string MissionSummary()
		{
			var total = MissionSuccesses + MissionAborts;
			return $"Missions: {total} concluded ({MissionSuccesses} succeeded, {MissionAborts} aborted/failed; " +
				$"{SpecialOpsSuccesses} special ops, {ReconSuccesses} recon)";
		}

		public IReadOnlyList<CoalitionMission> Missions => missions;

		public CoalitionMission CreateMission(MissionType type, int priority, CPos? target, string objective, int minForce = 0, int createdTick = 0)
		{
			var mission = new CoalitionMission($"OP-{++nextMissionId}", type, createdTick, priority, target, objective)
			{
				MinForce = minForce
			};
			missions.Add(mission);

			// A created feint or bait is a deception attempt; the outcome record feeds the planner.
			if (IsDeception(type))
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

		/// <summary>True for mission types that push into enemy territory and complete when the target clears.</summary>
		public static bool IsOffensive(MissionType type)
		{
			return type is MissionType.Attack or MissionType.Raid or MissionType.Counterattack
				or MissionType.Breakthrough or MissionType.Siege or MissionType.Harassment
				or MissionType.EconomyRaid or MissionType.ProductionRaid or MissionType.ExpansionDenial
				or MissionType.ChokepointSeizure or MissionType.Flank or MissionType.Pincer
				or MissionType.AirStrike or MissionType.NavalStrike or MissionType.NavalBlockade
				or MissionType.SupportPowerStrike;
		}

		/// <summary>
		/// True for directive-style missions that do not walk the offensive phase pipeline: recon,
		/// deception, withdrawal, and the defensive types. Their lifecycle is decided by the commander.
		/// </summary>
		public static bool IsStaticDirective(MissionType type)
		{
			return type is MissionType.Recon or MissionType.Feint or MissionType.Bait or MissionType.Retreat
				or MissionType.MobileDefense or MissionType.AntiAirUmbrella or MissionType.NavalScreen
				or MissionType.DelayingAction or MissionType.Evacuation or MissionType.Escort
				or MissionType.DeepRecon or MissionType.AirRecon or MissionType.NavalRecon
				or MissionType.RouteRecon or MissionType.ExpansionSearch or MissionType.DefenseProbe
				or MissionType.Demonstration or MissionType.DecoyTransport or MissionType.FakeBuildup;
		}

		/// <summary>True for deception missions whose success is measured by the enemy's reaction.</summary>
		public static bool IsDeception(MissionType type)
		{
			return type is MissionType.Feint or MissionType.Bait or MissionType.Demonstration
				or MissionType.DecoyTransport or MissionType.FakeBuildup;
		}

		/// <summary>True for the reconnaissance family.</summary>
		public static bool IsRecon(MissionType type)
		{
			return type is MissionType.Recon or MissionType.DeepRecon or MissionType.AirRecon
				or MissionType.NavalRecon or MissionType.RouteRecon or MissionType.ExpansionSearch
				or MissionType.DefenseProbe;
		}

		/// <summary>True for the defensive family (base defense plus the specialized screens).</summary>
		public static bool IsDefensive(MissionType type)
		{
			return type is MissionType.Defend or MissionType.MobileDefense or MissionType.AntiAirUmbrella
				or MissionType.NavalScreen or MissionType.DelayingAction or MissionType.Evacuation
				or MissionType.Escort;
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
				mission.Readiness = mission.MinForce <= 0 ? 1f : Math.Clamp(coalitionStrength / mission.MinForce, 0f, 1f);
				mission.Progress = (int)mission.Phase * 1f / (Enum.GetValues<MissionPhase>().Length - 1);

				switch (mission.Status)
				{
					case MissionStatus.Ready:
						mission.Status = MissionStatus.Executing;
						mission.PhaseTick = blackboard.Tick;
						break;

					case MissionStatus.Executing:
						if (!AdvancePhase(blackboard, mission))
							continue;
						if (mission.Phase == MissionPhase.Withdrawal)
						{
							mission.Status = MissionStatus.Aborted;
							mission.OutcomeReason ??= "withdrawal completed";
							continue;
						}

						if (IsOffensive(mission.Type))
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

						// Reconnaissance completes when the target region is explored or the enemy is located.
						if (IsRecon(mission.Type))
						{
							var targetRegion = mission.Target != null ? blackboard.RegionOf(mission.Target.Value).Index : -1;
							var complete = targetRegion >= 0
								? blackboard.Regions[targetRegion].FriendlyControl > 0.1f
								: blackboard.EnemyRegion >= 0;
							if (complete)
							{
								mission.Status = MissionStatus.Succeeded;
								mission.OutcomeReason = "recon complete";
								continue;
							}
						}

						// A feint or bait succeeds when it changes enemy behavior: enemy units redeploy
						// toward it. The drawn response feeds the deception record, which the commander
						// uses to keep funding deception or drop it when the enemy ignores it.
						if (IsDeception(mission.Type) && mission.Target != null)
						{
							var nearby = blackboard.EnemyIntel.Count(i =>
								(i.LastSeenCell - mission.Target.Value).LengthSquared <= 20 * 20);
							if (mission.DeceptionBaselineEnemyCount == 0)
								mission.DeceptionBaselineEnemyCount = nearby;

							var (drew, engaged) = MeasureDeceptionResponse(mission.DeceptionBaselineEnemyCount, nearby);
							if (drew)
							{
								mission.EnemyValueEngaged = engaged;
								mission.OutcomeReason = $"enemy redeployed toward {mission.Type.ToString().ToLowerInvariant()}";
								DeceptionSuccesses++;
								DeceptionEnemiesDrawn += engaged;
								CoalitionTelemetry.Log(blackboard.World,
									$"{mission.Type} {mission.Id} effective: drew {engaged} enemy units; DECEPTION_EFFECTIVENESS={engaged * 100f / System.Math.Max(1, mission.FriendlyValueCommitted):0.0}%");
								mission.Status = MissionStatus.Succeeded;
								continue;
							}
						}

						// Abort only when the coalition is hopelessly outnumbered. The old 1.8x threshold
						// fired during ordinary fog-induced strength swings and made the commander drop
						// every attack, which stalemated symmetric games. A 3x margin means the fight is
						// genuinely unwinnable; otherwise the attack runs its course and the tactical
						// brain's retreat logic manages a losing engagement.
						if (enemyStrength > coalitionStrength * 3.0f)
						{
							BeginWithdrawal(mission, blackboard.Tick, "coalition outmatched");
							CoalitionTelemetry.Log(blackboard.World, $"Mission {mission.Id} withdrawing: {mission.OutcomeReason} (coalition {coalitionStrength:0} vs enemy {enemyStrength:0})");
							continue;
						}

						if (mission.Target != null && !CoalitionRoutePlanner.RouteExists(blackboard.MapAnalysis,
							blackboard.HomeRegion, blackboard.RegionOf(mission.Target.Value).Index, MovementClass.Ground))
						{
							if (HandleRouteDisruption(mission, blackboard.Tick))
								CoalitionTelemetry.Log(blackboard.World, $"Mission {mission.Id} replanning: {mission.OutcomeReason}");
							else
								CoalitionTelemetry.Log(blackboard.World, $"Mission {mission.Id} aborted: {mission.OutcomeReason}");
							continue;
						}

						mission.PhaseTick++;
						break;

					default:
						// Terminal missions are dropped after they are reported; count the outcome for telemetry.
						if (mission.Status == MissionStatus.Succeeded)
						{
							MissionSuccesses++;
							if (mission.Type == MissionType.SpecialOps || mission.Type == MissionType.Transport)
								SpecialOpsSuccesses++;
							else if (IsRecon(mission.Type))
								ReconSuccesses++;
						}
						else if (mission.Status == MissionStatus.Aborted || mission.Status == MissionStatus.Failed)
							MissionAborts++;
						missions.Remove(mission);
						break;
				}
			}
		}

		/// <summary>Moves a failed operation into an explicit withdrawal phase before force release.</summary>
		public static void BeginWithdrawal(CoalitionMission mission, int tick, string reason)
		{
			mission.Phase = MissionPhase.Withdrawal;
			mission.PhaseTick = tick;
			mission.OutcomeReason = reason;
		}

		/// <summary>
		/// Replans twice after a route/bridge disruption, then aborts deterministically if no route can
		/// be established. Returns true when the mission was sent back to reconnaissance.
		/// </summary>
		public static bool HandleRouteDisruption(CoalitionMission mission, int tick, int maximumReplans = 2)
		{
			if (mission.ReplanAttempts >= maximumReplans)
			{
				mission.Status = MissionStatus.Aborted;
				mission.OutcomeReason = "target unreachable after replanning";
				return false;
			}

			mission.ReplanAttempts++;
			mission.Phase = MissionPhase.Recon;
			mission.PhaseTick = tick;
			mission.PlannedRegions = [];
			mission.StagingRegion = -1;
			mission.OutcomeReason = "route disrupted; replanning";
			return true;
		}

		/// <summary>
		/// Advances the mission one phase when its transition condition is satisfied. Returns false when
		/// the mission should not receive further objective evaluation this tick (it just changed phase).
		/// Non-offensive missions (recon, feint, bait, retreat) do not run the offensive pipeline:
		/// their phases are terminal by construction, and completion is decided by the caller.
		/// </summary>
		static bool AdvancePhase(CoalitionBlackboard blackboard, CoalitionMission mission)
		{
			// Recon, deception, withdrawal, and defensive missions stay in their own phase; only
			// offensive missions walk the pipeline.
			if (IsStaticDirective(mission.Type))
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

			var attack = active.FirstOrDefault(m => IsOffensive(m.Type) && m.Type != MissionType.AirStrike
				&& m.Type != MissionType.NavalStrike && m.Type != MissionType.NavalBlockade
				&& m.Type != MissionType.SupportPowerStrike);
			var feint = active.FirstOrDefault(m => m.Type == MissionType.Feint || m.Type == MissionType.Demonstration
				|| m.Type == MissionType.FakeBuildup);
			var defend = active.FirstOrDefault(m => IsDefensive(m.Type));
			var recon = active.FirstOrDefault(m => IsRecon(m.Type));
			var bait = active.FirstOrDefault(m => m.Type == MissionType.Bait);
			var transport = active.FirstOrDefault(m => m.Type == MissionType.Transport || m.Type == MissionType.SpecialOps || m.Type == MissionType.DecoyTransport);
			var domainStrike = active.FirstOrDefault(m => m.Type == MissionType.AirStrike
				|| m.Type == MissionType.NavalStrike || m.Type == MissionType.NavalBlockade);
			var pincer = active.FirstOrDefault(m => m.Type == MissionType.Pincer);
			var supportPower = active.FirstOrDefault(m => m.Type == MissionType.SupportPowerStrike);
			var retreat = forceRetreat || active.Any(m => m.Type == MissionType.Retreat || m.Phase == MissionPhase.Withdrawal);

			var sb = new StringBuilder();
			var strategy = attack != null ? "attack" : defend != null ? "defend" : "build";
			sb.Append("{\"strategy\":\"").Append(strategy).Append('"');
			if (attack != null && attack.Target != null)
				AppendTarget(sb, "attack", attack.Target.Value);
			if (domainStrike != null && domainStrike.Target != null)
			{
				AppendTarget(sb, "strike", domainStrike.Target.Value);
				sb.Append(",\"strikeKind\":\"")
					.Append(domainStrike.Type == MissionType.AirStrike ? "air" : "naval").Append('"');
			}
			if (pincer != null && pincer.Target != null)
				AppendTarget(sb, "pincer", pincer.Target.Value + new CVec(8, 0));
			if (supportPower != null && supportPower.Target != null)
				AppendTarget(sb, "supportPower", supportPower.Target.Value);
			if (feint != null && feint.Target != null)
			{
				AppendTarget(sb, "feint", feint.Target.Value);
				sb.Append(",\"deceptionKind\":\"")
					.Append(feint.Type.ToString().ToLowerInvariant()).Append('"');
			}
			if (recon != null && recon.Target != null)
				AppendTarget(sb, "recon", recon.Target.Value);
			if (bait != null && bait.Target != null)
				AppendTarget(sb, "bait", bait.Target.Value);
			if (defend != null && defend.Target != null)
			{
				AppendTarget(sb, "counter", defend.Target.Value);
				sb.Append(",\"defenseKind\":\"").Append(DefenseKind(defend.Type)).Append('"');
			}
			if (transport != null && transport.Target != null)
			{
				AppendTarget(sb, "transport", transport.Target.Value);

				// The brain only runs insertions when a transport kind is named.
				sb.Append(",\"transportKind\":\"naval\"");
			}

			// Force ownership is carried with every executable directive. A present empty owner list
			// means the arbiter could not allocate a force, so no coalition member may execute it by
			// accident; legacy plans without the assignments object remain backward-compatible.
			var assignments = new Dictionary<string, string[]>();
			void Assign(string key, CoalitionMission mission)
			{
				if (mission != null)
					assignments[key] = mission.AssignedForces.Distinct().OrderBy(f => f).ToArray();
			}

			Assign("attack", attack);
			Assign("strike", domainStrike);
			Assign("pincer", pincer);
			Assign("supportPower", supportPower);
			Assign("feint", feint);
			Assign("recon", recon);
			Assign("bait", bait);
			Assign("counter", defend);
			Assign("transport", transport);
			if (assignments.Count > 0)
				sb.Append(",\"assignments\":").Append(JsonSerializer.Serialize(assignments));

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

		static string DefenseKind(MissionType type)
		{
			return type switch
			{
				MissionType.MobileDefense => "mobile",
				MissionType.AntiAirUmbrella => "aa",
				MissionType.NavalScreen => "naval",
				MissionType.DelayingAction => "delay",
				MissionType.Evacuation => "evacuate",
				MissionType.Escort => "escort",
				_ => "defend"
			};
		}

		static void AppendTarget(StringBuilder sb, string key, CPos cell)
		{
			sb.Append(",\"").Append(key).Append("\":{\"x\":").Append(cell.X).Append(",\"y\":").Append(cell.Y).Append('}');
		}
	}
}
