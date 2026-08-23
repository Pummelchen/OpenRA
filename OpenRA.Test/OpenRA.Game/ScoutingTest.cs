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

using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits.BotModules.Coalition;

namespace OpenRA.Test
{
	/// <summary>
	/// Reconnaissance: which unit scouts, and where it is sent (handbook §6). Scouting is the
	/// precondition for naming an offensive objective, so these failures are silent and total - a
	/// commander that cannot find a base spends the whole match reacting.
	/// </summary>
	[TestFixture]
	sealed class ScoutingTest
	{
		// The shipped numbers.
		static ScoutCandidate Dog() => new("dog", 200, 100);
		static ScoutCandidate Rifle() => new("e1", 100, 54);
		static ScoutCandidate Ranger() => new("jeep", 500, 164);

		[TestCase(TestName = "The configured preference wins when it is buildable.")]
		public void PreferenceBeatsScoring()
		{
			// On raw score the ranger edges the dog (7.33 to 7.07), but it costs two and a half
			// times as much and a scout is expected to be lost.
			var chosen = ScoutSelection.Preferred(["dog", "e1", "jeep"], [Rifle(), Ranger(), Dog()]);
			Assert.That(chosen, Is.EqualTo("dog"));
		}

		[TestCase(TestName = "An unbuildable preference falls through instead of leaving the commander blind.")]
		public void FallsThroughWhenUnavailable()
		{
			// A dog needs a Soviet kennel. An Allied commander that only knew the preference would
			// never scout at all - which is exactly what happened before this fallback existed.
			var chosen = ScoutSelection.Preferred(["dog", "e1", "jeep"], [Rifle(), Ranger()]);
			Assert.That(chosen, Is.EqualTo("e1"));

			var derived = ScoutSelection.Preferred(["dog"], [Rifle(), Ranger()]);
			Assert.That(derived, Is.Not.Null, "With no preference available, score decides.");
		}

		[TestCase(TestName = "Expensive units are never bought as scouts.")]
		public void ScoutsStayCheap()
		{
			var mammoth = new ScoutCandidate("4tnk", 2000, 85);
			Assert.That(ScoutSelection.Best([mammoth]), Is.Null,
				"A scout is expected to die; spending real money on one spends it in the wrong place.");
			Assert.That(ScoutSelection.Best([mammoth, Rifle()]), Is.EqualTo("e1"));
		}

		[TestCase(TestName = "Immobile or free units are not scouts.")]
		public void DegenerateCandidates()
		{
			Assert.That(ScoutSelection.Best([new ScoutCandidate("pbox", 400, 0)]), Is.Null);
			Assert.That(ScoutSelection.Best([new ScoutCandidate("free", 0, 100)]), Is.Null);
			Assert.That(ScoutSelection.Best([]), Is.Null);
			Assert.That(ScoutSelection.Best(null), Is.Null);
		}

		[TestCase(TestName = "The sweep aims at the map edge so a scout walks as far as it can.")]
		public void SweepTargetsTheEdge()
		{
			var home = new CPos(64, 64);
			var sweep = RadialScoutPattern.Sweep(home, 128, 128, stepDegrees: 45);

			Assert.That(sweep, Is.Not.Empty);

			// Every target sits near a boundary, not next to home: a scout aimed at a nearby point
			// stops there and reveals nothing beyond it.
			foreach (var target in sweep)
			{
				var nearEdge = target.X <= 4 || target.Y <= 4 || target.X >= 123 || target.Y >= 123;
				Assert.That(nearEdge, Is.True, $"{target} is not on the map edge.");
			}
		}

		[TestCase(TestName = "Bearings are spread around the compass, not clustered on one arc.")]
		public void SweepCoversAllBearings()
		{
			var sweep = RadialScoutPattern.Sweep(new CPos(64, 64), 128, 128, stepDegrees: 90);

			Assert.That(sweep.Count, Is.EqualTo(4));
			Assert.That(sweep.Any(c => c.X > 64), Is.True, "east");
			Assert.That(sweep.Any(c => c.X < 64), Is.True, "west");
			Assert.That(sweep.Any(c => c.Y > 64), Is.True, "south");
			Assert.That(sweep.Any(c => c.Y < 64), Is.True, "north");
		}

		[TestCase(TestName = "Interleaving means early losses still leave the sweep spread out.")]
		public void InterleavingSeparatesConsecutiveProbes()
		{
			var home = new CPos(64, 64);
			var sweep = RadialScoutPattern.Sweep(home, 128, 128, stepDegrees: 45);
			var interleaved = RadialScoutPattern.Interleave(sweep);

			Assert.That(interleaved.Count, Is.EqualTo(sweep.Count), "No bearing may be dropped.");
			Assert.That(interleaved.Distinct().Count(), Is.EqualTo(sweep.Count));

			// The first two probes should not be neighbours on the circle - sending them down
			// adjacent lanes is the same mistake as sending them all one way.
			var firstIndex = sweep.ToList().IndexOf(interleaved[0]);
			var secondIndex = sweep.ToList().IndexOf(interleaved[1]);
			Assert.That(System.Math.Abs(firstIndex - secondIndex), Is.GreaterThan(1));
		}

		[TestCase(TestName = "Explored and unreachable bearings are skipped.")]
		public void SweepSkipsKnownAndBlockedGround()
		{
			var home = new CPos(64, 64);
			var all = RadialScoutPattern.UnexploredSweep(home, 128, 128,
				isExplored: _ => false, isReachable: _ => true, stepDegrees: 45);
			var none = RadialScoutPattern.UnexploredSweep(home, 128, 128,
				isExplored: _ => true, isReachable: _ => true, stepDegrees: 45);
			var blocked = RadialScoutPattern.UnexploredSweep(home, 128, 128,
				isExplored: _ => false, isReachable: _ => false, stepDegrees: 45);

			Assert.That(all, Is.Not.Empty);
			Assert.That(none, Is.Empty, "Ground already seen is not worth a scout.");
			Assert.That(blocked, Is.Empty, "Nor is ground the scout cannot reach.");
		}

		[TestCase(TestName = "A degenerate map yields no sweep rather than throwing.")]
		public void DegenerateMap()
		{
			Assert.That(RadialScoutPattern.Sweep(new CPos(0, 0), 0, 0), Is.Empty);
			Assert.That(RadialScoutPattern.Interleave(null), Is.Empty);
		}
	}
}
