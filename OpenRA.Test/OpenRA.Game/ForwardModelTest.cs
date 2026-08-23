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
using System.Diagnostics;
using NUnit.Framework;
using OpenRA.Mods.Common.Commander.Model;
using OpenRA.Mods.Common.Commander.Terrain;

namespace OpenRA.Test
{
	/// <summary>
	/// <para>
	/// The forward model: the piece the previous commander did not have, and whose absence is why it
	/// could not attack. Without a way to say what the state will look like later, posture can only
	/// be a function of the present - and every attack looks like a mistake at the moment it starts
	/// costing units.
	/// </para>
	/// <para>
	/// Correctness here is not "predicts the game exactly". It is that the model is monotone in the
	/// things that matter, conserves what should be conserved, and is cheap enough to search.
	/// </para>
	/// </summary>
	[TestFixture]
	sealed class ForwardModelTest
	{
		const int R = RoleStats.Roles;

		/// <summary>Three rooms in a row: two ends and a middle, with the far corridor narrower.</summary>
		static RegionGraph ThreeRoomMap()
		{
			const int W = 91, H = 41;
			return RegionGraph.Build(W, H, (x, y) =>
			{
				if (x <= 0 || y <= 0 || x >= W - 1 || y >= H - 1)
					return false;

				if (x == 30)
					return y >= 16 && y < 26;

				if (x == 60)
					return y >= 19 && y < 22;

				return true;
			}, RegionGraph.Settings.Default);
		}

		static RoleStats Uniform(float damage = 0.01f, float hitPoints = 1f)
		{
			var d = new float[R * R];
			Array.Fill(d, damage);
			var h = new float[R];
			Array.Fill(h, hitPoints);
			return new RoleStats(d, h);
		}

		static ForwardModel Model(out RegionGraph graph, ForwardModel.Parameters parameters = null)
		{
			graph = ThreeRoomMap();
			return new ForwardModel(graph, Uniform(), parameters);
		}

		/// <summary>
		/// A model that carries a measured army trend forward in full. Used where the mechanism is
		/// under test rather than the damping - the shrinkage factor has its own test, and baking it
		/// into every expectation would mean re-deriving arithmetic in eight places each time it is
		/// re-measured.
		/// </summary>
		static ForwardModel FullTrendModel(out RegionGraph graph)
		{
			graph = ThreeRoomMap();
			return new ForwardModel(graph, Uniform(), new ForwardModel.Parameters { ArmyTrendConfidence = 1f });
		}

		[TestCase(TestName = "Income starts from what was measured, not from a formula.")]
		public void IncomeIsAnchoredToObservation()
		{
			var model = Model(out _);
			var player = new PlayerState(3)
			{
				Harvesters = 8,
				Refineries = 2,
				ObservedIncomePerSecond = 240f,
				ObservedHarvesters = 8,
			};

			// With the harvester count unchanged the forecast must equal the observation exactly.
			// Anything else means the model is second-guessing a measurement.
			Assert.That(model.IncomePerSecond(player), Is.EqualTo(240f));
		}

		[TestCase(TestName = "Harvesters have diminishing returns.")]
		public void HarvestersDiminish()
		{
			var model = Model(out _);
			var player = new PlayerState(3)
			{
				Harvesters = 8,
				Refineries = 2,
				ObservedIncomePerSecond = 240f,
				ObservedHarvesters = 8,
			};

			player.Harvesters = 16;
			var doubled = model.IncomePerSecond(player);

			// Doubling the harvesters must raise income, but not double it: they queue at the same
			// refinery and drive to the same ore. A linear model over-forecast by about a fifth in
			// real games, and would recommend a harvester at any price.
			Assert.That(doubled, Is.GreaterThan(240f));
			Assert.That(doubled, Is.LessThan(480f),
				"Linear returns would make expansion unconditionally correct, which it is not.");

			player.Harvesters = 4;
			Assert.That(model.IncomePerSecond(player), Is.LessThan(240f), "And losing them must cost income.");
		}

		[TestCase(TestName = "No refinery means no income, whatever is driving about.")]
		public void RefineriesAreRequired()
		{
			var model = Model(out _);
			var player = new PlayerState(3)
			{
				Harvesters = 10,
				Refineries = 0,
				ObservedIncomePerSecond = 240f,
				ObservedHarvesters = 10,
			};

			Assert.That(model.IncomePerSecond(player), Is.EqualTo(0f));

			player.Refineries = 1;
			Assert.That(model.IncomePerSecond(player), Is.GreaterThan(0f));

			player.Harvesters = 0;
			Assert.That(model.IncomePerSecond(player), Is.EqualTo(0f));
		}

		[TestCase(TestName = "An unobserved player falls back to a flat rate.")]
		public void UnobservedIncomeFallsBack()
		{
			// The enemy's earnings cannot be seen, so their income has to be assumed rather than
			// measured. It must still be non-zero, or every plan would assume they are broke.
			var model = Model(out _);
			var player = new PlayerState(3) { Harvesters = 5, Refineries = 1 };

			Assert.That(model.IncomePerSecond(player), Is.GreaterThan(0f));
		}

		[TestCase(TestName = "Cash accumulates and production converts it to army.")]
		public void ProductionConvertsCashToArmy()
		{
			var model = FullTrendModel(out _);
			var state = new AbstractState(3);
			state.Self.Harvesters = 4;
			state.Self.Refineries = 2;
			state.Self.ObservedIncomePerSecond = 40f;
			state.Self.ObservedHarvesters = 4;
			state.Self.ProductionThroughput = 20f;
			state.Self.ArmyGrowthPerSecond = 20f;
			state.Self.Cash = 1000f;

			var before = state.Self.ArmyValue();
			var next = model.Step(state, new MacroAction(MacroVerb.Produce, 0), new MacroAction(MacroVerb.Defend, 0), 10f);

			Assert.That(next.Self.ArmyValue(), Is.GreaterThan(before));
			Assert.That(next.Self.ArmyValue(), Is.EqualTo(200f).Within(0.01f),
				"Ten seconds at twenty credits per second is two hundred credits of army.");
			Assert.That(next.Self.Cash, Is.LessThan(state.Self.Cash + (model.IncomePerSecond(state.Self) * 10f)));
			Assert.That(state.Self.ArmyValue(), Is.EqualTo(before),
				"Step must not modify the state it was given; the search expands several actions from one node.");
		}

		[TestCase(TestName = "Production is bounded by cash as well as by throughput.")]
		public void ProductionIsBoundedByCash()
		{
			// The coupling that instant-build cheats exposed the hard way: removing the time
			// constraint does not make units free, it makes the whole cost fall due at once.
			var model = FullTrendModel(out _);
			var state = new AbstractState(3);
			state.Self.ProductionThroughput = 1000f;
			state.Self.ArmyGrowthPerSecond = 1000f;
			state.Self.Cash = 150f;

			var next = model.Step(state, new MacroAction(MacroVerb.Produce, 0), new MacroAction(MacroVerb.Defend, 0), 10f);

			Assert.That(next.Self.ArmyValue(), Is.EqualTo(150f).Within(0.01f),
				"A queue that can absorb ten thousand credits still cannot spend money that is not there.");
			Assert.That(next.Self.Cash, Is.EqualTo(0f).Within(0.01f));
		}

		[TestCase(TestName = "Expanding costs cash now and pays income later.")]
		public void ExpansionTradesCashForIncome()
		{
			var model = Model(out _);
			var state = new AbstractState(3);
			state.Self.Cash = 5000f;
			state.Self.Harvesters = 2;
			state.Self.Refineries = 2;
			state.Self.ObservedIncomePerSecond = 30f;
			state.Self.ObservedHarvesters = 2;

			var incomeBefore = model.IncomePerSecond(state.Self);
			var next = model.Step(state, new MacroAction(MacroVerb.Expand, 1), new MacroAction(MacroVerb.Defend, 0), 5f);

			Assert.That(next.Self.Harvesters, Is.EqualTo(3));
			Assert.That(next.Self.Cash, Is.LessThan(state.Self.Cash));
			Assert.That(model.IncomePerSecond(next.Self), Is.GreaterThan(incomeBefore),
				"An expansion that does not raise income is not an expansion.");
		}

		[TestCase(TestName = "An expansion that cannot be afforded does not happen.")]
		public void ExpansionRequiresCash()
		{
			var model = Model(out _);
			var state = new AbstractState(3);
			state.Self.Cash = 100f;

			var next = model.Step(state, new MacroAction(MacroVerb.Expand, 1), new MacroAction(MacroVerb.Defend, 0), 5f);
			Assert.That(next.Self.Harvesters, Is.EqualTo(0));
			Assert.That(next.Self.Cash, Is.EqualTo(100f).Within(0.01f));
		}

		[TestCase(TestName = "Force takes time to cross the map, and further costs longer.")]
		public void MovementTakesTime()
		{
			var model = Model(out var graph);
			Assert.That(graph.Regions, Has.Length.EqualTo(3));

			var left = graph.RegionAt(10, 20);
			var middle = graph.RegionAt(45, 20);
			var right = graph.RegionAt(80, 20);

			Assert.That(model.TravelSeconds(left, left), Is.EqualTo(0));
			Assert.That(model.TravelSeconds(left, middle), Is.GreaterThan(0));
			Assert.That(model.TravelSeconds(left, right), Is.GreaterThan(model.TravelSeconds(left, middle)),
				"Crossing two rooms must cost more than crossing one, or distance means nothing to the plan.");

			var state = new AbstractState(3);
			state.Self.SetForce(left, CombatRole.Armor, 1000f);

			var next = model.Step(state, new MacroAction(MacroVerb.Attack, right), new MacroAction(MacroVerb.Defend, 0), 5f);
			Assert.That(next.Self.ArmyValueIn(left), Is.GreaterThan(0f), "Five seconds does not cross a map.");
			Assert.That(next.Self.ArmyValueIn(right), Is.GreaterThan(0f), "But some of it is on the way.");
			Assert.That(next.Self.ArmyValue(), Is.EqualTo(1000f).Within(0.01f),
				"Marching must move force, not create or destroy it.");
		}

		[TestCase(TestName = "Static defences do not march.")]
		public void DefencesStayPut()
		{
			var model = Model(out var graph);
			var left = graph.RegionAt(10, 20);
			var right = graph.RegionAt(80, 20);

			var state = new AbstractState(3);
			state.Self.SetForce(left, CombatRole.Defense, 500f);
			state.Self.SetForce(left, CombatRole.Armor, 500f);

			var next = model.Step(state, new MacroAction(MacroVerb.Attack, right), new MacroAction(MacroVerb.Defend, 0), 60f);

			Assert.That(next.Self.ForceValue(left, CombatRole.Defense), Is.EqualTo(500f).Within(0.01f),
				"A pillbox cannot join the assault, and a model that lets it will overvalue attacking.");
			Assert.That(next.Self.ForceValue(right, CombatRole.Defense), Is.EqualTo(0f));
			Assert.That(next.Self.ForceValue(left, CombatRole.Armor), Is.LessThan(500f));
		}

		[TestCase(TestName = "Harassment commits a detachment, not the army.")]
		public void HarassSendsOnlyADetachment()
		{
			var model = Model(out var graph);
			var left = graph.RegionAt(10, 20);
			var right = graph.RegionAt(80, 20);

			var state = new AbstractState(3);
			state.Self.SetForce(left, CombatRole.Armor, 1000f);

			var harass = model.Step(state, new MacroAction(MacroVerb.Harass, right), new MacroAction(MacroVerb.Defend, 0), 600f);
			var attack = model.Step(state, new MacroAction(MacroVerb.Attack, right), new MacroAction(MacroVerb.Defend, 0), 600f);

			Assert.That(harass.Self.ArmyValueIn(right), Is.LessThan(attack.Self.ArmyValueIn(right)),
				"If a raid commits as much as an assault, the commander has no way to raid.");
			Assert.That(harass.Self.ArmyValueIn(left), Is.GreaterThan(0f));
		}

		[TestCase(TestName = "Both sides fight where both sides are.")]
		public void ForcesInContactFight()
		{
			var model = Model(out var graph);
			var middle = graph.RegionAt(45, 20);

			var state = new AbstractState(3);
			state.Self.SetForce(middle, CombatRole.Armor, 1000f);
			state.Enemy.SetForce(middle, CombatRole.Armor, 1000f);

			var next = model.Step(state, new MacroAction(MacroVerb.Defend, middle), new MacroAction(MacroVerb.Defend, middle), 30f);

			Assert.That(next.Self.ArmyValueIn(middle), Is.LessThan(1000f));
			Assert.That(next.Enemy.ArmyValueIn(middle), Is.LessThan(1000f));
			Assert.That(next.Self.ArmyValueIn(middle), Is.EqualTo(next.Enemy.ArmyValueIn(middle)).Within(1f),
				"An even fight with neither side attacking is symmetric.");
		}

		[TestCase(TestName = "Attacking into a defended region costs more than meeting in the open.")]
		public void DefenderHasTheEdge()
		{
			var model = Model(out var graph);
			var middle = graph.RegionAt(45, 20);

			AbstractState Contested()
			{
				var s = new AbstractState(3);
				s.Self.SetForce(middle, CombatRole.Armor, 1000f);
				s.Enemy.SetForce(middle, CombatRole.Armor, 1000f);
				return s;
			}

			var assaulting = model.Step(Contested(), new MacroAction(MacroVerb.Attack, middle),
				new MacroAction(MacroVerb.Defend, middle), 30f);
			var meeting = model.Step(Contested(), new MacroAction(MacroVerb.Defend, middle),
				new MacroAction(MacroVerb.Defend, middle), 30f);

			Assert.That(assaulting.Self.ArmyValueIn(middle), Is.LessThan(meeting.Self.ArmyValueIn(middle)),
				"Walking onto prepared ground has to be priced, or the search will do it for free.");
		}

		[TestCase(TestName = "Control follows presence, but not instantly.")]
		public void ControlIsEarnedBySitting()
		{
			var model = Model(out var graph);
			var right = graph.RegionAt(80, 20);

			var state = new AbstractState(3);
			state.Self.SetForce(right, CombatRole.Armor, 1000f);

			var brief = model.Step(state, new MacroAction(MacroVerb.Defend, right), new MacroAction(MacroVerb.Defend, 0), 5f);
			Assert.That(brief.Control[right], Is.GreaterThan(0f));
			Assert.That(brief.Control[right], Is.LessThan(0.5f),
				"A region does not change hands because a column drove through it.");

			var sustained = model.Step(brief, new MacroAction(MacroVerb.Defend, right), new MacroAction(MacroVerb.Defend, 0), 120f);
			Assert.That(sustained.Control[right], Is.GreaterThan(brief.Control[right]));
		}

		[TestCase(TestName = "A step is cheap enough to search thousands of times.")]
		public void StepIsFastEnoughToSearch()
		{
			var model = Model(out var graph);
			var state = new AbstractState(graph.Regions.Length);
			state.Self.Cash = 5000f;
			state.Self.ProductionThroughput = 30f;
			state.Self.Harvesters = 4;
			state.Self.Refineries = 2;
			state.Self.SetForce(0, CombatRole.Armor, 2000f);
			state.Enemy.SetForce(graph.Regions.Length - 1, CombatRole.Armor, 2000f);

			const int Iterations = 20000;
			var self = new MacroAction(MacroVerb.Attack, graph.Regions.Length - 1);
			var enemy = new MacroAction(MacroVerb.Defend, graph.Regions.Length - 1);

			// Warm up, so the measurement is of the model and not of the JIT.
			for (var i = 0; i < 1000; i++)
				model.Step(state, self, enemy, 15f);

			// Best of three. This asks whether a step *can* be cheap enough to search, which is a
			// capability; a single timing on a loaded machine measures the scheduler instead.
			var microseconds = double.MaxValue;
			for (var attempt = 0; attempt < 3; attempt++)
			{
				var timer = Stopwatch.StartNew();
				for (var i = 0; i < Iterations; i++)
					model.Step(state, self, enemy, 15f);
				timer.Stop();
				microseconds = Math.Min(microseconds, timer.Elapsed.TotalMilliseconds * 1000.0 / Iterations);
			}

			// A two-minute lookahead at depth 8 needs thousands of rollouts inside a fifteen-second
			// review. The budget is 10 us; this asserts an order of magnitude of headroom so the
			// test does not fail on a busy machine while still catching a real regression.
			Assert.That(microseconds, Is.LessThan(100.0),
				$"Step took {microseconds:F2} us; the search cannot afford that.");
			TestContext.Out.WriteLine($"ForwardModel.Step: {microseconds:F2} us per call");
		}

		[TestCase(TestName = "Stepping is reproducible.")]
		public void SteppingIsDeterministic()
		{
			var model = Model(out var graph);
			var state = new AbstractState(graph.Regions.Length);
			state.Self.Cash = 3000f;
			state.Self.ProductionThroughput = 25f;
			state.Self.SetForce(0, CombatRole.Armor, 1200f);
			state.Enemy.SetForce(2, CombatRole.Infantry, 900f);

			var self = new MacroAction(MacroVerb.Attack, 2);
			var enemy = new MacroAction(MacroVerb.Defend, 2);

			var a = model.Step(state, self, enemy, 30f);
			var b = model.Step(state, self, enemy, 30f);

			Assert.That(b.Self.Forces, Is.EqualTo(a.Self.Forces));
			Assert.That(b.Enemy.Forces, Is.EqualTo(a.Enemy.Forces));
			Assert.That(b.Control, Is.EqualTo(a.Control));
			Assert.That(b.Self.Cash, Is.EqualTo(a.Self.Cash));
		}

		[TestCase(TestName = "The army forecast follows the growth actually measured.")]
		public void ArmyFollowsMeasuredGrowth()
		{
			var model = FullTrendModel(out var graph);
			var home = graph.RegionAt(10, 20);

			var state = new AbstractState(graph.Regions.Length);
			state.Self.SetForce(home, CombatRole.Armor, 1000f);
			state.Self.Cash = 5000f;
			state.Self.ProductionThroughput = 20f;
			state.Self.ArmyGrowthPerSecond = 10f;

			var next = model.Step(state, new MacroAction(MacroVerb.Produce, home),
				new MacroAction(MacroVerb.Defend, 0), 20f);

			Assert.That(next.Self.ArmyValue(), Is.EqualTo(1200f).Within(1f),
				"Twenty seconds of ten credits per second is two hundred credits of army.");
			Assert.That(next.Self.Cash, Is.LessThan(5000f), "And it is paid for.");
		}

		[TestCase(TestName = "A shrinking army is forecast to keep shrinking.")]
		public void ArmyCanShrink()
		{
			// Losses out of vision are not observable individually, but their net effect is - and a
			// model that can only forecast growth would over-state every army it ever predicts.
			var model = FullTrendModel(out var graph);
			var home = graph.RegionAt(10, 20);

			var state = new AbstractState(graph.Regions.Length);
			state.Self.SetForce(home, CombatRole.Armor, 1000f);
			state.Self.ArmyGrowthPerSecond = -20f;

			var next = model.Step(state, new MacroAction(MacroVerb.Defend, home),
				new MacroAction(MacroVerb.Defend, 0), 10f);

			Assert.That(next.Self.ArmyValue(), Is.EqualTo(800f).Within(1f));

			// And it cannot go below nothing.
			var wiped = model.Step(state, new MacroAction(MacroVerb.Defend, home),
				new MacroAction(MacroVerb.Defend, 0), 600f);
			Assert.That(wiped.Self.ArmyValue(), Is.EqualTo(0f).Within(0.01f));
		}

		[TestCase(TestName = "A measured army trend is carried forward only partly.")]
		public void ArmyTrendIsShrunkTowardNoChange()
		{
			// Measured over four matches on two maps, extrapolating the trend in full produced a
			// *worse* thirty-second forecast than assuming the army simply stays where it is -
			// 23-36% error against 20-24%. Army value at this horizon behaves close to a random
			// walk, so the trend is shrunk toward the no-change forecast. Damped beat both the full
			// extrapolation and the flat assumption, on three maps of four.
			var damped = Model(out var graph);
			var full = FullTrendModel(out _);

			AbstractState Growing()
			{
				var s = new AbstractState(graph.Regions.Length);
				s.Self.SetForce(0, CombatRole.Armor, 1000f);
				s.Self.Cash = 100000f;
				s.Self.ProductionThroughput = 100f;
				s.Self.ArmyGrowthPerSecond = 100f;
				return s;
			}

			var hold = new MacroAction(MacroVerb.Produce, 0);
			var idle = new MacroAction(MacroVerb.Defend, 0);

			var dampedArmy = damped.Step(Growing(), hold, idle, 30f).Self.ArmyValue();
			var fullArmy = full.Step(Growing(), hold, idle, 30f).Self.ArmyValue();

			Assert.That(dampedArmy, Is.GreaterThan(1000f), "The trend is not ignored...");
			Assert.That(dampedArmy, Is.LessThan(fullArmy), "...but neither is it taken at face value.");
		}

		[TestCase(TestName = "Unreachable regions report no travel time.")]
		public void UnreachableRegions()
		{
			// A solid wall: two regions with no connection, which is the naval question in miniature.
			const int W = 61, H = 41;
			var sealed_ = RegionGraph.Build(W, H, (x, y) =>
				x > 0 && y > 0 && x < W - 1 && y < H - 1 && x != 30, RegionGraph.Settings.Default);
			var model = new ForwardModel(sealed_, Uniform());

			var a = sealed_.RegionAt(10, 20);
			var b = sealed_.RegionAt(50, 20);
			Assert.That(model.TravelSeconds(a, b), Is.EqualTo(-1));
			Assert.That(model.TravelSeconds(a, 99), Is.EqualTo(-1), "Out-of-range regions must not throw.");

			var state = new AbstractState(sealed_.Regions.Length);
			state.Self.SetForce(a, CombatRole.Armor, 1000f);
			var next = model.Step(state, new MacroAction(MacroVerb.Attack, b), new MacroAction(MacroVerb.Defend, 0), 600f);

			Assert.That(next.Self.ArmyValueIn(b), Is.EqualTo(0f),
				"An army ordered somewhere it cannot walk must stay where it is.");
			Assert.That(next.Self.ArmyValueIn(a), Is.EqualTo(1000f).Within(0.01f));
		}
	}
}
