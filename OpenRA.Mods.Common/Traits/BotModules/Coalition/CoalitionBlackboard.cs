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
		SupportPowerRisk,
		ActiveCombat,
		Congestion
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

		/// <summary>Optional local posture for this region, overriding the global posture when set. StrategicPosture.None means use global.</summary>
		public StrategicPosture LocalPosture = StrategicPosture.None;

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
		public readonly Dictionary<string, int> ActivityCounts = [];
		public readonly float[] Capabilities = new float[Enum.GetValues<FriendlyCapability>().Length];
		public int TotalUnits;
		public float Strength;
		public float Readiness;
		public float Cohesion = 1f;
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

	/// <summary>One tracked enemy actor sighting with confidence decay and honesty status.</summary>
	public sealed class EnemyIntel
	{
		public readonly string Type;
		public readonly UnitClass Class;
		public CPos LastSeenCell;
		public int LastSeenTick;
		public float Confidence;
		public int MinCount;
		public int ExpectedCount;
		public int MaxCount;

		/// <summary>The honesty-ladder status of this sighting.</summary>
		public IntelStatus Status = IntelStatus.Observed;

		/// <summary>Ticks since the enemy was last observed.</summary>
		public int AgeTicks;

		/// <summary>Estimated position error in cells (0 when observed; grows for last-known mobile intel).</summary>
		public int PositionErrorCells;

		public EnemyIntel(Actor actor, UnitClass unitClass)
		{
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
		public float RaidResponseRate;
		public int RaidResponseSamples;
		public bool RespondsStronglyToFeints;
		public float FeintResponseRate;
		public int FeintResponseSamples;
		public bool MovesWholeArmyToDefend;
		public bool AttacksHarvesters;
		public int ExpansionCount;
		public float AverageExpansionTick;
		public int ExpansionSamples;

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

		/// <summary>
		/// Historical patterns only alter plans once the model is reliable. Below the threshold they
		/// remain context for reconnaissance, never a guaranteed prediction.
		/// </summary>
		public bool ShouldExploit(bool pattern, float minimumConfidence = 0.6f)
		{
			return pattern && Confidence >= minimumConfidence;
		}

		/// <summary>
		/// Derives a playstyle label from the scouted army and structure counts: a large army with few
		/// structures is pressing (rush), heavy structures without a matching army are turtling.
		/// </summary>
		public static string DerivePlaystyle(int army, int structures)
		{
			return army >= 8 && structures <= 2 ? "rush"
				: structures >= 5 && army <= structures ? "turtle"
				: "balanced";
		}

		/// <summary>
		/// Maps a scouted structure type to the enemy tech direction it reveals, or null when the
		/// structure does not indicate a direction (e.g. a barracks).
		/// </summary>
		public static string DerivePredictedBuild(string structureType)
		{
			return structureType switch
			{
				"afld" or "hpad" => "air",
				"spen" or "syrd" => "naval",
				"dome" or "atek" or "stek" => "tech",
				"weap" => "armor",
				_ => null
			};
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

		/// <summary>
		/// Committed forces grouped into one package per mission (req 26). A package may span several
		/// allied players: orders still go per-owner because OpenRA forbids anything else, but the
		/// commander assigns, inspects and scores the joint force as a single object.
		/// </summary>
		public IReadOnlyList<CoalitionForcePackage> ForcePackages => forcePackages ??= CoalitionForcePackage.Build(Forces);

		IReadOnlyList<CoalitionForcePackage> forcePackages;

		/// <summary>Drops the cached packaging after force assignments change.</summary>
		public void InvalidateForcePackages() { forcePackages = null; }

		/// <summary>
		/// Spatial control model (handbook §15.1). Rebuilt with the blackboard so the commander can
		/// answer *where* rather than only *what* - a region index cannot express that the enemy is
		/// thin on one flank and massed on the other.
		/// </summary>
		public InfluenceMap Influence { get; private set; }
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

		/// <summary>Number of refineries across the coalition.</summary>
		public int RefineryCount;

		/// <summary>Number of harvesters across the coalition.</summary>
		public int HarvesterCount;

		/// <summary>Number of harvesters currently active (not idle).</summary>
		public int ActiveHarvesterCount;

		/// <summary>Approximate remaining resource cells on the map.</summary>
		public int ResourceCellsRemaining;

		/// <summary>The region index of the coalition's average base position, or -1.</summary>
		public int HomeRegion = -1;

		/// <summary>The exact center cell of this commander's owned structures.</summary>
		public CPos HomeCell;

		/// <summary>The region index of the best-known enemy concentration, or -1.</summary>
		public int EnemyRegion = -1;

		/// <summary>
		/// A probable enemy base region inferred from the direction observed enemy forces arrive from,
		/// used only while no enemy structure has ever been seen. This is inference from observed
		/// contacts, never hidden state: it is what an attacking force's approach corridor tells you.
		/// Marked separately from <see cref="EnemyRegion"/> so a guess is never mistaken for a sighting.
		/// </summary>
		public int InferredEnemyRegion = -1;

		/// <summary>True when the offensive target is an inference rather than an observed position.</summary>
		public bool EnemyRegionIsInferred => EnemyRegion < 0 && InferredEnemyRegion >= 0;

		/// <summary>
		/// Shared counterattack gate: the tick of the last coalition-wide counterattack launch.
		/// Each bot checks this before firing its own counterattack so the coalition doesn't send
		/// N duplicate counterattack waves from N bots.
		/// </summary>
		public int LastCounterattackTick = int.MinValue;

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

		/// <summary>True in omniscient mode, where observation is complete and the fog-uncertainty floor is disabled.</summary>
		public readonly bool Omniscient;

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
			FrozenSet<string> antiAirTypes = null, FrozenSet<string> specialTypes = null,
			IEnumerable<EnemyIntel> seedIntel = null, bool omniscient = false)
		{
			World = world;
			Player = player;
			Team = team;
			Tick = world.WorldTick;
			Omniscient = omniscient;
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
			ExtractEconomyState();
			ExtractEnemyIntel(seedIntel, omniscient);
			ComputeHomeAndEnemyRegions();
			ComputeRegions();
			AddSuspectedIntel();
			ExtractEconomy();
			ComputeThreats();
			ComputeStrengths();
			BuildInfluenceMap();

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
			var positions = new Dictionary<string, List<WPos>>();
			foreach (var teamPlayer in Team)
			{
				groupByOwner[teamPlayer.InternalName] = new ForceGroup(teamPlayer.InternalName);
				positions[teamPlayer.InternalName] = [];
			}

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
					positions[owner] = [];
				}

				group.Counts[(int)unitClass]++;
				group.TotalUnits++;
				group.ByType[a.Info.Name] = group.ByType.GetValueOrDefault(a.Info.Name) + 1;
				var activity = a.CurrentActivity?.GetType().Name ?? "Idle";
				group.ActivityCounts[activity] = group.ActivityCounts.GetValueOrDefault(activity) + 1;
				foreach (var capability in CoalitionForceRegistry.FriendlyCapabilitiesFor(unitClass, a.Info.Name,
					artilleryTypes, submarineTypes, detectionTypes, transportTypes, scoutTypes, antiAirTypes, specialTypes))
					CoalitionForceRegistry.Record(capability, group.Capabilities);

				if (group.Status != ForceStatus.Moving && !a.IsIdle)
					group.Status = ForceStatus.Moving;

				if (unitClass != UnitClass.Structure)
					positions[owner].Add(a.CenterPosition);

				var health = a.TraitOrDefault<IHealth>();
				if (health != null)
					group.Strength += health.HP * 1f / health.MaxHP;
			}

			foreach (var group in groupByOwner.Values)
			{
				if (group.TotalUnits > 0)
					group.Strength /= group.TotalUnits;
				group.Cohesion = ComputeCohesion(positions[group.Owner]);
				group.Center = CenterOf(Team.First(t => t.InternalName == group.Owner));
				Forces.Add(group);
			}
		}

		/// <summary>0..1 cohesion: how tightly the force clusters around its own average position.</summary>
		public static float ComputeCohesion(List<WPos> positions)
		{
			if (positions.Count < 2)
				return 1f;

			var sumX = 0L;
			var sumY = 0L;
			var sumZ = 0L;
			foreach (var p in positions)
			{
				sumX += p.X;
				sumY += p.Y;
				sumZ += p.Z;
			}

			var center = new WPos((int)(sumX / positions.Count), (int)(sumY / positions.Count), (int)(sumZ / positions.Count));
			var sumDistance = 0L;
			foreach (var p in positions)
				sumDistance += (p - center).Length;

			// One cell is 1024 length units; cohesion halves around a ~30-cell spread.
			var averageDistance = sumDistance * 1f / positions.Count;
			return 1f / (1f + averageDistance / (30 * 1024f));
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

		/// <summary>
		/// Extracts the coalition's economy: refinery and harvester counts, active harvesters, and the
		/// approximate remaining resource cells, so expansion and raiding decisions can weigh resources.
		/// </summary>
		void ExtractEconomyState()
		{
			var teamIds = Team.Select(p => p.InternalName).ToHashSet();
			foreach (var a in World.Actors)
			{
				if (a.IsDead || !a.IsInWorld || !teamIds.Contains(a.Owner.InternalName))
					continue;

				if (a.Info.HasTraitInfo<RefineryInfo>())
					RefineryCount++;
				else if (a.Info.HasTraitInfo<HarvesterInfo>())
				{
					HarvesterCount++;
					if (!a.IsIdle)
						ActiveHarvesterCount++;
				}
			}

			var resourceLayer = World.WorldActor.TraitOrDefault<IResourceLayer>();
			if (resourceLayer != null)
			{
				foreach (var cell in World.Map.AllCells)
					if (resourceLayer.GetResource(cell).Type != null)
						ResourceCellsRemaining++;
			}
		}

		CPos CenterOf(Player p)
		{
			var structures = World.Actors.Where(a => a.IsInWorld && !a.IsDead && a.Owner == p && a.Info.HasTraitInfo<BuildingInfo>()).ToArray();
			if (structures.Length == 0)
				return p.HomeLocation;
			return World.Map.CellContaining(structures.Select(a => a.CenterPosition).Average());
		}

		void ExtractEnemyIntel(IEnumerable<EnemyIntel> seedIntel, bool omniscient)
		{
			// Preferred path: the commander's durable tracker supplies retained, status-tagged intel.
			if (seedIntel != null)
			{
				EnemyIntel.AddRange(seedIntel);
				return;
			}

			// Fallback (no tracker): fog-gated, observed-only extraction.
			foreach (var a in World.Actors)
			{
				if (a.IsDead || !a.IsInWorld || a.Owner == Player || a.OccupiesSpace == null)
					continue;
				if (Player.RelationshipWith(a.Owner) != PlayerRelationship.Enemy)
					continue;
				if (!omniscient && !Team.Any(ally => ally.Shroud.IsVisible(a.CenterPosition)))
					continue;

				EnemyIntel.Add(new EnemyIntel(a, classify(a))
				{
					LastSeenCell = a.Location,
					LastSeenTick = Tick,
					Status = IntelStatus.Observed
				});
			}
		}

		/// <summary>
		/// Marks unexplored regions adjacent to the known enemy base as SUSPECTED enemy presence —
		/// low confidence, no specific type, so they seed no combat threat but tell the commander
		/// where the enemy probably is and where to recon.
		/// </summary>
		void AddSuspectedIntel()
		{
			if (EnemyRegion < 0)
				return;

			foreach (var region in Regions)
			{
				if (region.FriendlyControl > 0)
					continue;
				if (!MapAnalysis.IsAdjacent(MovementClass.Ground, region.Index, EnemyRegion))
					continue;

				var center = new CPos((region.Bounds.Left + region.Bounds.Right) / 2, (region.Bounds.Top + region.Bounds.Bottom) / 2);
				EnemyIntel.Add(new EnemyIntel(string.Empty, UnitClass.Support)
				{
					LastSeenCell = center,
					LastSeenTick = Tick,
					Confidence = 0.2f,
					MinCount = 0,
					ExpectedCount = 0,
					MaxCount = 0,
					Status = IntelStatus.Suspected,
					PositionErrorCells = 16
				});
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
		/// with these. The THREAT_WEIGHT_SCALE environment variable (req 719) scales all threat values
		/// for self-play parameter sweeps.
		/// </summary>
		void ComputeThreats()
		{
			// Threat weight scale from env var (req 719): allows self-play sweeps to modulate
			// how much the AI weights enemy threats in route planning and target scoring.
			var threatScale = 1f;
			var envScale = Environment.GetEnvironmentVariable("THREAT_WEIGHT_SCALE");
			if (float.TryParse(envScale, out var parsed) && parsed > 0f)
				threatScale = parsed;

			foreach (var intel in EnemyIntel)
			{
				var region = RegionOf(intel.LastSeenCell);
				var threats = region.Threats;
				foreach (var capability in CapabilitiesFor(intel.Class, intel.Type, artilleryTypes, submarineTypes,
					detectionTypes, supportPowerStructures, productionStructures))
					threats[(int)capability] = Max(threats[(int)capability], ScaleThreat(intel.Confidence, threatScale));

				// Active combat: recently-observed enemy presence marks a region as a live combat zone.
				if (intel.Status == IntelStatus.Observed)
					threats[(int)CoalitionCapability.ActiveCombat] = Max(threats[(int)CoalitionCapability.ActiveCombat],
						ScaleThreat(intel.Confidence, threatScale));
			}

			// Exposure: regions with little friendly coverage are riskier to move through.
			foreach (var region in Regions)
				region.Threats[(int)CoalitionCapability.VisionExposure] = 1f - region.FriendlyControl;

			ComputeCongestion();
		}

		/// <summary>Applies a tuning scale while preserving the threat-field 0..1 contract.</summary>
		public static float ScaleThreat(float confidence, float scale)
		{
			return Math.Clamp(confidence * (scale > 0f ? scale : 1f), 0f, 1f);
		}

		/// <summary>
		/// Congestion: how densely the coalition's own combat units are packed into each region,
		/// normalized to 0..1. Overloaded corridors are slower and riskier to route through.
		/// </summary>
		void ComputeCongestion()
		{
			var teamIds = Team.Select(p => p.InternalName).ToHashSet();
			var counts = new int[Regions.Length];
			foreach (var a in World.Actors)
			{
				if (a.IsDead || !a.IsInWorld || a.OccupiesSpace == null || !teamIds.Contains(a.Owner.InternalName))
					continue;
				if (a.Info.HasTraitInfo<BuildingInfo>())
					continue;

				counts[RegionOf(a.Location).Index]++;
			}

			var max = counts.DefaultIfEmpty(0).Max();
			foreach (var region in Regions)
				region.Threats[(int)CoalitionCapability.Congestion] = max == 0 ? 0f : counts[region.Index] * 1f / max;
		}

		static float Max(float a, float b)
		{
			return a > b ? a : b;
		}

		void ComputeStrengths()
		{
			// Coalition strength = class-weighted combat power of MOBILE units (structures excluded to
			// match the enemy side, which never counts buildings). Health is deliberately not applied
			// here: the enemy side (IntelPower) has no health estimate, so discounting our own strength
			// while treating the enemy as full-health would make the commander bail on even fights.
			foreach (var force in Forces)
			{
				var power = 0f;
				for (var c = 0; c < force.Counts.Length; c++)
					if (force.Counts[c] > 0 && (UnitClass)c != UnitClass.Structure)
						power += CombatEstimator.ClassWeight((UnitClass)c) * force.Counts[c];

				CoalitionArmyStrength += force.TotalUnits > 0 ? power : 0f;
				force.Readiness = force.TotalUnits > 0 ? force.Strength * force.Cohesion : 0f;
			}

			// Enemy strength = class-weighted power of units visible RIGHT NOW, discounted by confidence.
			// Only Observed entries count toward the aggregate: the intel tracker dedupes by
			// (type, class, region), so a unit that moves across a region boundary would otherwise be
			// counted twice (Observed in its new region, LastKnown in the old), and dead units would
			// linger in the estimate for the memory window. Both inflate the enemy to ~2x the coalition
			// and make the commander bail on even fights as "outmatched". Hidden enemy strength is
			// handled by the fog-uncertainty floor below instead. Structures are excluded to match
			// ForcePower (which excludes buildings), so a predicted win ratio compares like-with-like.
			var confirmed = EnemyIntel.Where(i => i.Status == IntelStatus.Observed && i.Class != UnitClass.Structure).ToArray();
			EnemyArmyCount = confirmed.Length;
			EnemyArmyStrength = confirmed.Sum(CombatEstimator.IntelPower);

			// Fog uncertainty: under fair fog the observed enemy strength is a lower bound, because the
			// coalition only sees the part of the map it has explored. Assume the unexplored part could
			// hold an army at least as strong as the coalition's own, scaled by the unexplored fraction.
			// This keeps the commander honest (no 8x "advantage" over an enemy it has barely scouted) so
			// it builds a balanced force and commits properly instead of over-reaching. Skipped in
			// omniscient mode, where observation is already complete.
			if (!Omniscient)
			{
				var mapCells = World.Map.MapSize.Width * World.Map.MapSize.Height;
				var exploredFraction = mapCells > 0
					? Math.Clamp(Team.Max(ally => ally.Shroud.RevealedCells) * 1f / mapCells, 0f, 1f)
					: 0f;
				EnemyArmyStrength = Math.Max(EnemyArmyStrength, CoalitionArmyStrength * (1f - exploredFraction));
			}
		}

		/// <summary>
		/// Deposits every own unit and every observed enemy sighting into the influence grid.
		/// Reach scales with combat value: a tank projects control further than a rifleman, not
		/// because it sees further but because it can contest more ground.
		/// </summary>
		void BuildInfluenceMap()
		{
			var map = new InfluenceMap(World.Map.MapSize.Width, World.Map.MapSize.Height);
			var teamIds = Team.Select(p => p.InternalName).ToHashSet();

			foreach (var actor in World.Actors)
			{
				if (actor.IsDead || !actor.IsInWorld || actor.OccupiesSpace == null)
					continue;

				if (!teamIds.Contains(actor.Owner.InternalName))
					continue;

				var unitClass = classify(actor);
				var strength = CombatEstimator.ClassWeight(unitClass);
				if (strength <= 0f)
					continue;

				map.Add(new InfluenceSource(actor.Location.X, actor.Location.Y, strength,
					ReachFor(unitClass), IsOwn: true));
			}

			// Fair fog: only what the coalition has actually seen contributes, and a stale sighting
			// contributes less because confidence has decayed.
			foreach (var intel in EnemyIntel)
			{
				var strength = CombatEstimator.IntelPower(intel);
				if (strength <= 0f)
					continue;

				map.Add(new InfluenceSource(intel.LastSeenCell.X, intel.LastSeenCell.Y, strength,
					ReachFor(intel.Class), IsOwn: false));
			}

			Influence = map;
		}

		/// <summary>How far a unit class projects control, in cells.</summary>
		static int ReachFor(UnitClass unitClass)
		{
			return unitClass switch
			{
				UnitClass.Air => 12,
				UnitClass.Naval => 10,
				UnitClass.Armor => 8,
				UnitClass.Structure => 6,
				_ => 5
			};
		}

		void ComputeHomeAndEnemyRegions()
		{
			HomeCell = CenterOf(Player);
			HomeRegion = RegionOf(HomeCell).Index;
			var enemyStructures = EnemyIntel.Where(i => i.Class == UnitClass.Structure).ToArray();
			if (enemyStructures.Length > 0)
			{
				var cell = new CPos(
					(int)enemyStructures.Average(i => i.LastSeenCell.X),
					(int)enemyStructures.Average(i => i.LastSeenCell.Y));
				EnemyRegion = RegionOf(cell).Index;
				return;
			}

			// No enemy structure has ever been seen. Without an objective the coalition spends the
			// whole match reacting - out-trading the enemy while never threatening it - so infer the
			// most likely base from public map metadata: an unexplored starting location, preferring
			// the one nearest the direction enemy forces actually arrive from. Starting locations are
			// map data, not hidden player state, and the occupant is never read; this only says where
			// a base could be, which is exactly what a commander reasons from before scouting.
			var spawns = StartingLocations();
			if (spawns.Length == 0)
				return;

			var mobile = EnemyIntel.Where(i => i.Class != UnitClass.Structure).ToArray();
			var approach = mobile.Length == 0 ? (CPos?)null : new CPos(
				(int)mobile.Average(i => i.LastSeenCell.X),
				(int)mobile.Average(i => i.LastSeenCell.Y));

			var candidate = InferEnemyBaseCell(spawns, HomeCell, approach,
				cell => Team.Any(ally => ally.Shroud.IsExplored(cell)));

			if (candidate != null)
				InferredEnemyRegion = RegionOf(candidate.Value).Index;
		}

		/// <summary>Public starting locations declared by the map.</summary>
		CPos[] StartingLocations()
		{
			return World.Map.ActorDefinitions
				.Where(n => n.Value.Value == "mpspawn")
				.Select(n => new ActorReference(n.Key, n.Value).GetValue<LocationInit, CPos>())
				.ToArray();
		}

		/// <summary>
		/// Picks the most likely enemy base from the map's starting locations. Explored spawns are
		/// ruled out - the coalition has looked there and found no base - and of the rest the one
		/// closest to the axis enemy forces arrive along is preferred, falling back to the most
		/// distant spawn when there has been no contact at all. Pure, so it is testable without a World.
		/// </summary>
		public static CPos? InferEnemyBaseCell(CPos[] spawns, CPos home, CPos? approach, Func<CPos, bool> isExplored)
		{
			var candidates = spawns
				.Where(s => s != home && !(isExplored?.Invoke(s) ?? false))
				.ToArray();

			if (candidates.Length == 0)
				return null;

			// Deterministic ordering: every allied bot must infer the identical objective.
			if (approach == null)
				return candidates
					.OrderByDescending(s => (s - home).LengthSquared)
					.ThenBy(s => s.Y).ThenBy(s => s.X)
					.First();

			return candidates
				.OrderBy(s => (s - approach.Value).LengthSquared)
				.ThenBy(s => s.Y).ThenBy(s => s.X)
				.First();
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
