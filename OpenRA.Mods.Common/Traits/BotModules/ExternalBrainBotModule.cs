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

using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Consult an external model server for enhanced bot decisions. The bot state is serialized to JSON and posted " +
		"to the configured URL; the response plan (production, attack target, retreat) is applied on the game thread. " +
		"Requests are asynchronous with a timeout, so a missing or slow server falls back to the scripted bot brain. " +
		"Note: model-driven orders are not deterministic and will desync multiplayer games and replays - use in " +
		"single player or with replays disabled.")]
	[TraitLocation(SystemActors.Player)]
	public sealed class ExternalBrainBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Base URL of the model server, e.g. http://127.0.0.1:8765. Leave empty to disable the external brain.")]
		public readonly string ExternalBrainUrl = null;

		[Desc("Interval (in ticks) between model consultations.")]
		public readonly int ExternalBrainInterval = 200;

		[Desc("HTTP request timeout in milliseconds. A timed-out request falls back to the scripted brain.")]
		public readonly int ExternalBrainTimeout = 2000;

		public override object Create(ActorInitializer init) { return new ExternalBrainBotModule(this, init); }
	}

	public sealed class ExternalBrainBotModule : ConditionalTrait<ExternalBrainBotModuleInfo>, IBotTick
	{
		sealed class BotState
		{
			public int Tick { get; set; }
			public int Cash { get; set; }
			public int ArmyCount { get; set; }
			public object[] Own { get; set; }
			public object[] Enemies { get; set; }
		}

		sealed class UnitState
		{
			public string Type { get; set; }
			public int X { get; set; }
			public int Y { get; set; }
			public int HealthPercent { get; set; }
		}

		sealed class Plan
		{
			public string[] Produce { get; set; }
			public PlanTarget Attack { get; set; }
			public bool Retreat { get; set; }
		}

		sealed class PlanTarget
		{
			public int X { get; set; }
			public int Y { get; set; }
		}

		readonly ExternalBrainBotModuleInfo info;
		readonly HttpClient http = new();

		string pendingPlan;
		bool requestInFlight;
		int lastRequestTick;

		public ExternalBrainBotModule(ExternalBrainBotModuleInfo info, ActorInitializer init)
			: base(info)
		{
			this.info = info;
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (IsTraitDisabled || string.IsNullOrEmpty(info.ExternalBrainUrl))
				return;

			var world = bot.Player.World;

			// Apply a plan that was received asynchronously since the last tick.
			if (pendingPlan != null)
			{
				ApplyPlan(bot, pendingPlan);
				pendingPlan = null;
			}

			var tick = world.WorldTick;
			if (requestInFlight || tick - lastRequestTick < info.ExternalBrainInterval)
				return;

			lastRequestTick = tick;
			requestInFlight = true;
			var state = BuildSnapshot(bot, tick);
			_ = RequestPlanAsync(state);
		}

		/// <summary>Serializes the current bot state into a JSON snapshot for the model server.</summary>
		static string BuildSnapshot(IBot bot, int tick)
		{
			var player = bot.Player;
			var world = player.World;
			var resources = player.PlayerActor.TraitOrDefault<PlayerResources>();

			var own = world.Actors
				.Where(a => a.IsInWorld && !a.IsDead && a.Owner == player)
				.Select(a => new UnitState
				{
					Type = a.Info.Name,
					X = a.Location.X,
					Y = a.Location.Y,
					HealthPercent = HealthPercent(a)
				})
				.ToArray();

			var enemies = world.Actors
				.Where(a => a.IsInWorld && !a.IsDead && a.Owner != player
					&& player.RelationshipWith(a.Owner) == PlayerRelationship.Enemy
					&& player.Shroud.IsExplored(a.CenterPosition))
				.Select(a => new UnitState
				{
					Type = a.Info.Name,
					X = a.Location.X,
					Y = a.Location.Y,
					HealthPercent = HealthPercent(a)
				})
				.ToArray();

			var state = new BotState
			{
				Tick = tick,
				Cash = resources?.GetCashAndResources() ?? 0,
				ArmyCount = own.Length,
				Own = own,
				Enemies = enemies
			};

			return JsonSerializer.Serialize(state);
		}

		static int HealthPercent(Actor a)
		{
			var health = a.TraitOrDefault<IHealth>();
			return health == null ? 100 : health.HP * 100 / health.MaxHP;
		}

		/// <summary>Posts the snapshot to the model server. The result is consumed on the game thread.</summary>
		async Task RequestPlanAsync(string state)
		{
			try
			{
				using var timeout = new CancellationTokenSource(info.ExternalBrainTimeout);
				using var content = new StringContent(state, Encoding.UTF8, "application/json");
				var response = await http.PostAsync(info.ExternalBrainUrl, content, timeout.Token).ConfigureAwait(false);
				var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
				pendingPlan = body;
			}
			catch
			{
				// The scripted brain continues to make decisions while the model server is unavailable.
			}
			finally
			{
				requestInFlight = false;
			}
		}

		/// <summary>Applies the model's plan: production orders, an attack wave, or a retreat.</summary>
		static void ApplyPlan(IBot bot, string planJson)
		{
			Plan plan;
			try
			{
				plan = JsonSerializer.Deserialize<Plan>(planJson);
			}
			catch
			{
				return;
			}

			if (plan == null)
				return;

			var player = bot.Player;
			var world = player.World;

			if (plan.Produce != null)
			{
				var queues = player.PlayerActor.TraitsImplementing<ProductionQueue>()
					.Where(q => q.Enabled && q.CurrentItem() == null)
					.ToArray();

				foreach (var unitName in plan.Produce)
				{
					if (string.IsNullOrEmpty(unitName))
						continue;

					var queue = queues.FirstOrDefault(q => q.BuildableItems().Any(i => i.Name == unitName));
					if (queue != null)
						bot.QueueOrder(Order.StartProduction(queue.Actor, unitName, 1));
				}
			}

			if (plan.Attack != null)
			{
				var target = Target.FromCell(world, new CPos(plan.Attack.X, plan.Attack.Y));
				var army = world.Actors
					.Where(a => a.IsInWorld && !a.IsDead && a.Owner == player && !a.Info.HasTraitInfo<BuildingInfo>())
					.ToArray();
				if (army.Length > 0)
					bot.QueueOrder(new Order("AttackMove", null, target, false, groupedActors: army));
			}

			if (plan.Retreat)
			{
				var baseCenter = BaseCenter(world, player);
				if (baseCenter != null)
				{
					var cell = world.Map.CellContaining(baseCenter.Value);
					foreach (var a in world.Actors.Where(a => a.IsInWorld && !a.IsDead && a.Owner == player && !a.Info.HasTraitInfo<BuildingInfo>()))
						bot.QueueOrder(new Order("Move", a, Target.FromCell(world, cell), false));
				}
			}
		}

		static WPos? BaseCenter(World world, Player player)
		{
			var structures = world.Actors
				.Where(a => a.IsInWorld && !a.IsDead && a.Owner == player && a.Info.HasTraitInfo<BuildingInfo>())
				.Select(a => a.CenterPosition)
				.ToArray();
			return structures.Length == 0 ? null : structures.Average();
		}
	}
}
