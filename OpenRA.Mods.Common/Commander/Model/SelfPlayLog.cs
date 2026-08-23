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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenRA.Mods.Common.Commander.Model
{
	/// <summary>
	/// <para>
	/// Records feature vectors during a match and labels them when it ends, which is the training
	/// set the evaluation function is fitted on.
	/// </para>
	/// <para>
	/// The labelling is the subtle part. Every state observed in a game gets that game's outcome, so
	/// the opening position of a won game is labelled a win even though it was even at the time.
	/// That is correct and deliberate - it is how the model learns which early advantages actually
	/// convert - but it means samples within a game are enormously correlated, and a fit without
	/// regularisation will read a thousand samples from one game as a thousand independent facts.
	/// <see cref="LogisticFit"/> penalises accordingly.
	/// </para>
	/// </summary>
	public static class SelfPlayLog
	{
		/// <summary>One row: when it was taken, what the position looked like, and how it ended.</summary>
		public readonly record struct Row(int Tick, float[] Features, bool Won)
		{
			public string Serialise()
			{
				var builder = new StringBuilder();
				builder.Append(Tick.ToString(CultureInfo.InvariantCulture));
				builder.Append(',');
				builder.Append(Won ? '1' : '0');

				foreach (var f in Features)
				{
					builder.Append(',');
					builder.Append(f.ToString("R", CultureInfo.InvariantCulture));
				}

				return builder.ToString();
			}

			public static bool TryParse(string line, out Row row)
			{
				row = default;
				if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
					return false;

				var parts = line.Split(',');
				if (parts.Length != StateFeatures.Count + 2)
					return false;

				if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tick))
					return false;

				if (parts[1] != "0" && parts[1] != "1")
					return false;

				var features = new float[StateFeatures.Count];
				for (var i = 0; i < StateFeatures.Count; i++)
					if (!float.TryParse(parts[i + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out features[i]))
						return false;

				row = new Row(tick, features, parts[1] == "1");
				return true;
			}
		}

		/// <summary>
		/// Appends a completed match's samples. Append rather than overwrite: the training set is
		/// meant to accumulate across runs, which is the whole mechanism by which the commander gets
		/// stronger without anyone editing a threshold.
		/// </summary>
		public static void Append(string path, IEnumerable<(int Tick, float[] Features)> samples, bool won)
		{
			ArgumentNullException.ThrowIfNull(path);
			ArgumentNullException.ThrowIfNull(samples);

			var directory = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(directory))
				Directory.CreateDirectory(directory);

			var lines = samples
				.Where(s => s.Features != null && s.Features.Length == StateFeatures.Count)
				.Select(s => new Row(s.Tick, s.Features, won).Serialise());

			File.AppendAllLines(path, lines);
		}

		/// <summary>Reads a training set, skipping anything malformed rather than throwing on it.</summary>
		public static List<Row> Read(string path)
		{
			var rows = new List<Row>();
			if (string.IsNullOrEmpty(path) || !File.Exists(path))
				return rows;

			foreach (var line in File.ReadLines(path))
				if (Row.TryParse(line, out var row))
					rows.Add(row);

			return rows;
		}

		/// <summary>
		/// Splits rows into a training and a holdout set by hashing the game they came from, so that
		/// every sample of one game lands on the same side of the split.
		/// </summary>
		/// <remarks>
		/// Splitting at random would put samples from the same game in both sets, and since
		/// consecutive samples are nearly identical the holdout would then contain near-copies of
		/// the training data. The model would score beautifully and would have learned nothing that
		/// generalises. Games are identified by a caller-supplied key for exactly this reason.
		/// </remarks>
		public static (List<LogisticFit.Sample> Train, List<LogisticFit.Sample> Holdout) Split(
			IEnumerable<(Row Row, int GameKey)> rows, int holdoutEvery = 4)
		{
			ArgumentNullException.ThrowIfNull(rows);
			holdoutEvery = Math.Max(2, holdoutEvery);

			var all = new List<(Row Row, int GameKey)>(rows);

			// Stratified by outcome, not simply by game index. Won games are rare - nine of the
			// first four hundred - so a plain every-fourth-game split put almost none of them in the
			// holdout, and the grade that came back was a grade on predicting "lost" over and over.
			// Numbering won and lost games separately guarantees the holdout contains both.
			var order = new Dictionary<int, int>();
			var wonSeen = 0;
			var lostSeen = 0;

			foreach (var (row, gameKey) in all)
			{
				if (order.ContainsKey(gameKey))
					continue;

				order[gameKey] = row.Won ? wonSeen++ : lostSeen++;
			}

			var train = new List<LogisticFit.Sample>();
			var holdout = new List<LogisticFit.Sample>();

			foreach (var (row, gameKey) in all)
			{
				var sample = new LogisticFit.Sample(row.Features, row.Won);
				if (order[gameKey] % holdoutEvery == 0)
					holdout.Add(sample);
				else
					train.Add(sample);
			}

			return (train, holdout);
		}
	}
}
