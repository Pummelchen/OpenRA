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
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

namespace OpenRA.Test
{
	/// <summary>
	/// Expansion and economic defence (reqs 407-412). Expansion is the decision most often got wrong
	/// in both directions: expanding under pressure loses the harvesters and their escort, while
	/// never expanding loses the long game to an opponent who did.
	/// </summary>
	[TestFixture]
	sealed class ExpansionPolicyTest
	{
		[TestCase(TestName = "Expansion risk rises with local threat and with distance from relief (req 407).")]
		public void RiskCombinesThreatAndReach()
		{
			var nearQuiet = ExpansionPolicy.ExpansionRisk(siteThreat: 0.1f, distanceFromBase: 10, mapSpan: 128);
			var nearHot = ExpansionPolicy.ExpansionRisk(siteThreat: 0.9f, distanceFromBase: 10, mapSpan: 128);
			var farQuiet = ExpansionPolicy.ExpansionRisk(siteThreat: 0.1f, distanceFromBase: 120, mapSpan: 128);

			Assert.That(nearHot, Is.GreaterThan(nearQuiet));
			Assert.That(farQuiet, Is.GreaterThan(nearQuiet),
				"An expansion nobody can reach in time is undefended however quiet it looks.");
			Assert.That(nearQuiet, Is.InRange(0f, 1f));
		}

		[TestCase(TestName = "Posture decides whether expansion is an investment or a liability (req 408).")]
		public void PostureGatesExpansion()
		{
			Assert.That(ExpansionPolicy.PostureAllowsExpansion(StrategicPosture.Expansion), Is.True);
			Assert.That(ExpansionPolicy.PostureAllowsExpansion(StrategicPosture.Opening), Is.True);
			Assert.That(ExpansionPolicy.PostureAllowsExpansion(StrategicPosture.Containment), Is.True);

			// Expansion pays off only if the coalition survives to collect.
			Assert.That(ExpansionPolicy.PostureAllowsExpansion(StrategicPosture.Desperation), Is.False);
			Assert.That(ExpansionPolicy.PostureAllowsExpansion(StrategicPosture.Breakthrough), Is.False);
			Assert.That(ExpansionPolicy.PostureAllowsExpansion(StrategicPosture.Defensive), Is.False);
		}

		[TestCase(TestName = "A valuable site is taken only when the risk is acceptable.")]
		public void ExpansionWeighsValueAgainstRisk()
		{
			Assert.That(ExpansionPolicy.ShouldExpand(StrategicPosture.Expansion, siteValue: 1f, risk: 0.2f), Is.True);
			Assert.That(ExpansionPolicy.ShouldExpand(StrategicPosture.Expansion, siteValue: 1f, risk: 0.9f), Is.False);
			Assert.That(ExpansionPolicy.ShouldExpand(StrategicPosture.Desperation, siteValue: 1f, risk: 0.1f), Is.False,
				"Even a safe site is the wrong investment while losing.");
			Assert.That(ExpansionPolicy.ShouldExpand(StrategicPosture.Expansion, siteValue: 0f, risk: 0.1f), Is.False);
		}

		[TestCase(TestName = "A riskier expansion is garrisoned more heavily from the start (req 410).")]
		public void GarrisonScalesWithRisk()
		{
			var safe = ExpansionPolicy.DefensiveGarrison(risk: 0.1f, baseGarrison: 4);
			var exposed = ExpansionPolicy.DefensiveGarrison(risk: 0.9f, baseGarrison: 4);

			Assert.That(exposed, Is.GreaterThan(safe),
				"A site is escorted from the moment it is planned, not after it is first raided.");
			Assert.That(ExpansionPolicy.DefensiveGarrison(risk: 0f, baseGarrison: 0), Is.EqualTo(1),
				"Every expansion gets at least one defender.");
		}

		[TestCase(TestName = "A weaker economy commands a larger share of defence (req 411).")]
		public void EconomicWeaknessRaisesDefensiveShare()
		{
			var strong = ExpansionPolicy.EconomicDefenseShare(ownEconomicStrength: 100f, enemyEconomicStrength: 50f);
			var weak = ExpansionPolicy.EconomicDefenseShare(ownEconomicStrength: 50f, enemyEconomicStrength: 150f);

			Assert.That(weak, Is.GreaterThan(strong),
				"An economy already behind cannot afford to lose any more of it.");
			Assert.That(ExpansionPolicy.EconomicDefenseShare(0f, 100f), Is.EqualTo(0.5f),
				"With no economy left, defending what remains is the priority.");
			Assert.That(strong, Is.InRange(0.15f, 0.5f));
		}

		[TestCase(TestName = "A thin enemy economy is raided; a strong one is not (req 412).")]
		public void RaidingFollowsEconomicWeakness()
		{
			Assert.That(ExpansionPolicy.ShouldRaidEconomy(enemyEconomicStrength: 40f, ownEconomicStrength: 100f,
				availableRaiders: 6, minimumRaidForce: 4), Is.True);

			Assert.That(ExpansionPolicy.ShouldRaidEconomy(enemyEconomicStrength: 200f, ownEconomicStrength: 100f,
				availableRaiders: 6, minimumRaidForce: 4), Is.False,
				"Raiding a strong economy just loses the raiders.");

			Assert.That(ExpansionPolicy.ShouldRaidEconomy(enemyEconomicStrength: 40f, ownEconomicStrength: 100f,
				availableRaiders: 1, minimumRaidForce: 4), Is.False,
				"A lone unit sent to die is a gesture, not a raid.");
		}

		[TestCase(TestName = "Economic specialization needs enough allies that the fighting share survives (req 409).")]
		public void SpecializationNeedsEnoughAllies()
		{
			Assert.That(ExpansionPolicy.ShouldSpecializeEconomy(alliedPlayers: 4), Is.True);
			Assert.That(ExpansionPolicy.ShouldSpecializeEconomy(alliedPlayers: 2), Is.False,
				"Specializing in a two-player coalition halves the army.");
		}

		[TestCase(TestName = "Degenerate map and economy inputs do not divide by zero.")]
		public void DegenerateInputIsSafe()
		{
			Assert.That(ExpansionPolicy.ExpansionRisk(0.5f, 100, mapSpan: 0), Is.InRange(0f, 1f));
			Assert.That(ExpansionPolicy.EconomicDefenseShare(100f, 0f), Is.InRange(0.15f, 0.5f));
			Assert.That(ExpansionPolicy.ShouldRaidEconomy(0f, 0f, 10, 4), Is.False);
		}
	}
}
