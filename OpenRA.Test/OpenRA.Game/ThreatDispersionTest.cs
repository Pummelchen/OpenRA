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
	/// Human-attention exploitation (reqs 277-284): whether the coalition presents several
	/// coordinated threats a defender must answer separately, or one blob wearing several names.
	/// </summary>
	[TestFixture]
	sealed class ThreatDispersionTest
	{
		static PresentedThreat T(MissionType type, int region, string domain, int priority)
		{
			return new PresentedThreat(type, region, domain, priority);
		}

		[TestCase(TestName = "Several missions in one region are one threat, not many (req 277).")]
		public void OneRegionIsOneThreat()
		{
			var sameRegion = new[]
			{
				T(MissionType.Attack, 3, "land", 90),
				T(MissionType.Raid, 3, "land", 50),
				T(MissionType.AirStrike, 3, "air", 60)
			};

			Assert.That(ThreatDispersion.DistinctRegions(sameRegion), Is.EqualTo(1));
			Assert.That(ThreatDispersion.IsMultiThreat(sameRegion), Is.False,
				"A defender only has to defend one place, so this is a single front.");
		}

		[TestCase(TestName = "Pressure in separate regions and domains is counted separately (reqs 278, 279).")]
		public void SeparateRegionsAndDomains()
		{
			var spread = new[]
			{
				T(MissionType.Attack, 1, "land", 90),
				T(MissionType.EconomyRaid, 5, "land", 50),
				T(MissionType.AirStrike, 8, "air", 60)
			};

			Assert.That(ThreatDispersion.DistinctRegions(spread), Is.EqualTo(3));
			Assert.That(ThreatDispersion.DistinctDomains(spread), Is.EqualTo(2));
			Assert.That(ThreatDispersion.IsMultiThreat(spread), Is.True);
		}

		[TestCase(TestName = "Full-spectrum pressure needs assault, raid, strike and special ops together (req 280).")]
		public void FullSpectrumRequiresEveryComponent()
		{
			var full = new[]
			{
				T(MissionType.Attack, 1, "land", 90),
				T(MissionType.EconomyRaid, 5, "land", 50),
				T(MissionType.AirStrike, 8, "air", 60),
				T(MissionType.SpecialOps, 9, "special", 55)
			};

			Assert.That(ThreatDispersion.IsFullSpectrum(full), Is.True);

			var missingSpecial = new[]
			{
				T(MissionType.Attack, 1, "land", 90),
				T(MissionType.EconomyRaid, 5, "land", 50),
				T(MissionType.AirStrike, 8, "air", 60)
			};

			Assert.That(ThreatDispersion.IsFullSpectrum(missingSpecial), Is.False);
		}

		[TestCase(TestName = "Threats serve a common purpose only when exactly one is the main effort (req 281).")]
		public void CommonPurposeRequiresASingleMainEffort()
		{
			var supporting = new[]
			{
				T(MissionType.Attack, 1, "land", 90),
				T(MissionType.Raid, 5, "land", 50),
				T(MissionType.AirStrike, 8, "air", 60)
			};

			Assert.That(ThreatDispersion.SharesCommonPurpose(supporting), Is.True);

			// Two co-equal maxima mean the army is split between two objectives, which is the
			// failure this exists to detect - not a plan, just an even spread.
			var split = new[]
			{
				T(MissionType.Attack, 1, "land", 90),
				T(MissionType.Attack, 7, "land", 90)
			};

			Assert.That(ThreatDispersion.SharesCommonPurpose(split), Is.False);
			Assert.That(ThreatDispersion.SharesCommonPurpose([]), Is.False);
		}

		[TestCase(TestName = "A distraction must already be running to distract anything (reqs 283, 299).")]
		public void DistractionMustLeadTheOperation()
		{
			Assert.That(ThreatDispersion.DistractionPrecedes(1000, 1400, 200), Is.True);
			Assert.That(ThreatDispersion.DistractionPrecedes(1000, 1000, 200), Is.False,
				"Launched on the same tick, the distraction has drawn nobody yet.");
			Assert.That(ThreatDispersion.DistractionPrecedes(1000, 1100, 200), Is.False,
				"Too little lead time to redeploy against.");
			Assert.That(ThreatDispersion.DistractionPrecedes(-1, 1400, 200), Is.False,
				"No distraction was launched at all.");
		}

		[TestCase(TestName = "A defender is forced to choose only when it cannot cover every threat (req 284).")]
		public void ForcingAChoice()
		{
			Assert.That(ThreatDispersion.ForcesDefenderChoice(threatenedAssets: 3, defenderMobileGroups: 1), Is.True);
			Assert.That(ThreatDispersion.ForcesDefenderChoice(threatenedAssets: 2, defenderMobileGroups: 4), Is.False,
				"A defender with forces to spare is not being forced to choose.");
			Assert.That(ThreatDispersion.ForcesDefenderChoice(threatenedAssets: 1, defenderMobileGroups: 0), Is.False,
				"One threat is not a dilemma.");
		}

		[TestCase(TestName = "An overreaction is only exploited when the opponent model is reliable (req 282).")]
		public void OverreactionNeedsConfidence()
		{
			Assert.That(ThreatDispersion.OverreactionIsExploitable(enemyShareDrawn: 0.6f, modelConfidence: 0.8f), Is.True);
			Assert.That(ThreatDispersion.OverreactionIsExploitable(enemyShareDrawn: 0.6f, modelConfidence: 0.3f), Is.False,
				"A single coincidence is not a pattern to bet the army on.");
			Assert.That(ThreatDispersion.OverreactionIsExploitable(enemyShareDrawn: 0.1f, modelConfidence: 0.9f), Is.False,
				"A token response is not an overreaction.");
		}

		[TestCase(TestName = "Null and empty inputs are handled without throwing.")]
		public void EmptyInputIsSafe()
		{
			Assert.That(ThreatDispersion.DistinctRegions(null), Is.Zero);
			Assert.That(ThreatDispersion.DistinctDomains(null), Is.Zero);
			Assert.That(ThreatDispersion.IsMultiThreat(null), Is.False);
			Assert.That(ThreatDispersion.IsFullSpectrum(null), Is.False);
		}

		[TestCase(TestName = "Unassigned regions are not counted as distinct places.")]
		public void UnassignedRegionsAreIgnored()
		{
			var threats = new[]
			{
				T(MissionType.Attack, -1, "land", 90),
				T(MissionType.Raid, -1, "land", 50)
			};

			Assert.That(ThreatDispersion.DistinctRegions(threats), Is.Zero,
				"A mission with no located target does not pressure a place.");
			Assert.That(ThreatDispersion.IsMultiThreat(threats), Is.False);
		}
	}
}
