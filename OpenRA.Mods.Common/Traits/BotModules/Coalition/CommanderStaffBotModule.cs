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
using OpenRA.Mods.Common.Commander.Model;
using OpenRA.Mods.Common.Commander.Staff;
using OpenRA.Mods.Common.Commander.Terrain;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	[Desc("Runs the commander's staff of specialists and applies what they decide.",
		"",
		"Managers think on worker threads against an immutable snapshot; their intents are applied",
		"here, on the game thread, in a fixed order. OpenRA is lockstep with sync hashing, so",
		"issuing orders off-thread would desync replays intermittently and under load.")]
	[TraitLocation(SystemActors.Player)]
	public sealed class CommanderStaffBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Whether the staff is allowed to act. Off computes and logs without touching the game,",
			"which is how it gets measured against the commander it would replace.")]
		public readonly bool Enabled = false;

		[Desc("Ticks between staff cycles.")]
		public readonly int CycleInterval = 125;

		[Desc("Whether managers may think on worker threads.")]
		public readonly bool ThinkInParallel = true;

		[Desc("Locomotor whose passability defines the region graph.")]
		public readonly string Locomotor = "tracked";

		[Desc("Log every directive the chief issues.")]
		public readonly bool LogDirectives = true;

		[Desc("Probability that the chief takes a random stance instead of the one it chose.",
			"FOR DATA GENERATION ONLY - it deliberately plays worse. Zero in normal play.",
			"Without it the training set has no counterfactual: the chief is near-deterministic, so",
			"a dynamics model learns to ignore the action (measured at 0.74% sensitivity) and search",
			"over it never leaves the prior.")]
		public readonly float ExplorationRate = 0f;

		[Desc("Dump the derived capability registry once at match start, for inspection.")]
		public readonly bool AuditCapabilities = false;

		[Desc("Ticks between one-line summaries of the shared database. 2500 is roughly every 100 seconds.")]
		public readonly int DatabaseReportInterval = 2500;


		public override object Create(ActorInitializer init) { return new CommanderStaffBotModule(this); }
	}

	public sealed class CommanderStaffBotModule : ConditionalTrait<CommanderStaffBotModuleInfo>, IBotTick
	{
		readonly CommanderStaffBotModuleInfo info;
		readonly CommanderStaff staff = new();

		StateExtractor extractor;
		ForwardModel model;
		EnemyBelief belief;
		StrategyPosterior posterior;

		/// <summary>Shared per-match memory. Written here on the game thread, read by managers while they think.</summary>
		readonly WorldDatabase database = new();
		TacticalManager chief;

		/// <summary>The shared record, for the parts of the commander that are not on this staff.</summary>
		public WorldDatabase Database => database;

		/// <summary>What the chief has currently ordered. This is the commander's macro-action.</summary>
		public Directive CurrentDirective => staff?.Directive;

		/// <summary>The probability the behaviour policy gave to the stance it last took.</summary>
		public float LastPropensity => chief?.LastPropensity ?? 1f;
		readonly HashSet<uint> seenThisSweep = [];
		CombatRecordRegistry registry;
		RegionGraph graph;
		Map map;
		Player owner;

		bool initialised;
		bool leader;
		string lastDirective;
		float peakOwnArmy;
		float peakOwnBase;

		/// <summary>The chief's standing orders, for the executing modules to read.</summary>
		public Directive Directive => staff.Directive;

		/// <summary>
		/// The map cell the main effort is aimed at, if the chief has named one. Region centres
		/// rather than exact targets: the chief decides where the effort goes and the execution layer
		/// decides what to shoot at when it arrives.
		/// </summary>
		public CPos? ObjectiveCell => CellOf(staff.Directive.MainEffortRegion);

		/// <summary>Where to make a show of force, if the chief authorised one.</summary>
		public CPos? FeintCell => CellOf(staff.Directive.FeintRegion);

		CPos? CellOf(int? region)
		{
			if (!Driving || graph == null || map == null || region == null)
				return null;

			if (region.Value < 0 || region.Value >= graph.Regions.Length)
				return null;

			var r = graph.Regions[region.Value];
			return MapRegions.ToCell(map, r.CentreX, r.CentreY);
		}

		/// <summary>Whether the staff is actually driving.</summary>
		public bool Driving => !IsTraitDisabled && info.Enabled && leader && initialised;

		public CommanderStaffBotModule(CommanderStaffBotModuleInfo info)
			: base(info)
		{
			this.info = info;
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (IsTraitDisabled)
				return;

			var world = bot.Player.World;

			if (!initialised)
			{
				initialised = true;
				Initialise(world, bot.Player);
				return;
			}

			// One coalition, one staff. Every allied bot carries this trait, and letting each run its
			// own chief would produce several directives countermanding one another.
			if (!leader || extractor == null || world.WorldTick % info.CycleInterval != 0)
				return;

			UpdateDatabase(bot.Player, world);
			DrainCombatRecord(bot.Player, world);
			database.Available = SurveyAvailability(bot.Player);

			var snapshot = BuildSnapshot(bot.Player, world);
			var intents = staff.Think(snapshot);

			if (info.LogDirectives)
				LogDirective(world);

			if (world.WorldTick % info.DatabaseReportInterval == 0)
			{
				CoalitionTelemetry.Log(world, database.Summary());
				CoalitionTelemetry.Log(world, database.Available.Summary());

				if (info.AuditCapabilities)
					foreach (var line in CapabilityAudit.Availability(database.Available))
						CoalitionTelemetry.Log(world, line);
			}

			if (info.Enabled)
				Apply(bot, intents);
		}

		void Initialise(World world, Player player)
		{
			owner = player;
			map = world.Map;

			leader = CoalitionLeader(player) == player;
			if (!leader)
				return;

			var locomotor = world.Map.Rules.Actors[SystemActors.World]
				.TraitInfos<LocomotorInfo>()
				.FirstOrDefault(l => l.Name == info.Locomotor);

			if (locomotor == null)
				return;

			graph = MapRegions.Build(world.Map, locomotor);
			if (graph.Regions.Length == 0)
				return;

			extractor = new StateExtractor(world, graph);
			model = new ForwardModel(graph, extractor.BuildRoleStats());
			belief = new EnemyBelief(graph.Regions.Length, r => graph.Neighbours(r));
			posterior = new StrategyPosterior();

			staff.ThinkInParallel = info.ThinkInParallel;
			BuildStaff(world);

			database.Catalogue = new UnitCatalogue(world.Map.Rules);
			CoalitionTelemetry.Log(world, database.Catalogue.Summary());

			database.Capabilities = new CapabilityRegistry(world.Map.Rules, database.Catalogue);
			CoalitionTelemetry.Log(world, database.Capabilities.Summary());

			if (info.AuditCapabilities)
				foreach (var line in CapabilityAudit.Report(database.Capabilities))
					CoalitionTelemetry.Log(world, line);

			CoalitionTelemetry.Log(world,
				$"Staff assembled: {staff.Managers.Count} managers over {graph.Regions.Length} regions, " +
				$"parallel={info.ThinkInParallel}, driving={info.Enabled}");
		}

		/// <summary>
		/// The staff. Each manager owns one domain; the chief is added last and runs last, on
		/// everybody's reports.
		/// </summary>
		void BuildStaff(World world)
		{
			staff.Add(new MapAnalysisManager());
			staff.Add(new IntelligenceManager());
			staff.Add(new ScoutingManager());
			staff.Add(new EconomyManager());
			staff.Add(new BuildingProductionManager());
			staff.Add(new RecordsManager());
			staff.Add(new UpkeepManager());
			staff.Add(new EscortManager());
			staff.Add(new NavalManager());
			staff.Add(new UnitProductionManager());
			staff.Add(new TacticalAnalysisManager());
			staff.Add(new DefenceManager());
			staff.Add(new AttackCoordinationManager());
			staff.Add(new SpecialOperationsManager());

			staff.Add(new ForceArmManager
			{
				Name = "ground-force",
				Order = 70,
				Role = CombatRole.Armor,
				AlsoCounts = [CombatRole.Infantry, CombatRole.Artillery],
			});

			staff.Add(new ForceArmManager { Name = "air-force", Order = 71, Role = CombatRole.Aircraft });
			staff.Add(new ForceArmManager { Name = "naval-force", Order = 72, Role = CombatRole.Naval });

			chief = new TacticalManager();

			// Exploration is for generating training data and nothing else. It makes the commander
			// play worse on purpose, so that the dataset contains what a different choice would
			// have done - which is exactly what a dataset generated by a deterministic chief does
			// not contain, and why search trained on one was measured inert on every position.
			//
			// world.LocalRandom, not a fresh Random: this is a lockstep simulation and every client
			// must draw the same numbers in the same order or the match desyncs.
			if (info.ExplorationRate > 0f)
			{
				var random = world.LocalRandom;
				var rate = info.ExplorationRate;
				chief.ExplorationRate = rate;
				chief.Perturb = stance =>
					random.NextFloat() < rate
						? (Stance)random.Next(0, (int)Stance.Recover + 1)
						: stance;
			}

			// A trained network, if one is answering. It replaces exactly one decision - the
			// stance - so that any measured difference is attributable to that decision and not
			// to two commanders differing in a dozen ways at once.
			var neural = owner.PlayerActor.TraitsImplementing<NeuralChiefBotModule>()
				.FirstOrDefault(m => !m.IsTraitDisabled);

			if (neural != null)
				chief.Advisor = () => neural.Recommendation;

			staff.Add(chief);
		}

		/// <summary>
		/// <para>
		/// Walks what the commander can currently see and folds it into the database, then ages
		/// everything it could not see rather than forgetting it.
		/// </para>
		/// <para>
		/// Fog is respected exactly as it is everywhere else in this commander: an enemy actor is
		/// recorded only while an allied player can actually see the cell it stands on. What the
		/// database adds is memory of what was seen and WHEN, which is not the same as vision and is
		/// not cheating - it is the difference between a commander that forgets an enemy base the
		/// moment its scout dies and one that remembers where the base was and how long ago it
		/// looked.
		/// </para>
		/// <para>
		/// Runs on the game thread, before the staff thinks, so managers only ever read a database
		/// that nothing is writing to.
		/// </para>
		/// </summary>
		void UpdateDatabase(Player player, World world)
		{
			var tick = world.WorldTick;
			seenThisSweep.Clear();

			foreach (var actor in world.ActorsHavingTrait<IOccupySpace>())
			{
				if (actor.IsDead || !actor.IsInWorld || actor.Owner == null || actor.Owner.NonCombatant)
					continue;

				Allegiance side;
				if (actor.Owner == player)
					side = Allegiance.Self;
				else if (actor.Owner.IsAlliedWith(player))
					side = Allegiance.Ally;
				else
				{
					// Ours and our allies' actors need no line of sight; theirs do.
					if (!player.Shroud.IsVisible(actor.Location))
						continue;

					side = Allegiance.Enemy;
				}

				var health = actor.TraitOrDefault<Health>();
				var fraction = health == null || health.MaxHP <= 0 ? 1f : health.HP / (float)health.MaxHP;

				var region = -1;
				if (graph != null && MapRegions.ToGrid(map, actor.Location, out var gx, out var gy))
					region = graph.RegionAt(gx, gy);

				seenThisSweep.Add(actor.ActorID);
				database.Observe(actor.ActorID, actor.Info.Name, actor.Info.HasTraitInfo<BuildingInfo>(),
					side, actor.Location, fraction, tick, "staff", region);

				// A unit of ours that is carrying out an order is being looked after by whoever gave
				// it; one standing still is not, whatever anybody's report says. This is what makes
				// "unattended" a measured property of the world rather than a claim a manager can
				// make about its own diligence.
				//
				// Buildings are deliberately NOT marked here. Marking healthy ones every sweep was
				// tried and is worse than useless: the moment one takes damage it still reads as
				// freshly attended, so the retry gate suppresses the repair that the damage was
				// supposed to trigger - measured as two damaged buildings and zero repairs ordered.
				// For a building, attendance means somebody repaired it.
				if (side == Allegiance.Self && !actor.Info.HasTraitInfo<BuildingInfo>() && !actor.IsIdle)
					database.MarkAttended(actor.ActorID, "field", tick);

				// What each of ours will do when an enemy comes into range, recorded per actor. The
				// AI default is already to engage, but a default is not a guarantee: anything that
				// has ever been given a stance keeps it, and a unit that will not shoot until it is
				// shot at is fighting at a disadvantage it chose.
				if (side == Allegiance.Self)
				{
					var autoTarget = actor.TraitOrDefault<AutoTarget>();
					database.ObserveStance(actor.ActorID, autoTarget != null,
						autoTarget == null || autoTarget.Stance == UnitStance.AttackAnything,
						autoTarget?.Stance.ToString() ?? "");
				}
			}

			// Anything previously known and not visible now becomes stale, and anything previously
			// known that has since died is recorded as destroyed - the one thing the commander can
			// be certain of, and the thing that tells it whether a razed structure comes back.
			foreach (var entry in database.All)
			{
				if (seenThisSweep.Contains(entry.ActorId) || entry.Status == RecordStatus.Destroyed)
					continue;

				var actor = world.GetActorById(entry.ActorId);
				if (actor == null || actor.IsDead)
				{
					// Only a death we could actually have witnessed. Something that went out of
					// sight and later died unobserved is unknown, not confirmed dead - and treating
					// the two alike is how a commander decides a base is gone because it stopped
					// watching it.
					if (entry.Side != Allegiance.Enemy || player.Shroud.IsVisible(entry.LastKnownCell))
						database.RecordDestroyed(entry.ActorId, tick);
				}
			}

			database.AgeUnseen(tick, seenThisSweep.Contains);
		}

		/// <summary>
		/// Folds every kill since the last cycle into the record, crediting the type that made it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Both sides are recorded and both are useful. Ours tells production which of its units
		/// actually trade well here; theirs tells it what is doing the damage. Neither is knowable
		/// from the rules - a heavy tank is excellent against armour and useless against aircraft,
		/// so what any unit is "worth" depends entirely on what the opponent brought.
		/// </para>
		/// <para>
		/// Fog is not consulted, and it does not need to be: the commander is being told about
		/// deaths it caused or suffered, which it would know about either way. It learns nothing
		/// here about where anything is.
		/// </para>
		/// </remarks>
		void DrainCombatRecord(Player player, World world)
		{
			registry ??= world.WorldActor.TraitOrDefault<CombatRecordRegistry>();
			if (registry == null)
				return;

			foreach (var outcome in registry.Drain())
			{
				var victimValue = ValueOf(world, outcome.VictimType);

				// What ours killed.
				if (outcome.HasKiller && outcome.KillerOwner == player)
					database.RecordKill(outcome.KillerActorId, outcome.KillerType, victimValue,
						outcome.VictimType);

				// And what it cost us when we were the ones dying, which is the other half of any
				// exchange worth the name.
				if (outcome.VictimOwner == player)
					database.RecordLossValue(outcome.VictimType, victimValue);
			}
		}

		int ValueOf(World world, string type)
		{
			if (string.IsNullOrEmpty(type))
				return 0;

			var catalogued = database.Catalogue?.Find(type);
			if (catalogued != null)
				return catalogued.Cost;

			return world.Map.Rules.Actors.TryGetValue(type, out var actor)
				? actor.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0
				: 0;
		}

		/// <summary>
		/// Reads what the commander can actually start right now, from the engine rather than from
		/// assumption: which queues will accept which items, how long each would take including
		/// anything already queued ahead of it, whether the credits are there, and whether it would
		/// tip the base into a brownout.
		/// </summary>
		Availability SurveyAvailability(Player player)
		{
			var registry = database.Capabilities;
			if (registry == null)
				return new Availability();

			var resources = player.PlayerActor.TraitOrDefault<PlayerResources>();
			var power = player.PlayerActor.TraitOrDefault<PowerManager>();
			var cash = resources?.GetCashAndResources() ?? 0;
			var excess = power?.ExcessPower ?? 0;

			var options = new List<BuildOption>();
			foreach (var queue in player.PlayerActor.TraitsImplementing<ProductionQueue>())
			{
				if (!queue.Enabled)
					continue;

				// Whatever is already on this queue has to finish first, so the wait for a new item
				// is the backlog plus its own build time. A commander comparing "cheap now" against
				// "strong shortly" cannot do it without this number.
				var backlog = queue.AllQueued().Sum(i => Math.Max(0, i.RemainingTime));

				foreach (var item in queue.BuildableItems())
				{
					var capability = registry.Find(item.Name);
					if (capability == null)
						continue;

					var ticks = backlog + queue.GetBuildTime(item, item.TraitInfo<BuildableInfo>());

					options.Add(new BuildOption
					{
						Capability = capability,
						Queue = queue.Info.Type,
						TimeToField = ticks / (float)AbstractState.TicksPerSecond,
						Affordable = cash >= capability.Cost,
						CausesBrownout = capability.DrawsPower && excess + capability.Power < 0,
					});
				}
			}

			options.Sort((a, b) => a.TimeToField != b.TimeToField
				? a.TimeToField.CompareTo(b.TimeToField)
				: string.CompareOrdinal(a.Type, b.Type));

			var powers = new List<SupportPowerState>();
			var manager = player.PlayerActor.TraitOrDefault<SupportPowerManager>();
			if (manager != null)
			{
				foreach (var (key, instance) in manager.Powers.OrderBy(p => p.Key, StringComparer.Ordinal))
				{
					if (instance.Disabled)
						continue;

					powers.Add(new SupportPowerState(key, instance.Info.Name ?? key,
						instance.Ready, instance.RemainingTicks / (float)AbstractState.TicksPerSecond));
				}
			}

			// What we own, counted by what it can DO rather than by what it is called.
			var owned = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (var entry in database.Standing(Allegiance.Self))
			{
				var capability = registry.Find(entry.Type);
				if (capability == null)
					continue;

				foreach (var verb in Availability.VerbsOf(capability))
					owned[verb] = owned.GetValueOrDefault(verb) + 1;
			}

			return new Availability
			{
				Options = options,
				SupportPowers = powers,
				Cash = cash,
				PowerProvided = power?.PowerProvided ?? 0,
				PowerDrained = power?.PowerDrained ?? 0,
				InOutage = (power?.PowerOutageRemainingTicks ?? 0) > 0,
				OwnedByVerb = owned,
			};
		}

		CommanderSnapshot BuildSnapshot(Player player, World world)
		{
			var enemies = Enemies(player).ToArray();
			var state = extractor.Extract(player, enemies);

			UpdateBelief(player, state, world.WorldTick);
			belief.ApplyTo(state.Enemy);

			var resources = player.PlayerActor.TraitOrDefault<PlayerResources>();

			var structures = new Dictionary<string, int>();
			var units = new Dictionary<string, int>();
			foreach (var actor in world.ActorsHavingTrait<IOccupySpace>())
			{
				if (actor.Owner != player || actor.IsDead || !actor.IsInWorld)
					continue;

				var target = actor.Info.HasTraitInfo<BuildingInfo>() ? structures : units;
				target[actor.Info.Name] = target.GetValueOrDefault(actor.Info.Name) + 1;
			}

			var queues = new List<CommanderSnapshot.QueueSnapshot>();
			foreach (var queue in player.PlayerActor.TraitsImplementing<ProductionQueue>())
			{
				if (!queue.Enabled)
					continue;

				var current = queue.CurrentItem();
				queues.Add(new CommanderSnapshot.QueueSnapshot(
					queue.Info.Type, current?.Item ?? "", queue.AllQueued().Count()));
			}

			return new CommanderSnapshot
			{
				Tick = world.WorldTick,
				State = state,
				Graph = graph,
				Belief = belief,
				Opponent = posterior,
				Cash = resources?.GetCashAndResources() ?? 0,
				Earned = resources?.Earned ?? 0,
				Spent = resources?.Spent ?? 0,
				Queues = queues,
				Structures = structures,
				Units = units,
				Database = database,
			};
		}

		void UpdateBelief(Player self, AbstractState state, int tick)
		{
			belief.Propagate(info.CycleInterval / (float)AbstractState.TicksPerSecond);

			for (var region = 0; region < graph.Regions.Length; region++)
			{
				var r = graph.Regions[region];
				var cell = MapRegions.ToCell(map, r.CentreX, r.CentreY);
				if (!self.Shroud.IsVisible(cell))
					continue;

				belief.Observe(region, state.Enemy.ForcesIn(region), tick, state.Enemy.StructuresIn(region));
			}

			// An opponent exists whether or not it has been seen, anchored to peaks so that losing
			// our own army does not make us believe theirs vanished too.
			peakOwnArmy = Math.Max(peakOwnArmy, state.Self.ArmyValue());
			peakOwnBase = Math.Max(peakOwnBase, state.Self.BaseIntegrity);
			belief.AssumeUnseen(peakOwnArmy, tick, 750);
			belief.AssumeUnseenStructures(peakOwnBase, tick, 750);
		}

		/// <summary>
		/// Applies the staff's intents on the game thread, in the order the scheduler produced them.
		/// Only production is acted on directly; movement and posture are read from the directive by
		/// the modules that already own those, so this does not fight them for control.
		/// </summary>
		void Apply(IBot bot, IReadOnlyList<IManagerIntent> intents)
		{
			var queues = bot.Player.PlayerActor.TraitsImplementing<ProductionQueue>()
				.Where(q => q.Enabled)
				.ToArray();

			foreach (var intent in intents)
			{
				switch (intent)
				{
					case ProduceUnitIntent produce:
						QueueItem(bot, queues, produce.Unit, produce.Count);
						break;

					// Structures are deliberately NOT queued here. The base builder owns that queue
					// and has the placement, fraction and delay logic that makes a base coherent;
					// issuing construction orders alongside it means competing for one queue with no
					// arbitration. Measured, the staff doing so produced a base of four war
					// factories and a Tesla coil - no power, no refineries - against a baseline of
					// seven power plants, two refineries and a defensive line. Exchange ratio fell
					// from 1.12 to 0.44 and losses went from 2 to 8 across twelve matches.
					//
					// What the staff contributes to construction is its directive, which the
					// building manager's own requests already reflect.
					case RepairIntent repair:
						Repair(bot, repair);
						break;

					case RelocateIntent relocate:
						Relocate(bot, relocate);
						break;

					case SetAttackModeIntent attackMode:
						SetAttackMode(bot, attackMode);
						break;

					case EscortIntent escort:
						Escort(bot, escort);
						break;

					case CovertTransitIntent covert:
						CovertTransit(bot, covert);
						break;

					// The chief's overview, and the arbitration notes beneath it. Recorded rather than
					// acted on: this is the commander explaining itself, which is what makes a wrong
					// decision diagnosable after the fact instead of merely regrettable.
					// Not rate-limited here on purpose: the chief only produces an overview when it
					// issues a directive, which is once a minute, and gating that again on a tick
					// interval means the two almost never coincide.
					case AssessmentIntent assessment when assessment.Topic == "overview":
						CoalitionTelemetry.Log(bot.Player.World, $"CEO: {assessment.Finding}");
						break;

					case ConstructIntent:
						break;
				}
			}
		}

		/// <summary>
		/// Puts a damaged building of ours back into repair, and records that somebody attended to
		/// it so the same building is not offered up again on the next cycle.
		/// </summary>
		/// <remarks>
		/// This is not duplicating the engine's repair module. That one is an
		/// <c>IBotRespondToAttack</c> handler: it repairs while an attack notification is arriving
		/// and does nothing at all otherwise, so a building damaged in a raid that ends is never
		/// repaired. What is missing is not a repair order, it is anybody whose job is to notice.
		/// </remarks>
		void Repair(IBot bot, RepairIntent intent)
		{
			var actor = bot.Player.World.GetActorById(intent.ActorId);
			if (actor == null || actor.IsDead || !actor.IsInWorld || actor.Owner != bot.Player)
				return;

			var repairable = actor.TraitOrDefault<RepairableBuilding>();
			if (repairable == null || repairable.RepairActive)
				return;

			var health = actor.TraitOrDefault<Health>();
			if (health == null || health.DamageState <= DamageState.Undamaged || health.DamageState == DamageState.Dead)
				return;

			bot.QueueOrder(new Order("RepairBuilding", bot.Player.PlayerActor, Target.FromActor(actor), false));
			database.MarkAttended(intent.ActorId, "upkeep", bot.Player.World.WorldTick);
			database.RecordOrder(intent.ActorId, "RepairBuilding", "upkeep", bot.Player.World.WorldTick);
		}

		/// <summary>
		/// Moves an idle unit out of the base, and records that somebody dealt with it so it is not
		/// picked up again next cycle.
		/// </summary>
		/// <remarks>
		/// Deliberately restricted to units that are actually idle. A unit carrying out somebody
		/// else's order is not loitering, and overriding it here would be the staff countermanding
		/// the command centre - a single match already logs more than two thousand rejected order
		/// conflicts without any help from this.
		/// </remarks>
		void Relocate(IBot bot, RelocateIntent intent)
		{
			var actor = bot.Player.World.GetActorById(intent.ActorId);
			if (actor == null || actor.IsDead || !actor.IsInWorld || actor.Owner != bot.Player || !actor.IsIdle)
				return;

			var mobile = actor.TraitOrDefault<Mobile>();
			if (mobile == null || !mobile.CanEnterCell(intent.Destination, actor, BlockedByActor.Immovable))
				return;

			bot.QueueOrder(new Order("Move", actor, Target.FromCell(bot.Player.World, intent.Destination), false));
			database.MarkAttended(intent.ActorId, "upkeep", bot.Player.World.WorldTick);
			database.RecordOrder(intent.ActorId, $"Move {intent.Destination}", "upkeep", bot.Player.World.WorldTick);
		}

		/// <summary>Puts a unit into attack mode, so it engages rather than waiting to be engaged.</summary>
		void SetAttackMode(IBot bot, SetAttackModeIntent intent)
		{
			var actor = bot.Player.World.GetActorById(intent.ActorId);
			if (actor == null || actor.IsDead || !actor.IsInWorld || actor.Owner != bot.Player)
				return;

			var autoTarget = actor.TraitOrDefault<AutoTarget>();
			if (autoTarget == null || autoTarget.Stance == UnitStance.AttackAnything)
				return;

			bot.QueueOrder(new Order("SetUnitStance", actor, false)
			{
				ExtraData = (uint)UnitStance.AttackAnything,
			});

			database.RecordOrder(intent.ActorId, "SetUnitStance AttackAnything", "upkeep",
				bot.Player.World.WorldTick);
		}

		/// <summary>Sets one of our units to follow and defend a harvester for as long as both live.</summary>
		void Escort(IBot bot, EscortIntent intent)
		{
			var world = bot.Player.World;
			var escort = world.GetActorById(intent.EscortId);
			var harvester = world.GetActorById(intent.HarvesterId);

			if (escort == null || escort.IsDead || !escort.IsInWorld || escort.Owner != bot.Player)
				return;

			if (harvester == null || harvester.IsDead || !harvester.IsInWorld || harvester.Owner != bot.Player)
				return;

			if (escort.TraitOrDefault<Guard>() == null || harvester.TraitOrDefault<Guardable>() == null)
				return;

			bot.QueueOrder(new Order("Guard", escort, Target.FromActor(harvester), false));

			// The pairing is recorded on the escort rather than held in the manager, so it survives
			// across cycles and goes stale automatically when either actor dies.
			database.MarkAttended(intent.EscortId, EscortManager.Attendant, world.WorldTick);
			database.RecordOrder(intent.EscortId, $"Guard {intent.HarvesterId}",
				EscortManager.Attendant, world.WorldTick);
		}

		/// <summary>
		/// Silences operatives for the duration of an infiltration, and releases them afterwards.
		/// </summary>
		/// <remarks>
		/// Marking them attended by the covert attendant is what keeps the upkeep manager from
		/// helpfully putting them straight back into attack mode; releasing the mark is what lets it
		/// do so again once the operation is over.
		/// </remarks>
		void CovertTransit(IBot bot, CovertTransitIntent intent)
		{
			if (string.IsNullOrEmpty(intent.OperativeType))
				return;

			var world = bot.Player.World;
			var tick = world.WorldTick;

			// Driven off the record rather than by scanning every actor in the world. The scan is
			// the obvious way to write this and costs a full world walk per operative type per
			// cycle, for the sake of finding the handful of actors the record already indexes.
			foreach (var entry in database.Standing(Allegiance.Self))
			{
				if (entry.Type != intent.OperativeType)
					continue;

				var actor = world.GetActorById(entry.ActorId);
				if (actor == null || actor.IsDead || !actor.IsInWorld || actor.Owner != bot.Player)
					continue;

				var autoTarget = actor.TraitOrDefault<AutoTarget>();
				if (autoTarget == null)
					continue;

				var alreadyCovert = entry.AttendedBy == UpkeepManager.CovertAttendant;

				if (intent.InTransit)
				{
					if (alreadyCovert)
						continue;

					bot.QueueOrder(new Order("SetUnitStance", actor, false)
					{
						ExtraData = (uint)UnitStance.HoldFire,
					});

					database.MarkAttended(actor.ActorID, UpkeepManager.CovertAttendant, tick);
					database.RecordOrder(actor.ActorID, "SetUnitStance HoldFire",
						UpkeepManager.CovertAttendant, tick);
				}
				else if (alreadyCovert)
				{
					// Released. The upkeep manager restores attack mode on its next cycle, which
					// keeps one manager responsible for stance rather than two.
					database.MarkAttended(actor.ActorID, "", tick);
				}
			}
		}

		void QueueItem(IBot bot, ProductionQueue[] queues, string item, int count)
		{
			if (string.IsNullOrEmpty(item) || count <= 0)
				return;

			// Never re-order something already on its way. The staff reviews every 125 ticks while a
			// tank takes several hundred to build, so without this each request is issued dozens of
			// times and the queue fills with duplicates that crowd out everything else.
			if (queues.Any(q => q.AllQueued().Any(i => i.Item == item)))
				return;

			var queue = queues.FirstOrDefault(q =>
				q.Info.Type != "Building" && q.Info.Type != "Defense"
				&& q.BuildableItems().Any(i => i.Name == item));

			if (queue == null)
				return;

			bot.QueueOrder(Order.StartProduction(queue.Actor, item, Math.Min(count, 2)));
		}

		void LogDirective(World world)
		{
			var current = staff.Directive.ToString();
			if (current == lastDirective)
				return;

			lastDirective = current;
			CoalitionTelemetry.Log(world, "Chief: " + current);

			foreach (var report in staff.LastReports)
				CoalitionTelemetry.Log(world, "  " + report);
		}

		static Player CoalitionLeader(Player self)
		{
			var leader = self;
			foreach (var player in self.World.Players)
			{
				if (player.NonCombatant || !player.IsBot || !player.IsAlliedWith(self))
					continue;

				if (string.CompareOrdinal(player.InternalName, leader.InternalName) < 0)
					leader = player;
			}

			return leader;
		}

		static IEnumerable<Player> Enemies(Player self)
		{
			foreach (var player in self.World.Players)
				if (!player.NonCombatant && player != self && !player.IsAlliedWith(self))
					yield return player;
		}
	}
}
