#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of the
 * License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// <para>
	/// Bot starting capital. The lobby's Starting Cash option is global and cannot single out bots,
	/// so this trait raises bot players to a floor once the world is loaded.
	/// </para>
	/// <para>
	/// The property that matters is symmetry: it must apply on the sole basis of being a combatant
	/// bot, never on which bot. If it ever favoured the coalition over the scripted opponents, every
	/// benchmark number in the report would stop being a like-for-like comparison and would instead
	/// be measuring the handout.
	/// </para>
	/// </summary>
	[TestFixture]
	sealed class BotStartingCashTest
	{
		[TestCase(TestName = "Combatant bots are raised to the floor.")]
		public void BotsAreRaised()
		{
			Assert.That(BotStartingCash.CashFor(20000, 5000, isBot: true, nonCombatant: false), Is.EqualTo(20000));
		}

		[TestCase(TestName = "Humans and non-combatants are untouched.")]
		public void OnlyBotsAreRaised()
		{
			Assert.That(BotStartingCash.CashFor(20000, 5000, isBot: false, nonCombatant: false), Is.EqualTo(5000),
				"A human player's starting cash is the lobby's business, not this trait's.");

			Assert.That(BotStartingCash.CashFor(20000, 5000, isBot: true, nonCombatant: true), Is.EqualTo(5000),
				"Neutral and creep players do not build and must not be handed capital.");
		}

		[TestCase(TestName = "It is a floor, not a bonus.")]
		public void RaisingTheLobbyOptionStillWorks()
		{
			// Otherwise setting the lobby's Starting Cash above the floor would be silently ignored,
			// or worse, stack into an amount nobody configured.
			Assert.That(BotStartingCash.CashFor(20000, 50000, isBot: true, nonCombatant: false), Is.EqualTo(50000));
			Assert.That(BotStartingCash.CashFor(20000, 20000, isBot: true, nonCombatant: false), Is.EqualTo(20000));
		}

		[TestCase(TestName = "Zero disables the trait entirely.")]
		public void ZeroDisables()
		{
			Assert.That(BotStartingCash.CashFor(0, 5000, isBot: true, nonCombatant: false), Is.EqualTo(5000));
			Assert.That(BotStartingCash.CashFor(-1, 5000, isBot: true, nonCombatant: false), Is.EqualTo(5000));
		}
	}
}
