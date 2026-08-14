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
		protected readonly StrategicBrainBotModule Brain;
		protected readonly World World;
		protected readonly Player Player;
		protected readonly IBot Bot;
		protected readonly StrategicBrainBotModuleInfo Info;

		protected TacticalController(StrategicBrainBotModule brain)
		{
			Brain = brain;
			World = brain.World;
			Player = brain.Player;
			Bot = brain.Bot;
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

		/// <summary>Orders the ground component of an assault wave toward the target.</summary>
		public void Attack(Actor[] available, WPos target)
		{
			var land = Claim(LandUnits(available));
			if (land.Length == 0)
				return;

			Bot.QueueOrder(new Order("AttackMove", null, Target.FromPos(target), false, groupedActors: land));
			Executed = true;
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
				return;

			Bot.QueueOrder(new Order("AttackMove", null, Target.FromPos(target), false, groupedActors: air));
			Executed = true;
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
				return;

			Bot.QueueOrder(new Order("AttackMove", null, Target.FromPos(target), false, groupedActors: naval));
			Executed = true;
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
				return false;

			var transport = World.Actors
				.Where(a => a.IsInWorld && !a.IsDead && a.Owner == Player && Info.TransportTypes.Contains(a.Info.Name))
				.OrderBy(a => a.ActorID)
				.FirstOrDefault();
			if (transport == null)
				return false;

			var targetCell = target.Value;
			var cargo = transport.TraitOrDefault<Cargo>();

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
					return cargo == null || cargo.PassengerCount > 0 || payload.Length == 0
						? AdvanceAndContinue()
						: true;
				}

				case TransportState.WaitForWindow:
					// Hold until the synchronization window elapses (deception/distraction timing).
					windowElapsed++;
					return windowElapsed >= 30
						? AdvanceAndContinue()
						: true;

				case TransportState.Transit:
				{
					// Move toward the insertion point; abort (hold) if the transit becomes unsafe:
					// the transport takes heavy damage.
					var distance = (transport.CenterPosition - World.Map.CenterOfCell(targetCell)).LengthSquared;
					Bot.QueueOrder(new Order("Move", transport, Target.FromCell(World, targetCell), false));

					var health = transport.TraitOrDefault<IHealth>();
					var fraction = health == null ? 100 : health.HP * 100 / health.MaxHP;
					if (fraction < Info.RetreatHealthPercent)
					{
						Log($"Transport mission aborted: transport at {fraction}% health during transit");
						machine.Abort();
						return false;
					}

					if (distance <= BaseRadiusSquared(10))
						return AdvanceAndContinue();
					return true;
				}

				case TransportState.Approach:
				{
					var distance = (transport.CenterPosition - World.Map.CenterOfCell(targetCell)).LengthSquared;
					Bot.QueueOrder(new Order("Move", transport, Target.FromCell(World, targetCell), false));
					return distance <= BaseRadiusSquared(5)
						? AdvanceAndContinue()
						: true;
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
					return cargo != null && cargo.PassengerCount > 0
						? AdvanceAndContinue()
						: true;
				}

				case TransportState.Extract:
					Bot.QueueOrder(new Order("Move", transport, Target.FromCell(World, transport.Location), false));
					return AdvanceAndContinue();

				case TransportState.Hold:
					return false;

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
				return false;

			var asset = World.Actors
				.Where(a => a.IsInWorld && !a.IsDead && a.Owner == Player && Info.SpecialTypes.Contains(a.Info.Name))
				.OrderBy(a => a.ActorID)
				.FirstOrDefault();
			if (asset == null)
				return false;

			// If a transport exists, the transport controller handles loading; just reserve the
			// asset so it is not pulled into a wave while waiting.
			if (transportAvailable)
			{
				Brain.Claim([asset]);
				Executed = true;
				return true;
			}

			// No transport: the asset walks in directly. It is claimed so the wave never takes it,
			// and it is kept out of combat by moving instead of attacking.
			var claimed = Claim([asset]);
			if (claimed.Length == 0)
				return false;

			Bot.QueueOrder(new Order("Move", asset, Target.FromCell(World, target.Value), false));
			Log($"Special asset {asset.Info.Name} inserted on foot toward {target.Value}");
			Executed = true;
			return true;
		}
	}
}
