#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of the
 * License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Commander.Model;
using OpenRA.Mods.Common.Commander.Staff;

namespace OpenRA.Test
{
	/// <summary>
	/// The chief's decision expressed as features. The point of this representation is that the
	/// candidate stance is an <i>input</i>, so one model scores every option and the chief picks the
	/// best - which is what makes the thing fittable from outcomes alone.
	/// </summary>
	[TestFixture]
	sealed class ChiefFeaturesTest
	{
		static (CommanderSnapshot Snapshot, StaffContext Context) Situation(
			float ourArmy = 5000f, float theirArmy = 5000f, int cash = 5000, int earned = 20000,
			params ManagerReport[] reports)
		{
			var state = new AbstractState(8);
			state.Self.SetForce(0, CombatRole.Armor, ourArmy);
			state.Enemy.SetForce(4, CombatRole.Armor, theirArmy);
			state.Self.BaseIntegrity = 8000f;
			state.Self.PeakBaseIntegrity = 10000f;

			var snapshot = new CommanderSnapshot { Tick = 15000, State = state, Cash = cash, Earned = earned };
			var context = new StaffContext(Directive.Initial, [], reports.ToList());
			return (snapshot, context);
		}

		[TestCase(TestName = "The candidate stance is an input, not an output.")]
		public void StanceIsOneHotInput()
		{
			var (snapshot, context) = Situation();

			var assault = ChiefFeatures.Extract(snapshot, context, Stance.Assault);
			var defend = ChiefFeatures.Extract(snapshot, context, Stance.Defend);

			Assert.That(assault[(int)ChiefFeatures.Feature.StanceAssault], Is.EqualTo(1f));
			Assert.That(assault[(int)ChiefFeatures.Feature.StanceDefend], Is.EqualTo(0f));
			Assert.That(defend[(int)ChiefFeatures.Feature.StanceDefend], Is.EqualTo(1f));

			// Everything except the stance block must be identical, or the model would be learning
			// about the situation separately for each candidate rather than comparing them.
			for (var i = 0; i < (int)ChiefFeatures.Feature.StanceBuild; i++)
				Assert.That(defend[i], Is.EqualTo(assault[i]).Within(1e-6f));
		}

		[TestCase(TestName = "Exactly one stance is ever set.")]
		public void StanceIsExclusive()
		{
			var (snapshot, context) = Situation();

			foreach (Stance stance in Enum.GetValues<Stance>())
			{
				var f = ChiefFeatures.Extract(snapshot, context, stance);
				var set = Enumerable
					.Range((int)ChiefFeatures.Feature.StanceBuild, 6)
					.Count(i => f[i] == 1f);

				Assert.That(set, Is.EqualTo(1), $"{stance} set {set} stance features.");
			}
		}

		[TestCase(TestName = "A surplus reads as a large number, because it is a fault.")]
		public void SurplusIsVisible()
		{
			var spent = ChiefFeatures.Extract(Situation(cash: 1000, earned: 100000).Snapshot,
				Situation(cash: 1000, earned: 100000).Context, Stance.Assault);
			var hoarded = ChiefFeatures.Extract(Situation(cash: 74000, earned: 100000).Snapshot,
				Situation(cash: 74000, earned: 100000).Context, Stance.Assault);

			// The commander that banked 74% of its income across a match must look different here
			// from one that spent it, or no model fitted on this can ever learn the difference.
			Assert.That(hoarded[(int)ChiefFeatures.Feature.BankedFraction],
				Is.GreaterThan(spent[(int)ChiefFeatures.Feature.BankedFraction] + 0.5f));
		}

		[TestCase(TestName = "Force advantage is signed and scale-free.")]
		public void ForceAdvantageIsScaleFree()
		{
			float Advantage(float ours, float theirs)
			{
				var (s, c) = Situation(ours, theirs);
				return ChiefFeatures.Extract(s, c, Stance.Assault)[(int)ChiefFeatures.Feature.ForceAdvantage];
			}

			Assert.That(Advantage(3000f, 1000f), Is.EqualTo(Advantage(30000f, 10000f)).Within(1e-5f));
			Assert.That(Advantage(1000f, 1000f), Is.EqualTo(0f).Within(1e-5f));
			Assert.That(Advantage(1000f, 3000f), Is.LessThan(0f));
		}

		[TestCase(TestName = "Staff strain and waiting are summarised, not enumerated.")]
		public void StaffStateIsDigested()
		{
			var (snapshot, context) = Situation(reports:
			[
				new ManagerReport { Manager = "economy", Readiness = Readiness.Healthy },
				new ManagerReport { Manager = "defence", Readiness = Readiness.Strained },
				new ManagerReport { Manager = "unit-production", Readiness = Readiness.Critical, ReadyInSeconds = 120 },
				new ManagerReport { Manager = "intelligence", Readiness = Readiness.Healthy, Confidence = 0.7f },
			]);

			var f = ChiefFeatures.Extract(snapshot, context, Stance.Assault);

			// Two of four domains are in trouble. The chief needs that as one number, not as four
			// reports to re-read - a chief handed raw state is a chief doing everyone's job badly.
			Assert.That(f[(int)ChiefFeatures.Feature.StaffStrain], Is.EqualTo(0.5f).Within(1e-5f));
			Assert.That(f[(int)ChiefFeatures.Feature.OpponentConfidence], Is.EqualTo(0.7f).Within(1e-5f));
			Assert.That(f[(int)ChiefFeatures.Feature.WaitPressure], Is.GreaterThan(0.5f),
				"Two minutes of waiting should register as real pressure.");
		}

		[TestCase(TestName = "Knowing where they are is a feature in its own right.")]
		public void ObjectiveKnownIsExplicit()
		{
			var blind = Situation();
			var sighted = Situation(reports:
				new ManagerReport { Manager = "tactical-analysis", RegionOfInterest = 5 });

			Assert.That(ChiefFeatures.Extract(blind.Snapshot, blind.Context, Stance.Assault)
				[(int)ChiefFeatures.Feature.ObjectiveKnown], Is.EqualTo(0f));

			Assert.That(ChiefFeatures.Extract(sighted.Snapshot, sighted.Context, Stance.Assault)
				[(int)ChiefFeatures.Feature.ObjectiveKnown], Is.EqualTo(1f));
		}

		[TestCase(TestName = "Extraction is reproducible.")]
		public void ExtractionIsDeterministic()
		{
			var (snapshot, context) = Situation(reports:
				new ManagerReport { Manager = "intelligence", Confidence = 0.4f });

			var first = ChiefFeatures.Extract(snapshot, context, Stance.Pressure);
			for (var i = 0; i < 5; i++)
				Assert.That(ChiefFeatures.Extract(snapshot, context, Stance.Pressure), Is.EqualTo(first));
		}
	}
}
