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

namespace OpenRA.Test
{
	/// <summary>
	/// The evaluation function and the fit that produces it. Its job is to rank two futures, and
	/// its value to the search comes entirely from getting that ordering right.
	/// </summary>
	[TestFixture]
	sealed class WinProbabilityModelTest
	{
		static float[] Features(float army, float income = 0f, float integrity = 0f, float control = 0f)
		{
			var f = new float[StateFeatures.Count];
			f[(int)StateFeatures.Feature.ArmyAdvantage] = army;
			f[(int)StateFeatures.Feature.IncomeAdvantage] = income;
			f[(int)StateFeatures.Feature.BaseIntegrityAdvantage] = integrity;
			f[(int)StateFeatures.Feature.MapControl] = control;
			f[(int)StateFeatures.Feature.Bias] = 1f;
			return f;
		}

		[TestCase(TestName = "Advantage is signed, bounded and scale-free.")]
		public void AdvantageIsScaleFree()
		{
			// The whole point of a ratio: 3,000 against 1,000 is the same position as 30,000
			// against 10,000, and a weight fitted on one must apply to the other.
			Assert.That(StateFeatures.Advantage(3000f, 1000f),
				Is.EqualTo(StateFeatures.Advantage(30000f, 10000f)).Within(1e-6f));

			Assert.That(StateFeatures.Advantage(100f, 100f), Is.EqualTo(0f));
			Assert.That(StateFeatures.Advantage(100f, 0f), Is.EqualTo(1f));
			Assert.That(StateFeatures.Advantage(0f, 100f), Is.EqualTo(-1f));
			Assert.That(StateFeatures.Advantage(0f, 0f), Is.EqualTo(0f), "Nothing against nothing is parity, not a crash.");
		}

		[TestCase(TestName = "A probability, not a score.")]
		public void EvaluateReturnsAProbability()
		{
			var model = WinProbabilityModel.Default();

			Assert.That(model.Evaluate(Features(0f)), Is.EqualTo(0.5f).Within(1e-5f),
				"A position with no advantage anywhere is a coin flip.");

			var winning = model.Evaluate(Features(1f, 1f, 1f, 1f));
			var losing = model.Evaluate(Features(-1f, -1f, -1f, -1f));

			Assert.That(winning, Is.GreaterThan(0.9f));
			Assert.That(losing, Is.LessThan(0.1f));
			Assert.That(winning + losing, Is.EqualTo(1f).Within(1e-5f), "The model must be symmetric.");
		}

		[TestCase(TestName = "Extreme margins saturate instead of overflowing.")]
		public void SigmoidIsGuarded()
		{
			Assert.That(WinProbabilityModel.Sigmoid(1e9f), Is.EqualTo(1f));
			Assert.That(WinProbabilityModel.Sigmoid(-1e9f), Is.EqualTo(0f));
			Assert.That(WinProbabilityModel.Sigmoid(0f), Is.EqualTo(0.5f));
		}

		[TestCase(TestName = "Ordering is what the search needs, and it holds.")]
		public void OrderingIsMonotone()
		{
			var model = WinProbabilityModel.Default();
			var previous = 0f;

			// The search never uses the absolute number; it compares two futures. If the ordering
			// is wrong the search is worse than useless, because it will confidently pick the worse
			// of two plans.
			foreach (var army in new[] { -1f, -0.5f, -0.1f, 0f, 0.1f, 0.5f, 1f })
			{
				var p = model.Evaluate(Features(army));
				Assert.That(p, Is.GreaterThan(previous), $"More army must never score worse (at {army}).");
				previous = p;
			}
		}

		[TestCase(TestName = "The fit recovers a rule that is actually in the data.")]
		public void FitRecoversASeparableRule()
		{
			// Games won exactly when the army advantage was positive. The fitted model must find
			// that, or it cannot find anything.
			var samples = new List<LogisticFit.Sample>();
			for (var i = -20; i <= 20; i++)
			{
				if (i == 0)
					continue;

				var advantage = i / 20f;
				samples.Add(new LogisticFit.Sample(Features(advantage), advantage > 0f));
			}

			var result = LogisticFit.Fit(samples);

			Assert.That(result.Accuracy, Is.GreaterThan(0.95f));
			Assert.That(result.BrierScore, Is.LessThan(0.25f),
				"0.25 is what guessing produces; a model that cannot beat it has learned nothing.");
			Assert.That(result.Model.Weights[(int)StateFeatures.Feature.ArmyAdvantage], Is.GreaterThan(1f),
				"The weight on the feature that decided every game must be positive and large.");
		}

		[TestCase(TestName = "The fit is reproducible.")]
		public void FitIsDeterministic()
		{
			// Batch descent rather than stochastic, precisely so that a commander can be reproduced
			// from its training data and a regression can be bisected.
			var samples = Enumerable.Range(0, 50)
				.Select(i => new LogisticFit.Sample(Features((i % 10) / 10f - 0.5f), i % 3 == 0))
				.ToList();

			var a = LogisticFit.Fit(samples);
			var b = LogisticFit.Fit(samples);

			Assert.That(b.Model.Weights, Is.EqualTo(a.Model.Weights));
			Assert.That(b.LogLoss, Is.EqualTo(a.LogLoss));
		}

		[TestCase(TestName = "Regularisation keeps a correlated feature from running away.")]
		public void RegularisationBoundsWeights()
		{
			// Self-play states are enormously correlated - a thousand samples from one game are
			// nearly one sample - so an unpenalised fit will put an enormous weight on whatever
			// happened to separate the games it saw.
			var samples = Enumerable.Range(0, 200)
				.Select(i => new LogisticFit.Sample(Features(i < 100 ? 0.9f : -0.9f), i < 100))
				.ToList();

			var penalised = LogisticFit.Fit(samples, l2: 1.0f);
			var unpenalised = LogisticFit.Fit(samples, l2: 0f);

			var penalisedWeight = Math.Abs(penalised.Model.Weights[(int)StateFeatures.Feature.ArmyAdvantage]);
			var unpenalisedWeight = Math.Abs(unpenalised.Model.Weights[(int)StateFeatures.Feature.ArmyAdvantage]);

			Assert.That(penalisedWeight, Is.LessThan(unpenalisedWeight));
			Assert.That(penalised.Accuracy, Is.EqualTo(1f), "And it still gets the answer right.");
		}

		[TestCase(TestName = "An unlearnable rule produces an honest coin flip.")]
		public void UnlearnableDataProducesNoConfidence()
		{
			// Identical features, opposite outcomes. The only correct model says 50%, and a fit
			// that claimed more than that would be lying about what the data supports.
			var samples = Enumerable.Range(0, 100)
				.Select(i => new LogisticFit.Sample(Features(0.5f), i % 2 == 0))
				.ToList();

			var result = LogisticFit.Fit(samples);
			var p = result.Model.Evaluate(Features(0.5f));

			Assert.That(p, Is.EqualTo(0.5f).Within(0.05f));
			Assert.That(result.BrierScore, Is.EqualTo(0.25f).Within(0.02f));
		}

		[TestCase(TestName = "Weights survive a round trip.")]
		public void SerialisationRoundTrips()
		{
			var model = WinProbabilityModel.Default();
			var restored = WinProbabilityModel.Deserialise(model.Serialise());
			Assert.That(restored.Weights, Is.EqualTo(model.Weights));

			// A corrupt or truncated file must fall back to something usable rather than throwing
			// mid-match or, worse, producing a model of zeroes that rates every position at 50%.
			Assert.That(WinProbabilityModel.Deserialise("garbage").Weights, Is.EqualTo(model.Weights));
			Assert.That(WinProbabilityModel.Deserialise("1,2,3").Weights, Is.EqualTo(model.Weights));
			Assert.That(WinProbabilityModel.Deserialise(null).Weights, Is.EqualTo(model.Weights));
		}

		[TestCase(TestName = "Features are extracted in the documented order.")]
		public void ExtractionMatchesLayout()
		{
			var graph = OpenRA.Mods.Common.Commander.Terrain.RegionGraph.Build(40, 40,
				(x, y) => x > 0 && y > 0 && x < 39 && y < 39);

			var damage = new float[RoleStats.Roles * RoleStats.Roles];
			Array.Fill(damage, 1f);
			var hp = new float[RoleStats.Roles];
			Array.Fill(hp, 1f);
			var model = new ForwardModel(graph, new RoleStats(damage, hp));

			var state = new AbstractState(graph.Regions.Length);
			state.Self.SetForce(0, CombatRole.Armor, 3000f);
			state.Enemy.SetForce(0, CombatRole.Armor, 1000f);
			state.Self.BaseIntegrity = 5000f;
			state.Enemy.BaseIntegrity = 5000f;

			var features = StateFeatures.Extract(state, model);

			Assert.That(features, Has.Length.EqualTo(StateFeatures.Count));
			Assert.That(features[(int)StateFeatures.Feature.ArmyAdvantage], Is.EqualTo(0.5f).Within(1e-5f));
			Assert.That(features[(int)StateFeatures.Feature.BaseIntegrityAdvantage], Is.EqualTo(0f));
			Assert.That(features[(int)StateFeatures.Feature.Bias], Is.EqualTo(1f));
			Assert.That(features[(int)StateFeatures.Feature.ContestedFraction], Is.GreaterThan(0f),
				"Both sides are present in region 0, so something is contested.");
		}
	}
}
