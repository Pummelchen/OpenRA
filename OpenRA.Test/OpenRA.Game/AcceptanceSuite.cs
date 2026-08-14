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
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

namespace OpenRA.Test
{
	/// <summary>
	/// Final acceptance suite: the program-wide contract invariants that hold across subsystems.
	/// Each subsystem has its own focused suite (threat model, routes, targets, missions, deception,
	/// production, tools); this suite guards the cross-cutting contracts they all share.
	/// </summary>
	[TestFixture]
	sealed class AcceptanceSuite
	{
		[TestCase(TestName = "The threat-field wire keys map 1:1 onto the capability enum.")]
		public void ThreatFieldContract()
		{
			var capabilities = Enum.GetValues<CoalitionCapability>();
			Assert.That(CommandToolApi.CapabilityKeys.Length, Is.EqualTo(capabilities.Length),
				"Every capability must have exactly one wire key.");

			// The keys must be distinct and snake_case (threat_field.v1 contract).
			Assert.That(CommandToolApi.CapabilityKeys.Distinct().Count(), Is.EqualTo(capabilities.Length));
			Assert.That(CommandToolApi.CapabilityKeys.All(k => k.All(c => char.IsLower(c) || c == '_')), Is.True,
				"Wire keys are snake_case per the threat_field.v1 contract.");
		}

		[TestCase(TestName = "The aggregated threat profile always covers the full capability set.")]
		public void ThreatProfileShape()
		{
			var regions = new[]
			{
				new CoalitionRegion(0, new OpenRA.Primitives.Rectangle(0, 0, 5, 5)),
				new CoalitionRegion(1, new OpenRA.Primitives.Rectangle(5, 0, 5, 5))
			};
			regions[1].Threats[(int)CoalitionCapability.AntiAir] = 0.7f;

			var profile = ProductionContract.Aggregate(regions);

			Assert.That(profile.Length, Is.EqualTo(Enum.GetValues<CoalitionCapability>().Length));
			Assert.That(profile[(int)CoalitionCapability.AntiAir], Is.EqualTo(0.7f).Within(0.001f));
			Assert.That(profile, Is.All.InRange(0f, 1f), "Threat values are normalized to 0..1.");
		}

		[TestCase(TestName = "The Lanchester estimate is self-consistent at the extremes and symmetric.")]
		public void CombatEstimateInvariants()
		{
			// No enemy: certain win with no friendly losses.
			var (win, loss) = CombatEstimator.Estimate(100f, 0f);
			Assert.That(win, Is.EqualTo(1f));
			Assert.That(loss, Is.EqualTo(0f));

			// No friendly force: certain loss.
			(win, loss) = CombatEstimator.Estimate(0f, 100f);
			Assert.That(win, Is.EqualTo(0f));
			Assert.That(loss, Is.EqualTo(1f));

			// 2:1 advantage: win ratio 2, friendly loss fraction 0.25 (1 - 50/100 halved).
			(win, loss) = CombatEstimator.Estimate(100f, 50f);
			Assert.That(win, Is.EqualTo(2f).Within(0.001f));
			Assert.That(loss, Is.EqualTo(0.25f).Within(0.001f));

			// The side that is outmatched absorbs the full shortfall.
			(win, loss) = CombatEstimator.Estimate(50f, 100f);
			Assert.That(win, Is.EqualTo(0.5f).Within(0.001f));
			Assert.That(loss, Is.EqualTo(0.5f).Within(0.001f));
		}

		[TestCase(TestName = "Every documented tool is served by the engine (never UNKNOWN_TOOL).")]
		public void ToolSurfaceComplete()
		{
			var context = new ToolContext();
			var tools = new[]
			{
				"get_global_summary", "inspect_region", "inspect_force", "inspect_enemy_intelligence",
				"get_recent_events", "get_opponent_model", "get_uncertainties", "estimate_engagement",
				"score_targets", "plan_routes", "get_economy_state", "get_production_state",
				"compare_force_packages", "estimate_enemy_response", "find_attack_windows",
				"find_special_ops_routes", "get_mission_status", "get_force_readiness",
				"get_transport_status", "get_route_status"
			};

			foreach (var tool in tools)
			{
				var response = CommandToolApi.Execute(context, $"{{\"tool\":\"{tool}\",\"arguments\":{{}}}}");
				using var doc = System.Text.Json.JsonDocument.Parse(response);
				var error = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
				Assert.That(error, Is.Not.EqualTo("UNKNOWN_TOOL"), $"{tool} must be a served tool.");
			}
		}

		[TestCase(TestName = "Mission phases are a forward-only lifecycle.")]
		public void MissionPhaseForwardOnly()
		{
			var phases = Enum.GetValues<MissionPhase>();
			for (var i = 1; i < phases.Length; i++)
				Assert.That(phases[i], Is.GreaterThan(phases[i - 1]),
					$"Phase {phases[i - 1]} must precede {phases[i]}; the lifecycle never goes backwards.");
		}

		[TestCase(TestName = "Every mission status is terminal or active, with no gaps.")]
		public void MissionStatusComplete()
		{
			Assert.That(Enum.GetValues<MissionStatus>(), Is.EqualTo(new[]
			{
				MissionStatus.Ready, MissionStatus.Executing,
				MissionStatus.Succeeded, MissionStatus.Aborted, MissionStatus.Failed
			}));
		}
	}
}
