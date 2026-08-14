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

	/// <summary>An aggregated force: per-owner counts by class, per-type composition, capability profile, and activity.</summary>
	public sealed class ForceGroup
	{
		public readonly string Owner;
		public readonly int[] Counts = new int[Enum.GetValues<UnitClass>().Length];
		public readonly Dictionary<string, int> ByType = [];
		public readonly float[] Capabilities = new float[Enum.GetValues<FriendlyCapability>().Length];
		public int TotalUnits;
		public float Strength;
		public float Readiness;
		public CPos Center;

		/// <summary>Coarse movement state: idle when every member is idle, moving otherwise.</summary>
		public ForceStatus Status = ForceStatus.Idle;

		/// <summary>The mission this force is assigned to, set by the order arbiter.</summary>
		public string MissionId;

		/// <summary>The operational role the commander assigned to this force (main/escort/naval/defend).</summary>
		public string Role;

		/// <summary>0..1 fraction of the peak unit count lost, tracked across blackboard rebuilds.</summary>
		public float CasualtyFraction;

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

		/// <summary>Test-friendly constructor: plain intel without an actor reference.</summary>
		public EnemyIntel(string type, UnitClass unitClass)
		{
			Type = type;
			Class = unitClass;
			Confidence = 1f;
			MinCount = ExpectedCount = MaxCount = 1;
		}
	}

	/// <summary>A behavioral profile of the enemy, built from observed patterns.</summary>
	public sealed class OpponentModel
	{
		public float ArmorBias;
		public float InfantryBias;
		public float AirBias;
		public float NavalBias;
		public float StaticDefenseBias;
		public int PreferredAttackLane = -1;
		public float AverageResponseTime;
		public int ResponseSamples;
		public bool RespondsStronglyToRaids;
		public bool MovesWholeArmyToDefend;
		public bool AttacksHarvesters;
		public int ExpansionCount;

		/// <summary>0..1 confidence in the profile: more observations make the model more reliable.</summary>
		public float Confidence;

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
		public readonly List<SpecialAsset> SpecialAssets = [];
		public readonly List<SpecialAsset> Transports = [];
		public readonly List<ProductionFacility> Facilities = [];
		public readonly List<EnemyIntel> EnemyIntel = [];
		public readonly List<CoalitionEvent> Events = [];
		public readonly OpponentModel Opponent = new();

		/// <summary>Static terrain analysis of the map: region graph, chokepoints, components, resources.</summary>
		public readonly CoalitionMapAnalysis MapAnalysis;

		public int CoalitionCash;
		public float CoalitionArmyStrength;
		public float EnemyArmyStrength;
		public float EnemyArmyCount;

		/// <summary>Coalition power production, in engine power units.</summary>
		public int PowerProvided;

		/// <summary>Coalition power consumption, in engine power units.</summary>
		public int PowerDrained;

		/// <summary>Power surplus (negative = deficit), in engine power units.</summary>
		public int PowerExcess => PowerProvided - PowerDrained;

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
		readonly FrozenSet<string> artilleryTypes;
		readonly FrozenSet<string> submarineTypes;
		readonly FrozenSet<string> detectionTypes;
		readonly FrozenSet<string> transportTypes;
		readonly FrozenSet<string> scoutTypes;
		readonly FrozenSet<string> antiAirTypes;
		readonly FrozenSet<string> specialTypes;
		readonly FrozenSet<string> supportPowerStructures;
		readonly FrozenSet<string> productionStructures;

		public CoalitionBlackboard(World world, Player player, Player[] team, Func<Actor, UnitClass> classify,
			FrozenSet<string> waterTerrainTypes = null, int bigWaterMinimumCells = 0,
			FrozenSet<string> valuableResourceTypes = null, FrozenSet<string> artilleryTypes = null,
			FrozenSet<string> submarineTypes = null, FrozenSet<string> detectionTypes = null,
			FrozenSet<string> supportPowerStructures = null, FrozenSet<string> productionStructures = null,
			FrozenSet<string> transportTypes = null, FrozenSet<string> scoutTypes = null,
			FrozenSet<string> antiAirTypes = null, FrozenSet<string> specialTypes = null)
		{
			World = world;
			Player = player;
			Team = team;
			Tick = world.WorldTick;
			this.classify = classify;
			this.artilleryTypes = artilleryTypes ?? new HashSet<string> { "arty", "v2rl" }.ToFrozenSet();
			this.submarineTypes = submarineTypes ?? new HashSet<string> { "ss", "msub" }.ToFrozenSet();
			this.detectionTypes = detectionTypes ?? new HashSet<string> { "dog", "rdr" }.ToFrozenSet();
			this.transportTypes = transportTypes ?? [];
			this.scoutTypes = scoutTypes ?? [];
			this.antiAirTypes = antiAirTypes ?? [];
			this.specialTypes = specialTypes ?? [];
			this.supportPowerStructures = supportPowerStructures ?? new HashSet<string> { "iron", "pdox" }.ToFrozenSet();
			this.productionStructures = productionStructures ?? new HashSet<string>
			{
				"weap", "afld", "hpad", "spen", "syrd", "barr", "tent", "fact", "atek", "stek", "dome"
			}.ToFrozenSet();

			// Static terrain analysis: region graph, chokepoints, components, resources. Cached per map.
			MapAnalysis = CoalitionMapAnalysis.ForMap(world, waterTerrainTypes ?? new HashSet<string> { "Water" }.ToFrozenSet(),
				valuableResourceTypes ?? new HashSet<string> { "Ore", "Gems" }.ToFrozenSet());

			Regions = MapAnalysis.Regions;
			ExtractForces();
			ExtractSpecialAssets();
			ExtractProduction();
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
				group.ByType[a.Info.Name] = group.ByType.GetValueOrDefault(a.Info.Name) + 1;
				foreach (var capability in CoalitionForceRegistry.FriendlyCapabilitiesFor(unitClass, a.Info.Name,
					artilleryTypes, submarineTypes, detectionTypes, transportTypes, scoutTypes, antiAirTypes))
					CoalitionForceRegistry.Record(capability, group.Capabilities);

				if (group.Status != ForceStatus.Moving && !a.IsIdle)
					group.Status = ForceStatus.Moving;

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

		/// <summary>
		/// Registers scarce special assets (Tanya, spies, engineers) and transports individually, with
		/// position and cargo, so the commander can track and assign them without scanning the world.
		/// </summary>
		void ExtractSpecialAssets()
		{
			var teamIds = Team.Select(p => p.InternalName).ToHashSet();
			foreach (var a in World.Actors)
			{
				if (a.IsDead || !a.IsInWorld || !teamIds.Contains(a.Owner.InternalName))
					continue;
				if (a.OccupiesSpace == null)
					continue;

				var isTransport = transportTypes.Contains(a.Info.Name);
				var isSpecial = specialTypes.Contains(a.Info.Name);
				if (!isTransport && !isSpecial)
					continue;

				var cargo = a.TraitOrDefault<Cargo>()?.PassengerCount ?? 0;
				var asset = new SpecialAsset(a.Owner.InternalName, a.Info.Name, a.Location, cargo);
				if (isTransport)
					Transports.Add(asset);
				if (isSpecial)
					SpecialAssets.Add(asset);
			}
		}

		/// <summary>
		/// Extracts the coalition's live production state: every facility's current item, queued items,
		/// what it can build right now (prerequisites satisfied), and progress; plus the coalition power
		/// balance from each player's power manager.
		/// </summary>
		void ExtractProduction()
		{
			foreach (var p in Team)
			{
				var power = p.PlayerActor.TraitOrDefault<PowerManager>();
				if (power != null)
				{
					PowerProvided += power.PowerProvided;
					PowerDrained += power.PowerDrained;
				}

				foreach (var queue in p.PlayerActor.TraitsImplementing<ProductionQueue>())
				{
					if (!queue.Enabled)
						continue;

					var current = queue.CurrentItem();
					var facilityActor = queue.Actor;
					var cell = facilityActor.OccupiesSpace != null && facilityActor.IsInWorld
						? facilityActor.Location
						: p.HomeLocation;

					Facilities.Add(new ProductionFacility(p.InternalName, queue.Info.Type, facilityActor.Info.Name, cell)
					{
						Current = current?.Item,
						Queued = queue.AllQueued().Select(i => i.Item).Where(i => i != current?.Item).ToArray(),
						Buildable = queue.BuildableItems().Select(i => i.Name).ToArray(),
						ProgressPercent = ProgressOf(current)
					});
				}
			}
		}

		static int ProgressOf(ProductionItem item)
		{
			if (item == null || item.TotalTime <= 0)
				return 0;
			return (int)(100L * (item.TotalTime - item.RemainingTime) / item.TotalTime);
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

		/// <summary>0..1 fraction of allied support powers that are ready to fire, for planning.</summary>
		public float SupportPowerReadiness;

		/// <summary>True when the coalition holds at least one ready strategic superweapon.</summary>
		public bool HasReadySuperweapon;

		// Deception record: copied in from the command center's durable mission manager each rebuild,
		// so the planner and the LLM snapshot see how well feints and baits have drawn enemy responses.
		public int DeceptionAttempts;
		public int DeceptionSuccesses;
		public int DeceptionEnemiesDrawn;

		/// <summary>0..1: how often deception drew a measurable enemy response (0 when never attempted).</summary>
		public float DeceptionEffectiveness => Effectiveness(DeceptionAttempts, DeceptionSuccesses);

		/// <summary>Pure effectiveness formula so it can be unit-tested without a World.</summary>
		public static float Effectiveness(int attempts, int successes)
		{
			return attempts == 0 ? 0f : successes * 1f / attempts;
		}

		void ExtractEconomy()
		{
			foreach (var p in Team)
				CoalitionCash += p.PlayerActor.TraitOrDefault<PlayerResources>()?.GetCashAndResources() ?? 0;

			// Support-power readiness: count ready powers across the team, weighted by whether
			// they are strategic (superweapon-like) or tactical.
			var ready = 0;
			var total = 0;
			var readySuperweapon = false;
			foreach (var p in Team)
			{
				var manager = p.PlayerActor.TraitOrDefault<SupportPowerManager>();
				if (manager == null)
					continue;

				foreach (var kv in manager.Powers)
				{
					total++;
					if (!kv.Value.Ready)
						continue;

					ready++;
					if (supportPowerStructures.Count == 0)
						continue;

					// Approximate superweapons by the structures that grant them.
					readySuperweapon |= World.Actors.Any(a => a.IsInWorld && !a.IsDead && a.Owner == p
						&& supportPowerStructures.Contains(a.Info.Name));
				}
			}

			SupportPowerReadiness = total == 0 ? 0f : ready * 1f / total;
			HasReadySuperweapon = readySuperweapon;
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
		/// Maps a scouted enemy actor onto its threat capabilities. Pure and deterministic so it can
		/// be unit-tested without a World: given the type lists and the actor's class, returns the set
		/// of <see cref="CoalitionCapability"/> values the actor seeds.
		/// </summary>
		public static IEnumerable<CoalitionCapability> CapabilitiesFor(UnitClass unitClass, string type,
			FrozenSet<string> artilleryTypes, FrozenSet<string> submarineTypes, FrozenSet<string> detectionTypes,
			FrozenSet<string> supportPowerStructures, FrozenSet<string> productionStructures)
		{
			switch (unitClass)
			{
				case UnitClass.Air:
					yield return CoalitionCapability.AntiAir;
					yield return CoalitionCapability.AirToAir;
					break;
				case UnitClass.Armor:
					yield return CoalitionCapability.GroundAntiArmor;
					break;
				case UnitClass.Infantry:
					yield return CoalitionCapability.GroundAntiInfantry;
					break;
				case UnitClass.Naval:
					yield return CoalitionCapability.Naval;
					break;
				case UnitClass.Structure:
					yield return CoalitionCapability.StaticDefense;
					break;
			}

			if (artilleryTypes.Contains(type))
				yield return CoalitionCapability.Artillery;
			if (submarineTypes.Contains(type))
				yield return CoalitionCapability.Submarine;
			if (detectionTypes.Contains(type))
				yield return CoalitionCapability.Detection;
			if (unitClass == UnitClass.Structure && productionStructures.Contains(type))
				yield return CoalitionCapability.Reinforcement;
			if (unitClass == UnitClass.Structure && supportPowerStructures.Contains(type))
				yield return CoalitionCapability.SupportPowerRisk;
		}

		/// <summary>
		/// Independent per-region threat fields per combat capability, derived deterministically from
		/// enemy intel classes and type lists. The LLM and mission planners weight routes and targets
		/// with these.
		/// </summary>
		void ComputeThreats()
		{
			foreach (var intel in EnemyIntel)
			{
				var region = RegionOf(intel.LastSeenCell);
				var threats = region.Threats;
				foreach (var capability in CapabilitiesFor(intel.Class, intel.Type, artilleryTypes, submarineTypes,
					detectionTypes, supportPowerStructures, productionStructures))
					threats[(int)capability] = Max(threats[(int)capability], intel.Confidence);
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
