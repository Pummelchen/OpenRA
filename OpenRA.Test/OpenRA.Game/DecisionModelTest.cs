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
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

namespace OpenRA.Test
{
	/// <summary>
	/// The mathematical decision model (handbook §15): influence maps for where, Lanchester for
	/// whether, UCB1 for what has worked, and queueing for whether ore is keeping up.
	/// </summary>
	[TestFixture]
	sealed class DecisionModelTest
	{
		// --- §15.1 influence maps ------------------------------------------------------------
		[TestCase(TestName = "Influence falls off with distance and reaches zero at the source's reach.")]
		public void InfluenceDecays()
		{
			var map = new InfluenceMap(64, 64);
			map.Add(new InfluenceSource(32, 32, Strength: 100f, ReachCells: 16, IsOwn: true));

			var centre = map.Own(32 / InfluenceMap.TileSize, 32 / InfluenceMap.TileSize);
			var near = map.Own(32 / InfluenceMap.TileSize + 1, 32 / InfluenceMap.TileSize);
			var far = map.Own(0, 0);

			Assert.That(centre, Is.GreaterThan(near));
			Assert.That(near, Is.GreaterThan(0f));
			Assert.That(far, Is.Zero, "Influence must not leak across the whole map.");
		}

		[TestCase(TestName = "Control is the difference, tension is the sum, and they answer different questions.")]
		public void ControlAndTensionAreDistinct()
		{
			var map = new InfluenceMap(64, 64);
			map.Add(new InfluenceSource(32, 32, 100f, 8, true));
			map.Add(new InfluenceSource(32, 32, 100f, 8, false));

			const int Tx = 32 / InfluenceMap.TileSize;
			const int Ty = 32 / InfluenceMap.TileSize;

			Assert.That(map.Influence(Tx, Ty), Is.EqualTo(0f).Within(0.01f),
				"Evenly matched ground is controlled by nobody...");
			Assert.That(map.Tension(Tx, Ty), Is.GreaterThan(0f),
				"...which is not the same as nobody being there.");
			Assert.That(map.Vulnerability(Tx, Ty), Is.GreaterThan(0f),
				"Contested ground neither side dominates is exactly what vulnerability measures.");
		}

		[TestCase(TestName = "The front line is where influence changes sign, not the base perimeter.")]
		public void FrontLineIsTheSignChange()
		{
			var map = new InfluenceMap(80, 16);
			map.Add(new InfluenceSource(8, 8, 100f, 20, true));
			map.Add(new InfluenceSource(72, 8, 100f, 20, false));

			var front = map.FrontLine().ToArray();
			Assert.That(front, Is.Not.Empty);

			// The front sits between the two forces, not on top of either.
			var meanX = front.Average(t => t.X);
			Assert.That(meanX, Is.GreaterThan(8 / InfluenceMap.TileSize));
			Assert.That(meanX, Is.LessThan(72 / InfluenceMap.TileSize));
		}

		[TestCase(TestName = "The assault objective trades enemy value against how weakly it is held.")]
		public void AssaultTargetsValuableAndWeak()
		{
			var map = new InfluenceMap(64, 64);

			// A heavily defended cluster, and a lightly held one of similar value.
			map.Add(new InfluenceSource(16, 16, 200f, 8, false));
			map.Add(new InfluenceSource(48, 48, 20f, 8, false));
			map.Add(new InfluenceSource(48, 48, 15f, 8, true));

			var strong = (16 / InfluenceMap.TileSize, 16 / InfluenceMap.TileSize);
			var weak = (48 / InfluenceMap.TileSize, 48 / InfluenceMap.TileSize);

			var target = map.BestAssaultTile((x, y) =>
				(x, y) == strong || (x, y) == weak ? 100f : 0f);

			Assert.That(target, Is.EqualTo(weak),
				"Equal value, so the objective is the one that can actually be taken.");
		}

		[TestCase(TestName = "The feint objective maximises enemy investment against our own exposure.")]
		public void FeintTargetsWhereTheEnemyMustAnswer()
		{
			var map = new InfluenceMap(64, 64);
			map.Add(new InfluenceSource(16, 16, 100f, 8, false));  // enemy cares, we are absent
			map.Add(new InfluenceSource(48, 48, 100f, 8, false));  // enemy cares, but so do we
			map.Add(new InfluenceSource(48, 48, 200f, 8, true));

			var feint = map.BestFeintTile();
			Assert.That(feint, Is.EqualTo((16 / InfluenceMap.TileSize, 16 / InfluenceMap.TileSize)),
				"A feint into ground we already hold draws nothing.");
		}

		// --- §15.2 Lanchester ----------------------------------------------------------------
		[TestCase(TestName = "Two waves of ten lose to one wave of twenty, which a ratio cannot express.")]
		public void ConcentrationBeatsIncrements()
		{
			// Sequential halves against a defender that a concentrated force beats decisively.
			var concentrated = LanchesterModel.Predict(20f, 15f);
			var firstHalf = LanchesterModel.Predict(10f, 15f);

			Assert.That(concentrated.AttackerWins, Is.True);
			Assert.That(firstHalf.AttackerWins, Is.False,
				"Half the force loses outright, so the second half arrives against a winner.");
			Assert.That(LanchesterModel.ConcentrationAdvantage(20f), Is.EqualTo(2f).Within(0.001f),
				"Concentration is worth exactly double under the square law.");
		}

		[TestCase(TestName = "Survivors, not just the winner, decide whether an attack achieved anything.")]
		public void SurvivorsDecideValue()
		{
			var pyrrhic = LanchesterModel.Predict(22f, 20f);
			var decisive = LanchesterModel.Predict(40f, 20f);

			Assert.That(pyrrhic.AttackerWins, Is.True);
			Assert.That(pyrrhic.IsDecisive, Is.False,
				"Winning with nothing left takes no ground - there is nothing to hold it with.");
			Assert.That(decisive.IsDecisive, Is.True);
			Assert.That(decisive.SurvivingStrength, Is.GreaterThan(pyrrhic.SurvivingStrength));
		}

		[TestCase(TestName = "Required strength scales with the defender, not with a fixed unit count.")]
		public void RequiredStrengthScales()
		{
			var vsSmall = LanchesterModel.RequiredStrength(10f);
			var vsLarge = LanchesterModel.RequiredStrength(40f);

			Assert.That(vsLarge, Is.GreaterThan(vsSmall));
			Assert.That(LanchesterModel.Predict(vsLarge, 40f).AttackerWins, Is.True,
				"The computed requirement must actually win the fight it was computed for.");
			Assert.That(LanchesterModel.RequiredStrength(0f), Is.Zero);
		}

		[TestCase(TestName = "Splitting is refused unless both halves win their own battle decisively.")]
		public void SplittingIsUsuallyWrong()
		{
			Assert.That(LanchesterModel.ShouldSplit(30f, 20f, 20f), Is.False,
				"Two halves against two real defenders is how a strong army loses twice.");
			Assert.That(LanchesterModel.ShouldSplit(60f, 5f, 5f), Is.True,
				"Against token defence both fragments still win decisively.");
		}

		[TestCase(TestName = "An empty attacker loses and an undefended objective is taken intact.")]
		public void DegenerateEngagements()
		{
			Assert.That(LanchesterModel.Predict(0f, 10f).AttackerWins, Is.False);
			var uncontested = LanchesterModel.Predict(10f, 0f);
			Assert.That(uncontested.AttackerWins, Is.True);
			Assert.That(uncontested.LossFraction, Is.Zero);
		}

		// --- §15.3 UCB1 portfolio ------------------------------------------------------------
		[TestCase(TestName = "Every strategy is tried once before any is dismissed.")]
		public void UntriedArmsAreExploredFirst()
		{
			var portfolio = new StrategyPortfolio();
			portfolio.Record(StrategyArm.Assault, 0.9f);

			var chosen = portfolio.Select([StrategyArm.Assault, StrategyArm.Harass]);
			Assert.That(chosen, Is.EqualTo(StrategyArm.Harass),
				"A commander that never tries harassment cannot discover it works here.");
		}

		[TestCase(TestName = "With evidence, the portfolio commits to what is working.")]
		public void EvidenceShiftsSelection()
		{
			var portfolio = new StrategyPortfolio();
			for (var i = 0; i < 30; i++)
			{
				portfolio.Record(StrategyArm.Assault, 0.9f);
				portfolio.Record(StrategyArm.Harass, 0.1f);
			}

			Assert.That(portfolio.Select([StrategyArm.Assault, StrategyArm.Harass]),
				Is.EqualTo(StrategyArm.Assault));
			Assert.That(portfolio.BestKnown(), Is.EqualTo(StrategyArm.Assault));
		}

		[TestCase(TestName = "A well-tried arm carries less exploration bonus than a rarely-tried one.")]
		public void ExplorationFavoursTheLessTried()
		{
			// The UCB1 property that matters is relative: at the same point in time, the arm with
			// more evidence behind it is trusted more and padded less. That is what makes the
			// commander keep testing alternatives early and commit once the evidence is in.
			var portfolio = new StrategyPortfolio();
			for (var i = 0; i < 40; i++)
				portfolio.Record(StrategyArm.Assault, 0.5f);
			for (var i = 0; i < 3; i++)
				portfolio.Record(StrategyArm.Harass, 0.5f);

			var wellTried = portfolio.Score(StrategyArm.Assault) - portfolio.MeanReward(StrategyArm.Assault);
			var rarelyTried = portfolio.Score(StrategyArm.Harass) - portfolio.MeanReward(StrategyArm.Harass);

			Assert.That(wellTried, Is.LessThan(rarelyTried),
				"Equal observed reward, so the less-tried arm must still be worth exploring.");
			Assert.That(portfolio.Select([StrategyArm.Assault, StrategyArm.Harass]),
				Is.EqualTo(StrategyArm.Harass));
		}

		[TestCase(TestName = "Only feasible strategies are offered, and an empty set falls back safely.")]
		public void FeasibilityIsTheCallersBusiness()
		{
			var portfolio = new StrategyPortfolio();
			Assert.That(portfolio.Select([StrategyArm.Siege]), Is.EqualTo(StrategyArm.Siege));
			Assert.That(portfolio.Select([]), Is.EqualTo(StrategyArm.Consolidate));
			Assert.That(portfolio.Select(null), Is.EqualTo(StrategyArm.Consolidate));
		}

		// --- §15.4 harvester economics -------------------------------------------------------
		[TestCase(TestName = "Income is capped by refinery throughput, not by harvester count.")]
		public void RefineriesCapIncome()
		{
			// Ten harvesters against one refinery: the queue, not the fleet, sets the rate.
			var many = HarvesterEconomics.IncomePerSecond(10, 700f, 20f, refineries: 1, unloadSeconds: 5f);
			var few = HarvesterEconomics.IncomePerSecond(4, 700f, 20f, refineries: 1, unloadSeconds: 5f);

			Assert.That(many, Is.EqualTo(few).Within(0.01f),
				"Past saturation the marginal harvester is 1100 credits of nothing.");
			Assert.That(HarvesterEconomics.MarginalHarvesterValue(10, 700f, 20f, 1, 5f), Is.Zero);
		}

		[TestCase(TestName = "When travel dominates, a refinery buys more income than a harvester.")]
		public void RefineryBeatsHarvesterWhenSaturated()
		{
			// Saturated: another harvester adds nothing, another refinery lifts the cap.
			Assert.That(HarvesterEconomics.RefineryBeatsHarvester(
				harvesters: 10, loadValue: 700f, roundTripSeconds: 20f,
				refineries: 1, unloadSeconds: 5f, harvesterCost: 1100, refineryCost: 1400), Is.True);

			// Unsaturated: the fleet is the constraint, so buy a harvester.
			Assert.That(HarvesterEconomics.RefineryBeatsHarvester(
				harvesters: 1, loadValue: 700f, roundTripSeconds: 60f,
				refineries: 3, unloadSeconds: 5f, harvesterCost: 1100, refineryCost: 1400), Is.False);
		}

		[TestCase(TestName = "A distant expansion is discounted twice: by travel and by risk.")]
		public void ExpansionValueDiscountsDistanceAndRisk()
		{
			var near = HarvesterEconomics.ExpansionValue(oreVolume: 5000f, roundTripSeconds: 20f, risk: 0.1f);
			var far = HarvesterEconomics.ExpansionValue(oreVolume: 5000f, roundTripSeconds: 60f, risk: 0.5f);

			Assert.That(near, Is.GreaterThan(far * 3f),
				"A rich patch across the map is usually worth less than a modest one nearby.");
			Assert.That(HarvesterEconomics.ExpansionValue(0f, 20f, 0f), Is.Zero);
		}

		[TestCase(TestName = "A collapse in income is flagged as an emergency, not absorbed quietly.")]
		public void IncomeCollapseIsAnEmergency()
		{
			Assert.That(HarvesterEconomics.IsEconomicEmergency(currentIncome: 20f, peakIncome: 100f), Is.True);
			Assert.That(HarvesterEconomics.IsEconomicEmergency(currentIncome: 90f, peakIncome: 100f), Is.False);
			Assert.That(HarvesterEconomics.IsEconomicEmergency(0f, 0f), Is.False);
		}

		[TestCase(TestName = "Payback time is infinite when an investment earns nothing.")]
		public void PaybackIsInfiniteWithoutIncome()
		{
			Assert.That(HarvesterEconomics.PaybackSeconds(1400, 0f), Is.EqualTo(float.PositiveInfinity));
			Assert.That(HarvesterEconomics.PaybackSeconds(1400, 14f), Is.EqualTo(100f).Within(0.01f));
		}
	}
}
