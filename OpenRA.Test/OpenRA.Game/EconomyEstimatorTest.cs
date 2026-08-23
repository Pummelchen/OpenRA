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

using NUnit.Framework;
using OpenRA.Mods.Common.Commander.Model;

namespace OpenRA.Test
{
	/// <summary>
	/// The income estimator, and specifically the reason it sums before dividing. Averaging
	/// per-sample ratios biases the estimate low, because a harvester delivers roughly every half
	/// minute and a short window with no delivery contributes a ratio of zero.
	/// </summary>
	[TestFixture]
	sealed class EconomyEstimatorTest
	{
		[TestCase(TestName = "A measurement replaces the initial guess outright.")]
		public void FirstMeasurementReplacesTheGuess()
		{
			var estimator = new EconomyEstimator(initialIncomePerHarvester: 16f);
			Assert.That(estimator.IncomePerHarvester, Is.EqualTo(16f));

			// 4 harvesters earning 400 credits over 10 seconds is 10 per harvester per second.
			estimator.Observe(4, 400f, 10f);
			Assert.That(estimator.IncomePerHarvester, Is.EqualTo(10f).Within(0.01f),
				"There is no reason to average a measurement with a number nobody measured.");
		}

		[TestCase(TestName = "Lumpy delivery does not bias the estimate.")]
		public void LumpyDeliveryDoesNotBias()
		{
			// The real pattern: some windows contain a delivery, some contain none. The average
			// rate is unchanged, and the estimator must report it.
			var estimator = new EconomyEstimator();
			for (var i = 0; i < 40; i++)
				estimator.Observe(4, i % 2 == 0 ? 800f : 0f, 10f);

			// 400 credits per 10 s across 4 harvesters is 10 per harvester per second. The residual
			// swing comes from where in the alternating pattern the last sample fell, and is small
			// enough that the caller's own smoothing removes it; averaging the per-sample ratios
			// instead would report roughly half, and measurably did.
			Assert.That(estimator.IncomePerHarvester, Is.EqualTo(10f).Within(1.0f),
				"Averaging the per-sample ratios would report roughly half this, and did.");
		}

		[TestCase(TestName = "It tracks the near ore running out.")]
		public void TracksDecliningIncome()
		{
			var estimator = new EconomyEstimator();
			for (var i = 0; i < 30; i++)
				estimator.Observe(4, 600f, 10f);

			var rich = estimator.IncomePerHarvester;
			Assert.That(rich, Is.EqualTo(15f).Within(0.5f));

			// The patch runs dry and the trip gets longer.
			for (var i = 0; i < 30; i++)
				estimator.Observe(4, 200f, 10f);

			Assert.That(estimator.IncomePerHarvester, Is.LessThan(rich * 0.7f),
				"Income falling while the harvester count holds is the signal that the map is drying up.");
			Assert.That(estimator.IncomePerHarvester, Is.EqualTo(5f).Within(0.5f));
		}

		[TestCase(TestName = "Nonsense observations are ignored.")]
		public void RejectsNonsense()
		{
			var estimator = new EconomyEstimator();
			estimator.Observe(0, 500f, 10f);
			estimator.Observe(4, 500f, 0f);
			estimator.Observe(4, -100f, 10f);

			Assert.That(estimator.Samples, Is.EqualTo(0));
			Assert.That(estimator.IncomePerHarvester, Is.EqualTo(16f),
				"With nothing valid observed it must still report a usable rate.");
		}

		[TestCase(TestName = "A larger sample carries more weight than a smaller one.")]
		public void SamplesAreWeightedByEvidence()
		{
			// One long observation at 20/harvester/s, then one very short one at 0. The short
			// sample must barely move the estimate, because it is barely any evidence.
			var estimator = new EconomyEstimator();
			estimator.Observe(4, 4000f, 50f);
			var before = estimator.IncomePerHarvester;
			estimator.Observe(4, 0f, 1f);

			Assert.That(before, Is.EqualTo(20f).Within(0.01f));
			Assert.That(estimator.IncomePerHarvester, Is.GreaterThan(before * 0.9f));
		}
	}
}
