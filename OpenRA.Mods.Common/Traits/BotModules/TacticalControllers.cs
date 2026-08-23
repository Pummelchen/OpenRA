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
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>
	/// Base for per-domain tactical controllers. Each controller executes its mission intent
	/// deterministically at engine speed, claims its own units through the shared arbiter, and
	/// reports whether it could act. Controllers hold no long-lived state beyond the tick they
	/// are executed in, so multiple controllers never fight over the same unit.
	/// </summary>
	public abstract class TacticalController
	{
		readonly Dictionary<uint, uint> assignedTargets = [];
		readonly Dictionary<uint, int> lastOrderTicks = [];

		protected readonly StrategicBrainBotModule Brain;
		protected readonly StrategicBrainBotModuleInfo Info;
		protected World World => Brain.World;
		protected Player Player => Brain.Player;
		protected IBot Bot => Brain.Bot;

		protected TacticalController(StrategicBrainBotModule brain)
		{
			Brain = brain;
			Info = brain.Info;
		}

		/// <summary>The domain's units from the given pool, unclaimed so far this tick.</summary>
		protected Actor[] Claim(IEnumerable<Actor> pool)
		{
			return Brain.Claim(pool).ToArray();
		}

		protected void Log(string message)
		{
			CoalitionTelemetry.Log(World, message);
		}

		/// <summary>True when the controller executed an intent this tick.</summary>
		public bool Executed { get; protected set; }

		/// <summary>Why the controller could not execute its current intent.</summary>
		public string FailureReason { get; protected set; }

		/// <summary>True when the inability invalidates the plan and should trigger strategic replanning.</summary>
		public bool NeedsReplan { get; protected set; }

		protected void Unable(string reason, bool requestReplan)
		{
			FailureReason = reason;
			NeedsReplan = requestReplan;
			Log($"{GetType().Name} unable: {reason}{(requestReplan ? "; replan requested" : string.Empty)}");
			if (requestReplan)
				Brain.RequestStrategicReplan($"{GetType().Name}: {reason}");
		}

		protected void MarkExecuted()
		{
			Executed = true;
			FailureReason = null;
			NeedsReplan = false;
		}

		/// <summary>Returns only enemy actors that are observable by this player right now.</summary>
		protected Actor[] VisibleEnemiesAround(WPos center, int radiusCells)
		{
			return World.FindActorsInCircle(center, WDist.FromCells(radiusCells))
				.Where(a => a.IsInWorld && !a.IsDead && a.OccupiesSpace != null
					&& Player.RelationshipWith(a.Owner) == PlayerRelationship.Enemy
					&& Player.Shroud.IsVisible(a.CenterPosition) && a.CanBeViewedByPlayer(Player))
				.OrderBy(a => a.ActorID)
				.ToArray();
		}

		protected static bool CanAttackTarget(Actor attacker, Actor target)
		{
			var actorTarget = Target.FromActor(target);
			return attacker.TraitsImplementing<AttackBase>()
				.Any(a => !a.IsTraitDisabled && !a.IsTraitPaused && a.HasAnyValidWeapons(actorTarget));
		}

		protected static bool BusyAttack(Actor actor)
		{
			if (actor.IsIdle)
				return false;

			var activity = actor.CurrentActivity;
			return activity is Attack or FlyAttack || activity.NextActivity is Attack or FlyAttack;
		}

		protected static bool IsRearming(Actor actor)
		{
			return !actor.IsIdle && (actor.CurrentActivity.ActivitiesImplementing<Resupply>().Any()
				|| actor.CurrentActivity.ActivitiesImplementing<ReturnToBase>().Any());
		}

		static TacticalTargetProfile TargetProfile(Actor actor)
		{
			var health = actor.TraitOrDefault<IHealth>();
			var healthPercent = health == null || health.MaxHP <= 0 ? 100 : health.HP * 100 / health.MaxHP;
			return new TacticalTargetProfile(
				actor.Info.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0,
				healthPercent,
				actor.Info.HasTraitInfo<AttackBaseInfo>(),
				actor.Info.HasTraitInfo<BuildingInfo>(),
				actor.Info.HasTraitInfo<ProductionInfo>());
		}

		/// <summary>
		/// Acquires local visible contacts, assigns a bounded number of compatible attackers to each,
		/// and returns the units that received or retained direct-fire responsibilities.
		/// </summary>
		protected HashSet<Actor> Engage(Actor[] force, WPos center, int radiusCells,
			Func<Actor, bool> targetAllowed = null)
		{
			var engaged = new HashSet<Actor>();
			if (force.Length == 0)
				return engaged;

			var validUnitIds = force.Select(a => a.ActorID).ToHashSet();
			foreach (var id in assignedTargets.Keys.Where(id => !validUnitIds.Contains(id)).ToArray())
			{
				assignedTargets.Remove(id);
				lastOrderTicks.Remove(id);
			}

			var candidates = VisibleEnemiesAround(center, radiusCells)
				.Where(a => (targetAllowed == null || targetAllowed(a)) && force.Any(u => CanAttackTarget(u, a)))
				.ToArray();
			if (candidates.Length == 0)
				return engaged;

			var profiles = candidates.ToDictionary(a => a, TargetProfile);
			var assignments = candidates.ToDictionary(a => a, _ => 0);
			foreach (var unit in force.OrderBy(a => a.ActorID))
			{
				// Preserve an attack that is already executing. Retargeting mid-shot throws away
				// weapon cycles and is the main source of destructive micro-order churn.
				if (BusyAttack(unit))
				{
					if (assignedTargets.TryGetValue(unit.ActorID, out var assignedTargetId))
					{
						var assignedTarget = candidates.FirstOrDefault(target => target.ActorID == assignedTargetId);
						if (assignedTarget != null)
							assignments[assignedTarget]++;
					}

					engaged.Add(unit);
					continue;
				}

				var choices = candidates
					.Where(target => CanAttackTarget(unit, target)
						&& assignments[target] < TacticalEngagement.FocusSlots(profiles[target]))
					.Select(target => new
					{
						Target = target,
						Score = TacticalEngagement.TargetScore(profiles[target],
							(unit.CenterPosition - target.CenterPosition).LengthSquared)
					})
					.OrderByDescending(choice => choice.Score)
					.ThenBy(choice => choice.Target.ActorID)
					.ToArray();
				if (choices.Length == 0)
					continue;

				var target = choices[0].Target;
				assignments[target]++;
				engaged.Add(unit);

				var sameDirective = assignedTargets.TryGetValue(unit.ActorID, out var targetId)
					&& targetId == target.ActorID;
				lastOrderTicks.TryGetValue(unit.ActorID, out var lastOrderTick);
				if (!sameDirective || TacticalEngagement.ShouldRefreshOrder(unit.IsIdle, World.WorldTick,
					lastOrderTick, Info.TacticalOrderRefreshTicks))
				{
					Bot.QueueOrder(new Order("Attack", unit, Target.FromActor(target), false));
					lastOrderTicks[unit.ActorID] = World.WorldTick;
				}

				assignedTargets[unit.ActorID] = target.ActorID;
			}

			return engaged;
		}

		/// <summary>Advances non-engaged units while rate-limiting path refreshes and preserving live attacks.</summary>
		protected void Advance(IEnumerable<Actor> units, WPos target, string order = "AttackMove")
		{
			foreach (var unit in units.OrderBy(a => a.ActorID))
			{
				if (BusyAttack(unit) || IsRearming(unit))
					continue;

				lastOrderTicks.TryGetValue(unit.ActorID, out var lastOrderTick);
				if (!TacticalEngagement.ShouldRefreshOrder(unit.IsIdle, World.WorldTick,
					lastOrderTick, Info.TacticalOrderRefreshTicks))
					continue;

				Bot.QueueOrder(new Order(order, unit, Target.FromPos(target), false));
				lastOrderTicks[unit.ActorID] = World.WorldTick;
				assignedTargets.Remove(unit.ActorID);
			}
		}
	}

	/// <summary>Ground controller: the land component of assault waves.</summary>
	public sealed class GroundController : TacticalController
	{
		public GroundController(StrategicBrainBotModule brain)
			: base(brain) { }

		public Actor[] LandUnits(IEnumerable<Actor> pool)
		{
			return pool.Where(a => !Info.AirUnitTypes.Contains(a.Info.Name) && !Info.NavalPriority.Contains(a.Info.Name)).ToArray();
		}

		/// <summary>
		/// The structure the main force should attack, or null when nothing worth attacking is
		/// visible. Fog-safe: only currently observable enemy actors are considered.
		/// </summary>
		Actor SelectSiegeTarget(WPos objective, out int visibleDefences)
		{
			var structures = VisibleEnemiesAround(objective, Info.SiegeScanRadius)
				.Where(a => a.Info.HasTraitInfo<BuildingInfo>())
				.ToArray();

			visibleDefences = structures.Count(IsDefence);
			if (structures.Length == 0)
				return null;

			var candidates = structures.Select(a => new SiegeCandidate(a.Info.Name, a.Location,
				(a.CenterPosition - objective).Length / 1024, IsDefence(a))).ToArray();

			var chosen = SiegeTargeting.SelectMainForceTarget(candidates);
			if (chosen == null)
				return null;

			return structures.FirstOrDefault(a => a.Location == chosen.Value.Cell);
		}

		/// <summary>The defence artillery should be reducing, or null when none is visible.</summary>
		Actor SelectDefenceTarget(WPos objective)
		{
			var defences = VisibleEnemiesAround(objective, Info.SiegeScanRadius)
				.Where(a => a.Info.HasTraitInfo<BuildingInfo>() && IsDefence(a))
				.ToArray();

			if (defences.Length == 0)
				return null;

			var candidates = defences.Select(a => new SiegeCandidate(a.Info.Name, a.Location,
				(a.CenterPosition - objective).Length / 1024, true)).ToArray();

			var chosen = SiegeTargeting.SelectArtilleryTarget(candidates);
			return chosen == null ? null : defences.FirstOrDefault(a => a.Location == chosen.Value.Cell);
		}

		/// <summary>A structure that shoots back is a defence; anything else is an objective.</summary>
		static bool IsDefence(Actor a)
		{
			return a.Info.HasTraitInfo<AttackBaseInfo>();
		}

		/// <summary>Orders the ground component of an assault wave toward the target.</summary>
		public void Attack(Actor[] available, WPos target)
		{
			var land = Claim(LandUnits(available));
			if (land.Length == 0)
			{
				Unable("no available ground force", true);
				return;
			}

			// Artillery screening: artillery units (v2rl, arty) hold behind the main force so they
			// fire from range instead of charging into melee. Send them to a point pulled back from
			// the target by ~8 cells along the axis from the base to the target.
			var artilleryTypes = new HashSet<string> { "v2rl", "arty" };
			var artillery = land.Where(a => artilleryTypes.Contains(a.Info.Name)).ToArray();
			var antiAir = land.Where(a => !artilleryTypes.Contains(a.Info.Name)
				&& Info.AntiAirUnits.Contains(a.Info.Name)).ToArray();
			var mainForce = land.Where(a => !artilleryTypes.Contains(a.Info.Name)
				&& !Info.AntiAirUnits.Contains(a.Info.Name)).ToArray();

			// Siege targeting (handbook §7): once the objective is in sight, the main force attacks a
			// structure rather than attack-moving to a cell. An attack-move engages whatever it meets,
			// which on a defended base means grinding against the perimeter pillbox while the economy
			// that replaces it keeps running - high exchange, nothing killed that matters, draw.
			var siegeTarget = SelectSiegeTarget(target, out var visibleDefences);

			if (mainForce.Length > 0 && siegeTarget != null)
			{
				// Artillery reduces the defence first where it can; the main force goes for what
				// actually costs the opponent the game.
				Bot.QueueOrder(new Order("Attack", null, Target.FromActor(siegeTarget), false, groupedActors: mainForce));
				Log($"Siege: main force attacking {siegeTarget.Info.Name} ({visibleDefences} defences visible)");
			}
			else if (mainForce.Length > 0)
			{
				// Speed coordination: fast units (tanks) must not outrun slow support (infantry, AA).
				// Units that are more than 15 cells ahead of the group center hold position briefly
				// so the formation stays together.
				if (mainForce.Length > 3)
				{
					var center = mainForce.Select(a => a.CenterPosition).Average();
					var spread = Info.FormationMaxLeadCells * 1024;
					var ahead = mainForce.Where(a => TacticalFormation.IsAheadOfCenter(a.CenterPosition,
						target, center, (long)spread * spread)).ToArray();
					var followers = mainForce.Except(ahead).ToArray();
					if (ahead.Length > 0 && followers.Length > 0)
					{
						// Followers advance; ahead units hold at the group center to let the rest catch up.
						Bot.QueueOrder(new Order("AttackMove", null, Target.FromPos(target), false, groupedActors: followers));
						Bot.QueueOrder(new Order("AttackMove", null, Target.FromPos(center), false, groupedActors: ahead));
					}
					else
						Bot.QueueOrder(new Order("AttackMove", null, Target.FromPos(target), false, groupedActors: mainForce));
				}
				else
					Bot.QueueOrder(new Order("AttackMove", null, Target.FromPos(target), false, groupedActors: mainForce));
			}

			// Anti-air remains with the valuable screening force instead of racing to the objective.
			if (antiAir.Length > 0)
			{
				var supportAnchor = mainForce.Length > 0 ? mainForce.Select(a => a.CenterPosition).Average() : target;
				Bot.QueueOrder(new Order("AttackMove", null, Target.FromPos(supportAnchor), false, groupedActors: antiAir));
			}

			if (artillery.Length > 0 && visibleDefences > 0)
			{
				var defenceTarget = SelectDefenceTarget(target);
				if (defenceTarget != null)
				{
					Bot.QueueOrder(new Order("Attack", null, Target.FromActor(defenceTarget), false, groupedActors: artillery));
					Log($"Siege: artillery reducing {defenceTarget.Info.Name}");
					MarkExecuted();
					return;
				}
			}

			if (artillery.Length > 0)
			{
				var baseCenter = Brain.BaseCenter();
				if (baseCenter != null)
				{
					var screen = mainForce.Concat(antiAir).ToArray();
					var screenCenter = screen.Length > 0 ? screen.Select(a => a.CenterPosition).Average() : target;
					var artilleryTarget = TacticalFormation.ArtilleryPullbackTarget(screenCenter,
						baseCenter.Value, Info.ArtilleryScreenOffsetCells * 1024);
					Bot.QueueOrder(new Order("AttackMove", null, Target.FromPos(artilleryTarget), false, groupedActors: artillery));
				}
				else
					Bot.QueueOrder(new Order("AttackMove", null, Target.FromPos(target), false, groupedActors: artillery));
			}

			MarkExecuted();
		}
	}

	/// <summary>Air controller: the air component of assault waves.</summary>
	public sealed class AirController : TacticalController
	{
		public AirController(StrategicBrainBotModule brain)
			: base(brain) { }

		public Actor[] AirUnits(IEnumerable<Actor> pool)
		{
			return pool.Where(a => Info.AirUnitTypes.Contains(a.Info.Name)).ToArray();
		}

		/// <summary>Orders the air component of an assault wave toward the target.</summary>
		public void Attack(Actor[] available, WPos target)
		{
			var air = Claim(AirUnits(available));
			if (air.Length == 0)
			{
				Unable("no available air force", true);
				return;
			}

			var ready = new List<Actor>();
			foreach (var actor in air)
			{
				if (IsRearming(actor))
					continue;

				var ammoPools = actor.TraitsImplementing<AmmoPool>().ToArray();
				var rearmable = actor.TraitOrDefault<Rearmable>();
				var needsBase = rearmable != null && ammoPools.Any(pool => rearmable.Info.AmmoPools.Contains(pool.Info.Name) && !pool.HasAmmo);
				if (needsBase)
				{
					Bot.QueueOrder(new Order("ReturnToBase", actor, false));
					continue;
				}

				ready.Add(actor);
			}

			if (ready.Count > 0)
			{
				var airForce = ready.ToArray();
				var center = airForce.Select(a => a.CenterPosition).Average();
				var representative = airForce[0];
				bool SafeTarget(Actor candidate)
				{
					var antiAir = VisibleEnemiesAround(candidate.CenterPosition, Info.TacticalAirDangerRadius)
						.Count(enemy => CanAttackTarget(enemy, representative));
					return antiAir * 3 < airForce.Length;
				}

				var engaged = Engage(airForce, center, Info.TacticalEngagementScanRadius * 2, SafeTarget);
				var objectiveAntiAir = VisibleEnemiesAround(target, Info.TacticalAirDangerRadius)
					.Count(enemy => CanAttackTarget(enemy, representative));
				if (objectiveAntiAir * 3 >= airForce.Length)
				{
					foreach (var actor in airForce.Where(a => !engaged.Contains(a)))
						Bot.QueueOrder(new Order("ReturnToBase", actor, false));
				}
				else
					Advance(airForce.Where(a => !engaged.Contains(a)), target);
			}

			MarkExecuted();
		}
	}

	/// <summary>Naval controller: the naval component of assault waves and naval screening.</summary>
	public sealed class NavalController : TacticalController
	{
		public NavalController(StrategicBrainBotModule brain)
			: base(brain) { }

		public Actor[] NavalUnits(IEnumerable<Actor> pool)
		{
			return pool.Where(a => Info.NavalPriority.Contains(a.Info.Name)).ToArray();
		}

		/// <summary>Orders the naval component of an assault wave toward the target.</summary>
		public void Attack(Actor[] available, WPos target)
		{
			var naval = Claim(NavalUnits(available));
			if (naval.Length == 0)
			{
				Unable("no available naval force", true);
				return;
			}

			var center = naval.Select(a => a.CenterPosition).Average();
			var engaged = Engage(naval, center, Info.TacticalEngagementScanRadius * 2);
			Advance(naval.Where(a => !engaged.Contains(a)), target);
			MarkExecuted();
		}
	}

	/// <summary>
	/// Transport controller: drives the explicit transport state machine - assemble, load,
	/// wait-for-window, transit, approach, unload, and extraction when requested - and aborts
	/// (holding position) when the transit becomes unsafe. Operates independently of the main
	/// army; the payload is claimed so no other controller orders it mid-insertion.
	/// </summary>
	public sealed class TransportController : TacticalController
	{
		// Extraction is enabled so a special asset inserted by transport can be recovered and reused.
		readonly TransportStateMachine machine = new(extractOnCompletion: true);
		readonly List<CPos> routeWaypoints = [];
		CPos? plannedFor;
		int windowElapsed;

		public TransportController(StrategicBrainBotModule brain)
			: base(brain) { }

		/// <summary>The current transport state, for telemetry and the directive.</summary>
		public TransportState State => machine.State;

		/// <summary>True when the transport mission completed its cycle this tick.</summary>
		public bool Completed => machine.Complete;

		/// <summary>True when the mission aborted because transit became unsafe.</summary>
		public bool Aborted => machine.Aborted;

		/// <summary>
		/// Executes the current state and advances when its completion condition is met. Returns true
		/// when the mission is still active (the caller keeps the transport target), false when it
		/// finished or aborted this tick.
		/// </summary>
		public bool Execute(CPos? target, string kind, int worldTick)
		{
			if (target == null || kind == null)
			{
				Unable("transport target or kind missing", true);
				return false;
			}

			var transport = World.Actors
				.Where(a => a.IsInWorld && !a.IsDead && a.Owner == Player && Info.TransportTypes.Contains(a.Info.Name))
				.OrderBy(a => a.ActorID)
				.FirstOrDefault();
			if (transport == null)
			{
				Unable("no transport available", true);
				return false;
			}

			MarkExecuted();

			var targetCell = target.Value;
			var cargo = transport.TraitOrDefault<Cargo>();

			// Plan the threat-weighted stealth route once per target; the transit state follows the
			// intermediate waypoints so the transport avoids AA, detection, and exposed ground.
			if (plannedFor != targetCell)
			{
				plannedFor = targetCell;
				routeWaypoints.Clear();
				routeWaypoints.AddRange(Brain.PlanTransportRoute(targetCell));
			}

			switch (machine.State)
			{
				case TransportState.Assemble:
					// Advance to loading as soon as the transport exists.
					return AdvanceAndContinue();

				case TransportState.Load:
				{
					// Load payload units; they are claimed so the main army does not order them elsewhere.
					var payload = Claim(World.Actors
						.Where(a => a.IsInWorld && !a.IsDead && a.Owner == Player && Info.TransportPayloadTypes.Contains(a.Info.Name)))
						.Take(4)
						.ToArray();
					if (payload.Length > 0 && (cargo == null || cargo.PassengerCount == 0))
						Bot.QueueOrder(new Order("EnterTransport", null, Target.FromActor(transport), false, groupedActors: payload));

					// Advance once loaded, or when there is nothing to load (no cargo trait / no payload).
					return (cargo != null && cargo.PassengerCount <= 0 && payload.Length != 0) || AdvanceAndContinue();
				}

				case TransportState.WaitForWindow:
					// Hold until the synchronization window elapses (deception/distraction timing).
					windowElapsed++;
					return windowElapsed < 30 || AdvanceAndContinue();

				case TransportState.Transit:
				{
					// Follow the planned stealth route: move to the next waypoint, then the insertion
					// point; abort (hold) if the transit becomes unsafe.
					var destination = routeWaypoints.Count > 0 ? routeWaypoints[0] : targetCell;
					var distance = (transport.CenterPosition - World.Map.CenterOfCell(destination)).LengthSquared;
					Bot.QueueOrder(new Order("Move", transport, Target.FromCell(World, destination), false));

					var health = transport.TraitOrDefault<IHealth>();
					var fraction = health == null ? 100 : health.HP * 100 / health.MaxHP;
					if (fraction < Info.RetreatHealthPercent)
					{
						Log($"Transport mission aborted: transport at {fraction}% health during transit");
						Unable("transport became unsafe during transit", true);
						machine.Abort();
						return false;
					}

					if (distance <= BaseRadiusSquared(10))
					{
						if (routeWaypoints.Count > 0)
							routeWaypoints.RemoveAt(0);
						else
							return AdvanceAndContinue();
					}

					return true;
				}

				case TransportState.Approach:
				{
					var distance = (transport.CenterPosition - World.Map.CenterOfCell(targetCell)).LengthSquared;
					Bot.QueueOrder(new Order("Move", transport, Target.FromCell(World, targetCell), false));
					return distance > BaseRadiusSquared(5) || AdvanceAndContinue();
				}

				case TransportState.Unload:
					Bot.QueueOrder(new Order("Unload", transport, false));
					Log($"Transport unloaded at {targetCell}");
					return AdvanceAndContinue();

				case TransportState.ExtractionRequest:
				case TransportState.ReturnForExtraction:
					// The extraction cycle reuses the transit approach toward the same target: the
					// transport returns, payload reboards, and the mission completes on extraction.
					Bot.QueueOrder(new Order("Move", transport, Target.FromCell(World, targetCell), false));
					return AdvanceAndContinue();

				case TransportState.Reload:
				{
					var payload = Claim(World.Actors
						.Where(a => a.IsInWorld && !a.IsDead && a.Owner == Player && Info.TransportPayloadTypes.Contains(a.Info.Name)))
						.Take(4)
						.ToArray();
					if (payload.Length > 0 && (cargo == null || cargo.PassengerCount == 0))
						Bot.QueueOrder(new Order("EnterTransport", null, Target.FromActor(transport), false, groupedActors: payload));
					return cargo == null || cargo.PassengerCount <= 0 || AdvanceAndContinue();
				}

				case TransportState.Extract:
					Bot.QueueOrder(new Order("Move", transport, Target.FromCell(World, transport.Location), false));
					return AdvanceAndContinue();

				case TransportState.Hold:

				default:
					return false;
			}
		}

		bool AdvanceAndContinue()
		{
			machine.Advance();
			return !machine.Complete;
		}

		static long BaseRadiusSquared(int cells)
		{
			var length = WDist.FromCells(cells).Length;
			return (long)length * length;
		}
	}

	/// <summary>
	/// Special operations controller: inserts scarce special assets (spies, engineers) at the
	/// designated rear-area target. When a transport is available the asset rides it; otherwise
	/// the asset walks in directly. In both cases the asset is claimed so no other controller
	/// pulls it into a wave.
	/// </summary>
	public sealed class SpecialOpsController : TacticalController
	{
		public SpecialOpsController(StrategicBrainBotModule brain)
			: base(brain) { }

		/// <summary>
		/// Executes a special insertion. Returns true when the asset was committed to the mission
		/// this tick (via transport or on foot).
		/// </summary>
		public bool Execute(CPos? target, string kind, bool transportAvailable)
		{
			if (target == null || kind == null || Info.SpecialTypes.Count == 0)
			{
				Unable("special-operation target, kind, or asset configuration missing", true);
				return false;
			}

			var asset = World.Actors
				.Where(a => a.IsInWorld && !a.IsDead && a.Owner == Player && Info.SpecialTypes.Contains(a.Info.Name))
				.OrderBy(a => a.ActorID)
				.FirstOrDefault();
			if (asset == null)
			{
				Unable("no special-operation asset available", true);
				return false;
			}

			// If a transport exists, the transport controller handles loading; just reserve the
			// asset so it is not pulled into a wave while waiting.
			if (transportAvailable)
			{
				Brain.Claim([asset]);
				MarkExecuted();
				return true;
			}

			// No transport: the asset walks in directly. It is claimed so the wave never takes it,
			// and it is kept out of combat by moving instead of attacking.
			var claimed = Claim([asset]);
			if (claimed.Length == 0)
				return false;

			Bot.QueueOrder(new Order("Move", asset, Target.FromCell(World, target.Value), false));
			Log($"Special asset {asset.Info.Name} inserted on foot toward {target.Value}");
			MarkExecuted();
			return true;
		}
	}
}
