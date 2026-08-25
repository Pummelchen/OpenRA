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
	/// Writes the full position - every actor on record, every region, and the globals - as one line
	/// per sample, for training a model outside the game.
	/// </para>
	/// <para>
	/// This exists because the existing training log is nine hand-picked scalars, and nine scalars
	/// cannot distinguish two positions that a player would call completely different. "Enemy army
	/// value 14,000" is the same number whether that army is massed at a chokepoint or scattered
	/// across four fronts with its refineries undefended. A model asked to predict outcomes from
	/// those nine numbers is being asked to do something impossible, and its ceiling has nothing to
	/// do with its architecture.
	/// </para>
	/// <para>
	/// So the export is deliberately close to raw: one token per actor with its type, position,
	/// health, side and - importantly - <b>how long ago it was actually seen</b>, so a model can
	/// learn to discount stale sightings itself rather than being handed a decay constant somebody
	/// guessed. Region and global tokens carry the rest.
	/// </para>
	/// <para>
	/// Fog is respected: enemy entries come from the shared database, which only ever recorded what
	/// was genuinely observed. The export cannot see anything the commander could not.
	/// </para>
	/// </summary>
	public static class StateExport
	{
		/// <summary>Fields per entity token, in order. The Python side reads this contract.</summary>
		public static readonly string[] EntityFields =
			["type", "x", "y", "health", "side", "structure", "armed", "cost", "staleSeconds", "region"];

		/// <summary>Fields per region token, in order.</summary>
		public static readonly string[] RegionFields =
			["ourArmy", "enemyArmy", "ourStructures", "enemyStructures", "control", "centreX", "centreY"];

		/// <summary>Fields in the global vector, in order.</summary>
		public static readonly string[] GlobalFields =
			["seconds", "cash", "earned", "spent", "bankedFraction", "power", "harvesters", "refineries",
			 "ourArmyValue", "enemyArmyValue", "ourStructureCount", "enemyStructuresKnown", "queuesIdle"];

		/// <summary>
		/// Most actors we will write for one sample. A cap keeps the file bounded on a late-game
		/// position with a thousand units; the tail is dropped by ascending actor id so the choice
		/// is deterministic rather than "whatever the enumerator gave us".
		/// </summary>
		public const int MaximumEntities = 512;

		public sealed class Sample
		{
			public int Tick { get; init; }
			public IReadOnlyList<float[]> Entities { get; init; } = [];
			public IReadOnlyList<float[]> Regions { get; init; } = [];
			public float[] Globals { get; init; } = [];
		}

		/// <summary>
		/// Builds one sample from what the commander currently knows.
		/// </summary>
		public static Sample Capture(
			WorldDatabase database, AbstractState state, UnitCatalogue catalogue,
			int tick, int cash, int earned, int spent, int queuesIdle)
		{
			ArgumentNullException.ThrowIfNull(database);
			ArgumentNullException.ThrowIfNull(state);

			var entities = new List<float[]>(MaximumEntities);
			foreach (var entry in database.All)
			{
				if (entities.Count >= MaximumEntities)
					break;

				// Things known to be dead are not part of the position. They are part of the
				// history, which the loss records already carry.
				if (entry.Status == RecordStatus.Destroyed)
					continue;

				var info = catalogue?.Find(entry.Type);

				entities.Add([
					TypeId(catalogue, entry.Type),
					entry.LastKnownCell.X,
					entry.LastKnownCell.Y,
					entry.HealthFraction,
					entry.Side switch { Allegiance.Self => 0f, Allegiance.Ally => 1f, _ => 2f },
					entry.IsStructure ? 1f : 0f,
					info != null && info.IsArmed ? 1f : 0f,
					info?.Cost ?? 0,
					Math.Min(entry.SecondsSinceSeen(tick), 600f),
					entry.Region,
				]);
			}

			var regions = new List<float[]>(state.RegionCount);
			for (var r = 0; r < state.RegionCount; r++)
			{
				regions.Add([
					state.Self.ArmyValueIn(r),
					state.Enemy.ArmyValueIn(r),
					state.Self.StructuresIn(r),
					state.Enemy.StructuresIn(r),
					state.Control != null && r < state.Control.Length ? state.Control[r] : 0f,
					0f,
					0f,
				]);
			}

			var globals = new[]
			{
				tick / (float)AbstractState.TicksPerSecond,
				cash,
				earned,
				spent,
				earned <= 0 ? 0f : Math.Clamp(cash / (float)earned, 0f, 1f),
				0f,
				database.CountOf("harv"),
				database.CountOf("proc"),
				state.Self.ArmyValue(),
				state.Enemy.ArmyValue(),
				database.Standing(Allegiance.Self).Count(e => e.IsStructure),
				database.EnemyStructures().Count(),
				queuesIdle,
			};

			return new Sample { Tick = tick, Entities = entities, Regions = regions, Globals = globals };
		}

		/// <summary>
		/// A stable integer per actor type, taken from the catalogue's own ordering.
		/// </summary>
		/// <remarks>
		/// Deliberately an index into a sorted list rather than a hash: the model learns an embedding
		/// per id, so the id must mean the same thing in every match and on every machine. A hash
		/// would be stable too but would scatter related types arbitrarily; the catalogue order at
		/// least keeps the mod's own naming together.
		/// </remarks>
		static float TypeId(UnitCatalogue catalogue, string type)
		{
			if (catalogue == null || string.IsNullOrEmpty(type))
				return 0f;

			for (var i = 0; i < catalogue.All.Count; i++)
				if (string.Equals(catalogue.All[i].Type, type, StringComparison.Ordinal))
					return i + 1;

			return 0f;
		}

		/// <summary>
		/// Appends samples as JSON lines, labelled with the match result.
		/// </summary>
		/// <remarks>
		/// One line per sample rather than one file per match, so that a training run is a single
		/// streaming read. The match id lets the Python side split by match rather than by row -
		/// splitting by row would put samples from the same game on both sides of the holdout and
		/// report a score that is mostly memorisation.
		/// </remarks>
		public static void Append(string path, IEnumerable<Sample> samples, bool won, float margin, int matchId)
		{
			ArgumentNullException.ThrowIfNull(samples);
			if (string.IsNullOrEmpty(path))
				return;

			var directory = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(directory))
				Directory.CreateDirectory(directory);

			using var writer = new StreamWriter(path, append: true);
			foreach (var sample in samples)
				writer.WriteLine(Serialise(sample, won, margin, matchId));
		}

		static string Serialise(Sample sample, bool won, float margin, int matchId)
		{
			var builder = new StringBuilder(4096);
			builder.Append("{\"match\":").Append(matchId.ToString(CultureInfo.InvariantCulture));
			builder.Append(",\"tick\":").Append(sample.Tick.ToString(CultureInfo.InvariantCulture));
			builder.Append(",\"won\":").Append(won ? '1' : '0');

			// A graded outcome as well as the binary one, and it is the label that actually gets
			// used. Almost every match here ends at the time limit rather than in a victory, so
			// "won" is zero for essentially the entire dataset - a single-class target that teaches
			// a model nothing except to output the base rate. How far ahead the commander finished
			// is defined for every match, including the drawn ones, and is monotone in the thing
			// being predicted. Chess engines evaluate positions rather than only win/draw/loss for
			// much the same reason.
			builder.Append(",\"margin\":").Append(margin.ToString("R", CultureInfo.InvariantCulture));

			AppendMatrix(builder, ",\"entities\":", sample.Entities);
			AppendMatrix(builder, ",\"regions\":", sample.Regions);

			builder.Append(",\"globals\":");
			AppendVector(builder, sample.Globals);

			builder.Append('}');
			return builder.ToString();
		}

		static void AppendMatrix(StringBuilder builder, string key, IReadOnlyList<float[]> rows)
		{
			builder.Append(key).Append('[');
			for (var i = 0; i < rows.Count; i++)
			{
				if (i > 0)
					builder.Append(',');

				AppendVector(builder, rows[i]);
			}

			builder.Append(']');
		}

		static void AppendVector(StringBuilder builder, IReadOnlyList<float> values)
		{
			builder.Append('[');
			for (var i = 0; i < values.Count; i++)
			{
				if (i > 0)
					builder.Append(',');

				// "R" round-trips, and these files are read by another language: a truncated float
				// that parses differently on the Python side is a silent training bug.
				builder.Append(values[i].ToString("R", CultureInfo.InvariantCulture));
			}

			builder.Append(']');
		}
	}
}
