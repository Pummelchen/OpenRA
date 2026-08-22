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

using System;

namespace OpenRA.Mods.Common.Traits.BotModules.Coalition
{
	/// <summary>Pure counterattack opportunity evaluation from observed, fog-honest evidence.</summary>
	public static class CounterattackAssessment
	{
		public sealed class Decision
		{
			public readonly bool ShouldLaunch;
			public readonly bool EnemyDepleted;
			public readonly bool OriginExposed;
			public readonly bool ProductionWindow;
			public readonly string Reason;

			public Decision(bool shouldLaunch, bool enemyDepleted, bool originExposed,
				bool productionWindow, string reason)
			{
				ShouldLaunch = shouldLaunch;
				EnemyDepleted = enemyDepleted;
				OriginExposed = originExposed;
				ProductionWindow = productionWindow;
				Reason = reason;
			}
		}

		/// <summary>
		/// Launches only with a meaningful friendly wave and either observed attacker depletion or an
		/// exposed production target. Unknown enemy strength is treated conservatively, not as zero.
		/// </summary>
		public static Decision Evaluate(int friendlyUnits, int enemyAtDefense, int observedEnemyNow,
			int enemyNearOrigin, bool productionAtOrigin, int minWaveSize)
		{
			var enemyDepleted = enemyAtDefense > 0 && observedEnemyNow < enemyAtDefense;
			var originExposed = observedEnemyNow > 0 && enemyNearOrigin * 3 <= observedEnemyNow;
			var productionWindow = productionAtOrigin && originExposed;
			var localAdvantage = friendlyUnits >= Math.Max(minWaveSize, Math.Max(1, enemyNearOrigin) * 2);
			var launch = localAdvantage && (enemyDepleted || productionWindow);
			var reason = productionWindow ? "exposed enemy production"
				: enemyDepleted ? "attackers depleted" : "no verified counterattack window";
			return new Decision(launch, enemyDepleted, originExposed, productionWindow, reason);
		}
	}
}
