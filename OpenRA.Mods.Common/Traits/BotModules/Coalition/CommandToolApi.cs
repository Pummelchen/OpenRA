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
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	/// <summary>
	/// A plain-data snapshot of the blackboard state that the tool API executes against. The command
	/// center builds one from the live blackboard each tick; tests build it directly, so every tool is
	/// unit-testable without a World.
	/// </summary>
	public sealed class ToolContext
	{
		public int Tick;
		public int Timestep = 40;
		public CoalitionRegion[] Regions = [];
		public ForceGroup[] Forces = [];
		public SpecialAsset[] SpecialAssets = [];
		public SpecialAsset[] Transports = [];
		public ProductionFacility[] Facilities = [];
		public EnemyIntel[] EnemyIntel = [];
		public CoalitionEvent[] Events = [];
		public OpponentModel Opponent = new();
		public int CoalitionCash;
		public Dictionary<string, int> MemberCash = [];
		public int HomeRegion = -1;
		public int EnemyRegion = -1;
		public float CoalitionArmyStrength;
		public float EnemyArmyStrength;
		public int EnemyArmyCount;
		public float DeceptionEffectiveness;
		public int DeceptionEnemiesDrawn;
		public int PowerProvided;
		public int PowerDrained;
		public int PowerExcess => PowerProvided - PowerDrained;
		public CoalitionMapAnalysis MapAnalysis;
		public float[][] ThreatField;

		/// <summary>The region containing a cell, matching the blackboard's partition.</summary>
		public int RegionOf(CPos cell)
		{
			foreach (var region in Regions)
				if (region.Bounds.Contains(cell.X, cell.Y))
					return region.Index;
			return 0;
		}
	}

	/// <summary>
	/// The engine-validated LLM tool API. Every tool call is validated against the real blackboard
	/// snapshot (regions, forces, capabilities, coordinates) and answered from deterministic engine
	/// computations (combat estimator, target evaluator, route planner) - the commander never receives
	/// fabricated mechanics, only engine state and engine results. Pure JSON in, JSON out.
	/// </summary>
	public static class CommandToolApi
	{
		/// <summary>Per-capability snake_case keys, matching the threat_field.v1 contract.</summary>
		public static readonly string[] CapabilityKeys =
		{
			"ground_anti_armor", "ground_anti_infantry", "artillery", "anti_air", "air_to_air",
			"naval", "submarine", "vision_exposure", "detection", "static_defense",
			"reinforcement", "support_power_risk"
		};

		/// <summary>Snake_case keys for the friendly functional capability profile.</summary>
		public static readonly string[] FriendlyCapabilityKeys =
		{
			"anti_air", "anti_armor", "anti_infantry", "artillery", "recon", "transport",
			"naval", "air", "detection", "anti_structure"
		};

		/// <summary>
		/// Executes one tool call: <c>{"tool": "&lt;name&gt;", "arguments": {...}}</c>. Returns the
		/// JSON envelope <c>{"ok":true,"result":{...}}</c> or <c>{"ok":false,"error":"&lt;code&gt;","message":"..."}</c>.
		/// </summary>
		public static string Execute(ToolContext context, string requestJson)
		{
			JsonDocument request;
			try
			{
				request = JsonDocument.Parse(requestJson);
			}
			catch (JsonException)
			{
				return Error("INVALID_REQUEST", "Request is not valid JSON.");
			}

			using (request)
			{
				var root = request.RootElement;
				if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("tool", out var toolElement)
					|| toolElement.ValueKind != JsonValueKind.String)
					return Error("INVALID_REQUEST", "Missing string field \"tool\".");

				var tool = toolElement.GetString();
				var args = root.TryGetProperty("arguments", out var argsElement) ? argsElement : default;

				try
				{
					switch (tool)
					{
						case "get_global_summary":
							return GetGlobalSummary(context);
						case "inspect_region":
							return InspectRegion(context, args);
						case "inspect_force":
							return InspectForce(context, args);
						case "inspect_enemy_intelligence":
							return InspectEnemyIntelligence(context, args);
						case "get_recent_events":
							return GetRecentEvents(context, args);
						case "get_opponent_model":
							return GetOpponentModel(context);
						case "get_uncertainties":
							return GetUncertainties(context);
						case "estimate_engagement":
							return EstimateEngagement(context, args);
						case "score_targets":
							return ScoreTargets(context, args);
						case "plan_routes":
							return PlanRoutes(context, args);
						case "get_economy_state":
							return GetEconomyState(context);
						case "get_production_state":
							return GetProductionState(context);
						default:
							return Error("UNKNOWN_TOOL", $"Unknown tool \"{tool}\".");
					}
				}
				catch (ArgumentException e)
				{
					return Error("INVALID_ARGUMENTS", e.Message);
				}
				catch (KeyNotFoundException e)
				{
					return Error("UNKNOWN_REFERENCE", e.Message);
				}
				catch (FormatException e)
				{
					return Error("INVALID_ARGUMENTS", e.Message);
				}
				catch (InvalidOperationException e)
				{
					// A value of the wrong JSON kind (string where a number is required, etc.).
					return Error("INVALID_ARGUMENTS", e.Message);
				}
			}
		}

		// ------------------------------------------------------------------------------------
		// Tools
		// ------------------------------------------------------------------------------------

		static string GetGlobalSummary(ToolContext context)
		{
			var ratio = context.CoalitionArmyStrength <= 0 ? 0 : context.EnemyArmyStrength / context.CoalitionArmyStrength;
			var posture = ratio > 1.2 ? "defend" : ratio < 0.8 ? "attack" : "build";

			return Ok(new JsonObject
			{
				["posture"] = posture,
				["force_ratio"] = Round(ratio),
				["coalition_cash"] = context.CoalitionCash,
				["coalition_army_strength"] = Round(context.CoalitionArmyStrength),
				["enemy_army_strength"] = Round(context.EnemyArmyStrength),
				["enemy_army_count"] = context.EnemyArmyCount,
				["home_region"] = context.HomeRegion,
				["enemy_region"] = context.EnemyRegion,
				["deception_effectiveness"] = Round(context.DeceptionEffectiveness),
				["deception_enemies_drawn"] = context.DeceptionEnemiesDrawn
			});
		}

		static string InspectRegion(ToolContext context, JsonElement args)
		{
			var index = ResolveRegion(context, Require(args, "region"), "region");
			var region = context.Regions[index];

			return Ok(new JsonObject
			{
				["region"] = index,
				["bounds"] = new JsonObject
				{
					["x0"] = region.Bounds.Left,
					["y0"] = region.Bounds.Top,
					["x1"] = region.Bounds.Right,
					["y1"] = region.Bounds.Bottom
				},
				["friendly_control"] = Round(region.FriendlyControl),
				["enemy_pressure"] = Round(region.EnemyPressure),
				["buildable_cells"] = context.MapAnalysis?.BuildableCells?[index] ?? 0,
				["expansion_value"] = Round(context.MapAnalysis?.ExpansionValue?[index] ?? 0f),
				["threats"] = ThreatObject(region.Threats)
			});
		}

		static string InspectForce(ToolContext context, JsonElement args)
		{
			var force = ResolveForce(context, Require(args, "force"), "force");

			var byType = new JsonObject();
			foreach (var kv in force.ByType)
				byType[kv.Key] = kv.Value;

			return Ok(new JsonObject
			{
				["owner"] = force.Owner,
				["composition"] = new JsonObject
				{
					["infantry"] = force.Counts[(int)UnitClass.Infantry],
					["armor"] = force.Counts[(int)UnitClass.Armor],
					["air"] = force.Counts[(int)UnitClass.Air],
					["naval"] = force.Counts[(int)UnitClass.Naval],
					["support"] = force.Counts[(int)UnitClass.Support],
					["structure"] = force.Counts[(int)UnitClass.Structure]
				},
				["by_type"] = byType,
				["capabilities"] = FriendlyCapabilityObject(force.Capabilities),
				["total_units"] = force.TotalUnits,
				["strength"] = Round(force.Strength),
				["readiness"] = Round(force.Readiness),
				["status"] = force.Status.ToString().ToLowerInvariant(),
				["mission"] = force.MissionId,
				["role"] = force.Role,
				["casualty_fraction"] = Round(force.CasualtyFraction),
				["center"] = new JsonObject { ["x"] = force.Center.X, ["y"] = force.Center.Y }
			});
		}

		static JsonObject FriendlyCapabilityObject(float[] capabilities)
		{
			var obj = new JsonObject();
			for (var c = 0; c < capabilities.Length && c < FriendlyCapabilityKeys.Length; c++)
				obj[FriendlyCapabilityKeys[c]] = capabilities[c] > 0 ? 1 : 0;
			return obj;
		}

		static string InspectEnemyIntelligence(ToolContext context, JsonElement args)
		{
			var region = -1;
			if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("region", out var regionElement))
				region = ResolveRegion(context, regionElement, "region");

			var intel = context.EnemyIntel
				.Where(i => region < 0 || context.RegionOf(i.LastSeenCell) == region)
				.Select(i => IntelObject(context, i))
				.ToArray();

			return Ok(new JsonArray(intel));
		}

		static string GetRecentEvents(ToolContext context, JsonElement args)
		{
			var since = 0;
			if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("since_tick", out var sinceElement))
				since = sinceElement.GetInt32();

			var events = context.Events
				.Where(e => e.Tick >= since)
				.Select(e => new JsonObject
				{
					["tick"] = e.Tick,
					["type"] = e.Type,
					["region"] = e.Cell.HasValue ? context.RegionOf(e.Cell.Value) : -1,
					["x"] = e.Cell.HasValue ? e.Cell.Value.X : -1,
					["y"] = e.Cell.HasValue ? e.Cell.Value.Y : -1,
					["payload"] = e.Payload ?? string.Empty
				})
				.ToArray();

			return Ok(new JsonArray(events));
		}

		static string GetOpponentModel(ToolContext context)
		{
			var opponent = context.Opponent;
			return Ok(new JsonObject
			{
				["armor_bias"] = Round(opponent.ArmorBias),
				["infantry_bias"] = Round(opponent.InfantryBias),
				["air_bias"] = Round(opponent.AirBias),
				["naval_bias"] = Round(opponent.NavalBias),
				["static_defense_bias"] = Round(opponent.StaticDefenseBias),
				["preferred_attack_lane"] = opponent.PreferredAttackLane,
				["average_response_time"] = Round(opponent.AverageResponseTime),
				["response_samples"] = opponent.ResponseSamples,
				["responds_strongly_to_raids"] = opponent.RespondsStronglyToRaids,
				["moves_whole_army_to_defend"] = opponent.MovesWholeArmyToDefend,
				["attacks_harvesters"] = opponent.AttacksHarvesters,
				["expansion_count"] = opponent.ExpansionCount,
				["confidence"] = Round(opponent.Confidence),
				["playstyle"] = opponent.Playstyle,
				["predicted_build"] = opponent.PredictedBuild
			});
		}

		static string GetUncertainties(ToolContext context)
		{
			var questions = context.EnemyIntel
				.Where(i => i.Confidence < 0.5f || AgeSeconds(context, i) > 60)
				.Select(i => new JsonObject
				{
					["question"] = $"enemy_{i.Type}_position",
					["value"] = Round(i.Confidence)
				})
				.ToArray();

			return Ok(new JsonArray(questions));
		}

		static string EstimateEngagement(ToolContext context, JsonElement args)
		{
			var a = ResolveForce(context, Require(args, "force_a"), "force_a");
			var b = ResolveForce(context, Require(args, "force_b"), "force_b");

			var powerA = ForcePower(a);
			var powerB = ForcePower(b);
			var (winRatio, friendlyLoss) = CombatEstimator.Estimate(powerA, powerB);
			var (_, enemyLoss) = CombatEstimator.Estimate(powerB, powerA);

			return Ok(new JsonObject
			{
				["force_a"] = a.Owner,
				["force_b"] = b.Owner,
				["force_a_power"] = Round(powerA),
				["force_b_power"] = Round(powerB),
				["win_ratio"] = Round(winRatio),
				["expected_friendly_loss_fraction"] = Round(friendlyLoss),
				["expected_enemy_loss_fraction"] = Round(enemyLoss),
				["model_version"] = "v1"
			});
		}

		static string ScoreTargets(ToolContext context, JsonElement args)
		{
			var region = -1;
			var weights = TargetWeights.Balanced();
			if (args.ValueKind == JsonValueKind.Object)
			{
				if (args.TryGetProperty("region", out var regionElement))
					region = ResolveRegion(context, regionElement, "region");
				if (args.TryGetProperty("posture", out var postureElement) && postureElement.ValueKind == JsonValueKind.String)
					weights = postureElement.GetString() switch
					{
						"balanced" => TargetWeights.Balanced(),
						"raiding" => TargetWeights.Raiding(),
						"breakthrough" => TargetWeights.Breakthrough(),
						var other => throw new ArgumentException($"Unknown posture \"{other}\".")
					};
			}

			var scored = new List<(string Type, int X, int Y, int Region, float Score, float Confidence)>();
			foreach (var intel in context.EnemyIntel.Where(i => i.Class == UnitClass.Structure))
			{
				var targetRegion = context.RegionOf(intel.LastSeenCell);
				if (region >= 0 && region != targetRegion)
					continue;

				var route = CoalitionRoutePlanner.FindRoute(context.MapAnalysis, context.ThreatField,
					context.HomeRegion, targetRegion, MovementClass.Ground, RouteWeights.Assault());
				if (!route.Found)
					continue;

				var (economy, production, technology) = TargetEvaluator.Classify(intel.Type);
				var uncertainty = intel.Confidence < 0.5f ? 1f : 0.3f;
				var breakdown = TargetEvaluator.Score(intel.Type, economy, production, technology,
					targetRegion, route.Cost, friendlyLossRisk: 0.2f,
					enemyReinforcementRisk: context.Regions[targetRegion].Threats[(int)CoalitionCapability.Reinforcement],
					enemyCounterattackRisk: context.Regions[targetRegion].Threats[(int)CoalitionCapability.GroundAntiArmor],
					uncertainty, context.MapAnalysis, MovementClass.Ground, weights);

				scored.Add((intel.Type, intel.LastSeenCell.X, intel.LastSeenCell.Y, targetRegion, breakdown.Total, intel.Confidence));
			}

			var targets = scored.OrderByDescending(s => s.Score)
				.Select(s => new JsonObject
				{
					["type"] = s.Type,
					["x"] = s.X,
					["y"] = s.Y,
					["region"] = s.Region,
					["score"] = Round(s.Score),
					["confidence"] = Round(s.Confidence)
				})
				.ToArray();

			return Ok(new JsonArray(targets));
		}

		static string PlanRoutes(ToolContext context, JsonElement args)
		{
			var from = ResolveRegion(context, Require(args, "from_region"), "from_region");
			var to = ResolveRegion(context, Require(args, "to_region"), "to_region");
			var movementClass = MovementClass.Ground;
			var weights = RouteWeights.Assault();

			if (args.TryGetProperty("movement", out var movementElement) && movementElement.ValueKind == JsonValueKind.String)
				movementClass = movementElement.GetString() switch
				{
					"ground" => MovementClass.Ground,
					"naval" => MovementClass.Naval,
					"air" => MovementClass.Air,
					var other => throw new ArgumentException($"Unknown movement class \"{other}\".")
				};

			if (args.TryGetProperty("profile", out var profileElement) && profileElement.ValueKind == JsonValueKind.String)
				weights = profileElement.GetString() switch
				{
					"assault" => RouteWeights.Assault(),
					"stealth" => RouteWeights.Stealth(),
					"recon" => RouteWeights.Recon(),
					"retreat" => RouteWeights.Retreat(),
					var other => throw new ArgumentException($"Unknown route profile \"{other}\".")
				};

			if (args.TryGetProperty("weights", out var weightOverrides) && weightOverrides.ValueKind == JsonValueKind.Object)
				foreach (var property in weightOverrides.EnumerateObject())
					ApplyRouteWeight(weights, property.Name, property.Value.GetSingle());

			var route = CoalitionRoutePlanner.FindRoute(context.MapAnalysis, context.ThreatField,
				from, to, movementClass, weights);

			if (!route.Found)
				return Ok(new JsonObject { ["found"] = false, ["cost"] = double.MaxValue, ["regions"] = new JsonArray() });

			var regions = new JsonArray();
			foreach (var region in route.Regions)
				regions.Add(region);

			return Ok(new JsonObject
			{
				["found"] = true,
				["cost"] = Round(route.Cost),
				["regions"] = regions
			});
		}

		static string GetEconomyState(ToolContext context)
		{
			var members = new JsonObject();
			foreach (var kv in context.MemberCash)
				members[kv.Key] = kv.Value;

			return Ok(new JsonObject
			{
				["coalition_cash"] = context.CoalitionCash,
				["power_provided"] = context.PowerProvided,
				["power_drained"] = context.PowerDrained,
				["power_excess"] = context.PowerExcess,
				["members"] = members
			});
		}

		static string GetProductionState(ToolContext context)
		{
			var facilities = context.Facilities
				.Select(f => new JsonObject
				{
					["owner"] = f.Owner,
					["queue"] = f.QueueType,
					["structure"] = f.Structure,
					["x"] = f.Cell.X,
					["y"] = f.Cell.Y,
					["current"] = f.Current,
					["queued"] = new JsonArray(f.Queued.Select(q => (JsonNode)q).ToArray()),
					["buildable"] = new JsonArray(f.Buildable.Select(b => (JsonNode)b).ToArray()),
					["progress_percent"] = f.ProgressPercent
				})
				.ToArray();

			return Ok(new JsonArray(facilities));
		}

		// ------------------------------------------------------------------------------------
		// Engine helpers
		// ------------------------------------------------------------------------------------

		/// <summary>Lanchester-style power of a force group: class weights scaled by average health.</summary>
		static float ForcePower(ForceGroup force)
		{
			var health = force.Strength > 0 ? force.Strength : 1f;
			var power = 0f;
			for (var c = 0; c < force.Counts.Length; c++)
				power += CombatEstimator.ClassWeight((UnitClass)c) * force.Counts[c] * health;

			return power;
		}

		static float AgeSeconds(ToolContext context, EnemyIntel intel)
		{
			return (context.Tick - intel.LastSeenTick) * context.Timestep / 1000f;
		}

		static JsonObject IntelObject(ToolContext context, EnemyIntel intel)
		{
			return new JsonObject
			{
				["type"] = intel.Type,
				["class"] = intel.Class.ToString().ToLowerInvariant(),
				["last_seen"] = new JsonObject
				{
					["x"] = intel.LastSeenCell.X,
					["y"] = intel.LastSeenCell.Y,
					["tick"] = intel.LastSeenTick,
					["region"] = context.RegionOf(intel.LastSeenCell)
				},
				["age_seconds"] = Round(AgeSeconds(context, intel)),
				["confidence"] = Round(intel.Confidence),
				["count"] = new JsonObject
				{
					["min"] = intel.MinCount,
					["expected"] = intel.ExpectedCount,
					["max"] = intel.MaxCount
				}
			};
		}

		static JsonObject ThreatObject(float[] threats)
		{
			var obj = new JsonObject();
			for (var c = 0; c < threats.Length && c < CapabilityKeys.Length; c++)
				obj[CapabilityKeys[c]] = Round(threats[c]);
			return obj;
		}

		static void ApplyRouteWeight(RouteWeights weights, string key, float value)
		{
			switch (key.Replace("-", "_"))
			{
				case "distance": weights.Distance = value; break;
				case "combat_threat": weights.CombatThreat = value; break;
				case "anti_air": weights.AntiAirThreat = value; break;
				case "artillery": weights.ArtilleryThreat = value; break;
				case "vision_exposure": weights.VisionExposure = value; break;
				case "detection": weights.DetectionExposure = value; break;
				case "chokepoint_risk": weights.ChokepointRisk = value; break;
				case "reinforcement": weights.ReinforcementRisk = value; break;
				case "support_power_risk": weights.SupportPowerRisk = value; break;
				default: throw new KeyNotFoundException($"Unknown route weight \"{key}\".");
			}
		}

		// ------------------------------------------------------------------------------------
		// Argument validation
		// ------------------------------------------------------------------------------------

		static JsonElement Require(JsonElement args, string name)
		{
			if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var value))
				throw new ArgumentException($"Missing required argument \"{name}\".");

			return value;
		}

		static int ResolveRegion(ToolContext context, JsonElement value, string name)
		{
			int index;
			if (value.ValueKind == JsonValueKind.Number)
				index = value.GetInt32();
			else if (value.ValueKind == JsonValueKind.String)
			{
				var text = value.GetString();
				if (text.StartsWith("REGION_", StringComparison.OrdinalIgnoreCase))
					text = text[7..];
				if (!int.TryParse(text, out index))
					throw new ArgumentException($"Argument \"{name}\" is not a region id.");
			}
			else
				throw new ArgumentException($"Argument \"{name}\" must be a region id (index or \"REGION_n\").");

			if (index < 0 || index >= context.Regions.Length)
				throw new KeyNotFoundException($"Unknown region \"{index}\".");

			return index;
		}

		static ForceGroup ResolveForce(ToolContext context, JsonElement value, string name)
		{
			if (value.ValueKind != JsonValueKind.String)
				throw new ArgumentException($"Argument \"{name}\" must be a force/owner id.");

			var text = value.GetString();
			if (text.StartsWith("FORCE_", StringComparison.OrdinalIgnoreCase))
				text = text[6..];

			var force = context.Forces.FirstOrDefault(f => f.Owner == text);
			if (force == null)
				throw new KeyNotFoundException($"Unknown force \"{text}\".");

			return force;
		}

		static double Round(float value)
		{
			return Math.Round(value, 3);
		}

		static string Ok(JsonNode result)
		{
			return JsonSerializer.Serialize(new JsonObject { ["ok"] = true, ["result"] = result });
		}

		static string Error(string code, string message)
		{
			return JsonSerializer.Serialize(new JsonObject
			{
				["ok"] = false,
				["error"] = code,
				["message"] = message
			});
		}
	}
}
