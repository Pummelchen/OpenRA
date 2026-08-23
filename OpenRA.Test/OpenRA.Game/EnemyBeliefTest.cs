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

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Commander.Model;

namespace OpenRA.Test
{
	/// <summary>
	/// The belief state, and above all its treatment of negative evidence - the half of Bayesian
	/// updating that scouting exists to produce, and the half most bots leave out.
	/// </summary>
	[TestFixture]
	sealed class EnemyBeliefTest
	{
		/// <summary>A line of five regions: 0-1-2-3-4.</summary>
		static EnemyBelief Line(int regions = 5, float diffusion = 0.02f)
		{
			IEnumerable<int> Neighbours(int r)
			{
				if (r > 0)
					yield return r - 1;

				if (r < regions - 1)
					yield return r + 1;
			}

			return new EnemyBelief(regions, Neighbours) { DiffusionPerSecond = diffusion };
		}

		static float[] Armour(float credits)
		{
			var f = new float[RoleStats.Roles];
			f[(int)CombatRole.Armor] = credits;
			return f;
		}

		[TestCase(TestName = "Seeing something records it exactly.")]
		public void ObservationIsExact()
		{
			var belief = Line();
			belief.Observe(2, Armour(3000f), tick: 100);

			Assert.That(belief.Expected(2, CombatRole.Armor), Is.EqualTo(3000f));
			Assert.That(belief.ExpectedIn(2), Is.EqualTo(3000f));
			Assert.That(belief.TicksSinceSeen(2, 100), Is.EqualTo(0));
		}

		[TestCase(TestName = "Looking somewhere and finding nothing eliminates it.")]
		public void NegativeEvidenceEliminates()
		{
			// The property this whole class exists for. A decay timer would leave a ghost here
			// indefinitely; a belief state removes it, and the enemy's real strength is thereby
			// concentrated into the places still unseen.
			var belief = Line();
			belief.Observe(2, Armour(3000f), tick: 100);
			belief.Propagate(30f);

			Assert.That(belief.Expected(1, CombatRole.Armor), Is.GreaterThan(0f), "Belief spread next door...");

			belief.ObserveEmpty(1, tick: 200);
			Assert.That(belief.Expected(1, CombatRole.Armor), Is.EqualTo(0f),
				"...and looking there and finding nothing must remove it, not fade it.");
			Assert.That(belief.Expected(2, CombatRole.Armor), Is.GreaterThan(0f),
				"Without disturbing what is still believed elsewhere.");
		}

		[TestCase(TestName = "Belief spreads only along terrain the enemy can cross.")]
		public void BeliefSpreadsAlongTheGraph()
		{
			// Regions 0-1-2 connected, 3-4 connected, no link between the halves. Belief must not
			// cross a gap that units cannot cross - the graph already encodes that, so nothing here
			// needs to know about water.
			IEnumerable<int> Split(int r)
			{
				if (r == 0)
					yield return 1;
				else if (r == 1)
				{
					yield return 0;
					yield return 2;
				}
				else if (r == 2)
					yield return 1;
				else if (r == 3)
					yield return 4;
				else if (r == 4)
					yield return 3;
			}

			var belief = new EnemyBelief(5, Split) { DiffusionPerSecond = 0.05f };
			belief.Observe(0, Armour(1000f), tick: 0);

			for (var i = 0; i < 20; i++)
				belief.Propagate(10f);

			Assert.That(belief.Expected(1, CombatRole.Armor), Is.GreaterThan(0f));
			Assert.That(belief.Expected(2, CombatRole.Armor), Is.GreaterThan(0f));
			Assert.That(belief.Expected(3, CombatRole.Armor), Is.EqualTo(0f),
				"An unreachable region can hold no belief, however long it has been.");
			Assert.That(belief.Expected(4, CombatRole.Armor), Is.EqualTo(0f));
		}

		[TestCase(TestName = "Spreading conserves what is believed to exist.")]
		public void PropagationConserves()
		{
			// Units that leave one region arrive in another; they do not multiply, and they do not
			// evaporate. A model that lost mass here would quietly forget the enemy exists.
			var belief = Line();
			belief.Observe(2, Armour(4000f), tick: 0);

			for (var i = 0; i < 50; i++)
				belief.Propagate(10f);

			Assert.That(belief.ExpectedTotal(), Is.EqualTo(4000f).Within(1f));
		}

		[TestCase(TestName = "Static defences do not wander.")]
		public void DefencesDoNotMove()
		{
			// A pillbox believed at a choke is still at that choke a minute later. Letting belief
			// about it diffuse would make the commander think a fortified position had softened.
			var belief = Line();
			var defence = new float[RoleStats.Roles];
			defence[(int)CombatRole.Defense] = 2000f;
			belief.Observe(2, defence, tick: 0);

			for (var i = 0; i < 30; i++)
				belief.Propagate(10f);

			Assert.That(belief.Expected(2, CombatRole.Defense), Is.EqualTo(2000f).Within(0.01f));
			Assert.That(belief.Expected(1, CombatRole.Defense), Is.EqualTo(0f));
		}

		[TestCase(TestName = "Belief spreads gradually, not instantly.")]
		public void SpreadIsGradual()
		{
			// Losing contact must not immediately smear the enemy across the map. They have to
			// actually drive there, and how long that takes is what the region graph is for.
			var belief = Line();
			belief.Observe(2, Armour(1000f), tick: 0);

			belief.Propagate(1f);
			var nearby = belief.Expected(1, CombatRole.Armor);
			Assert.That(nearby, Is.GreaterThan(0f));
			Assert.That(nearby, Is.LessThan(100f), "One second does not move an army next door.");
			Assert.That(belief.Expected(4, CombatRole.Armor), Is.EqualTo(0f),
				"And certainly not two regions away.");
		}

		[TestCase(TestName = "A scout is sent where belief is both large and stale.")]
		public void UncertaintyGuidesScouting()
		{
			var belief = Line();
			belief.Observe(0, Armour(50f), tick: 10000);
			belief.Observe(1, Armour(5000f), tick: 10000);
			belief.ObserveEmpty(2, tick: 10000);

			// Region 3 has never been looked at; regions 0-2 were just seen. The one worth a scout
			// is the one still unknown.
			var target = belief.MostUncertainRegion(10000);
			Assert.That(new[] { 3, 4 }, Does.Contain(target),
				"Somewhere just observed is not worth scouting, however much is in it.");
		}

		[TestCase(TestName = "Belief writes into the state the search plans on.")]
		public void AppliesToState()
		{
			var belief = Line();
			belief.Observe(3, Armour(2500f), tick: 0);

			var enemy = new PlayerState(5);
			belief.ApplyTo(enemy);

			Assert.That(enemy.ForceValue(3, CombatRole.Armor), Is.EqualTo(2500f));
			Assert.That(enemy.ArmyValue(), Is.EqualTo(2500f),
				"So the search plans against what is believed, not only against what is visible.");
		}

		[TestCase(TestName = "Out-of-range regions are answered, not thrown on.")]
		public void OutOfRangeIsSafe()
		{
			var belief = Line(3);
			Assert.That(belief.Expected(99, CombatRole.Armor), Is.EqualTo(0f));
			Assert.That(belief.ExpectedIn(-1), Is.EqualTo(0f));
			Assert.That(() => belief.Observe(99, Armour(1f), 0), Throws.Nothing);
			Assert.That(() => belief.ObserveEmpty(-1, 0), Throws.Nothing);
		}
	}
}
