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

using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Starts every bot-controlled player with a fixed amount of cash.",
		"",
		"The lobby's Starting Cash option is global and cannot single out bots, so this trait raises",
		"bot players to the configured floor once the world is loaded. It applies to every bot type",
		"equally - the coalition commander and the scripted opponents alike - so bot-versus-bot",
		"measurements stay a like-for-like comparison. Against a human it is an advantage, and",
		"should be reported as one.",
		"",
		"This is a floor, not a bonus: a bot already at or above the amount is left alone, so",
		"raising the lobby's Starting Cash above it still works as expected.")]
	[TraitLocation(SystemActors.Player)]
	public class BotStartingCashInfo : TraitInfo
	{
		[Desc("Cash every bot player is raised to at the start of the match. 0 disables the trait.")]
		public readonly int Amount = 0;

		public override object Create(ActorInitializer init) { return new BotStartingCash(this); }
	}

	public class BotStartingCash : INotifyCreated
	{
		/// <summary>
		/// Per-run override, set by the headless harness. Exists so a batch can generate training
		/// data under a configuration that actually produces decisive games without editing the
		/// shipped mod default - at 20,000 the commander cannot close a mirror match even in forty
		/// minutes of game time, so every match is a draw and every label is identical, which
		/// teaches a model nothing.
		/// </summary>
		public static int? AmountOverride;

		readonly BotStartingCashInfo info;

		public BotStartingCash(BotStartingCashInfo info)
		{
			this.info = info;
		}

		void INotifyCreated.Created(Actor self)
		{
			var owner = self.Owner;
			var resources = self.TraitOrDefault<PlayerResources>();
			if (resources == null)
				return;

			// Assigned rather than granted through GiveCash: this is starting capital, not income,
			// and GiveCash also raises Earned. Inflating Earned would misreport every economy
			// statistic the commander and the telemetry read back - income rate, harvester value,
			// and the economic-emergency test all treat Earned as money the base actually produced.
			resources.Cash = CashFor(AmountOverride ?? info.Amount, resources.Cash, owner.IsBot, owner.NonCombatant);
		}

		/// <summary>
		/// The cash a player should hold at match start: the configured floor for a combatant bot,
		/// and whatever they already had for everyone else.
		/// </summary>
		public static int CashFor(int amount, int currentCash, bool isBot, bool nonCombatant)
		{
			if (amount <= 0 || !isBot || nonCombatant)
				return currentCash;

			return currentCash < amount ? amount : currentCash;
		}
	}
}
