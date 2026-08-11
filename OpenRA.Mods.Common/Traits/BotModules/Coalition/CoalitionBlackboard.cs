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
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	/// <summary>Combat capability domains used for threat fields and production contracts.</summary>
	public enum CoalitionCapability
	{
		GroundAntiArmor,
		GroundAntiInfantry,
		Artillery,
		AntiAir,
		AirToAir,
		Naval,
		Submarine,
		VisionExposure,
		Detection,
		StaticDefense,
		Reinforcement,
		SupportPowerRisk
	}

	/// <summary>Broad unit classification used for force aggregation.</summary>
	public enum UnitClass
	{
		Infantry,
		Armor,
		Air,
		Naval,
		Support,
		Structure
	}

	/// <summary>Intelligence honesty ladder: how an enemy sighting is known.</summary>
	public enum IntelStatus
	{
		Observed,
		LastKnown,
		Inferred,
		Suspected,
		Unknown
	}

	/// <summary>A rectangular partition of the map with live control and threat data.</summary>
	public sealed class CoalitionRegion
	{
		public readonly int Index;
		public readonly Rectangle Bounds;
		public float FriendlyControl;
		public float EnemyPressure;
		public readonly float[] Threats = new float[Enum.GetValues<CoalitionCapability>().Length];

		public CoalitionRegion(int index, Rectangle bounds)
		{
			Index = index;
			Bounds = bounds;
		}
	}

	/// <summary>An aggregated force: per-owner counts by class plus strength/readiness.</summary>
	public sealed class ForceGroup
	{
		public readonly string Owner;
		public readonly int[] Counts = new int[Enum.GetValues<UnitClass>().Length];
		public int TotalUnits;
		public float Strength;
		public float Readiness;
		public CPos Center;

		public ForceGroup(string owner)
		{
			Owner = owner;
		}
	}

	/// <summary>One tracked enemy actor sighting with confidence decay.</summary>
	public sealed class EnemyIntel
	{
		public readonly Actor Actor;
		public readonly string Type;
		public readonly UnitClass Class;
		public CPos LastSeenCell;
		public int LastSeenTick;
		public float Confidence;
		public int MinCount;
		public int ExpectedCount;
		public int MaxCount;

		public EnemyIntel(Actor actor, UnitClass unitClass)
		{
			Actor = actor;
			Type = actor.Info.Name;
			Class = unitClass;
			Confidence = 1f;
			MinCount = ExpectedCount = MaxCount = 1;
		}
	}

	/// <summary>A behavioral profile of the enemy, built from observed patterns.</summary>
	public sealed class OpponentModel
	{
		public float ArmorBias;
		public float AirBias;
		public float StaticDefenseBias;
		public int PreferredAttackLane = -1;
		public float AverageResponseTime;
		public int ResponseSamples;
		public bool RespondsStronglyToRaids;
		public bool MovesWholeArmyToDefend;
		public bool AttacksHarvesters;
		public int ExpansionCount;

		/// <summary>"rush", "turtle", "balanced", or "unknown" - derived from the scouted shape.</summary>
		public string Playstyle = "unknown";

		/// <summary>The most advanced scouted enemy tech direction: "air", "naval", "armor", or "unknown".</summary>
		public string PredictedBuild = "unknown";

		public void RecordResponseTime(float seconds)
		{
			AverageResponseTime = (AverageResponseTime * ResponseSamples + seconds) / (ResponseSamples + 1);
			ResponseSamples++;
		}
	}

	/// <summary>A notable event that wakes strategic reasoning.</summary>
	public sealed class CoalitionEvent
	{
		public readonly int Tick;
		public readonly string Type;
		public readonly CPos? Cell;
		public readonly string Payload;

		public CoalitionEvent(int tick, string type, CPos? cell = null, string payload = null)
		{
			Tick = tick;
			Type = type;
			Cell = cell;
			Payload = payload;
		}
	}

	/// <summary>
	/// The deterministic world model shared by every allied bot. All bots compute the identical
	/// blackboard from the shared world state and allied shroud, so coalition decisions are
	/// deterministic and synchronized without message passing.
	/// </summary>
	public sealed class CoalitionBlackboard
	{
		public const int MaxEvents = 64;

		public readonly World World;
		public readonly Player Player;
		public readonly Player[] Team;
		public readonly int Tick;

		public readonly CoalitionRegion[] Regions;
		public readonly List<ForceGroup> Forces = [];
		public readonly List<EnemyIntel> EnemyIntel = [];
		public readonly List<CoalitionEvent> Events = [];
		public readonly OpponentModel Opponent = new();

		/// <summary>Static terrain analysis of the map: region graph, chokepoints, components, resources.</summary>
		public readonly CoalitionMapAnalysis MapAnalysis;

		public int CoalitionCash;
		public float CoalitionArmyStrength;
		public float EnemyArmyStrength;
		public float EnemyArmyCount;

		/// <summary>The region index of the coalition's average base position, or -1.</summary>
		public int HomeRegion = -1;

		/// <summary>The region index of the best-known enemy concentration, or -1.</summary>
		public int EnemyRegion = -1;

		/// <summary>
		/// Per-region threat arrays in map-analysis order, for route planning. The route planner
		/// consumes this as a <c>float[][]</c> keyed by region index then capability.
		/// </summary>
		public float[][] ThreatField()
		{
			var field = new float[Regions.Length][];
			for (var i = 0; i < Regions.Length; i++)
				field[i] = Regions[i].Threats;
			return field;
		}

		/// <summary>
		/// True when the coalition has explored a water body large enough to make naval production worthwhile.
		/// A tiny lake is not worth a shipyard, and without it coordinated strikes never wait for ships.
		/// </summary>
		public readonly bool HasBigWater;

		readonly Func<Actor, UnitClass> classify;

		public CoalitionBlackboard(World world, Player player, Player[] team, Func<Actor, UnitClass> classify,
			FrozenSet<string> waterTerrainTypes = null, int bigWaterMinimumCells = 0,
			FrozenSet<string> valuableResourceTypes = null)
		{
			World = world;
			Player = player;
			Team = team;
			Tick = world.WorldTick;
			this.classify = classify;

			// Static terrain analysis: region graph, chokepoints, components, resources. Cached per map.
			MapAnalysis = CoalitionMapAnalysis.ForMap(world, waterTerrainTypes ?? new HashSet<string> { "Water" }.ToFrozenSet(),
				valuableResourceTypes ?? new HashSet<string> { "Ore", "Gems" }.ToFrozenSet());

			Regions = MapAnalysis.Regions;
			ExtractForces();
			ExtractEnemyIntel();
			ExtractEconomy();
			ComputeRegions();
			ComputeThreats();
			ComputeStrengths();
			ComputeHomeAndEnemyRegions();

			// The shipyard/coordinated-strike gates only make sense when the coalition can actually see
			// a usable body of water. The shroud is shared across the team, so every bot computes the
			// same result.
			HasBigWater = waterTerrainTypes != null && AIUtils.HasLargeWaterBody(World.Map,
				c => Team.Any(ally => ally.Shroud.IsExplored(c)), waterTerrainTypes, bigWaterMinimumCells);
		}

		public CoalitionRegion RegionOf(CPos cell)
		{
			foreach (var region in Regions)
				if (region.Bounds.Contains(cell.X, cell.Y))
					return region;
			return Regions[0];
		}

		void ExtractForces()
		{
			var teamIds = Team.Select(p => p.InternalName).ToHashSet();
			var groupByOwner = new Dictionary<string, ForceGroup>();
			foreach (var teamPlayer in Team)
				groupByOwner[teamPlayer.InternalName] = new ForceGroup(teamPlayer.InternalName);

			foreach (var a in World.Actors)
			{
				if (a.IsDead || !a.IsInWorld || !teamIds.Contains(a.Owner.InternalName))
					continue;

				// Player actors have no position and are not part of any force.
				if (a.OccupiesSpace == null)
					continue;

				var unitClass = classify(a);
				var owner = a.Owner.InternalName;
				if (!groupByOwner.TryGetValue(owner, out var group))
				{
					group = new ForceGroup(owner);
					groupByOwner[owner] = group;
				}

				group.Counts[(int)unitClass]++;
				group.TotalUnits++;
				var health = a.TraitOrDefault<IHealth>();
				if (health != null)
					group.Strength += health.HP * 1f / health.MaxHP;
			}

			foreach (var group in groupByOwner.Values)
			{
				if (group.TotalUnits > 0)
					group.Strength /= group.TotalUnits;
				group.Center = CenterOf(Team.First(t => t.InternalName == group.Owner));
				Forces.Add(group);
			}
		}

		CPos CenterOf(Player p)
		{
			var structures = World.Actors.Where(a => a.IsInWorld && !a.IsDead && a.Owner == p && a.Info.HasTraitInfo<BuildingInfo>()).ToArray();
			if (structures.Length == 0)
				return p.HomeLocation;
			return World.Map.CellContaining(structures.Select(a => a.CenterPosition).Average());
		}

		void ExtractEnemyIntel()
		{
			var enemyActors = World.Actors.Where(a =>
				a.IsInWorld && !a.IsDead && a.Owner != Player && a.OccupiesSpace != null && Player.RelationshipWith(a.Owner) == PlayerRelationship.Enemy);

			foreach (var a in enemyActors)
			{
				var seen = Team.Any(ally => ally.Shroud.IsExplored(a.CenterPosition));
				if (!seen)
					continue;

				EnemyIntel.Add(new EnemyIntel(a, classify(a)));
			}
		}

		void ExtractEconomy()
		{
			foreach (var p in Team)
				CoalitionCash += p.PlayerActor.TraitOrDefault<PlayerResources>()?.GetCashAndResources() ?? 0;
		}

		void ComputeRegions()
		{
			// Friendly control = explored cells in the region (allied shroud is shared).
			// Enemy pressure = fraction of explored enemy sightings in the region.
			foreach (var region in Regions)
			{
				var explored = 0;
				var total = 0;
				for (var y = region.Bounds.Top; y < region.Bounds.Bottom; y++)
					for (var x = region.Bounds.Left; x < region.Bounds.Right; x++)
					{
						total++;
						if (Team.Any(ally => ally.Shroud.IsExplored(new CPos(x, y))))
							explored++;
					}

				region.FriendlyControl = total == 0 ? 0 : explored * 1f / total;
			}

			var sightingsByRegion = new Dictionary<CoalitionRegion, int>();
			foreach (var intel in EnemyIntel)
			{
				var region = RegionOf(intel.LastSeenCell);
				sightingsByRegion[region] = sightingsByRegion.GetValueOrDefault(region) + 1;
			}

			var maxSightings = sightingsByRegion.Values.DefaultIfEmpty(0).Max();
			if (maxSightings > 0)
				foreach (var kv in sightingsByRegion)
					kv.Key.EnemyPressure = kv.Value * 1f / maxSightings;
		}

		/// <summary>
		/// Independent per-region threat fields per combat capability, derived deterministically from
		/// enemy intel classes. The LLM and mission planners weight routes and targets with these.
		/// </summary>
		void ComputeThreats()
		{
			foreach (var intel in EnemyIntel)
			{
				var region = RegionOf(intel.LastSeenCell);
				var threats = region.Threats;
				switch (intel.Class)
				{
					case UnitClass.Air:
						threats[(int)CoalitionCapability.AntiAir] = Max(threats[(int)CoalitionCapability.AntiAir], intel.Confidence);
						threats[(int)CoalitionCapability.AirToAir] = Max(threats[(int)CoalitionCapability.AirToAir], intel.Confidence);
						break;
					case UnitClass.Armor:
						threats[(int)CoalitionCapability.GroundAntiArmor] = Max(threats[(int)CoalitionCapability.GroundAntiArmor], intel.Confidence);
						break;
					case UnitClass.Infantry:
						threats[(int)CoalitionCapability.GroundAntiInfantry] = Max(threats[(int)CoalitionCapability.GroundAntiInfantry], intel.Confidence);
						break;
					case UnitClass.Naval:
						threats[(int)CoalitionCapability.Naval] = Max(threats[(int)CoalitionCapability.Naval], intel.Confidence);
						break;
					case UnitClass.Structure:
						threats[(int)CoalitionCapability.StaticDefense] = Max(threats[(int)CoalitionCapability.StaticDefense], intel.Confidence);
						break;
				}
			}

			// Exposure: regions with little friendly coverage are riskier to move through.
			foreach (var region in Regions)
				region.Threats[(int)CoalitionCapability.VisionExposure] = 1f - region.FriendlyControl;
		}

		static float Max(float a, float b)
		{
			return a > b ? a : b;
		}

		void ComputeStrengths()
		{
			foreach (var intel in EnemyIntel)
			{
				// Confidence decays over time; the expectation window widens as intel ages.
				var ageTicks = Tick - intel.LastSeenTick;
				var ageSeconds = ageTicks * World.Timestep / 1000f;
				const float HalfLife = 30f;
				intel.Confidence = MathF.Pow(0.5f, ageSeconds / HalfLife);
				if (ageSeconds > 60)
				{
					intel.MinCount = 0;
					intel.ExpectedCount = 1;
					intel.MaxCount = 3;
				}
			}

			// Coalition strength = force groups weighted by readiness; enemy strength = sightings.
			foreach (var force in Forces)
			{
				CoalitionArmyStrength += force.TotalUnits > 0 ? force.Strength * force.TotalUnits : 0;
				force.Readiness = force.TotalUnits > 0 ? 1f : 0f;
			}

			EnemyArmyCount = EnemyIntel.Count;
			EnemyArmyStrength = EnemyIntel.Sum(i => i.Confidence);
		}

		void ComputeHomeAndEnemyRegions()
		{
			HomeRegion = RegionOf(CenterOf(Player)).Index;
			var enemyStructures = EnemyIntel.Where(i => i.Class == UnitClass.Structure).ToArray();
			if (enemyStructures.Length > 0)
			{
				var cell = World.Map.CellContaining(enemyStructures.Select(i => i.Actor.CenterPosition).Average());
				EnemyRegion = RegionOf(cell).Index;
			}
		}

		/// <summary>Appends a new event, dropping stale entries beyond the cap.</summary>
		public void AddEvent(string type, CPos? cell = null, string payload = null)
		{
			Events.Add(new CoalitionEvent(Tick, type, cell, payload));
			if (Events.Count > MaxEvents)
				Events.RemoveRange(0, Events.Count - MaxEvents);
		}
	}
}
