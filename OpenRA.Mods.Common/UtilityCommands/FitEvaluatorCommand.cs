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

namespace OpenRA.Mods.Common.UtilityCommands
{
	/// <summary>
	/// Fits the commander's win-probability model to accumulated self-play games and reports how
	/// well it does on games it was not fitted on. This is the phase 3 gate of the rebuild.
	/// </summary>
	sealed class FitEvaluatorCommand : IUtilityCommand
	{
		string IUtilityCommand.Name => "--fit-evaluator";

		bool IUtilityCommand.ValidateArguments(string[] args) => args.Length >= 2;

		[Desc("LOG=<path> [L2=0.01] [ITERATIONS=2000] [HOLDOUT=4]",
			"Fit the commander's evaluation function to logged self-play outcomes.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			Game.ModData = utility.ModData;

			var path = Arg(args, "LOG") ?? Path.Combine(Platform.SupportDir, "commander-training.csv");
			var l2 = float.TryParse(Arg(args, "L2"), out var parsedL2) ? parsedL2 : 0.01f;
			var iterations = int.TryParse(Arg(args, "ITERATIONS"), out var parsedIterations) ? parsedIterations : 2000;
			var holdoutEvery = int.TryParse(Arg(args, "HOLDOUT"), out var parsedHoldout) ? parsedHoldout : 4;

			var rows = SelfPlayLog.Read(path);
			if (rows.Count == 0)
			{
				Console.WriteLine($"No usable samples in '{path}'.");
				Console.WriteLine("Generate some with: utility.sh ra --simulate MAP=... BOT_TYPES=ai,rush");
				return;
			}

			// Rows from consecutive matches are appended to one file, and a match always starts at a
			// lower tick than the previous one ended on. That falling edge is the game boundary.
			var keyed = new List<(SelfPlayLog.Row Row, int GameKey)>();
			var game = 0;
			var previousTick = int.MinValue;
			foreach (var row in rows)
			{
				if (row.Tick < previousTick)
					game++;

				previousTick = row.Tick;
				keyed.Add((row, game));
			}

			var games = game + 1;
			var (train, holdout) = SelfPlayLog.Split(keyed, holdoutEvery);

			Console.WriteLine($"{rows.Count} samples over {games} games from {path}");
			Console.WriteLine($"  train {train.Count}, holdout {holdout.Count} " +
				$"(split by game, so no game appears in both)");

			if (train.Count == 0 || holdout.Count == 0)
			{
				Console.WriteLine("  not enough games to hold any out - play more before trusting a fit.");
				return;
			}

			var wonFraction = rows.Count(r => r.Won) / (float)rows.Count;
			Console.WriteLine($"  {wonFraction:P1} of samples come from won games");

			var fitted = LogisticFit.Fit(train, iterations: iterations, l2: l2);
			var onHoldout = LogisticFit.Score(fitted.Model, holdout, 0);
			var defaultOnHoldout = LogisticFit.Score(WinProbabilityModel.Default(), holdout, 0);

			// The bar every model must clear: predicting the base rate for every position, which
			// requires no features and no fitting at all.
			var baseRateBrier = holdout.Average(s =>
			{
				var residual = wonFraction - (s.Won ? 1f : 0f);
				return residual * residual;
			});

			Console.WriteLine();
			Console.WriteLine($"  {"",-22} {"Brier",8} {"LogLoss",9} {"Accuracy",9}");
			Console.WriteLine($"  {"fitted (train)",-22} {fitted.BrierScore,8:F4} {fitted.LogLoss,9:F4} {fitted.Accuracy,9:P1}");
			Console.WriteLine($"  {"fitted (holdout)",-22} {onHoldout.BrierScore,8:F4} {onHoldout.LogLoss,9:F4} {onHoldout.Accuracy,9:P1}");
			Console.WriteLine($"  {"hand-written (holdout)",-22} {defaultOnHoldout.BrierScore,8:F4} " +
				$"{defaultOnHoldout.LogLoss,9:F4} {defaultOnHoldout.Accuracy,9:P1}");
			Console.WriteLine($"  {"base rate (holdout)",-22} {baseRateBrier,8:F4}");
			Console.WriteLine($"  {"coin flip",-22} {0.25f,8:F4}");

			Console.WriteLine();
			Console.WriteLine("  weights, largest influence first:");
			foreach (var line in fitted.Model.Describe())
				Console.WriteLine("    " + line);

			Console.WriteLine();
			Console.WriteLine("  serialised: " + fitted.Model.Serialise());

			var beatsCoinFlip = onHoldout.BrierScore < 0.25f;
			var beatsBaseRate = onHoldout.BrierScore < baseRateBrier;
			Console.WriteLine();
			Console.WriteLine($"  gate: holdout Brier below 0.25 ....... {(beatsCoinFlip ? "PASS" : "FAIL")}");
			Console.WriteLine($"        holdout Brier below base rate .. {(beatsBaseRate ? "PASS" : "FAIL")}");

			if (!beatsBaseRate)
				Console.WriteLine("        (a model that cannot beat the base rate has learned nothing " +
					"from the features, whatever its absolute score says)");
		}

		static string Arg(string[] args, string key)
		{
			var prefix = key + "=";
			foreach (var a in args)
				if (a.StartsWith(prefix, StringComparison.Ordinal))
					return a[prefix.Length..];

			return null;
		}
	}
}
