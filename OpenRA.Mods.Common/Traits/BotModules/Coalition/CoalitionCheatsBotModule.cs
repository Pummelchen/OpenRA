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
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	[Desc("Grants the coalition bot developer-mode advantages: instant construction, building",
		"outside the base radius, no power constraint, and the full tech tree.",
		"",
		"THIS IS CHEATING, and it is opt-in for exactly that reason. The advantages are applied to",
		"the bot's own player only - the cheats lobby option is global and would hand the same",
		"advantages to the opponent, which would measure nothing. Fog of war is deliberately NOT",
		"touched: the commander still only sees what it has scouted, so its decisions remain honest",
		"even when its construction is not.",
		"",
		"Any benchmark run with this enabled is not a fair-play result and must be reported as such.")]
	[TraitLocation(SystemActors.Player)]
	public sealed class CoalitionCheatsBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Structures and units complete immediately, with no build time.")]
		public readonly bool InstantBuild = true;

		[Desc("Buildings may be placed outside the construction yard's radius.")]
		public readonly bool BuildAnywhere = true;

		[Desc("Production and defences ignore the power supply.")]
		public readonly bool UnlimitedPower = true;

		[Desc("The whole tech tree is available without its prerequisite buildings.")]
		public readonly bool AllTech = true;

		[Desc("Cash granted per interval. This is not optional garnish: FastBuild collapses build",
			"time to a single tick, and an item's cost is drawn down over its build time, so with",
			"instant build the full price falls due immediately. Without matching income the",
			"queues stall on cash they never accumulate and the bot builds less than it would have",
			"built honestly - measured on shattered-mountain/805 vs Rush, instant build alone turns",
			"a draw into an 8550/31400 defeat, while the same cheats with this income produce a",
			"61000/25050 exchange. Set to 0 only to reproduce that handicap deliberately.")]
		public readonly int CashPerInterval = 2000;

		[Desc("Ticks between cash grants, when CashPerInterval is set.")]
		public readonly int CashInterval = 250;

		public override object Create(ActorInitializer init) { return new CoalitionCheatsBotModule(this); }
	}

	public sealed class CoalitionCheatsBotModule : ConditionalTrait<CoalitionCheatsBotModuleInfo>, IBotTick
	{
		readonly CoalitionCheatsBotModuleInfo info;
		bool applied;

		public CoalitionCheatsBotModule(CoalitionCheatsBotModuleInfo info)
			: base(info)
		{
			this.info = info;
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (IsTraitDisabled)
				return;

			var player = bot.Player;
			var playerActor = player.PlayerActor;

			if (!applied)
			{
				var developerMode = playerActor.TraitOrDefault<DeveloperMode>();
				if (developerMode == null)
					return;

				// DeveloperMode only accepts these while it is enabled, which depends on the lobby.
				// If it is off there is nothing to grant and nothing to warn about - the bot simply
				// plays fair, which is the correct failure mode for a cheat switch.
				if (!developerMode.Enabled)
				{
					applied = true;
					CoalitionTelemetry.Log(player.World,
						"Cheats requested but developer mode is unavailable in this lobby; playing fair.");
					return;
				}

				// Toggled through the same orders a human uses, so the effects are applied to this
				// player alone rather than to everyone in the lobby.
				var granted = 0;
				foreach (var order in GrantedOrders(info))
				{
					if (AlreadyHeld(developerMode, order))
						continue;

					bot.QueueOrder(new Order(order, playerActor, false));
					granted++;
				}

				applied = true;
				CoalitionTelemetry.Log(player.World,
					$"CHEATS ENABLED for {player.InternalName}: {granted} advantages granted " +
					$"(instant={info.InstantBuild} anywhere={info.BuildAnywhere} " +
					$"power={info.UnlimitedPower} tech={info.AllTech}) - results are not fair play");
			}

			if (ShouldGrantCash(player.World.WorldTick, info.CashPerInterval, info.CashInterval))
				playerActor.TraitOrDefault<PlayerResources>()?.GiveCash(info.CashPerInterval);
		}

		/// <summary>
		/// The developer-mode orders this configuration grants, in the order they are issued.
		/// </summary>
		/// <remarks>
		/// Every order named here is a construction or economy advantage. None of them is a
		/// visibility advantage, and that is the whole point of enumerating them in one place: the
		/// commander is allowed to build dishonestly, never to see dishonestly. DevVisibility,
		/// DevGiveExploration and DevAll are deliberately absent, and a test asserts they stay
		/// absent, because adding one would silently invalidate every intelligence result the
		/// coalition has ever been measured on.
		/// </remarks>
		public static IEnumerable<string> GrantedOrders(CoalitionCheatsBotModuleInfo info)
		{
			if (info.InstantBuild)
				yield return DeveloperMode.Orders.FastBuild;

			if (info.BuildAnywhere)
				yield return DeveloperMode.Orders.BuildAnywhere;

			if (info.UnlimitedPower)
				yield return DeveloperMode.Orders.UnlimitedPower;

			if (info.AllTech)
				yield return DeveloperMode.Orders.EnableTech;
		}

		/// <summary>
		/// Whether cash is due this tick. Guards the interval so a misconfigured 0 grants every
		/// tick rather than dividing by zero.
		/// </summary>
		public static bool ShouldGrantCash(int worldTick, int cashPerInterval, int cashInterval)
		{
			return cashPerInterval > 0 && worldTick % System.Math.Max(1, cashInterval) == 0;
		}

		static bool AlreadyHeld(DeveloperMode developerMode, string order)
		{
			return order switch
			{
				DeveloperMode.Orders.FastBuild => developerMode.FastBuild,
				DeveloperMode.Orders.BuildAnywhere => developerMode.BuildAnywhere,
				DeveloperMode.Orders.UnlimitedPower => developerMode.UnlimitedPower,
				DeveloperMode.Orders.EnableTech => developerMode.AllTech,
				_ => false,
			};
		}
	}
}
