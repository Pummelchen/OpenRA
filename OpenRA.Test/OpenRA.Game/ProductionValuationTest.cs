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

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Commander.Model;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

namespace OpenRA.Test
{
	/// <summary>
	/// Deciding what to build by computing it rather than by consulting a hand-ordered list. The
	/// lists this replaces were opinions fixed at the moment they were written; these numbers come
	/// from the mod's own damage, cost and health tables.
	/// </summary>
	[TestFixture]
	sealed class ProductionValuationTest
	{
		static UnitCombatProfile Unit(string name, int cost, int hp, string armour,
			(string Armour, float Dps)[] damage)
		{
			var versus = damage.ToDictionary(d => d.Armour, d => d.Dps);
			return new UnitCombatProfile(name, cost, hp, armour, versus, rangeCells: 4,
				canTargetAir: false, canTargetGround: true);
		}

		[TestCase(TestName = "It picks the unit that beats what the enemy actually has.")]
		public void CountersTheActualComposition()
		{
			// An anti-armour specialist and an anti-infantry one, priced the same. Which is correct
			// depends entirely on what is across the map - which is exactly what a fixed list
			// cannot express.
			var candidates = new[]
			{
				Unit("tankbuster", 1000, 400, "Heavy", [("Heavy", 100f), ("None", 5f)]),
				Unit("flamer", 1000, 400, "Heavy", [("Heavy", 5f), ("None", 100f)]),
			};

			var vsArmour = ProductionValuation.Rank(candidates,
				new Dictionary<string, float> { ["Heavy"] = 10000f });
			Assert.That(vsArmour[0].Unit, Is.EqualTo("tankbuster"));

			var vsInfantry = ProductionValuation.Rank(candidates,
				new Dictionary<string, float> { ["None"] = 10000f });
			Assert.That(vsInfantry[0].Unit, Is.EqualTo("flamer"));
		}

		[TestCase(TestName = "A mixed enemy is weighted, not guessed at.")]
		public void MixedCompositionIsWeighted()
		{
			var candidates = new[]
			{
				Unit("tankbuster", 1000, 400, "Heavy", [("Heavy", 100f), ("None", 5f)]),
				Unit("flamer", 1000, 400, "Heavy", [("Heavy", 5f), ("None", 100f)]),
			};

			// Three quarters armour: the specialist for it wins, but the ranking is a continuum
			// rather than a switch - which is the degree a hand-written list cannot carry.
			var ranked = ProductionValuation.Rank(candidates,
				new Dictionary<string, float> { ["Heavy"] = 7500f, ["None"] = 2500f });

			Assert.That(ranked[0].Unit, Is.EqualTo("tankbuster"));
			Assert.That(ranked[0].Score, Is.GreaterThan(ranked[1].Score));
		}

		[TestCase(TestName = "Cost efficiency, not raw damage, decides it.")]
		public void EfficiencyBeatsRawPower()
		{
			// The heavier unit does more damage and costs four times as much. Per credit the cheap
			// one wins, and per credit is what matters when the constraint is money.
			var candidates = new[]
			{
				Unit("cheap", 500, 300, "Light", [("Heavy", 40f)]),
				Unit("heavy", 2000, 600, "Heavy", [("Heavy", 80f)]),
			};

			var ranked = ProductionValuation.Rank(candidates,
				new Dictionary<string, float> { ["Heavy"] = 10000f });

			Assert.That(ranked[0].Unit, Is.EqualTo("cheap"));
		}

		[TestCase(TestName = "Urgency prices in how long a thing takes to arrive.")]
		public void UrgencyPenalisesExpensiveUnits()
		{
			// This commander died at 17,000 ticks holding 137,760 credits because the first entry on
			// its list cost 2,000 and took most of a minute while the enemy was already at the door.
			// A unit that arrives after the base has fallen is worth nothing.
			var candidates = new[]
			{
				Unit("cheap", 400, 200, "None", [("Heavy", 20f)]),
				Unit("heavy", 2000, 900, "Heavy", [("Heavy", 110f)]),
			};

			var enemy = new Dictionary<string, float> { ["Heavy"] = 10000f };

			var relaxed = ProductionValuation.Rank(candidates, enemy, urgency: 0f);
			var pressed = ProductionValuation.Rank(candidates, enemy, urgency: 1f);

			// Whatever wins when there is time, the cheap unit must gain ground when there is not.
			var relaxedGap = relaxed.First(v => v.Unit == "heavy").Score
				- relaxed.First(v => v.Unit == "cheap").Score;
			var pressedGap = pressed.First(v => v.Unit == "heavy").Score
				- pressed.First(v => v.Unit == "cheap").Score;

			Assert.That(pressedGap, Is.LessThan(relaxedGap),
				"urgency did not shift the balance toward what can arrive in time");
		}

		[TestCase(TestName = "With nothing seen it judges on durability, not on a guess.")]
		public void UnknownEnemyFallsBackHonestly()
		{
			// Being confidently wrong about an unseen enemy is how a commander builds the wrong
			// counter and finds out too late.
			var candidates = new[]
			{
				Unit("tough", 1000, 900, "Heavy", [("Heavy", 10f)]),
				Unit("fragile", 1000, 100, "None", [("Heavy", 200f)]),
			};

			var ranked = ProductionValuation.Rank(candidates, new Dictionary<string, float>());

			Assert.That(ranked[0].Unit, Is.EqualTo("tough"));
			Assert.That(ranked[0].Rationale, Does.Contain("no enemy seen"));
		}

		[TestCase(TestName = "A weapon that cannot hit the target scores nothing for it.")]
		public void InvalidTargetsScoreZero()
		{
			// InvalidTargets is not decoration: a heavy tank's shell declares it cannot be fired at
			// infantry, and counting its damage anyway would have the commander believe tanks answer
			// massed infantry.
			var candidates = new[] { Unit("cannon", 1000, 400, "Heavy", [("Heavy", 100f)]) };

			var ranked = ProductionValuation.Rank(candidates,
				new Dictionary<string, float> { ["None"] = 10000f });

			Assert.That(ranked[0].Score, Is.EqualTo(0f));
		}

		[TestCase(TestName = "Ranking is reproducible.")]
		public void RankingIsDeterministic()
		{
			var candidates = new[]
			{
				Unit("a", 800, 400, "Heavy", [("Heavy", 50f)]),
				Unit("b", 800, 400, "Heavy", [("Heavy", 50f)]),
				Unit("c", 900, 450, "Heavy", [("Heavy", 56f)]),
			};

			var enemy = new Dictionary<string, float> { ["Heavy"] = 5000f };
			var first = ProductionValuation.Rank(candidates, enemy).Select(v => v.Unit).ToArray();

			for (var i = 0; i < 5; i++)
				Assert.That(ProductionValuation.Rank(candidates, enemy).Select(v => v.Unit), Is.EqualTo(first));
		}

		[TestCase(TestName = "Composition is measured in credits, not headcount.")]
		public void CompositionIsValueWeighted()
		{
			// One mammoth is a bigger problem than one rifleman, and counting bodies would say
			// otherwise.
			var composition = ProductionValuation.CompositionOf(
			[
				("4tnk", 2000, "Heavy"),
				("e1", 100, "None"),
				("e1", 100, "None"),
			]);

			Assert.That(composition["Heavy"], Is.EqualTo(2000f));
			Assert.That(composition["None"], Is.EqualTo(200f));
		}
	}
}
