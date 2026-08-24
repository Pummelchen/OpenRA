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

			var snapshot = BuildSnapshot(bot.Player, world);
			var intents = staff.Think(snapshot);

			if (info.LogDirectives)
				LogDirective(world);

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
			BuildStaff();

			CoalitionTelemetry.Log(world,
				$"Staff assembled: {staff.Managers.Count} managers over {graph.Regions.Length} regions, " +
				$"parallel={info.ThinkInParallel}, driving={info.Enabled}");
		}

		/// <summary>
		/// The staff. Each manager owns one domain; the chief is added last and runs last, on
		/// everybody's reports.
		/// </summary>
		void BuildStaff()
		{
			staff.Add(new MapAnalysisManager());
			staff.Add(new IntelligenceManager());
			staff.Add(new ScoutingManager());
			staff.Add(new EconomyManager());
			staff.Add(new BuildingProductionManager());
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

			staff.Add(new TacticalManager());
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

					case ConstructIntent construct:
						QueueItem(bot, queues, construct.Structure, 1);
						break;
				}
			}
		}

		static void QueueItem(IBot bot, ProductionQueue[] queues, string item, int count)
		{
			if (string.IsNullOrEmpty(item) || count <= 0)
				return;

			var queue = queues.FirstOrDefault(q => q.BuildableItems().Any(i => i.Name == item));
			if (queue == null)
				return;

			bot.QueueOrder(Order.StartProduction(queue.Actor, item, Math.Min(count, 4)));
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
