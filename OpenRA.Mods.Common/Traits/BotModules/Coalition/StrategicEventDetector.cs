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
		int lastActiveAttackCount = -1;
		int lastFailedMissionCount = -1;
		int lastReadyTransportCount = -1;
		int lastCompletedMissionCount = -1;
		int lastRouteSignature = int.MinValue;
		int lastCoalitionCash = -1;

		/// <summary>Feeds the current state and returns the detected event, or null.</summary>
		public string Detect(int enemyRegion, int enemyStructureCount, int ownStructureCount,
			int enemyIntelCount, bool highValueSeen, int activeAttackCount = 0,
			int failedMissionCount = 0, int readyTransportCount = 0,
			int completedMissionCount = 0, int routeSignature = 0, int coalitionCash = 0)
		{
			// Snapshot the previous state from the fields, then commit the current state so every
			// field stays consistent even when an event fires (no stale fields on the next call).
			var previous = (EnemyRegion: lastEnemyRegion, EnemyStructures: lastEnemyStructureCount,
				OwnStructures: lastOwnStructureCount, EnemyIntel: lastEnemyIntelCount,
				HighValueSeen: lastHighValueSeen);
			var previousOperations = (ActiveAttacks: lastActiveAttackCount, FailedMissions: lastFailedMissionCount,
				ReadyTransports: lastReadyTransportCount, CompletedMissions: lastCompletedMissionCount,
				RouteSignature: lastRouteSignature, CoalitionCash: lastCoalitionCash);
			lastEnemyRegion = enemyRegion;
			lastEnemyStructureCount = enemyStructureCount;
			lastOwnStructureCount = ownStructureCount;
			lastEnemyIntelCount = enemyIntelCount;
			lastHighValueSeen = highValueSeen;
			lastActiveAttackCount = activeAttackCount;
			lastFailedMissionCount = failedMissionCount;
			lastReadyTransportCount = readyTransportCount;
			lastCompletedMissionCount = completedMissionCount;
			lastRouteSignature = routeSignature;
			lastCoalitionCash = coalitionCash;

			if (previous.EnemyRegion < 0 && enemyRegion >= 0)
				return "enemy base discovered";

			if (previous.EnemyStructures >= 0 && enemyStructureCount > previous.EnemyStructures * 2 + 1)
				return "enemy composition changed (new structures)";

			if (previous.OwnStructures > 0 && ownStructureCount < previous.OwnStructures)
				return "allied production lost";

			if (previous.EnemyIntel > 0 && enemyIntelCount == 0)
				return "contact with enemy main army lost";

			if (highValueSeen && !previous.HighValueSeen)
				return "high-value enemy structure discovered";

			if (previousOperations.ActiveAttacks >= 0 && activeAttackCount > previousOperations.ActiveAttacks)
				return "major allied attack started";

			if (previousOperations.FailedMissions >= 0 && failedMissionCount > previousOperations.FailedMissions)
				return "major attack failed";

			if (previousOperations.ReadyTransports >= 0 && readyTransportCount > previousOperations.ReadyTransports)
				return "transport ready";

			if (previousOperations.CompletedMissions >= 0 && completedMissionCount > previousOperations.CompletedMissions)
				return "mission completed";

			if (previousOperations.RouteSignature != int.MinValue && routeSignature != previousOperations.RouteSignature)
				return "major route or bridge changed";

			if (previousOperations.CoalitionCash >= 0
				&& System.Math.Abs(coalitionCash - previousOperations.CoalitionCash)
					>= System.Math.Max(2000, previousOperations.CoalitionCash / 4))
				return "major economy change";

			return null;
		}
	}
}
