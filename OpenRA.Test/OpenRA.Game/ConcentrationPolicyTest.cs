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
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

namespace OpenRA.Test
{
	/// <summary>
	/// Force concentration (reqs 343-349). The rule under test is that local superiority decides
	/// battles and total army size does not: an army larger overall but equal everywhere wins
	/// nothing.
	/// </summary>
	[TestFixture]
	sealed class ConcentrationPolicyTest
	{
		static Front F(int region, float own, float enemy, bool main = false)
		{
			return new Front(region, own, enemy, main);
		}

		[TestCase(TestName = "The main effort is the front where strength buys the most advantage (req 343).")]
		public void MainEffortIsTheBestRatio()
		{
			var fronts = new[]
			{
				F(1, own: 100f, enemy: 100f),
				F(2, own: 60f, enemy: 20f),
				F(3, own: 40f, enemy: 80f)
			};

			// Region 2 is not the largest force, but it is where the coalition is strongest
			// relative to what it faces - which is where an attack actually takes ground.
			Assert.That(ConcentrationPolicy.SelectMainEffort(fronts), Is.EqualTo(2));
		}

		[TestCase(TestName = "Local superiority needs a real edge, not parity (req 346).")]
		public void SuperiorityIsNotParity()
		{
			Assert.That(ConcentrationPolicy.HasLocalSuperiority(F(1, 150f, 100f)), Is.True);
			Assert.That(ConcentrationPolicy.HasLocalSuperiority(F(1, 100f, 100f)), Is.False,
				"Attacking at 1:1 trades evenly, which does not take ground.");
			Assert.That(ConcentrationPolicy.HasLocalSuperiority(F(1, 10f, 0f)), Is.True,
				"An uncontested front is superiority by definition.");
		}

		[TestCase(TestName = "An even spread across fronts is detected as having no main effort (req 345).")]
		public void EvenSpreadIsAFailureState()
		{
			var even = new[] { F(1, 100f, 50f), F(2, 100f, 50f), F(3, 100f, 50f) };
			Assert.That(ConcentrationPolicy.IsSpreadEvenly(even), Is.True,
				"Equal effort everywhere means no main effort exists, whatever the plan says.");

			var concentrated = new[] { F(1, 250f, 50f), F(2, 30f, 50f), F(3, 20f, 50f) };
			Assert.That(ConcentrationPolicy.IsSpreadEvenly(concentrated), Is.False);

			Assert.That(ConcentrationPolicy.IsSpreadEvenly([F(1, 100f, 50f)]), Is.False,
				"A single front cannot be spread evenly across anything.");
		}

		[TestCase(TestName = "Concentration is refused when a front left behind cannot hold (reqs 348, 349).")]
		public void ConcentrationMustNotExposeTheBase()
		{
			var safe = new[] { F(1, 200f, 50f), F(2, 60f, 100f) };
			Assert.That(ConcentrationPolicy.ConcentrationIsSafe(safe, mainEffortRegion: 1), Is.True);

			// A breakthrough bought by losing the base is not an advantage.
			var exposed = new[] { F(1, 200f, 50f), F(2, 10f, 100f) };
			Assert.That(ConcentrationPolicy.ConcentrationIsSafe(exposed, mainEffortRegion: 1), Is.False);
		}

		[TestCase(TestName = "Massing requires both superiority there and safety elsewhere (req 347).")]
		public void MassingNeedsBothConditions()
		{
			var fronts = new[] { F(1, 300f, 100f), F(2, 80f, 100f) };
			Assert.That(ConcentrationPolicy.ShouldMass(fronts, candidateRegion: 1), Is.True);

			// Superiority nowhere means there is nothing to mass against yet.
			var noEdge = new[] { F(1, 100f, 100f), F(2, 100f, 100f) };
			Assert.That(ConcentrationPolicy.ShouldMass(noEdge, candidateRegion: 1), Is.False);

			Assert.That(ConcentrationPolicy.ShouldMass(fronts, candidateRegion: 99), Is.False,
				"A front that does not exist cannot be massed against.");
		}

		[TestCase(TestName = "Secondary operations are funded only once the main effort is winning (req 344).")]
		public void SecondaryBudgetFollowsTheMainEffort()
		{
			Assert.That(ConcentrationPolicy.SecondaryBudget(F(1, 300f, 100f), 0.3f), Is.EqualTo(0.3f));
			Assert.That(ConcentrationPolicy.SecondaryBudget(F(1, 100f, 100f), 0.3f), Is.Zero,
				"With the main effort at parity, every unit spent elsewhere is a unit not deciding it.");
		}

		[TestCase(TestName = "Main-effort selection is deterministic so allied bots agree.")]
		public void SelectionIsDeterministic()
		{
			var a = new[] { F(1, 100f, 50f), F(2, 100f, 50f) };
			var b = new[] { F(2, 100f, 50f), F(1, 100f, 50f) };

			Assert.That(ConcentrationPolicy.SelectMainEffort(a), Is.EqualTo(ConcentrationPolicy.SelectMainEffort(b)),
				"Equidistant candidates must resolve identically regardless of input order.");
		}

		[TestCase(TestName = "Empty and degenerate inputs are handled without throwing.")]
		public void DegenerateInput()
		{
			Assert.That(ConcentrationPolicy.SelectMainEffort([]), Is.EqualTo(-1));
			Assert.That(ConcentrationPolicy.SelectMainEffort(null), Is.EqualTo(-1));
			Assert.That(ConcentrationPolicy.IsSpreadEvenly(null), Is.False);
			Assert.That(ConcentrationPolicy.ConcentrationIsSafe([], 1), Is.False);
			Assert.That(ConcentrationPolicy.IsSpreadEvenly([F(1, 0f, 0f), F(2, 0f, 0f)]), Is.False,
				"An army of zero strength is not spread; it does not exist.");
		}
	}
}
