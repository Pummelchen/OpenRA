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
using OpenRA.Mods.Common.Commander.Model;

namespace OpenRA.Mods.Common.Commander.Staff
{
	/// <summary>
	/// <para>
	/// Keeper of the commander's records. Reads the shared database and tells the chief the two things
	/// about it that no other manager is in a position to notice.
	/// </para>
	/// <para>
	/// <b>Whether the enemy is being destroyed or merely damaged.</b> Every other report counts what
	/// the commander has destroyed, which rises steadily whether or not it is winning. The database
	/// records where a structure stood when it died, so a new structure on that spot is visibly a
	/// replacement rather than a discovery. Measured over a long match against the rushing opponent,
	/// the commander destroyed eighty-nine structures and the opponent finished holding a hundred
	/// and forty-two: by the only number anybody was reporting, that match was going extremely well.
	/// A commander that cannot tell demolition from attrition will besiege a base forever.
	/// </para>
	/// <para>
	/// <b>How old its picture of the enemy is.</b> Confidence in this staff has generally meant "how
	/// sure is the classifier", never "when did anyone last look". Those come apart exactly when it
	/// matters - a commander is most confident about an enemy base in the minutes after its last
	/// scout there died.
	/// </para>
	/// </summary>
	public sealed class RecordsManager : ICommanderManager
	{
		public string Name => "records";
		public int Order => 15;
		public int Interval => 250;
		public bool CanThinkInParallel => true;

		/// <summary>Replacements per destroyed structure above which the siege is not actually reducing anything.</summary>
		public float AttritionThreshold { get; init; } = 0.5f;

		/// <summary>Seconds after which the picture of a remembered enemy structure is called old.</summary>
		public float StaleSeconds { get; init; } = 90f;

		public void Think(CommanderSnapshot snapshot, StaffContext context)
		{
			var database = snapshot.Database;
			if (database == null)
				return;

			var destroyed = database.All.Count(e =>
				e.Side == Allegiance.Enemy && e.IsStructure && e.Status == RecordStatus.Destroyed);

			var standing = database.EnemyStructures().ToArray();
			var rebuilt = database.EnemyRebuilds;

			// Where the enemy's base is, as far as anyone has actually looked. The most recently
			// confirmed structure is the least speculative thing to point the chief at.
			var freshest = standing
				.OrderByDescending(e => e.LastSeenTick)
				.FirstOrDefault();

			var oldestSeconds = standing.Length == 0
				? 0f
				: standing.Max(e => e.SecondsSinceSeen(snapshot.Tick));

			// Replacement rate. Destroying ten and watching eight go back up is not a siege that is
			// nearly finished; it is a siege that is not working.
			var replacement = destroyed <= 0 ? 0f : rebuilt / (float)destroyed;
			var outpaced = destroyed > 0 && replacement >= AttritionThreshold;

			var blind = standing.Length > 0 && oldestSeconds >= StaleSeconds
				&& standing.All(e => e.Status != RecordStatus.Live);

			context.Report(new ManagerReport
			{
				Manager = Name,

				// Being outpaced is a strained position however good the raw kill count looks, and
				// saying so is this manager's entire reason for existing. It is never Critical:
				// nothing here is an emergency, and reporting one would pin the chief in Recover.
				Readiness =
					outpaced ? Readiness.Strained
					: blind ? Readiness.Strained
					: standing.Length == 0 ? Readiness.NotApplicable
					: Readiness.Healthy,

				Headline = standing.Length == 0
					? $"{database.Count} tracked, no enemy structures on record"
					: outpaced
						? $"{destroyed} enemy structures destroyed, {rebuilt} rebuilt ({replacement:P0} replaced) - " +
							$"attrition, not demolition; {standing.Length} still standing"
						: blind
							? $"{standing.Length} enemy structures on record, none seen for {oldestSeconds:F0}s"
							: $"{standing.Length} enemy structures on record, {destroyed} destroyed, " +
								$"{rebuilt} rebuilt, oldest sighting {oldestSeconds:F0}s",

				// Point the chief at the best-attested part of the enemy's base rather than the
				// most-guessed-at one.
				RegionOfInterest = freshest != null && freshest.Region >= 0 ? freshest.Region : null,

				// How current the picture is, which is a different question from how sure the
				// classifier is and is the one the chief should weigh before committing.
				Confidence = standing.Length == 0
					? 0f
					: standing.Average(e => WorldDatabase.Confidence(e, snapshot.Tick)),
			});
		}
	}
}
