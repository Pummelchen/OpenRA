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

using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

namespace OpenRA.Test
{
	/// <summary>
	/// <para>
	/// The opt-in cheat module. Two things are worth asserting here, and neither is that the
	/// configuration flags hold the values they were assigned.
	/// </para>
	/// <para>
	/// The first is the fog boundary. The coalition is permitted to build dishonestly and is
	/// specifically not permitted to see dishonestly, because every intelligence result the
	/// commander has been measured on - scouting budgets, the honesty ladder, last-known decay -
	/// becomes meaningless the moment the shroud is lifted for free. That boundary lives in one
	/// method and is asserted rather than documented.
	/// </para>
	/// <para>
	/// The second is the cash schedule, which is not decoration. FastBuild collapses build time to
	/// a single tick and cost is drawn down over build time, so instant build makes the full price
	/// fall due immediately. Measured on shattered-mountain/805 against Rush, instant build with no
	/// income turns a draw into an 8550/31400 defeat - the cheat is a handicap without it.
	/// </para>
	/// </summary>
	[TestFixture]
	sealed class CoalitionCheatsTest
	{
		[TestCase(TestName = "No granted advantage lifts the shroud.")]
		public void CheatsNeverGrantVisibility()
		{
			var info = new CoalitionCheatsBotModuleInfo();
			var granted = CoalitionCheatsBotModule.GrantedOrders(info).ToArray();

			Assert.That(granted, Is.Not.Empty, "The module is meant to grant something.");

			foreach (var forbidden in new[]
			{
				DeveloperMode.Orders.Visibility,
				DeveloperMode.Orders.GiveExploration,
				DeveloperMode.Orders.ResetExploration,
				DeveloperMode.Orders.All,
			})
				Assert.That(granted, Has.No.Member(forbidden),
					$"{forbidden} would hand the commander free intelligence and invalidate every " +
					"reconnaissance measurement in the suite.");
		}

		[TestCase(TestName = "Each advantage is granted only when its flag is set.")]
		public void FlagsSelectTheirOwnOrder()
		{
			var all = CoalitionCheatsBotModule.GrantedOrders(new CoalitionCheatsBotModuleInfo()).ToArray();
			Assert.That(all, Is.EquivalentTo(new[]
			{
				DeveloperMode.Orders.FastBuild,
				DeveloperMode.Orders.BuildAnywhere,
				DeveloperMode.Orders.UnlimitedPower,
				DeveloperMode.Orders.EnableTech,
			}));

			var noTech = new CoalitionCheatsBotModuleInfo();
			FieldLoader.Load(noTech, new MiniYaml("", new[]
			{
				new MiniYamlNode("AllTech", "false"),
				new MiniYamlNode("InstantBuild", "false"),
			}.ToList()));

			var reduced = CoalitionCheatsBotModule.GrantedOrders(noTech).ToArray();
			Assert.That(reduced, Has.No.Member(DeveloperMode.Orders.EnableTech));
			Assert.That(reduced, Has.No.Member(DeveloperMode.Orders.FastBuild));
			Assert.That(reduced, Has.Member(DeveloperMode.Orders.BuildAnywhere));
		}

		[TestCase(TestName = "Instant build ships with the income that makes it an advantage.")]
		public void InstantBuildIsPairedWithIncome()
		{
			var info = new CoalitionCheatsBotModuleInfo();

			// Not a taste question. With FastBuild on and no income the queues stall on cash they
			// never accumulate, and the bot builds less than it would have built honestly.
			Assert.That(info.InstantBuild, Is.True);
			Assert.That(info.CashPerInterval, Is.GreaterThan(0),
				"Instant build without matching income is a measured handicap, not a cheat.");
		}

		[TestCase(TestName = "Cash is granted on the interval, and never divides by zero.")]
		public void CashSchedule()
		{
			Assert.That(CoalitionCheatsBotModule.ShouldGrantCash(0, 2000, 250), Is.True);
			Assert.That(CoalitionCheatsBotModule.ShouldGrantCash(250, 2000, 250), Is.True);
			Assert.That(CoalitionCheatsBotModule.ShouldGrantCash(249, 2000, 250), Is.False);

			Assert.That(CoalitionCheatsBotModule.ShouldGrantCash(1234, 0, 250), Is.False,
				"Zero cash per interval is the switch that disables the income advantage.");

			// A misconfigured interval must degrade to every tick rather than throwing mid-match.
			Assert.That(CoalitionCheatsBotModule.ShouldGrantCash(7, 2000, 0), Is.True);
			Assert.That(CoalitionCheatsBotModule.ShouldGrantCash(7, 2000, -5), Is.True);
		}
	}
}
