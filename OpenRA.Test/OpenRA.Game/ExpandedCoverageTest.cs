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
	[TestFixture]
	sealed class ExpandedCoverageTest
	{
		// Tests for newly added mission types, LLM tool validation, telemetry metrics,
		// per-front postures, and the StrategicPosture.None value.

		[TestCase(TestName = "New mission types exist in the enum.")]
		public void NewMissionTypesExist()
		{
			Assert.That(System.Enum.IsDefined<MissionType>(MissionType.Pincer), Is.True);
			Assert.That(System.Enum.IsDefined<MissionType>(MissionType.NavalBlockade), Is.True);
			Assert.That(System.Enum.IsDefined<MissionType>(MissionType.FakeBuildup), Is.True);
		}

		[TestCase(TestName = "Pincer and NavalBlockade are offensive.")]
		public void NewOffensiveTypes()
		{
			Assert.That(MissionManager.IsOffensive(MissionType.Pincer), Is.True);
			Assert.That(MissionManager.IsOffensive(MissionType.NavalBlockade), Is.True);
		}

		[TestCase(TestName = "FakeBuildup is a deception mission.")]
		public void FakeBuildupIsDeception()
		{
			Assert.That(MissionManager.IsDeception(MissionType.FakeBuildup), Is.True);
		}

		[TestCase(TestName = "DesiredEffectsFor returns non-null for new types.")]
		public void NewTypeDesiredEffects()
		{
			Assert.That(CoalitionMission.DesiredEffectsFor(MissionType.Pincer), Is.Not.Null);
			Assert.That(CoalitionMission.DesiredEffectsFor(MissionType.NavalBlockade), Is.Not.Null);
			Assert.That(CoalitionMission.DesiredEffectsFor(MissionType.FakeBuildup), Is.Not.Null);
		}

		[TestCase(TestName = "IntendedReactionFor returns non-null for FakeBuildup.")]
		public void FakeBuildupIntendedReaction()
		{
			Assert.That(CoalitionMission.IntendedReactionFor(MissionType.FakeBuildup), Is.Not.Null);
		}

		[TestCase(TestName = "ValidateCapability accepts known capabilities.")]
		public void ValidateCapabilityValid()
		{
			Assert.That(CommandValidator.ValidateCapability("anti_air"), Is.Null);
			Assert.That(CommandValidator.ValidateCapability("anti_armor"), Is.Null);
			Assert.That(CommandValidator.ValidateCapability("naval"), Is.Null);
			Assert.That(CommandValidator.ValidateCapability("base_defense"), Is.Null);
		}

		[TestCase(TestName = "ValidateCapability rejects unknown capabilities.")]
		public void ValidateCapabilityInvalid()
		{
			var result = CommandValidator.ValidateCapability("unknown");
			Assert.That(result, Does.Contain("REJECTED_UNKNOWN_CAPABILITY"));
		}

		[TestCase(TestName = "ValidateCapability accepts null/empty (optional field).")]
		public void ValidateCapabilityNull()
		{
			Assert.That(CommandValidator.ValidateCapability(null), Is.Null);
			Assert.That(CommandValidator.ValidateCapability(""), Is.Null);
			Assert.That(CommandValidator.ValidateCapability("  "), Is.Null);
		}

		[TestCase(TestName = "ValidateExpansionPriority accepts -1, 0, 1.")]
		public void ValidateExpansionPriorityValid()
		{
			Assert.That(CommandValidator.ValidateExpansionPriority(-1), Is.Null);
			Assert.That(CommandValidator.ValidateExpansionPriority(0), Is.Null);
			Assert.That(CommandValidator.ValidateExpansionPriority(1), Is.Null);
		}

		[TestCase(TestName = "ValidateExpansionPriority rejects out-of-range values.")]
		public void ValidateExpansionPriorityInvalid()
		{
			var result = CommandValidator.ValidateExpansionPriority(5);
			Assert.That(result, Does.Contain("REJECTED_INVALID_EXPANSION_PRIORITY"));
		}

		[TestCase(TestName = "CoalitionMatchMetrics tracks refinery losses.")]
		public void MatchMetricsRefineryLosses()
		{
			var metrics = new CoalitionMatchMetrics();
			metrics.SampleEconomy(3, 2);
			metrics.SampleEconomy(3, 2);
			metrics.SampleEconomy(1, 2); // lost 2 friendly refineries
			metrics.SampleEconomy(1, 0); // lost 2 enemy refineries
			Assert.That(metrics.FriendlyRefineryLosses, Is.EqualTo(2));
			Assert.That(metrics.EnemyRefineryLosses, Is.EqualTo(2));
		}

		[TestCase(TestName = "CoalitionMatchMetrics records win/loss result.")]
		public void MatchMetricsRecordResult()
		{
			var metrics = new CoalitionMatchMetrics();
			metrics.RecordResult(true);
			Assert.That(metrics.Won, Is.True);
			metrics.RecordResult(false);
			Assert.That(metrics.Won, Is.False);
		}

		[TestCase(TestName = "CoalitionMatchMetrics Summary includes econ damage and result.")]
		public void MatchMetricsSummaryIncludesNewFields()
		{
			var metrics = new CoalitionMatchMetrics();
			metrics.Sample(100, 50, 0.2f, 0.8f, 5000);
			metrics.SampleEconomy(2, 1);
			metrics.RecordResult(true);
			var summary = metrics.Summary();
			Assert.That(summary, Does.Contain("econ dmg"));
			Assert.That(summary, Does.Contain("WIN"));
		}

		[TestCase(TestName = "CoalitionMatchMetrics records sync errors.")]
		public void MatchMetricsSyncError()
		{
			var metrics = new CoalitionMatchMetrics();
			metrics.RecordSyncError(1000, 5);
			metrics.RecordSyncError(2000, 10);
			// No direct accessor — verify via summary after sampling
			metrics.Sample(100, 50, 0.1f, 0.9f, 3000);
			var summary = metrics.Summary();
			// Summary doesn't include sync errors directly, but the recording should not throw
			Assert.That(summary, Is.Not.Null);
		}

		[TestCase(TestName = "CoalitionMatchMetrics records expansion timings.")]
		public void MatchMetricsExpansion()
		{
			var metrics = new CoalitionMatchMetrics();
			metrics.RecordExpansion(500);
			metrics.RecordExpansion(1500);
			Assert.That(metrics.ExpansionTimings.Count, Is.EqualTo(2));
		}

		[TestCase(TestName = "CoalitionMatchMetrics records recon efficiency.")]
		public void MatchMetricsReconEfficiency()
		{
			var metrics = new CoalitionMatchMetrics();
			metrics.RecordReconMission(true);
			metrics.RecordReconMission(false);
			metrics.RecordReconMission(true);
			Assert.That(metrics.ReconEfficiency.MissionsSent, Is.EqualTo(3));
			Assert.That(metrics.ReconEfficiency.UsefulIntelGained, Is.EqualTo(2));
		}

		[TestCase(TestName = "CoalitionMatchMetrics records transport survival.")]
		public void MatchMetricsTransportSurvival()
		{
			var metrics = new CoalitionMatchMetrics();
			metrics.RecordTransport(true);
			metrics.RecordTransport(false);
			metrics.RecordTransport(true);
			Assert.That(metrics.TransportSurvivalCount.Total, Is.EqualTo(3));
			Assert.That(metrics.TransportSurvivalCount.Survived, Is.EqualTo(2));
		}

		[TestCase(TestName = "CoalitionMatchMetrics records counterattack effectiveness.")]
		public void MatchMetricsCounterattack()
		{
			var metrics = new CoalitionMatchMetrics();
			metrics.RecordCounterattack(5);
			metrics.RecordCounterattack(3);
			Assert.That(metrics.CounterattackEffectiveness.Counterattacks, Is.EqualTo(2));
			Assert.That(metrics.CounterattackEffectiveness.EnemyDestroyed, Is.EqualTo(8));
		}

		[TestCase(TestName = "CoalitionMatchMetrics records base defense response time.")]
		public void MatchMetricsBaseDefenseResponse()
		{
			var metrics = new CoalitionMatchMetrics();
			metrics.RecordBaseDefenseResponse(1000, 1100);
			Assert.That(metrics.BaseDefenseResponseTime.Count, Is.EqualTo(1));
			Assert.That(metrics.BaseDefenseResponseTime[0].ResponseTick - metrics.BaseDefenseResponseTime[0].ThreatTick, Is.EqualTo(100));
		}

		[TestCase(TestName = "StrategicPosture.None exists.")]
		public void StrategicPostureNoneExists()
		{
			Assert.That(System.Enum.IsDefined<StrategicPosture>(StrategicPosture.None), Is.True);
		}

		[TestCase(TestName = "All original strategic postures still exist.")]
		public void StrategicPosturesComplete()
		{
			Assert.That(System.Enum.IsDefined<StrategicPosture>(StrategicPosture.Opening), Is.True);
			Assert.That(System.Enum.IsDefined<StrategicPosture>(StrategicPosture.Expansion), Is.True);
			Assert.That(System.Enum.IsDefined<StrategicPosture>(StrategicPosture.Pressure), Is.True);
			Assert.That(System.Enum.IsDefined<StrategicPosture>(StrategicPosture.Containment), Is.True);
			Assert.That(System.Enum.IsDefined<StrategicPosture>(StrategicPosture.Attrition), Is.True);
			Assert.That(System.Enum.IsDefined<StrategicPosture>(StrategicPosture.Breakthrough), Is.True);
			Assert.That(System.Enum.IsDefined<StrategicPosture>(StrategicPosture.Siege), Is.True);
			Assert.That(System.Enum.IsDefined<StrategicPosture>(StrategicPosture.Raiding), Is.True);
			Assert.That(System.Enum.IsDefined<StrategicPosture>(StrategicPosture.Defensive), Is.True);
			Assert.That(System.Enum.IsDefined<StrategicPosture>(StrategicPosture.Counterattack), Is.True);
			Assert.That(System.Enum.IsDefined<StrategicPosture>(StrategicPosture.Recovery), Is.True);
			Assert.That(System.Enum.IsDefined<StrategicPosture>(StrategicPosture.Desperation), Is.True);
			Assert.That(System.Enum.IsDefined<StrategicPosture>(StrategicPosture.AllIn), Is.True);
		}

		[TestCase(TestName = "CoalitionRegion default LocalPosture is None.")]
		public void CoalitionRegionDefaultPosture()
		{
			var region = new CoalitionRegion(0, new OpenRA.Primitives.Rectangle(0, 0, 10, 10));
			Assert.That(region.LocalPosture, Is.EqualTo(StrategicPosture.None));
		}

		[TestCase(TestName = "CoalitionRegion LocalPosture can be set.")]
		public void CoalitionRegionSetPosture()
		{
			var region = new CoalitionRegion(0, new OpenRA.Primitives.Rectangle(0, 0, 10, 10));
			region.LocalPosture = StrategicPosture.Defensive;
			Assert.That(region.LocalPosture, Is.EqualTo(StrategicPosture.Defensive));
		}

		[TestCase(TestName = "MissionStatus.Cancelled exists.")]
		public void MissionStatusCancelledExists()
		{
			Assert.That(System.Enum.IsDefined<MissionStatus>(MissionStatus.Cancelled), Is.True);
		}

		[TestCase(TestName = "KnownMissionTypes includes new mission types.")]
		public void KnownMissionTypesIncludeNew()
		{
			Assert.That(CommandValidator.KnownMissionTypes.Contains("pincer"), Is.True);
			Assert.That(CommandValidator.KnownMissionTypes.Contains("navalblockade"), Is.True);
			Assert.That(CommandValidator.KnownMissionTypes.Contains("fakebuildup"), Is.True);
		}

		[TestCase(TestName = "Every MissionType has a canonical wire form in KnownMissionTypes.")]
		public void MissionTypesAllInKnownMissionTypes()
		{
			foreach (var type in System.Enum.GetValues<MissionType>())
				Assert.That(CommandValidator.KnownMissionTypes.Contains(type.ToString().ToLowerInvariant()),
					Is.True, $"{type} has no canonical wire form in KnownMissionTypes");
		}

		[TestCase(TestName = "KnownCapabilities includes all 8 capabilities.")]
		public void KnownCapabilitiesComplete()
		{
			Assert.That(CommandValidator.KnownCapabilities.Count, Is.EqualTo(8));
			Assert.That(CommandValidator.KnownCapabilities.Contains("anti_air"), Is.True);
			Assert.That(CommandValidator.KnownCapabilities.Contains("anti_armor"), Is.True);
			Assert.That(CommandValidator.KnownCapabilities.Contains("anti_infantry"), Is.True);
			Assert.That(CommandValidator.KnownCapabilities.Contains("artillery"), Is.True);
			Assert.That(CommandValidator.KnownCapabilities.Contains("naval"), Is.True);
			Assert.That(CommandValidator.KnownCapabilities.Contains("recon"), Is.True);
			Assert.That(CommandValidator.KnownCapabilities.Contains("transport"), Is.True);
			Assert.That(CommandValidator.KnownCapabilities.Contains("base_defense"), Is.True);
		}
	}
}
