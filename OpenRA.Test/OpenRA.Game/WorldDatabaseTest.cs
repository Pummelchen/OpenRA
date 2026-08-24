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

using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Commander.Model;

namespace OpenRA.Test
{
	/// <summary>
	/// The commander's shared per-match memory: what it has seen, how long ago, and who ordered what.
	/// </summary>
	[TestFixture]
	sealed class WorldDatabaseTest
	{
		const int Second = AbstractState.TicksPerSecond;

		static WorldDatabase Database() => new();

		[TestCase(TestName = "Losing sight of something ages it rather than forgetting it.")]
		public void UnseenBecomesStaleNotGone()
		{
			var database = Database();
			database.Observe(1, "fact", true, Allegiance.Enemy, new CPos(10, 10), 1f, 0, "scout");

			// Nothing is visible on the next sweep - the scout died.
			database.AgeUnseen(30 * Second, _ => false);

			var entry = database.Find(1);
			Assert.That(entry, Is.Not.Null);
			Assert.That(entry.Status, Is.EqualTo(RecordStatus.Stale),
				"An enemy that stopped being visible has not stopped existing.");
			Assert.That(entry.LastKnownCell, Is.EqualTo(new CPos(10, 10)),
				"Where it was last seen is the most useful thing the commander still knows.");
			Assert.That(database.EnemyStructures().Count(), Is.EqualTo(1),
				"A remembered base is still a base worth attacking.");
		}

		[TestCase(TestName = "Age is recorded, so 'nothing there' and 'nobody looked' stay distinguishable.")]
		public void AgeIsRecorded()
		{
			var database = Database();
			database.Observe(1, "proc", true, Allegiance.Enemy, new CPos(5, 5), 1f, 0, "scout");
			database.AgeUnseen(90 * Second, _ => false);

			var entry = database.Find(1);
			Assert.That(entry.SecondsSinceSeen(90 * Second), Is.EqualTo(90f).Within(0.001f));
			Assert.That(database.StaleFor(60f).Count(), Is.EqualTo(1));
			Assert.That(database.StaleFor(120f).Count(), Is.EqualTo(0));
		}

		[TestCase(TestName = "Confidence decays with how long ago something was actually seen.")]
		public void ConfidenceDecaysWithAge()
		{
			var database = Database();
			database.Observe(1, "weap", true, Allegiance.Enemy, new CPos(5, 5), 1f, 0, "scout");

			var live = database.Find(1);
			Assert.That(WorldDatabase.Confidence(live, 0), Is.EqualTo(1f),
				"Something being looked at right now is not in doubt.");

			database.AgeUnseen(30 * Second, _ => false);
			Assert.That(WorldDatabase.Confidence(live, 30 * Second, halfLifeSeconds: 30f),
				Is.EqualTo(0.5f).Within(0.01f), "One half-life, half the confidence.");

			Assert.That(WorldDatabase.Confidence(live, 120 * Second, halfLifeSeconds: 30f),
				Is.LessThan(0.1f), "A four-minute-old reading is barely evidence at all.");
		}

		[TestCase(TestName = "A structure rebuilt where one was destroyed is counted as a replacement.")]
		public void RebuildingIsVisible()
		{
			// The measured failure this exists to expose: against the rushing opponent the commander
			// destroyed 89 structures and the opponent finished the match holding 142. By the only
			// number anybody reported - structures destroyed - that match was going well.
			var database = Database();
			var spot = new CPos(20, 20);

			database.Observe(1, "proc", true, Allegiance.Enemy, spot, 1f, 0, "staff");
			database.RecordDestroyed(1, 10 * Second);

			Assert.That(database.EnemyRebuilds, Is.EqualTo(0));

			// A replacement of the same type appears - and deliberately NOT on the same ground,
			// because an opponent that loses a refinery builds the next one somewhere safer.
			database.Observe(2, "proc", true, Allegiance.Enemy, new CPos(40, 40), 1f, 60 * Second, "staff");

			Assert.That(database.EnemyRebuilds, Is.EqualTo(1),
				"Destroying a refinery and watching it go back up is attrition, not demolition.");
			Assert.That(database.Find(2).RebuildCount, Is.EqualTo(1));
			Assert.That(database.EnemyStructures().Count(), Is.EqualTo(1),
				"The destroyed one is gone; the replacement stands.");
		}

		[TestCase(TestName = "A death that was witnessed is certain; the entry is not merely stale.")]
		public void DestroyedIsCertain()
		{
			var database = Database();
			database.Observe(1, "e1", false, Allegiance.Enemy, new CPos(1, 1), 1f, 0, "staff");
			database.RecordDestroyed(1, 5 * Second);

			Assert.That(database.Find(1).Status, Is.EqualTo(RecordStatus.Destroyed));
			Assert.That(database.Standing(Allegiance.Enemy).Count(), Is.EqualTo(0));

			// Ageing must not resurrect it into merely-unseen.
			database.AgeUnseen(60 * Second, _ => false);
			Assert.That(database.Find(1).Status, Is.EqualTo(RecordStatus.Destroyed));
		}

		[TestCase(TestName = "Orders record who issued them and when.")]
		public void OrderProvenanceIsRecorded()
		{
			// A single match logged more than two thousand rejected order conflicts. Without a
			// record of who ordered what, a manager cannot tell whether a unit is idle because
			// nobody wants it or because somebody else is already using it.
			var database = Database();
			database.Observe(7, "3tnk", false, Allegiance.Self, new CPos(3, 3), 1f, 0, "staff");
			database.RecordOrder(7, "Attack R4", "attack-coordination", 100);

			var entry = database.Find(7);
			Assert.That(entry.LastOrder, Is.EqualTo("Attack R4"));
			Assert.That(entry.OrderedBy, Is.EqualTo("attack-coordination"));
			Assert.That(entry.SecondsSinceOrdered(100 + (10 * Second)), Is.EqualTo(10f).Within(0.001f));
		}

		[TestCase(TestName = "Being seen is not being looked after.")]
		public void ObservationIsNotAttendance()
		{
			// The distinction the whole upkeep idea rests on. A damaged power plant sits in plain
			// sight of the entire staff for a whole match while nobody repairs it, and by every
			// visibility measure it is perfectly well attended.
			var database = Database();
			database.Observe(1, "powr", true, Allegiance.Self, new CPos(4, 4), 0.6f, 0, "staff");

			var entry = database.Find(1);
			Assert.That(entry.SecondsSinceSeen(0), Is.EqualTo(0f), "It is being looked at.");
			Assert.That(entry.SecondsSinceAttended(0), Is.EqualTo(float.MaxValue),
				"And nothing whatsoever has been done about it.");

			Assert.That(database.Neglected(60f).Select(e => e.ActorId), Is.EquivalentTo(new uint[] { 1 }));

			database.MarkAttended(1, "upkeep", 0);
			Assert.That(database.Neglected(60f).Count(), Is.EqualTo(0),
				"Once somebody has ordered the repair it is no longer neglected.");
		}

		[TestCase(TestName = "Things that want nothing doing are not counted as neglected.")]
		public void HealthyThingsAreNotNeglected()
		{
			// Otherwise the handful that need attention are buried in a list of everything owned.
			var database = Database();
			database.Observe(1, "powr", true, Allegiance.Self, new CPos(1, 1), 1f, 0, "staff");
			database.Observe(2, "3tnk", false, Allegiance.Self, new CPos(2, 2), 1f, 0, "staff");
			database.MarkAttended(2, "field", 0);

			database.AgeUnseen(300 * Second, _ => true);

			Assert.That(database.Neglected(60f).Count(), Is.EqualTo(0),
				"An undamaged building and a unit already carrying out an order are fine.");

			// Now damage the building. It wants something doing, and nobody has done it.
			database.Observe(1, "powr", true, Allegiance.Self, new CPos(1, 1), 0.4f, 300 * Second, "staff");
			Assert.That(database.Neglected(0f).Select(e => e.ActorId), Is.EquivalentTo(new uint[] { 1 }));
		}

		[TestCase(TestName = "Damaged buildings of ours are listed worst first.")]
		public void DamagedListedWorstFirst()
		{
			var database = Database();
			database.Observe(1, "powr", true, Allegiance.Self, new CPos(1, 1), 0.8f, 0, "staff");
			database.Observe(2, "proc", true, Allegiance.Self, new CPos(2, 2), 0.3f, 0, "staff");
			database.Observe(3, "weap", true, Allegiance.Self, new CPos(3, 3), 1f, 0, "staff");
			database.Observe(4, "3tnk", false, Allegiance.Self, new CPos(4, 4), 0.2f, 0, "staff");

			Assert.That(database.Damaged().Select(e => e.ActorId).ToArray(), Is.EqualTo(new uint[] { 2, 1 }),
				"Worst first, buildings only - a damaged tank is not a repair job.");
		}

		[TestCase(TestName = "Enumeration is ordered by actor id, never by hash layout.")]
		public void EnumerationIsDeterministic()
		{
			// This is a lockstep simulation with sync hashing. A commander whose decisions depend on
			// dictionary iteration order desyncs, and does so intermittently and under load.
			var database = Database();
			foreach (var id in new uint[] { 91, 3, 57, 12, 40 })
				database.Observe(id, "e1", false, Allegiance.Self, new CPos(1, 1), 1f, 0, "staff");

			Assert.That(database.All.Select(e => e.ActorId).ToArray(),
				Is.EqualTo(new uint[] { 3, 12, 40, 57, 91 }));
		}

		[TestCase(TestName = "The database is per-match memory and keeps nothing across one.")]
		public void ClearedBetweenMatches()
		{
			var database = Database();
			database.Observe(1, "fact", true, Allegiance.Enemy, new CPos(9, 9), 1f, 0, "staff");
			database.RecordDestroyed(1, 10);
			database.Clear();

			Assert.That(database.Count, Is.EqualTo(0));
			Assert.That(database.EnemyRebuilds, Is.EqualTo(0));

			// And a razed spot from the previous match must not make the next one's first refinery
			// look like a rebuild.
			database.Observe(2, "fact", true, Allegiance.Enemy, new CPos(9, 9), 1f, 0, "staff");
			Assert.That(database.EnemyRebuilds, Is.EqualTo(0));

			// And each destroyed structure is replaced at most once, so a base the commander keeps
			// re-sighting does not inflate into an opponent that rebuilds endlessly.
			database.Observe(3, "fact", true, Allegiance.Enemy, new CPos(11, 11), 1f, 0, "staff");
			Assert.That(database.EnemyRebuilds, Is.EqualTo(0));
		}
	}
}
