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

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	/// <summary>
	/// Detects material events between strategic reviews: enemy base discovery, enemy composition
	/// changes, loss of allied production, loss of contact with the enemy, and discovery of a
	/// high-value enemy structure. Pure and deterministic so it is unit-testable without a World:
	/// the caller feeds it the current blackboard summary each review.
	/// </summary>
	public sealed class StrategicEventDetector
	{
		int lastEnemyRegion = -1;
		int lastEnemyStructureCount = -1;
		int lastOwnStructureCount = -1;
		int lastEnemyIntelCount = -1;
		bool lastHighValueSeen;

		/// <summary>Feeds the current state and returns the detected event, or null.</summary>
		public string Detect(int enemyRegion, int enemyStructureCount, int ownStructureCount,
			int enemyIntelCount, bool highValueSeen)
		{
			// Snapshot the previous state from the fields, then commit the current state so every
			// field stays consistent even when an event fires (no stale fields on the next call).
			var previous = (lastEnemyRegion, lastEnemyStructureCount, lastOwnStructureCount,
				lastEnemyIntelCount, lastHighValueSeen);
			lastEnemyRegion = enemyRegion;
			lastEnemyStructureCount = enemyStructureCount;
			lastOwnStructureCount = ownStructureCount;
			lastEnemyIntelCount = enemyIntelCount;
			lastHighValueSeen = highValueSeen;

			if (previous.Item1 < 0 && enemyRegion >= 0)
				return "enemy base discovered";

			if (previous.Item2 >= 0 && enemyStructureCount > previous.Item2 * 2 + 1)
				return "enemy composition changed (new structures)";

			if (previous.Item3 > 0 && ownStructureCount < previous.Item3)
				return "allied production lost";

			if (previous.Item4 > 0 && enemyIntelCount == 0)
				return "contact with enemy main army lost";

			if (highValueSeen && !previous.Item5)
				return "high-value enemy structure discovered";

			return null;
		}
	}
}
