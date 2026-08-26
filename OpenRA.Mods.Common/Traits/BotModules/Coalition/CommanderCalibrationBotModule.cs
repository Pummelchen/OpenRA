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
using System.IO;
using System.Linq;
using OpenRA.Mods.Common.Commander.Model;
using OpenRA.Mods.Common.Commander.Terrain;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	[Desc("Scores the commander's forward model against what actually happens, and reports the",
		"result at the end of the match. This is the phase 2 gate of the rebuild: a model whose",
		"predictions nobody checks is a model that lies to the search, and the search cannot tell.",
		"",
		"Purely observational - it issues no orders and changes no behaviour.")]
	[TraitLocation(SystemActors.Player)]
	public sealed class CommanderCalibrationBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Ticks between predictions.")]
		public readonly int SampleInterval = 250;

		[Desc("How far ahead to predict, in ticks. 750 is the 30 seconds the gate is stated over.")]
		public readonly int Horizon = 750;

		[Desc("Relative error at or below which the model is considered calibrated.")]
		public readonly float Threshold = 0.15f;

		[Desc("Log every settled prediction against its actual, for diagnosing bias.")]
		public readonly bool Trace = false;

		[Desc("Locomotor whose passability defines the region graph.")]
		public readonly string Locomotor = "tracked";

		[Desc("File the feature vectors are appended to when the match ends, for fitting the",
			"win-probability model. Relative paths resolve against the support directory. Empty",
			"disables logging.")]
		public readonly string TrainingLog = "";

		[Desc("File to append the full position to, as JSON lines: every actor on record, every",
			"region, and the globals. Unlike TrainingLog's nine scalars this is close to raw, so a",
			"model can learn its own features instead of inheriting somebody's. Empty disables it.")]
		public readonly string StateLog = "";

		public override object Create(ActorInitializer init) { return new CommanderCalibrationBotModule(this); }
	}

	public sealed class CommanderCalibrationBotModule : ConditionalTrait<CommanderCalibrationBotModuleInfo>,
		IBotTick, INotifyWinStateChanged
	{
		readonly CommanderCalibrationBotModuleInfo info;
		readonly PredictionCalibration calibration = new();

		StateExtractor extractor;
		ForwardModel model;
		EconomyEstimator economy;
		ForwardModel.Parameters parameters;
		float[] atPrediction;
		float previousArmyValue;
		float measuredIncome;
		float smoothedSpend;
		readonly Queue<(int Tick, int Earned)> earnedHistory = new();
		readonly List<(int Tick, float[] Features)> trainingSamples = [];
		WinProbabilityModel evaluator;
		Player owner;
		CommanderStaffBotModule staff;

		/// <summary>The most recent abstract state, for anything that needs the position as the
		/// calibrator saw it rather than rebuilding one of its own.</summary>
		public AbstractState LatestState { get; private set; }
		readonly List<StateExport.Sample> stateSamples = [];
		bool logged;
		bool subscribed;
		float armyGrowth;
		int previousEarned;
		int previousSpent;
		int previousSampleTick;
		bool initialised;
		int lastReportedAt;

		public CommanderCalibrationBotModule(CommanderCalibrationBotModuleInfo info)
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

				var locomotor = world.Map.Rules.Actors[SystemActors.World]
					.TraitInfos<LocomotorInfo>()
					.FirstOrDefault(l => l.Name == info.Locomotor);

				if (locomotor == null)
					return;

				var graph = MapRegions.Build(world.Map, locomotor);
				if (graph.Regions.Length == 0)
					return;

				extractor = new StateExtractor(world, graph);

				parameters = new ForwardModel.Parameters();
				economy = new EconomyEstimator();
				model = new ForwardModel(graph, extractor.BuildRoleStats(), parameters);
				evaluator = WinProbabilityModel.Default();
				owner = bot.Player;

				// Most simulated matches end at the tick limit, where no win state is ever set and
				// INotifyWinStateChanged never fires. Learning only from the quarter of games that
				// end decisively would train the model on precisely the biased quarter.
				if (!string.IsNullOrEmpty(info.TrainingLog) && !subscribed)
				{
					subscribed = true;
					HeadlessSkirmish.Ending += OnSimulationEnding;
				}

				CoalitionTelemetry.Log(world,
					$"Calibration: {graph.Regions.Length} regions, {graph.Chokepoints.Length} chokepoints, " +
					$"horizon {info.Horizon} ticks");

				if (info.Trace)
					calibration.Diagnostic = line => CoalitionTelemetry.Log(world, "Calib-trace " + line);
			}

			if (extractor == null || model == null)
				return;

			if (world.WorldTick % info.SampleInterval != 0)
				return;

			// Measure income and spending before extracting, so the state carries observed rates
			// rather than assumptions. Both are exact: Earned and Spent are running totals.
			var resources = bot.Player.PlayerActor.TraitOrDefault<PlayerResources>();
			var earned = resources?.Earned ?? 0;
			var spent = resources?.Spent ?? 0;
			var elapsed = (world.WorldTick - previousSampleTick) / (float)AbstractState.TicksPerSecond;

			if (previousSampleTick > 0 && elapsed > 0f)
			{
				measuredIncome = Math.Max(0, earned - previousEarned) / elapsed;

				// Smoothed, because a single sample of a rate is not a rate. Deliveries and
				// purchases arrive in lumps, and feeding the lumps straight into a thirty-second
				// forecast turns measurement noise into prediction error.
				var spendSample = Math.Max(0, spent - previousSpent) / elapsed;
				smoothedSpend = smoothedSpend <= 0f ? spendSample : (smoothedSpend * 0.7f) + (spendSample * 0.3f);
				extractor.ObservedSpendRate = smoothedSpend;

				economy.Observe(CountOwn<HarvesterInfo>(world, bot.Player), Math.Max(0, earned - previousEarned), elapsed);

				// Income over a trailing window the same length as the horizon. An exponential
				// average was tried and lost to a plain ten-second spot reading, twice: smoothing
				// trades variance for lag, and against a rising income lag costs more than noise
				// does. A window matching the horizon has neither problem - it is the same quantity
				// being forecast, measured over the same span, just shifted back in time.
				earnedHistory.Enqueue((world.WorldTick, earned));
				while (earnedHistory.Count > 1 && world.WorldTick - earnedHistory.Peek().Tick > info.Horizon)
					earnedHistory.Dequeue();

				var oldest = earnedHistory.Peek();
				var windowSeconds = (world.WorldTick - oldest.Tick) / (float)AbstractState.TicksPerSecond;
				extractor.ObservedIncomePerSecond = windowSeconds > 0f
					? Math.Max(0, earned - oldest.Earned) / windowSeconds
					: measuredIncome;
			}

			previousEarned = earned;
			previousSpent = spent;
			previousSampleTick = world.WorldTick;

			var enemies = Enemies(bot.Player).ToArray();
			var state = extractor.Extract(bot.Player, enemies);
			LatestState = state;

			// Net army growth: the one army quantity that is actually measurable under fog.
			// Production alone is not - total spending includes refineries and harvesters that never
			// join the army - and losses out of vision are not observable at all.
			var armyNow = state.Self.ArmyValue();
			if (previousArmyValue > 0f && elapsed > 0f)
			{
				var sample = (armyNow - previousArmyValue) / elapsed;
				armyGrowth = armyGrowth == 0f ? sample : (armyGrowth * 0.7f) + (sample * 0.3f);
			}

			extractor.ObservedArmyGrowthPerSecond = armyGrowth;
			state.Self.ArmyGrowthPerSecond = armyGrowth;

			previousArmyValue = armyNow;
			// Actual income is what was earned, not what the model says would have been earned.
			var now = PredictionCalibration.Measure(state, measuredIncome);

			// Judge whatever has come due, against the snapshot taken when it was predicted.
			calibration.Settle(world.WorldTick, now, earned,
				info.Horizon / (float)AbstractState.TicksPerSecond);

			atPrediction = now;

			// Record what this position looked like. It is labelled when the match ends, with the
			// result of the match - so the opening of a won game counts as a win even though it was
			// even at the time. That is the point: it is how the model learns which early
			// advantages actually convert.
			if (!string.IsNullOrEmpty(info.TrainingLog))
				trainingSamples.Add((world.WorldTick, StateFeatures.Extract(state, model)));

			// The same position, exported whole. Sampled on the same clock and labelled by the same
			// result, so the two logs are directly comparable - which is the point: the question
			// stage one has to answer is whether the full position predicts better than the nine
			// scalars, and that is only a fair question if nothing else differs.
			if (!string.IsNullOrEmpty(info.StateLog))
			{
				staff ??= owner.PlayerActor.TraitsImplementing<CommanderStaffBotModule>()
					.FirstOrDefault(m => !m.IsTraitDisabled);

				var database = staff?.Database;
				if (database != null)
				{
					var purse = owner.PlayerActor.TraitOrDefault<PlayerResources>();
					var idle = owner.PlayerActor.TraitsImplementing<ProductionQueue>()
						.Count(q => q.Enabled && q.CurrentItem() == null);

					// The chief's standing order at this moment - the macro-action a policy head
					// will be trained to imitate first and improve on later.
					var directive = staff.CurrentDirective;
					var action = directive == null
						? new[] { -1f, -1f, -1f, 1f }
						: new[]
						{
							(float)(int)directive.Stance,
							directive.MainEffortRegion ?? -1,
							directive.ReserveFraction,

							// How likely the behaviour policy was to take this stance here. The
							// fourth field, so older logs without it still parse.
							staff.LastPropensity,
						};

					stateSamples.Add(StateExport.Capture(
						database, state, database.Catalogue, world.WorldTick,
						purse?.GetCashAndResources() ?? 0,
						purse?.Earned ?? 0,
						purse?.Spent ?? 0,
						idle, action));
				}
			}

			// Predict the horizon under the plan both sides are most likely to be following:
			// keep producing, hold ground. A forward model that is only accurate when told the
			// future is not a forward model.
			var seconds = info.Horizon / (float)AbstractState.TicksPerSecond;
			var predicted = model.Step(state,
				new MacroAction(MacroVerb.Produce, 0),
				new MacroAction(MacroVerb.Produce, 0), seconds);

			calibration.Predict(world.WorldTick + info.Horizon,
				PredictionCalibration.Measure(predicted, model.IncomePerSecond(predicted.Self)), earned, now);

			// Report periodically so a run that never ends still yields a measurement.
			// Reported cumulatively on a cadence, so a run that never reaches a natural end still
			// yields a measurement, and the last report of a long run covers the whole match.
			if (calibration.Scored >= 20 && calibration.Scored % 20 == 0 && calibration.Scored != lastReportedAt)
			{
				lastReportedAt = calibration.Scored;
				foreach (var line in calibration.Report(info.Threshold))
					CoalitionTelemetry.Log(world, "Calibration " + line);
			}
		}

		static int CountOwn<T>(World world, Player player) where T : TraitInfo
		{
			var count = 0;
			foreach (var actor in world.ActorsHavingTrait<IOccupySpace>())
				if (actor.Owner == player && !actor.IsDead && actor.IsInWorld && actor.Info.HasTraitInfo<T>())
					count++;

			return count;
		}

		void OnSimulationEnding(World endingWorld)
		{
			// The event is static, so it reaches every module in the process including any left over
			// from a previous run. Only respond for our own world.
			if (owner == null || endingWorld != owner.World)
				return;

			HeadlessSkirmish.Ending -= OnSimulationEnding;
			subscribed = false;

			// A draw is not a win, and recording it as one would teach the model that the position
			// which produced a draw is the position that wins games - which is exactly the mistake
			// this commander already makes.
			WriteTrainingLog(owner.WinState == WinState.Won);
		}

		void INotifyWinStateChanged.OnPlayerWon(Player winner)
		{
			if (winner == owner)
				WriteTrainingLog(won: true);
		}

		void INotifyWinStateChanged.OnPlayerLost(Player loser)
		{
			if (loser == owner)
				WriteTrainingLog(won: false);
		}

		void WriteTrainingLog(bool won)
		{
			// Once per match. Win and loss notifications can both arrive for allied players, and a
			// double-labelled game would put contradictory rows into the training set.
			if (logged || owner == null || trainingSamples.Count == 0 || string.IsNullOrEmpty(info.TrainingLog))
				return;

			logged = true;

			var path = Path.IsPathRooted(info.TrainingLog)
				? info.TrainingLog
				: Path.Combine(Platform.SupportDir, info.TrainingLog);

			if (subscribed)
			{
				HeadlessSkirmish.Ending -= OnSimulationEnding;
				subscribed = false;
			}

			try
			{
				SelfPlayLog.Append(path, trainingSamples, won);

				if (!string.IsNullOrEmpty(info.StateLog) && stateSamples.Count > 0)
				{
					// A match id that is stable for this match and distinct between matches, so the
					// Python side can hold out whole games. Splitting by row instead would put
					// samples from one game on both sides of the split and score memorisation.
					var matchId = owner.World.WorldTick + owner.InternalName.GetHashCode(StringComparison.Ordinal);

					// How far ahead we finished, in structures, normalised to -1..1. Structures are
					// the win condition, so this is the graded version of the result rather than a
					// proxy for it.
					var ours = owner.World.ActorsHavingTrait<Building>()
						.Count(a => !a.IsDead && a.IsInWorld && a.Owner == owner);
					var theirs = owner.World.ActorsHavingTrait<Building>()
						.Count(a => !a.IsDead && a.IsInWorld && a.Owner != null
							&& !a.Owner.NonCombatant && !a.Owner.IsAlliedWith(owner));

					var margin = ours + theirs <= 0 ? 0f : (ours - theirs) / (float)(ours + theirs);

					StateExport.Append(Platform.ResolvePath(info.StateLog), stateSamples, won, margin, matchId);
					CoalitionTelemetry.Log(owner.World,
						$"State log: {stateSamples.Count} positions, margin {margin:F2} -> {info.StateLog}");
				}
				CoalitionTelemetry.Log(owner.World,
					$"Training log: {trainingSamples.Count} samples labelled {(won ? "won" : "lost")} -> {path}");
			}
			catch (IOException e)
			{
				// A training log that cannot be written must not take the match down with it.
				CoalitionTelemetry.Log(owner.World, $"Training log failed: {e.Message}");
			}
		}

		/// <summary>Current win probability, for telemetry and for the search to come.</summary>
		public float WinProbability(AbstractState state) =>
			evaluator == null || model == null ? 0.5f : evaluator.Evaluate(state, model);

		static IEnumerable<Player> Enemies(Player self)
		{
			foreach (var player in self.World.Players)
				if (!player.NonCombatant && player != self && !player.IsAlliedWith(self))
					yield return player;
		}
	}
}
