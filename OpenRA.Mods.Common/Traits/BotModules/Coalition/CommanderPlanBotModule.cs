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
using OpenRA.Mods.Common.Commander.Model;
using OpenRA.Mods.Common.Commander.Search;
using OpenRA.Mods.Common.Commander.Terrain;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	[Desc("Searches for a plan and commits to it. This is the rebuilt commander's decision layer:",
		"it replaces re-deriving a posture from the instantaneous army ratio every review, which is",
		"the defect that produced 38 draws - every attack makes that ratio worse before it makes it",
		"better, so the old logic recalled assaults at exactly the moment they start working.",
		"",
		"It does not execute anything. The existing controllers carry the plan out; only the",
		"decision changes.")]
	[TraitLocation(SystemActors.Player)]
	public sealed class CommanderPlanBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Whether the searched plan is allowed to drive the commander. Off means it is computed",
			"and logged but not acted on, which is how it gets measured against the old behaviour.")]
		public readonly bool Enabled = true;

		[Desc("Ticks between plan reviews. 375 is fifteen seconds.")]
		public readonly int ReviewInterval = 375;

		[Desc("Ticks a committed plan runs before it may be reconsidered at all.")]
		public readonly int CommitmentTicks = 1500;

		[Desc("Search node budget per review.")]
		public readonly int SearchIterations = 2000;

		[Desc("Fraction of launch strength below which a plan is abandoned. Deliberately low: an",
			"assault that has lost half its force and is standing in the enemy base is usually",
			"closer to winning than one that turned around.")]
		public readonly float MinimumForceFraction = 0.4f;

		[Desc("Locomotor whose passability defines the region graph.")]
		public readonly string Locomotor = "tracked";

		[Desc("Serialised win-probability model weights. Empty uses the hand-written defaults.")]
		public readonly string EvaluatorWeights = "";

		[Desc("Fraction of our own army value to assume the unseen enemy has. Without an assumption",
			"of this kind a blind commander believes it is winning, because nothing it can see is",
			"opposing it - measured at 0.93 win probability while having observed nothing at all.")]
		public readonly float UnseenEnemyFraction = 1f;

		[Desc("Ticks after which a region counts as unobserved for the purpose of hiding an army.")]
		public readonly int StaleVisibilityTicks = 750;

		public override object Create(ActorInitializer init) { return new CommanderPlanBotModule(this); }
	}

	public sealed class CommanderPlanBotModule : ConditionalTrait<CommanderPlanBotModuleInfo>, IBotTick
	{
		readonly CommanderPlanBotModuleInfo info;

		StateExtractor extractor;
		ForwardModel model;
		WinProbabilityModel evaluator;
		PuppetSearch search;
		EnemyBelief belief;
		StrategyPosterior posterior;
		readonly HashSet<string> reportedStructures = [];
		bool enemyBaseFound;
		RegionGraph graph;
		Map map;

		bool initialised;
		bool leader;
		float peakOwnArmy;
		float peakSeenEnemyArmy;
		float peakOwnBase;
		int lastReviewTick;
		int plansCommitted;
		int plansCompleted;
		float lastSearchValue;

		/// <summary>The plan currently being carried out, or null if none is committed.</summary>
		public Plan Current { get; private set; }

		/// <summary>Whether the searched plan is allowed to drive the commander.</summary>
		public bool Driving => !IsTraitDisabled && info.Enabled && Current != null && Current.IsActive;

		/// <summary>The map cell the current plan is aimed at, if it targets a place at all.</summary>
		public CPos? ObjectiveCell
		{
			get
			{
				if (!Driving || graph == null || map == null)
					return null;

				var region = Current.Objective.Region;
				if (region < 0 || region >= graph.Regions.Length)
					return null;

				var r = graph.Regions[region];
				return MapRegions.ToCell(map, r.CentreX, r.CentreY);
			}
		}

		/// <summary>
		/// <para>
		/// Where a scout is worth sending: the region carrying the most believed-but-unseen enemy.
		/// </para>
		/// <para>
		/// Measured, the old radial sweep sent forty scouts to map edges and the top row - 22,2 /
		/// 23,2 / 97,2 / 98,2 on a 127x127 map - and located the enemy base exactly zero times in a
		/// whole match. Every assault that followed therefore took empty ground. The belief state
		/// already knows which places are both stale and likely to be hiding something, which is
		/// precisely the question "where should I look" asks.
		/// </para>
		/// </summary>
		public CPos? ScoutTarget
		{
			get
			{
				if (IsTraitDisabled || belief == null || graph == null || map == null || !leader)
					return null;

				var region = belief.MostUncertainRegion(lastReviewTick);
				if (region < 0 || region >= graph.Regions.Length)
					return null;

				var r = graph.Regions[region];
				return MapRegions.ToCell(map, r.CentreX, r.CentreY);
			}
		}

		/// <summary>
		/// <para>
		/// How likely the opponent is to be going for air, and how confident that reading is.
		/// </para>
		/// <para>
		/// This is what an opponent model is for. Waiting to see an aircraft before building
		/// anti-air is waiting until the aircraft is overhead; an airfield sighted at four minutes
		/// says what is coming at six, and the counter takes time to build. The confidence term
		/// matters as much as the probability - acting on a posterior that is still nearly uniform
		/// is just superstition, so both have to clear a bar before anything changes.
		/// </para>
		/// </summary>
		public bool ExpectsEnemyAir =>
			!IsTraitDisabled && posterior != null
			&& posterior[OpponentStrategy.Air] > 0.35f
			&& posterior.Confidence() > 0.15f;

		/// <summary>The opponent model, for telemetry and for the search to plan against.</summary>
		public StrategyPosterior Opponent => posterior;

		/// <summary>What the plan wants done, for the executor to translate.</summary>
		public MacroVerb? Verb => Driving ? Current.Objective.Verb : null;

		public CommanderPlanBotModule(CommanderPlanBotModuleInfo info)
			: base(info)
		{
			this.info = info;
		}

		void IBotTick.BotTick(IBot bot)
		{
			if (IsTraitDisabled)
				return;

			var world = bot.Player.World;

			if (!initialised)
			{
				initialised = true;
				Initialise(world, bot.Player);
				return;
			}

			// One coalition, one plan. Every allied bot carries this trait, and letting each search
			// independently produced four different plans driving one shared command centre - which
			// showed up as reviews quadrupling from 53 to 213 in a four-bot match, each bot
			// countermanding the last. The coalition's whole premise is that it acts on a single
			// plan, so exactly one member decides it.
			if (!leader || extractor == null || world.WorldTick % info.ReviewInterval != 0)
				return;

			var enemies = Enemies(bot.Player).ToArray();
			var state = extractor.Extract(bot.Player, enemies);

			lastReviewTick = world.WorldTick;
			UpdateBelief(bot.Player, state, world.WorldTick);
			belief.ApplyTo(state.Enemy);

			ReviewAndPlan(world, state);
		}

		void Initialise(World world, Player player)
		{
			map = world.Map;

			// Chosen by name so every member of the team reaches the same answer without any
			// message passing, which is how the rest of the coalition already stays in step.
			leader = CoalitionLeader(player) == player;
			if (!leader)
				return;

			var locomotor = world.Map.Rules.Actors[SystemActors.World]
				.TraitInfos<LocomotorInfo>()
				.FirstOrDefault(l => l.Name == info.Locomotor);

			if (locomotor == null)
				return;

			graph = MapRegions.Build(world.Map, locomotor);
			if (graph.Regions.Length == 0)
				return;

			extractor = new StateExtractor(world, graph);
			model = new ForwardModel(graph, extractor.BuildRoleStats());
			evaluator = WinProbabilityModel.Deserialise(info.EvaluatorWeights);
			search = new PuppetSearch(model, evaluator, new PuppetSearch.Settings
			{
				Iterations = info.SearchIterations,
			});

			belief = new EnemyBelief(graph.Regions.Length, r => graph.Neighbours(r));
			posterior = new StrategyPosterior();

			CoalitionTelemetry.Log(world,
				$"Commander plan: {graph.Regions.Length} regions, {graph.Chokepoints.Length} chokepoints, " +
				$"driving={info.Enabled}");
		}

		/// <summary>
		/// Folds this review's observations into the belief. A visible region is recorded exactly -
		/// including when it is empty, which is the negative evidence that makes scouting worth
		/// paying for - and everything unseen is allowed to have moved.
		/// </summary>
		void UpdateBelief(Player self, AbstractState state, int tick)
		{
			belief.Propagate(info.ReviewInterval / (float)AbstractState.TicksPerSecond);

			for (var region = 0; region < graph.Regions.Length; region++)
			{
				var r = graph.Regions[region];
				var cell = MapRegions.ToCell(map, r.CentreX, r.CentreY);
				if (!self.Shroud.IsVisible(cell))
					continue;

				// The extracted state already respects fog, so what it holds for a visible region is
				// what is actually there - zero included.
				belief.Observe(region, state.Enemy.ForcesIn(region), tick, state.Enemy.StructuresIn(region));
			}

			ObserveOpponentTells(self, tick);

			// An opponent exists whether or not it has been seen, and it has to be somewhere nobody
			// has looked. This is a prior, not knowledge: it makes no claim about where they are
			// beyond "not where we have already looked", and it shrinks as the map is uncovered.
			//
			// Anchored to peaks rather than to current strength, and that distinction was measured.
			// Scaling the assumption to our *current* army means that losing our army also makes us
			// believe the enemy's has vanished - a commander whose position collapsed then rated
			// itself back at 0.79 because it had nothing left to compare against. Peaks do not move
			// backwards, so the estimate degrades gracefully instead of flattering a rout.
			peakOwnArmy = Math.Max(peakOwnArmy, state.Self.ArmyValue());
			peakSeenEnemyArmy = Math.Max(peakSeenEnemyArmy, belief.ExpectedTotal());

			var assumed = Math.Max(peakSeenEnemyArmy, peakOwnArmy * info.UnseenEnemyFraction);
			belief.AssumeUnseen(assumed, tick, info.StaleVisibilityTicks);

			// And they have a base. Without assuming one the evaluator sees nothing left to destroy,
			// so an assault appears to accomplish nothing - which is how a commander ends up rating
			// every position at 0.92 while never planning an attack.
			peakOwnBase = Math.Max(peakOwnBase, state.Self.BaseIntegrity);
			belief.AssumeUnseenStructures(peakOwnBase * info.UnseenEnemyFraction, tick, info.StaleVisibilityTicks);
		}

		/// <summary>
		/// Feeds the opponent model. Each enemy structure type counts once - seeing the same airfield
		/// twenty times is one piece of evidence, not twenty, and treating it as twenty would drive
		/// the posterior to certainty on a single observation.
		/// </summary>
		void ObserveOpponentTells(Player self, int tick)
		{
			// Phase C's gate, counted directly rather than inferred. "Did reconnaissance find the
			// enemy base" was previously answered by looking for a telemetry line that did not
			// exist, which is not an answer.
			if (!enemyBaseFound)
			{
				foreach (var actor in self.World.ActorsHavingTrait<Building>())
				{
					if (actor.Owner == null || actor.IsDead || !actor.IsInWorld
						|| actor.Owner == self || actor.Owner.IsAlliedWith(self) || actor.Owner.NonCombatant)
						continue;

					if (actor.Info.Name != "fact" || !self.Shroud.IsVisible(actor.Location))
						continue;

					enemyBaseFound = true;
					CoalitionTelemetry.Log(self.World,
						$"ENEMY BASE LOCATED at {actor.Location} after {tick / 25}s");
					break;
				}
			}

			foreach (var actor in self.World.ActorsHavingTrait<Building>())
			{
				if (actor.Owner == null || actor.IsDead || !actor.IsInWorld
					|| actor.Owner == self || actor.Owner.IsAlliedWith(self) || actor.Owner.NonCombatant)
					continue;

				if (!self.Shroud.IsVisible(actor.Location) || !reportedStructures.Add(actor.Info.Name))
					continue;

				posterior.Observe(StrategyPosterior.StructureLikelihood(CategoryOf(actor.Info.Name)));

				var (best, probability) = posterior.Best();
				CoalitionTelemetry.Log(self.World,
					$"Opponent tell: saw {actor.Info.Name} - now {best.ToString().ToLowerInvariant()} " +
					$"at {probability:P0} (confidence {posterior.Confidence():P0})");
			}
		}

		/// <summary>Maps an actor name to the tell it represents, from the mod's own naming.</summary>
		static string CategoryOf(string name)
		{
			return name switch
			{
				"barr" or "tent" or "kenn" => "barracks",
				"proc" => "refinery",
				"pbox" or "hbox" or "gun" or "ftur" or "tsla" or "agun" or "sam" => "defence",
				"dome" or "atek" or "stek" or "fix" => "tech",
				"afld" or "hpad" or "afld.ukraine" => "airfield",
				"spen" or "syrd" => "shipyard",
				_ => "unknown",
			};
		}

		void ReviewAndPlan(World world, AbstractState state)
		{
			var tick = world.WorldTick;

			if (Current != null && Current.IsActive)
			{
				var region = Current.Objective.Region;
				var strength = state.Self.ArmyValue();
				var objectiveStillExists = region < 0 || region >= state.RegionCount
					|| Current.Objective.Verb != MacroVerb.Attack
					|| state.Enemy.ArmyValueIn(region) > 0f
					|| state.Value[region] > 0f;

				// Only searched for an alternative once the commitment has run out; searching every
				// review and comparing would reintroduce the thrash this design exists to prevent.
				var alternative = -1f;
				if (tick >= Current.CommittedUntilTick)
					alternative = search.Search(state).BestValue;

				var status = Current.Review(tick, strength, state.Self.BaseIntegrity,
					objectiveStillExists, alternative);

				if (status == PlanStatus.Active)
					return;

				plansCompleted++;
				CoalitionTelemetry.Log(world,
					$"Plan ended: {Current.Objective} after {(tick - Current.StartTick) / 25}s, " +
					$"{status.ToString().ToLowerInvariant()}, expected {Current.ExpectedValue:F2}");

				Current = null;
			}

			// Nothing committed: choose something and commit to it.
			var result = search.Search(state);
			var value = result.BestValue;
			lastSearchValue = value;

			Current = new Plan
			{
				Objective = result.Best,
				StartTick = tick,
				CommittedUntilTick = tick + info.CommitmentTicks,
				LaunchStrength = state.Self.ArmyValue(),
				LaunchHomeIntegrity = state.Self.BaseIntegrity,
				ExpectedValue = value,
				MinimumForceFraction = info.MinimumForceFraction,
			};

			plansCommitted++;
			CoalitionTelemetry.Log(world,
				$"Plan committed: {result.Best} for {info.CommitmentTicks / 25}s " +
				$"(win probability {value:F2}, runner-up {result.RunnerUpValue:F2}, {result.NodesExpanded} nodes)");
		}

		/// <summary>Current estimate of winning, for telemetry.</summary>
		public float WinProbability => lastSearchValue;

		public int PlansCommitted => plansCommitted;

		public int PlansCompleted => plansCompleted;

		/// <summary>
		/// The one member of an alliance that decides the plan. Deterministic and agreed by every
		/// member independently: the lowest internal name among allied bots.
		/// </summary>
		static Player CoalitionLeader(Player self)
		{
			var leader = self;
			foreach (var player in self.World.Players)
			{
				if (player.NonCombatant || !player.IsBot || !player.IsAlliedWith(self))
					continue;

				if (string.CompareOrdinal(player.InternalName, leader.InternalName) < 0)
					leader = player;
			}

			return leader;
		}

		static IEnumerable<Player> Enemies(Player self)
		{
			foreach (var player in self.World.Players)
				if (!player.NonCombatant && player != self && !player.IsAlliedWith(self))
					yield return player;
		}
	}
}
