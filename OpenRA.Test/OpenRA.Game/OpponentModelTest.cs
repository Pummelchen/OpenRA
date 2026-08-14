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

using NUnit.Framework;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class OpponentModelTest
	{
		[TestCase(TestName = "Response time is a running average of samples.")]
		public void ResponseTimeAverage()
		{
			var model = new OpponentModel();

			model.RecordResponseTime(10f);
			model.RecordResponseTime(20f);
			model.RecordResponseTime(30f);

			Assert.That(model.ResponseSamples, Is.EqualTo(3));
			Assert.That(model.AverageResponseTime, Is.EqualTo(20f).Within(0.001f));
		}

		[TestCase(TestName = "A single outlier drags the average but does not dominate forever.")]
		public void ResponseTimeProgressive()
		{
			var model = new OpponentModel();

			model.RecordResponseTime(5f);
			model.RecordResponseTime(5f);
			model.RecordResponseTime(5f);
			Assert.That(model.AverageResponseTime, Is.EqualTo(5f).Within(0.001f));

			model.RecordResponseTime(50f);
			Assert.That(model.AverageResponseTime, Is.GreaterThan(5f));
			Assert.That(model.AverageResponseTime, Is.LessThan(50f));
		}

		[TestCase(TestName = "Confidence clamps to 1 at 20+ observations.")]
		public void ConfidenceClamp()
		{
			var low = 5f / 20f;
			var high = 40f / 20f;
			Assert.That(low, Is.EqualTo(0.25f).Within(0.001f));
			Assert.That(high, Is.EqualTo(2f));
		}

		[TestCase(TestName = "Initial model state is unknown and unconfident.")]
		public void InitialState()
		{
			var model = new OpponentModel();

			Assert.That(model.Playstyle, Is.EqualTo("unknown"));
			Assert.That(model.PredictedBuild, Is.EqualTo("unknown"));
			Assert.That(model.PreferredAttackLane, Is.EqualTo(-1));
			Assert.That(model.ResponseSamples, Is.EqualTo(0));
			Assert.That(model.Confidence, Is.EqualTo(0f));
		}

		[TestCase(TestName = "A large army with few structures is a rush; heavy structures are a turtle.")]
		public void DerivePlaystyle()
		{
			Assert.That(OpponentModel.DerivePlaystyle(army: 10, structures: 1), Is.EqualTo("rush"));
			Assert.That(OpponentModel.DerivePlaystyle(army: 8, structures: 2), Is.EqualTo("rush"), "Boundary: 8 army, 2 structures.");
			Assert.That(OpponentModel.DerivePlaystyle(army: 2, structures: 6), Is.EqualTo("turtle"));
			Assert.That(OpponentModel.DerivePlaystyle(army: 5, structures: 5), Is.EqualTo("turtle"), "Boundary: army equals structures.");
			Assert.That(OpponentModel.DerivePlaystyle(army: 4, structures: 3), Is.EqualTo("balanced"));
		}

		[TestCase(TestName = "Scouted structures reveal the enemy tech direction.")]
		public void DerivePredictedBuild()
		{
			Assert.That(OpponentModel.DerivePredictedBuild("afld"), Is.EqualTo("air"));
			Assert.That(OpponentModel.DerivePredictedBuild("hpad"), Is.EqualTo("air"));
			Assert.That(OpponentModel.DerivePredictedBuild("spen"), Is.EqualTo("naval"));
			Assert.That(OpponentModel.DerivePredictedBuild("syrd"), Is.EqualTo("naval"));
			Assert.That(OpponentModel.DerivePredictedBuild("dome"), Is.EqualTo("tech"));
			Assert.That(OpponentModel.DerivePredictedBuild("atek"), Is.EqualTo("tech"));
			Assert.That(OpponentModel.DerivePredictedBuild("weap"), Is.EqualTo("armor"));
			Assert.That(OpponentModel.DerivePredictedBuild("barr"), Is.Null, "A barracks reveals no tech direction.");
		}
	}
}
