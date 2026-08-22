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

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;
using OpenRA.Primitives;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class CommandToolApiTest
	{
		// A 2x3 region grid: 0-1-2 on top, 3-4-5 on bottom, connected by vertical columns.
		static CoalitionRegion[] GridRegions()
		{
			return
			[
				new CoalitionRegion(0, Rectangle.FromLTRB(0, 0, 5, 5)),
				new CoalitionRegion(1, Rectangle.FromLTRB(5, 0, 10, 5)),
				new CoalitionRegion(2, Rectangle.FromLTRB(10, 0, 15, 5)),
				new CoalitionRegion(3, Rectangle.FromLTRB(0, 5, 5, 10)),
				new CoalitionRegion(4, Rectangle.FromLTRB(5, 5, 10, 10)),
				new CoalitionRegion(5, Rectangle.FromLTRB(10, 5, 15, 10))
			];
		}

		static CoalitionMapAnalysis MapWith(List<int>[] adjacency)
		{
			var regions = GridRegions();
			var chokepoints = regions.Select(_ => System.Array.Empty<int>().ToFrozenSet()).ToArray();
			var (components, count) = CoalitionMapAnalysis.ConnectedComponents(adjacency);
			var allComponents = new[] { components, components, components, components };
			return new CoalitionMapAnalysis(regions, [adjacency, adjacency, adjacency, adjacency],
				[chokepoints, chokepoints, chokepoints, chokepoints],
				allComponents, [count, count, count, count], [], 15, 10,
				new int[regions.Length], new float[regions.Length], new float[regions.Length]);
		}

		static List<int>[] Grid()
		{
			var adjacency = Enumerable.Range(0, 6).Select(_ => new List<int>()).ToArray();
			void Link(int a, int b)
			{
				adjacency[a].Add(b);
				adjacency[b].Add(a);
			}

			Link(0, 1);
			Link(1, 2);
			Link(3, 4);
			Link(4, 5);
			Link(0, 3);
			Link(1, 4);
			Link(2, 5);
			return adjacency;
		}

		static float[][] Threats(CoalitionRegion[] regions)
		{
			// Populate the regions' own threat arrays (as ComputeThreats does in production); the
			// threat field for route planning is the same arrays.
			regions[5].Threats[(int)CoalitionCapability.StaticDefense] = 0.9f;
			regions[5].Threats[(int)CoalitionCapability.Reinforcement] = 0.6f;
			regions[2].Threats[(int)CoalitionCapability.AntiAir] = 0.8f;
			return regions.Select(r => r.Threats).ToArray();
		}

		static ToolContext Context()
		{
			var regions = GridRegions();
			var forces = new[]
			{
				new ForceGroup("Multi0")
				{
					TotalUnits = 12,
					Strength = 1f,
					Readiness = 1f,
					Center = new CPos(2, 2)
				},
				new ForceGroup("Multi1")
				{
					TotalUnits = 10,
					Strength = 0.8f,
					Readiness = 1f,
					Center = new CPos(7, 2)
				}
			};
			forces[0].Counts[(int)UnitClass.Armor] = 10;
			forces[0].Counts[(int)UnitClass.Infantry] = 2;
			forces[0].ActivityCounts["AttackMove"] = 8;
			forces[0].ActivityCounts["Idle"] = 4;
			forces[1].Counts[(int)UnitClass.Infantry] = 10;
			var facility = new ProductionFacility("Multi0", "Vehicle", "weap", new CPos(1, 1))
			{
				Buildable = ["3tnk", "jeep"]
			};

			var intel = new[]
			{
				new EnemyIntel("weap", UnitClass.Structure) { LastSeenCell = new CPos(12, 8), LastSeenTick = 900, Confidence = 0.9f },
				new EnemyIntel("powr", UnitClass.Structure) { LastSeenCell = new CPos(13, 8), LastSeenTick = 950, Confidence = 0.6f },
				new EnemyIntel("3tnk", UnitClass.Armor) { LastSeenCell = new CPos(11, 2), LastSeenTick = 500, Confidence = 0.2f }
			};

			return new ToolContext
			{
				Tick = 1000,
				Timestep = 40,
				Regions = regions,
				Forces = forces,
				Facilities = [facility],
				Missions =
				[
					new MissionState { Id = "OP-1", Type = "attack", Status = "executing", Phase = "execution", Target = new CPos(12, 8), Priority = 80 }
				],
				EnemyIntel = intel,
				Events =
				[
					new CoalitionEvent(500, "enemy_base_discovered", new CPos(12, 8), "weap"),
					new CoalitionEvent(900, "posture_change", null, "attack")
				],
				Opponent = new OpponentModel { ArmorBias = 0.7f, Playstyle = "rush", Confidence = 0.5f },
				CoalitionCash = 5000,
				MemberCash = new Dictionary<string, int> { ["Multi0"] = 3000, ["Multi1"] = 2000 },
				HomeRegion = 0,
				EnemyRegion = 5,
				CoalitionArmyStrength = 60f,
				EnemyArmyStrength = 20f,
				EnemyArmyCount = 3,
				DeceptionEffectiveness = 0.5f,
				DeceptionEnemiesDrawn = 6,
				MapAnalysis = MapWith(Grid()),
				ThreatField = Threats(regions)
			};
		}

		static string Call(ToolContext context, string tool, string argumentsJson = "{}")
		{
			return CommandToolApi.Execute(context, $"{{\"tool\":\"{tool}\",\"arguments\":{argumentsJson}}}");
		}

		static JsonElement Result(string response)
		{
			using var doc = JsonDocument.Parse(response);
			return doc.RootElement.Clone();
		}

		[TestCase(TestName = "Unknown tools are rejected, not fabricated.")]
		public void UnknownToolRejected()
		{
			var response = Result(Call(Context(), "teleport_army"));
			Assert.That(response.GetProperty("ok").GetBoolean(), Is.False);
			Assert.That(response.GetProperty("error").GetString(), Is.EqualTo("UNKNOWN_TOOL"));
		}

		[TestCase(TestName = "Malformed requests are rejected.")]
		public void MalformedRequestRejected()
		{
			var response = Result(CommandToolApi.Execute(Context(), "not json"));
			Assert.That(response.GetProperty("error").GetString(), Is.EqualTo("INVALID_REQUEST"));

			response = Result(CommandToolApi.Execute(Context(), "{\"arguments\":{}}"));
			Assert.That(response.GetProperty("error").GetString(), Is.EqualTo("INVALID_REQUEST"));
		}

		[TestCase(TestName = "Missing required arguments are rejected.")]
		public void MissingArgumentRejected()
		{
			var response = Result(Call(Context(), "inspect_region", "{}"));
			Assert.That(response.GetProperty("error").GetString(), Is.EqualTo("INVALID_ARGUMENTS"));
		}

		[TestCase(TestName = "Unknown regions and forces are rejected with engine codes.")]
		public void UnknownReferencesRejected()
		{
			var response = Result(Call(Context(), "inspect_region", "{\"region\":99}"));
			Assert.That(response.GetProperty("error").GetString(), Is.EqualTo("UNKNOWN_REFERENCE"));

			response = Result(Call(Context(), "inspect_force", "{\"force\":\"Multi9\"}"));
			Assert.That(response.GetProperty("error").GetString(), Is.EqualTo("UNKNOWN_REFERENCE"));
		}

		[TestCase(TestName = "Unknown enum values are rejected instead of silently defaulting.")]
		public void UnknownEnumsRejected()
		{
			var response = Result(Call(Context(), "plan_routes", "{\"from_region\":0,\"to_region\":5,\"movement\":\"airship\"}"));
			Assert.That(response.GetProperty("error").GetString(), Is.EqualTo("INVALID_ARGUMENTS"));

			response = Result(Call(Context(), "score_targets", "{\"posture\":\"siege\"}"));
			Assert.That(response.GetProperty("error").GetString(), Is.EqualTo("INVALID_ARGUMENTS"));

			response = Result(Call(Context(), "plan_routes", "{\"from_region\":0,\"to_region\":5,\"profile\":\"siege\"}"));
			Assert.That(response.GetProperty("error").GetString(), Is.EqualTo("INVALID_ARGUMENTS"));

			response = Result(Call(Context(), "plan_routes", "{\"from_region\":0,\"to_region\":5,\"weights\":{\"wormhole\":1}}"));
			Assert.That(response.GetProperty("error").GetString(), Is.EqualTo("UNKNOWN_REFERENCE"));
		}

		[TestCase(TestName = "Region ids accept indices and REGION_n labels.")]
		public void RegionLabelForms()
		{
			var byIndex = Result(Call(Context(), "inspect_region", "{\"region\":3}"));
			var byLabel = Result(Call(Context(), "inspect_region", "{\"region\":\"REGION_3\"}"));

			Assert.That(byIndex.GetProperty("result").GetProperty("region").GetInt32(), Is.EqualTo(3));
			Assert.That(byLabel.GetProperty("result").GetProperty("region").GetInt32(), Is.EqualTo(3));
		}

		[TestCase(TestName = "inspect_region reports the engine threat fields with contract keys.")]
		public void InspectRegionThreats()
		{
			var response = Result(Call(Context(), "inspect_region", "{\"region\":5}"));
			var result = response.GetProperty("result");
			var threats = result.GetProperty("threats");

			Assert.That(threats.GetProperty("static_defense").GetDouble(), Is.EqualTo(0.9).Within(0.001));
			Assert.That(threats.GetProperty("reinforcement").GetDouble(), Is.EqualTo(0.6).Within(0.001));
			Assert.That(threats.GetProperty("anti_air").GetDouble(), Is.EqualTo(0.0).Within(0.001));
		}

		[TestCase(TestName = "inspect_force reports engine-computed composition and strength.")]
		public void InspectForce()
		{
			var response = Result(Call(Context(), "inspect_force", "{\"force\":\"Multi0\"}"));
			var result = response.GetProperty("result");
			var composition = result.GetProperty("composition");

			Assert.That(composition.GetProperty("armor").GetInt32(), Is.EqualTo(10));
			Assert.That(result.GetProperty("total_units").GetInt32(), Is.EqualTo(12));
			Assert.That(result.GetProperty("strength").GetDouble(), Is.EqualTo(1.0).Within(0.001));
			Assert.That(result.GetProperty("activities").GetProperty("AttackMove").GetInt32(), Is.EqualTo(8));
		}

		[TestCase(TestName = "inspect_enemy_intelligence filters by region and reports confidence.")]
		public void InspectEnemyIntelligence()
		{
			var all = Result(Call(Context(), "inspect_enemy_intelligence"));
			Assert.That(all.GetProperty("result").GetArrayLength(), Is.EqualTo(3));

			var filtered = Result(Call(Context(), "inspect_enemy_intelligence", "{\"region\":5}"));
			var intel = filtered.GetProperty("result").EnumerateArray().ToArray();
			Assert.That(intel.Length, Is.EqualTo(2), "Both structures sit in region 5.");
			Assert.That(intel.All(e => e.GetProperty("type").GetString() is "weap" or "powr"), Is.True);
		}

		[TestCase(TestName = "get_recent_events filters the engine event log by tick.")]
		public void RecentEvents()
		{
			var all = Result(Call(Context(), "get_recent_events"));
			Assert.That(all.GetProperty("result").GetArrayLength(), Is.EqualTo(2));

			var recent = Result(Call(Context(), "get_recent_events", "{\"since_tick\":600}"));
			var events = recent.GetProperty("result").EnumerateArray().ToArray();
			Assert.That(events.Length, Is.EqualTo(1));
			Assert.That(events[0].GetProperty("type").GetString(), Is.EqualTo("posture_change"));
		}

		[TestCase(TestName = "get_opponent_model returns the engine's behavioral profile.")]
		public void OpponentModel()
		{
			var result = Result(Call(Context(), "get_opponent_model")).GetProperty("result");

			Assert.That(result.GetProperty("armor_bias").GetDouble(), Is.EqualTo(0.7).Within(0.001));
			Assert.That(result.GetProperty("playstyle").GetString(), Is.EqualTo("rush"));
			Assert.That(result.GetProperty("confidence").GetDouble(), Is.EqualTo(0.5).Within(0.001));
		}

		[TestCase(TestName = "get_uncertainties lists stale or low-confidence intelligence.")]
		public void Uncertainties()
		{
			var result = Result(Call(Context(), "get_uncertainties")).GetProperty("result");
			var questions = result.EnumerateArray().ToArray();

			// The 3tnk sighting (confidence 0.2) is the only uncertain entry; 500 ticks at 40 ms is 20 s.
			Assert.That(questions.Length, Is.EqualTo(1));
			Assert.That(questions[0].GetProperty("question").GetString(), Is.EqualTo("enemy_3tnk_position"));
		}

		[TestCase(TestName = "estimate_engagement computes the matchup-adjusted estimate from force groups.")]
		public void EstimateEngagement()
		{
			// Multi0: 10 armor (x3, +25% vs infantry) + 2 infantry (x1) = 37.5 + 2 = 39.5.
			// Multi1: 10 infantry (x1) at 0.8 health, matched against armor = 8.
			var result = Result(Call(Context(), "estimate_engagement", "{\"force_a\":\"Multi0\",\"force_b\":\"Multi1\"}")).GetProperty("result");

			Assert.That(result.GetProperty("force_a_power").GetDouble(), Is.EqualTo(39.5).Within(0.001));
			Assert.That(result.GetProperty("force_b_power").GetDouble(), Is.EqualTo(8.0).Within(0.001));
			Assert.That(result.GetProperty("win_ratio").GetDouble(), Is.EqualTo(39.5 / 8.0).Within(0.001));
			Assert.That(result.GetProperty("model_version").GetString(), Is.EqualTo("v2"));
		}

		[TestCase(TestName = "score_targets ranks enemy structures by the engine target model.")]
		public void ScoreTargets()
		{
			var result = Result(Call(Context(), "score_targets")).GetProperty("result");
			var targets = result.EnumerateArray().ToArray();

			Assert.That(targets.Length, Is.EqualTo(2), "Both scouted structures are scored.");
			Assert.That(targets.Select(t => t.GetProperty("type").GetString()).ToArray(),
				Is.EqualTo(new[] { "weap", "powr" }), "Production outweighs a plain power plant.");

			var scores = targets.Select(t => t.GetProperty("score").GetDouble()).ToArray();
			Assert.That(scores[0], Is.GreaterThan(scores[1]), "Targets are ranked by score descending.");
		}

		[TestCase(TestName = "plan_routes returns an engine-planned region path with cost.")]
		public void PlanRoutes()
		{
			var result = Result(Call(Context(), "plan_routes", "{\"from_region\":0,\"to_region\":5}")).GetProperty("result");

			Assert.That(result.GetProperty("found").GetBoolean(), Is.True);
			Assert.That(result.GetProperty("cost").GetDouble(), Is.GreaterThanOrEqualTo(0));

			var regions = result.GetProperty("regions").EnumerateArray().Select(r => r.GetInt32()).ToArray();
			Assert.That(regions[0], Is.EqualTo(0));
			Assert.That(regions[^1], Is.EqualTo(5));
		}

		[TestCase(TestName = "plan_routes validates unknown regions and rejected weight keys.")]
		public void PlanRoutesValidation()
		{
			var response = Result(Call(Context(), "plan_routes", "{\"from_region\":0,\"to_region\":99}"));
			Assert.That(response.GetProperty("error").GetString(), Is.EqualTo("UNKNOWN_REFERENCE"));
		}

		[TestCase(TestName = "get_global_summary derives the posture from the engine force ratio.")]
		public void GlobalSummary()
		{
			var result = Result(Call(Context(), "get_global_summary")).GetProperty("result");

			Assert.That(result.GetProperty("posture").GetString(), Is.EqualTo("attack"), "Outnumbered enemy force ratio is below 0.8.");
			Assert.That(result.GetProperty("coalition_cash").GetInt32(), Is.EqualTo(5000));
			Assert.That(result.GetProperty("deception_effectiveness").GetDouble(), Is.EqualTo(0.5).Within(0.001));
		}

		[TestCase(TestName = "get_economy_state reports coalition and per-member cash.")]
		public void EconomyState()
		{
			var result = Result(Call(Context(), "get_economy_state")).GetProperty("result");

			Assert.That(result.GetProperty("coalition_cash").GetInt32(), Is.EqualTo(5000));
			Assert.That(result.GetProperty("members").GetProperty("Multi1").GetInt32(), Is.EqualTo(2000));
		}

		[TestCase(TestName = "get_economy_state reports the coalition power balance.")]
		public void EconomyPower()
		{
			var context = Context();
			context.PowerProvided = 1200;
			context.PowerDrained = 800;

			var result = Result(Call(context, "get_economy_state")).GetProperty("result");

			Assert.That(result.GetProperty("power_provided").GetInt32(), Is.EqualTo(1200));
			Assert.That(result.GetProperty("power_drained").GetInt32(), Is.EqualTo(800));
			Assert.That(result.GetProperty("power_excess").GetInt32(), Is.EqualTo(400));
		}

		[TestCase(TestName = "inspect_force reports capabilities, status, and assignment.")]
		public void InspectForceCapabilities()
		{
			var context = Context();
			var force = context.Forces[0];
			force.ByType["mig"] = 4;
			force.Capabilities[(int)FriendlyCapability.Air] = 1f;
			force.Capabilities[(int)FriendlyCapability.AntiAir] = 1f;
			force.Status = ForceStatus.Moving;
			force.MissionId = "OP-9";
			force.Role = "main";
			force.CasualtyFraction = 0.25f;

			var result = Result(Call(context, "inspect_force", "{\"force\":\"Multi0\"}")).GetProperty("result");

			Assert.That(result.GetProperty("by_type").GetProperty("mig").GetInt32(), Is.EqualTo(4));
			Assert.That(result.GetProperty("capabilities").GetProperty("air").GetInt32(), Is.EqualTo(1));
			Assert.That(result.GetProperty("capabilities").GetProperty("anti_air").GetInt32(), Is.EqualTo(1));
			Assert.That(result.GetProperty("status").GetString(), Is.EqualTo("moving"));
			Assert.That(result.GetProperty("mission").GetString(), Is.EqualTo("OP-9"));
			Assert.That(result.GetProperty("role").GetString(), Is.EqualTo("main"));
			Assert.That(result.GetProperty("casualty_fraction").GetDouble(), Is.EqualTo(0.25).Within(0.001));
		}

		[TestCase(TestName = "get_production_state reports every facility's queue and progress.")]
		public void ProductionState()
		{
			var context = Context();
			context.Facilities =
			[
				new ProductionFacility("Multi0", "Vehicle", "weap", new CPos(4, 4))
				{
					Current = "2tnk",
					Queued = ["3tnk"],
					Buildable = ["2tnk", "3tnk"],
					ProgressPercent = 50
				}
			];

			var result = Result(Call(context, "get_production_state")).GetProperty("result");
			var facilities = result.EnumerateArray().ToArray();

			Assert.That(facilities.Length, Is.EqualTo(1));
			Assert.That(facilities[0].GetProperty("owner").GetString(), Is.EqualTo("Multi0"));
			Assert.That(facilities[0].GetProperty("queue").GetString(), Is.EqualTo("Vehicle"));
			Assert.That(facilities[0].GetProperty("current").GetString(), Is.EqualTo("2tnk"));
			Assert.That(facilities[0].GetProperty("queued").GetArrayLength(), Is.EqualTo(1));
			Assert.That(facilities[0].GetProperty("progress_percent").GetInt32(), Is.EqualTo(50));
		}

		[TestCase(TestName = "inspect_region reports buildable cells and expansion value.")]
		public void InspectRegionExpansion()
		{
			var context = Context();
			var map = context.MapAnalysis;
			map.BuildableCells[3] = 20;
			map.ExpansionValue[3] = 1.5f;

			var result = Result(Call(context, "inspect_region", "{\"region\":3}")).GetProperty("result");

			Assert.That(result.GetProperty("buildable_cells").GetInt32(), Is.EqualTo(20));
			Assert.That(result.GetProperty("expansion_value").GetDouble(), Is.EqualTo(1.5).Within(0.001));
		}

		[TestCase(TestName = "inspect_enemy_intelligence carries the honesty status so fact is distinguishable from inference.")]
		public void IntelStatusHonesty()
		{
			var context = Context();
			context.EnemyIntel =
			[
				new EnemyIntel("3tnk", UnitClass.Armor)
				{
					LastSeenCell = new CPos(2, 2), LastSeenTick = 1000,
					Confidence = 1f, Status = IntelStatus.Observed
				},
				new EnemyIntel("3tnk", UnitClass.Armor)
				{
					LastSeenCell = new CPos(4, 4), LastSeenTick = 500,
					Confidence = 0.4f, Status = IntelStatus.LastKnown, PositionErrorCells = 4
				},
				new EnemyIntel(string.Empty, UnitClass.Support)
				{
					LastSeenCell = new CPos(12, 8), LastSeenTick = 1000,
					Confidence = 0.2f, Status = IntelStatus.Suspected, PositionErrorCells = 16
				}
			];

			var result = Result(Call(context, "inspect_enemy_intelligence")).GetProperty("result");
			var entries = result.EnumerateArray().ToArray();

			Assert.That(entries, Has.Length.EqualTo(3));
			Assert.That(entries[0].GetProperty("status").GetString(), Is.EqualTo("observed"));
			Assert.That(entries[0].GetProperty("confidence").GetDouble(), Is.EqualTo(1.0).Within(0.001));
			Assert.That(entries[0].GetProperty("position_error_cells").GetInt32(), Is.EqualTo(0));

			Assert.That(entries[1].GetProperty("status").GetString(), Is.EqualTo("last_known"));
			Assert.That(entries[1].GetProperty("confidence").GetDouble(), Is.LessThan(1.0));
			Assert.That(entries[1].GetProperty("position_error_cells").GetInt32(), Is.GreaterThanOrEqualTo(1));

			Assert.That(entries[2].GetProperty("status").GetString(), Is.EqualTo("suspected"));
			Assert.That(entries[2].GetProperty("type").GetString(), Is.Empty, "A suspected region has no specific unit type.");
		}

		[TestCase(TestName = "Suspected intel is a hypothesis, surfaced as an uncertainty and never scored as a target.")]
		public void SuspectedIntelNotScored()
		{
			var context = Context();
			context.EnemyIntel = context.EnemyIntel.Concat(
			[
				new EnemyIntel(string.Empty, UnitClass.Structure)
				{
					LastSeenCell = new CPos(1, 1),
					LastSeenTick = 1000,
					Confidence = 0.2f,
					Status = IntelStatus.Suspected,
					PositionErrorCells = 16
				}
			]).ToArray();

			// The suspected structure-class entry is a hypothesis, not a confirmed sighting, so it is
			// excluded from target scoring even though it carries a structure class.
			var targets = Result(Call(context, "score_targets")).GetProperty("result").EnumerateArray().ToArray();
			Assert.That(targets.Length, Is.EqualTo(2), "Only the two confirmed structures are scored.");
			Assert.That(targets.All(t => t.GetProperty("type").GetString() != string.Empty), Is.True);

			// It instead surfaces as an intelligence question the commander can act on.
			var uncertainties = Result(Call(context, "get_uncertainties")).GetProperty("result").EnumerateArray().ToArray();
			Assert.That(uncertainties.Any(u => u.GetProperty("question").GetString()
				.StartsWith("suspected_enemy_in_region_", System.StringComparison.Ordinal)),
				Is.True, "Suspected presence is reported as a recon question, not a target.");
		}

		[TestCase(TestName = "Every tool returns a top-level ok flag and a result or machine-readable error.")]
		public void ToolResponseSchema()
		{
			var context = Context();
			var tools = new[]
			{
				"get_global_summary", "inspect_region", "inspect_force", "inspect_enemy_intelligence",
				"get_recent_events", "get_opponent_model", "get_uncertainties", "estimate_engagement",
				"score_targets", "plan_routes", "get_economy_state", "get_production_state",
				"compare_force_packages", "estimate_enemy_response", "find_attack_windows",
				"find_special_ops_routes", "get_mission_status", "get_force_readiness",
				"get_transport_status", "get_route_status", "set_production_directive",
				"set_expansion_priority", "request_capability", "create_mission", "modify_mission",
				"cancel_mission", "assign_force", "release_force", "set_reserve", "request_recon",
				"set_strategic_posture"
			};

			foreach (var tool in tools)
			{
				var root = Result(Call(context, tool));
				Assert.That(root.TryGetProperty("ok", out _), Is.True, $"{tool} must report an ok flag.");

				var ok = root.GetProperty("ok").GetBoolean();
				if (ok)
					Assert.That(root.TryGetProperty("result", out _), Is.True, $"{tool} success must carry a result.");
				else
					Assert.That(root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String,
						Is.True, $"{tool} failure must carry a machine-readable error string.");
			}
		}

		[TestCase(TestName = "Mutation tools return engine-validated command intent patches.")]
		public void MutationToolsReturnValidatedPatches()
		{
			var context = Context();
			var calls = new (string Tool, string Arguments, string Field)[]
			{
				("set_production_directive", "{\"units\":[\"3tnk\"]}", "production_directive"),
				("set_expansion_priority", "{\"priority\":1}", "expansion_priority"),
				("request_capability", "{\"capability\":\"anti_air\"}", "request_capability"),
				("create_mission", "{\"type\":\"raid\",\"x\":2,\"y\":2,\"priority\":60}", "missions"),
				("modify_mission", "{\"mission\":\"OP-1\"}", "modify_missions"),
				("cancel_mission", "{\"mission\":\"OP-1\"}", "cancel_missions"),
				("assign_force", "{\"force\":\"Multi0\",\"mission\":\"OP-1\"}", "assign_force"),
				("release_force", "{\"force\":\"Multi1\"}", "release_force"),
				("set_reserve", "{\"fraction\":4}", "reserve_fraction"),
				("request_recon", "{\"region\":1}", "missions"),
				("set_strategic_posture", "{\"posture\":\"defend\"}", "posture")
			};

			foreach (var (tool, arguments, field) in calls)
			{
				var root = Result(Call(context, tool, arguments));
				Assert.That(root.GetProperty("ok").GetBoolean(), Is.True, tool);
				var result = root.GetProperty("result");
				Assert.That(result.GetProperty("accepted").GetBoolean(), Is.True, tool);
				Assert.That(result.GetProperty("plan_patch").TryGetProperty(field, out _), Is.True, tool);
			}
		}

		[TestCase(TestName = "Mutation tools reject illegal units, missions, capabilities, reserves, and force conflicts.")]
		public void MutationToolsRejectInvalidCommands()
		{
			var context = Context();
			context.Forces[0].MissionId = "OTHER";
			var calls = new (string Tool, string Arguments)[]
			{
				("set_production_directive", "{\"units\":[\"not-a-unit\"]}"),
				("set_expansion_priority", "{\"priority\":5}"),
				("request_capability", "{\"capability\":\"magic\"}"),
				("create_mission", "{\"type\":\"raid\",\"x\":999,\"y\":2}"),
				("assign_force", "{\"force\":\"Multi0\",\"mission\":\"OP-1\"}"),
				("set_reserve", "{\"fraction\":99}"),
				("set_strategic_posture", "{\"posture\":\"panic\"}")
			};

			foreach (var (tool, arguments) in calls)
			{
				var root = Result(Call(context, tool, arguments));
				Assert.That(root.GetProperty("ok").GetBoolean(), Is.False, tool);
				Assert.That(root.GetProperty("error").GetString(), Is.EqualTo("INVALID_ARGUMENTS"), tool);
			}
		}

		[TestCase(TestName = "Consuming the last meaningful reserve requires strong justification.")]
		public void ReserveCommitmentRequiresJustification()
		{
			var context = Context();
			var rejected = Result(Call(context, "set_reserve", "{\"fraction\":10}"));
			Assert.That(rejected.GetProperty("ok").GetBoolean(), Is.False);
			Assert.That(rejected.GetProperty("message").GetString(), Does.Contain("REJECTED_UNJUSTIFIED_RESERVE_COMMITMENT"));

			var accepted = Result(Call(context, "set_reserve",
				"{\"fraction\":10,\"justification\":\"Decisive breach is open and the enemy reserve is depleted.\"}"));
			Assert.That(accepted.GetProperty("ok").GetBoolean(), Is.True);
			var patch = accepted.GetProperty("result").GetProperty("plan_patch");
			Assert.That(patch.GetProperty("reserve_fraction").GetInt32(), Is.EqualTo(10));
			Assert.That(patch.GetProperty("reserve_justification").GetString(), Does.Contain("breach"));
		}
	}
}
