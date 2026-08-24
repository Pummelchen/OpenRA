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

namespace OpenRA.Mods.Common.Commander.Staff
{
	/// <summary>
	/// <para>
	/// The chief's decision, reduced to numbers a model can be fitted to.
	/// </para>
	/// <para>
	/// This exists so the chief can stop being a set of hand-picked thresholds. Its rules are
	/// currently reasonable guesses - wait ninety seconds, treat a surplus as a fault, want 25%
	/// confidence before feinting - and every one of those numbers is a claim that has never been
	/// checked against an outcome.
	/// </para>
	/// <para>
	/// <b>On learning this rather than writing it.</b> Deep reinforcement learning is the obvious
	/// suggestion and it does not fit the budget. A match here costs 26 seconds of wall clock, so
	/// this machine produces roughly 20,000 games a day, while learning an RTS policy from scratch
	/// takes on the order of a million episodes - fifty days of continuous compute per training run,
	/// before a single hyperparameter is questioned. Attention over regions and units is the right
	/// inductive bias for this problem and it is still the wrong tool at this data scale.
	/// </para>
	/// <para>
	/// What ten thousand games <i>is</i> enough for is supervised value learning - which position
	/// wins - and that is exactly what a chief needs. So the decision is framed as scoring
	/// candidates rather than emitting an action: each candidate directive becomes a feature vector,
	/// the best-scoring one is issued, and the pairing of features to eventual outcome is the
	/// training set. Rules supply the scores until a fitted model beats them, and nothing about the
	/// interface changes when one does.
	/// </para>
	/// </summary>
	public static class ChiefFeatures
	{
		/// <summary>Layout of the feature vector. The last entry is the constant term.</summary>
		public enum Feature
		{
			/// <summary>Own army against believed enemy army, signed.</summary>
			ForceAdvantage,

			/// <summary>Fraction of income sitting unspent. High is a fault, not a strength.</summary>
			BankedFraction,

			/// <summary>How intact our own base is, against its own peak.</summary>
			BaseIntact,

			/// <summary>Whether an objective has been identified at all.</summary>
			ObjectiveKnown,

			/// <summary>Confidence in the opponent model, 0 to 1.</summary>
			OpponentConfidence,

			/// <summary>Seconds the slowest domain says it needs, squashed into 0..1.</summary>
			WaitPressure,

			/// <summary>Fraction of the staff reporting a strained or failing domain.</summary>
			StaffStrain,

			/// <summary>Match progress - the same position means different things at 5 and 25 minutes.</summary>
			MatchProgress,

			/// <summary>The candidate being scored, one-hot across the stances.</summary>
			StanceBuild,
			StanceProbe,
			StancePressure,
			StanceAssault,
			StanceDefend,
			StanceRecover,

			Bias,
		}

		public const int Count = (int)Feature.Bias + 1;

		/// <summary>Squashes a positive quantity into 0..1, reading one half at the reference value.</summary>
		public static float Saturate(float value, float reference)
		{
			if (value <= 0f || reference <= 0f)
				return 0f;

			return value / (value + reference);
		}

		/// <summary>Signed advantage in -1..1.</summary>
		public static float Advantage(float mine, float theirs)
		{
			var total = mine + theirs;
			return total <= 0f ? 0f : Math.Clamp((mine - theirs) / total, -1f, 1f);
		}

		/// <summary>
		/// Turns the staff's reports plus one candidate stance into a feature vector. The stance is
		/// part of the input rather than the output, so a single model scores every candidate and
		/// the chief picks the best - which is what allows the model to be fitted from outcomes
		/// alone, with no need to know what the right answer was at the time.
		/// </summary>
		public static float[] Extract(CommanderSnapshot snapshot, StaffContext context, Stance candidate)
		{
			ArgumentNullException.ThrowIfNull(snapshot);
			ArgumentNullException.ThrowIfNull(context);

			var features = new float[Count];
			var state = snapshot.State;

			var ourArmy = state?.Self.ArmyValue() ?? 0f;
			var theirArmy = state?.Enemy.ArmyValue() ?? 0f;
			features[(int)Feature.ForceAdvantage] = Advantage(ourArmy, theirArmy);
			features[(int)Feature.BankedFraction] = snapshot.BankedFraction;

			var peak = state?.Self.PeakBaseIntegrity ?? 0f;
			features[(int)Feature.BaseIntact] = peak <= 0f
				? 1f
				: Math.Clamp((state?.Self.BaseIntegrity ?? 0f) / peak, 0f, 1f);

			features[(int)Feature.ObjectiveKnown] =
				context.From("tactical-analysis")?.RegionOfInterest != null ? 1f : 0f;
			features[(int)Feature.OpponentConfidence] = context.From("intelligence")?.Confidence ?? 0f;
			features[(int)Feature.WaitPressure] = Saturate(context.LongestWait ?? 0, 60f);

			var strained = 0;
			foreach (var report in context.Reports)
				if (report.Readiness is Readiness.Strained or Readiness.Critical)
					strained++;

			features[(int)Feature.StaffStrain] = context.Reports.Count == 0
				? 0f
				: strained / (float)context.Reports.Count;

			features[(int)Feature.MatchProgress] = Saturate(snapshot.Seconds, 600f);

			features[(int)StanceFeature(candidate)] = 1f;
			features[(int)Feature.Bias] = 1f;
			return features;
		}

		static Feature StanceFeature(Stance stance) => stance switch
		{
			Stance.Build => Feature.StanceBuild,
			Stance.Probe => Feature.StanceProbe,
			Stance.Pressure => Feature.StancePressure,
			Stance.Assault => Feature.StanceAssault,
			Stance.Defend => Feature.StanceDefend,
			_ => Feature.StanceRecover,
		};

		public static string NameOf(int index) =>
			index >= 0 && index < Count ? ((Feature)index).ToString() : "?";
	}
}
