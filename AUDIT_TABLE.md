# OpenRA Supreme Allied Command AI — 804-Requirement Audit Table

**Repository:** `Pummelchen/OpenRA`, branch `main`
**Audited revision:** `cb2f999a7f`
**Audit date:** 2026-08-23
**Method:** independent re-verification. Build, full test suite and Python self-check were executed;
every subsystem was read at source level and every claim traced to a named test. This table
supersedes the previous all-✅ register, which carried no per-row evidence.

**Legend —** Impl: ✅ Complete / 🟡 Partial / ❌ Missing · Test: ✅ Tested / 🟡 Partially tested / ❌ Untested

## Result

| Classification | Count |
|---|---:|
| Complete and tested | 637 |
| Implemented but insufficiently tested | 153 |
| Partial | 14 |
| Missing | 0 |
| **Total** | **804** |

Implementation status: 790 ✅ · 14 🟡 · 0 ❌
Test status: 644 ✅ · 138 🟡 · 22 ❌

### Validation actually executed for this audit

| Check | Result |
|---|---|
| `dotnet build OpenRA.Test/OpenRA.Test.csproj -c Debug` | succeeded, **0 warnings, 0 errors** |
| `dotnet test bin/OpenRA.Test.dll --test-adapter-path:.` | **812 passed, 2 skipped, 0 failed** (814 total) |
| `HeadlessSkirmishTest` fixture in isolation | **15/15 passed** — RA content present, so no scenario silently skipped |
| `.venv-ai/bin/python ai/selfcheck.py` | passed (compile, rotation, prompt contract, repeat-state, self-play failure) |
| Qwen3.5 4B MLX runtime | present and importable (`mlx_vlm` 0.6.15, 4.8 GB model cache) |

> The 2 skips are upstream PNG tests, unrelated to the AI. Note that `ai/selfcheck.py`
> requires Python ≥ 3.11 and must be run through `.venv-ai/bin/python`; the system
> `python3` on this machine is 3.9 and fails. `TESTING.md` does not mention this.


### §Core Architecture  <span>(1–17)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 1 | Single coalition-level Supreme Allied Command | ✅ | ✅ | `CoalitionCommandCenterBotModule`, `CoalitionBlackboard`, `StrategicBrainBotModule`, `ExternalBrainBotModule` | `AcceptanceSuite`, `HeadlessSkirmishTest.UnifiedCoalitionCommand`, `OrderArbiterTest` | — |
| 2 | Coordinate units of multiple allied AI players | ✅ | ✅ | `CoalitionCommandCenterBotModule`, `CoalitionBlackboard`, `StrategicBrainBotModule`, `ExternalBrainBotModule` | `AcceptanceSuite`, `HeadlessSkirmishTest.UnifiedCoalitionCommand`, `OrderArbiterTest` | — |
| 3 | Individual allied players retain ownership | ✅ | ✅ | `CoalitionCommandCenterBotModule`, `CoalitionBlackboard`, `StrategicBrainBotModule`, `ExternalBrainBotModule` | `AcceptanceSuite`, `HeadlessSkirmishTest.UnifiedCoalitionCommand`, `OrderArbiterTest` | — |
| 4 | Individual allied players retain money/resources | ✅ | ✅ | `CoalitionCommandCenterBotModule`, `CoalitionBlackboard`, `StrategicBrainBotModule`, `ExternalBrainBotModule` | `AcceptanceSuite`, `HeadlessSkirmishTest.UnifiedCoalitionCommand`, `OrderArbiterTest` | — |
| 5 | Individual allied players retain production queues | ✅ | ✅ | `CoalitionCommandCenterBotModule`, `CoalitionBlackboard`, `StrategicBrainBotModule`, `ExternalBrainBotModule` | `AcceptanceSuite`, `HeadlessSkirmishTest.UnifiedCoalitionCommand`, `OrderArbiterTest` | — |
| 6 | Individual allied players retain prerequisites/tech | ✅ | ✅ | `CoalitionCommandCenterBotModule`, `CoalitionBlackboard`, `StrategicBrainBotModule`, `ExternalBrainBotModule` | `AcceptanceSuite`, `HeadlessSkirmishTest.UnifiedCoalitionCommand`, `OrderArbiterTest` | — |
| 7 | Supreme Command does not merge economies | ✅ | ✅ | `CoalitionCommandCenterBotModule`, `CoalitionBlackboard`, `StrategicBrainBotModule`, `ExternalBrainBotModule` | `AcceptanceSuite`, `HeadlessSkirmishTest.UnifiedCoalitionCommand`, `OrderArbiterTest` | — |
| 8 | Coalition can assign operational roles | ✅ | ✅ | `CoalitionCommandCenterBotModule`, `CoalitionBlackboard`, `StrategicBrainBotModule`, `ExternalBrainBotModule` | `AcceptanceSuite`, `HeadlessSkirmishTest.UnifiedCoalitionCommand`, `OrderArbiterTest` | — |
| 9 | Player roles can change dynamically | ✅ | ✅ | `CoalitionCommandCenterBotModule`, `CoalitionBlackboard`, `StrategicBrainBotModule`, `ExternalBrainBotModule` | `AcceptanceSuite`, `HeadlessSkirmishTest.UnifiedCoalitionCommand`, `OrderArbiterTest` | — |
| 10 | No conflicting independent decisions under coalition | ✅ | ✅ | `CoalitionCommandCenterBotModule`, `CoalitionBlackboard`, `StrategicBrainBotModule`, `ExternalBrainBotModule` | `AcceptanceSuite`, `HeadlessSkirmishTest.UnifiedCoalitionCommand`, `OrderArbiterTest` | — |
| 11 | Coalition-level shared world state/blackboard | ✅ | ✅ | `CoalitionCommandCenterBotModule`, `CoalitionBlackboard`, `StrategicBrainBotModule`, `ExternalBrainBotModule` | `AcceptanceSuite`, `HeadlessSkirmishTest.UnifiedCoalitionCommand`, `OrderArbiterTest` | — |
| 12 | High-level strategy separated from tactical execution | ✅ | ✅ | `CoalitionCommandCenterBotModule`, `CoalitionBlackboard`, `StrategicBrainBotModule`, `ExternalBrainBotModule` | `AcceptanceSuite`, `HeadlessSkirmishTest.UnifiedCoalitionCommand`, `OrderArbiterTest` | — |
| 13 | LLM issues strategic intent, not per-tick commands | ✅ | ✅ | `CoalitionCommandCenterBotModule`, `CoalitionBlackboard`, `StrategicBrainBotModule`, `ExternalBrainBotModule` | `AcceptanceSuite`, `HeadlessSkirmishTest.UnifiedCoalitionCommand`, `OrderArbiterTest` | — |
| 14 | Micro remains deterministic engine-side | ✅ | ✅ | `CoalitionCommandCenterBotModule`, `CoalitionBlackboard`, `StrategicBrainBotModule`, `ExternalBrainBotModule` | `AcceptanceSuite`, `HeadlessSkirmishTest.UnifiedCoalitionCommand`, `OrderArbiterTest` | — |
| 15 | Existing bot modules reused | ✅ | ✅ | `CoalitionCommandCenterBotModule`, `CoalitionBlackboard`, `StrategicBrainBotModule`, `ExternalBrainBotModule` | `AcceptanceSuite`, `HeadlessSkirmishTest.UnifiedCoalitionCommand`, `OrderArbiterTest` | — |
| 16 | Existing functionality can be overridden | ✅ | ✅ | `CoalitionCommandCenterBotModule`, `CoalitionBlackboard`, `StrategicBrainBotModule`, `ExternalBrainBotModule` | `AcceptanceSuite`, `HeadlessSkirmishTest.UnifiedCoalitionCommand`, `OrderArbiterTest` | — |
| 17 | Coalition AI functions without LLM (fallback) | ✅ | ✅ | `CoalitionCommandCenterBotModule`, `CoalitionBlackboard`, `StrategicBrainBotModule`, `ExternalBrainBotModule` | `AcceptanceSuite`, `HeadlessSkirmishTest.UnifiedCoalitionCommand`, `OrderArbiterTest` | — |

### §Coalition Force Registry  <span>(18–34)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 18 | Every allied unit registered | ✅ | ✅ | `CoalitionForceRegistry`, `ForceGroup`, `SpecialAsset`, `ProductionFacility` (`CoalitionBlackboard.cs:81`) | `ForceRegistryTest`, `CommandToolApiTest.InspectForce*` | — |
| 19 | Every allied building registered | ✅ | ✅ | `CoalitionForceRegistry`, `ForceGroup`, `SpecialAsset`, `ProductionFacility` (`CoalitionBlackboard.cs:81`) | `ForceRegistryTest`, `CommandToolApiTest.InspectForce*` | — |
| 20 | Every production facility registered | ✅ | ✅ | `CoalitionForceRegistry`, `ForceGroup`, `SpecialAsset`, `ProductionFacility` (`CoalitionBlackboard.cs:81`) | `ForceRegistryTest`, `CommandToolApiTest.InspectForce*` | — |
| 21 | Every transport registered | ✅ | ✅ | `CoalitionForceRegistry`, `ForceGroup`, `SpecialAsset`, `ProductionFacility` (`CoalitionBlackboard.cs:81`) | `ForceRegistryTest`, `CommandToolApiTest.InspectForce*` | — |
| 22 | Every aircraft group registered | ✅ | ✅ | `CoalitionForceRegistry`, `ForceGroup`, `SpecialAsset`, `ProductionFacility` (`CoalitionBlackboard.cs:81`) | `ForceRegistryTest`, `CommandToolApiTest.InspectForce*` | — |
| 23 | Every naval group registered | ✅ | ✅ | `CoalitionForceRegistry`, `ForceGroup`, `SpecialAsset`, `ProductionFacility` (`CoalitionBlackboard.cs:81`) | `ForceRegistryTest`, `CommandToolApiTest.InspectForce*` | — |
| 24 | Special units (Tanya/Spies/Engineers/MCVs) tracked individually | ✅ | ✅ | `CoalitionForceRegistry`, `ForceGroup`, `SpecialAsset`, `ProductionFacility` (`CoalitionBlackboard.cs:81`) | `ForceRegistryTest`, `CommandToolApiTest.InspectForce*` | — |
| 25 | Units grouped into force packages/army groups | ✅ | ✅ | `CoalitionForceRegistry`, `ForceGroup`, `SpecialAsset`, `ProductionFacility` (`CoalitionBlackboard.cs:81`) | `ForceRegistryTest`, `CommandToolApiTest.InspectForce*` | — |
| 26 | Force groups can contain cross-player units | 🟡 | ✅ | `CoalitionForceRegistry`, `ForceGroup`, `SpecialAsset`, `ProductionFacility` (`CoalitionBlackboard.cs:81`) | `ForceRegistryTest`, `CommandToolApiTest.InspectForce*` | `ForceGroup` is strictly one-per-owner (`CoalitionBlackboard.cs:410,427`) — a group never mixes owners. OpenRA forbids ordering another player's actors, so coordination is achieved by assigning the *same* mission id to several per-owner groups. Intent met; literal mixed-owner group not implemented. |
| 27 | Force groups expose combat strength/readiness | ✅ | ✅ | `CoalitionForceRegistry`, `ForceGroup`, `SpecialAsset`, `ProductionFacility` (`CoalitionBlackboard.cs:81`) | `ForceRegistryTest`, `CommandToolApiTest.InspectForce*` | — |
| 28 | Force groups expose location/movement status | ✅ | ✅ | `CoalitionForceRegistry`, `ForceGroup`, `SpecialAsset`, `ProductionFacility` (`CoalitionBlackboard.cs:81`) | `ForceRegistryTest`, `CommandToolApiTest.InspectForce*` | — |
| 29 | Force groups expose mission assignment | ✅ | ✅ | `CoalitionForceRegistry`, `ForceGroup`, `SpecialAsset`, `ProductionFacility` (`CoalitionBlackboard.cs:81`) | `ForceRegistryTest`, `CommandToolApiTest.InspectForce*` | — |
| 30 | Force groups expose health/casualty state | ✅ | ✅ | `CoalitionForceRegistry`, `ForceGroup`, `SpecialAsset`, `ProductionFacility` (`CoalitionBlackboard.cs:81`) | `ForceRegistryTest`, `CommandToolApiTest.InspectForce*` | — |
| 31 | Force groups expose capabilities (AA, artillery, etc.) | ✅ | ✅ | `CoalitionForceRegistry`, `ForceGroup`, `SpecialAsset`, `ProductionFacility` (`CoalitionBlackboard.cs:81`) | `ForceRegistryTest`, `CommandToolApiTest.InspectForce*` | — |
| 32 | Dead/destroyed actors auto-removed | ✅ | ✅ | `CoalitionForceRegistry`, `ForceGroup`, `SpecialAsset`, `ProductionFacility` (`CoalitionBlackboard.cs:81`) | `ForceRegistryTest`, `CommandToolApiTest.InspectForce*` | — |
| 33 | Newly created units auto-discovered | ✅ | ✅ | `CoalitionForceRegistry`, `ForceGroup`, `SpecialAsset`, `ProductionFacility` (`CoalitionBlackboard.cs:81`) | `ForceRegistryTest`, `CommandToolApiTest.InspectForce*` | — |
| 34 | Forces can be released and reassigned | ✅ | ✅ | `CoalitionForceRegistry`, `ForceGroup`, `SpecialAsset`, `ProductionFacility` (`CoalitionBlackboard.cs:81`) | `ForceRegistryTest`, `CommandToolApiTest.InspectForce*` | — |

### §Order Ownership & Arbitration  <span>(35–46)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 35 | Central order arbiter prevents unit conflicts | ✅ | ✅ | `CoalitionOrderArbiter`, `ForceCommitment`, `ArbiterPriority`, `CommandValidator` | `OrderArbiterTest`, `CommandValidatorTest`, `MissionLifecycleTest` | — |
| 36 | Every committed unit has mission owner | ✅ | ✅ | `CoalitionOrderArbiter`, `ForceCommitment`, `ArbiterPriority`, `CommandValidator` | `OrderArbiterTest`, `CommandValidatorTest`, `MissionLifecycleTest` | — |
| 37 | Every committed unit has tactical role | ✅ | ✅ | `CoalitionOrderArbiter`, `ForceCommitment`, `ArbiterPriority`, `CommandValidator` | `OrderArbiterTest`, `CommandValidatorTest`, `MissionLifecycleTest` | — |
| 38 | Every commitment has release condition | ✅ | ✅ | `CoalitionOrderArbiter`, `ForceCommitment`, `ArbiterPriority`, `CommandValidator` | `OrderArbiterTest`, `CommandValidatorTest`, `MissionLifecycleTest` | — |
| 39 | Emergency orders override lower-priority | ✅ | ✅ | `CoalitionOrderArbiter`, `ForceCommitment`, `ArbiterPriority`, `CommandValidator` | `OrderArbiterTest`, `CommandValidatorTest`, `MissionLifecycleTest` | — |
| 40 | Special-op missions reserve special assets | ✅ | ✅ | `CoalitionOrderArbiter`, `ForceCommitment`, `ArbiterPriority`, `CommandValidator` | `OrderArbiterTest`, `CommandValidatorTest`, `MissionLifecycleTest` | — |
| 41 | Active combat missions outrank routine | ✅ | ✅ | `CoalitionOrderArbiter`, `ForceCommitment`, `ArbiterPriority`, `CommandValidator` | `OrderArbiterTest`, `CommandValidatorTest`, `MissionLifecycleTest` | — |
| 42 | Defense requests without stealing from critical ops | ✅ | ✅ | `CoalitionOrderArbiter`, `ForceCommitment`, `ArbiterPriority`, `CommandValidator` | `OrderArbiterTest`, `CommandValidatorTest`, `MissionLifecycleTest` | — |
| 43 | Conflicting LLM mission assignments detected | ✅ | ✅ | `CoalitionOrderArbiter`, `ForceCommitment`, `ArbiterPriority`, `CommandValidator` | `OrderArbiterTest`, `CommandValidatorTest`, `MissionLifecycleTest` | — |
| 44 | Invalid mission assignments rejected | ✅ | ✅ | `CoalitionOrderArbiter`, `ForceCommitment`, `ArbiterPriority`, `CommandValidator` | `OrderArbiterTest`, `CommandValidatorTest`, `MissionLifecycleTest` | — |
| 45 | Rejected commands return machine-readable reasons | ✅ | ✅ | `CoalitionOrderArbiter`, `ForceCommitment`, `ArbiterPriority`, `CommandValidator` | `OrderArbiterTest`, `CommandValidatorTest`, `MissionLifecycleTest` | — |
| 46 | Mission cancellation releases assigned units | ✅ | ✅ | `CoalitionOrderArbiter`, `ForceCommitment`, `ArbiterPriority`, `CommandValidator` | `OrderArbiterTest`, `CommandValidatorTest`, `MissionLifecycleTest` | — |

### §World State Extraction  <span>(47–70)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 47 | AI can read authoritative game-engine state | ✅ | ✅ | `CoalitionBlackboard`, `CommandToolApi.GetEconomyState/GetProductionState`, `CoalitionMapAnalysis` | `CommandToolApiTest`, `MapAnalysisTest`, `WaterAreaTest`, `HeadlessSkirmishTest` | — |
| 48 | Friendly unit types available | ✅ | ✅ | `CoalitionBlackboard`, `CommandToolApi.GetEconomyState/GetProductionState`, `CoalitionMapAnalysis` | `CommandToolApiTest`, `MapAnalysisTest`, `WaterAreaTest`, `HeadlessSkirmishTest` | — |
| 49 | Friendly unit positions available | ✅ | ✅ | `CoalitionBlackboard`, `CommandToolApi.GetEconomyState/GetProductionState`, `CoalitionMapAnalysis` | `CommandToolApiTest`, `MapAnalysisTest`, `WaterAreaTest`, `HeadlessSkirmishTest` | — |
| 50 | Friendly unit health available | ✅ | ✅ | `CoalitionBlackboard`, `CommandToolApi.GetEconomyState/GetProductionState`, `CoalitionMapAnalysis` | `CommandToolApiTest`, `MapAnalysisTest`, `WaterAreaTest`, `HeadlessSkirmishTest` | — |
| 51 | Friendly unit activity/order state available | ✅ | ✅ | `CoalitionBlackboard`, `CommandToolApi.GetEconomyState/GetProductionState`, `CoalitionMapAnalysis` | `CommandToolApiTest`, `MapAnalysisTest`, `WaterAreaTest`, `HeadlessSkirmishTest` | — |
| 52 | Friendly structures available | ✅ | ✅ | `CoalitionBlackboard`, `CommandToolApi.GetEconomyState/GetProductionState`, `CoalitionMapAnalysis` | `CommandToolApiTest`, `MapAnalysisTest`, `WaterAreaTest`, `HeadlessSkirmishTest` | — |
| 53 | Production facilities available | ✅ | ✅ | `CoalitionBlackboard`, `CommandToolApi.GetEconomyState/GetProductionState`, `CoalitionMapAnalysis` | `CommandToolApiTest`, `MapAnalysisTest`, `WaterAreaTest`, `HeadlessSkirmishTest` | — |
| 54 | Production queues available | ✅ | ✅ | `CoalitionBlackboard`, `CommandToolApi.GetEconomyState/GetProductionState`, `CoalitionMapAnalysis` | `CommandToolApiTest`, `MapAnalysisTest`, `WaterAreaTest`, `HeadlessSkirmishTest` | — |
| 55 | Production progress available | ✅ | ✅ | `CoalitionBlackboard`, `CommandToolApi.GetEconomyState/GetProductionState`, `CoalitionMapAnalysis` | `CommandToolApiTest`, `MapAnalysisTest`, `WaterAreaTest`, `HeadlessSkirmishTest` | — |
| 56 | Player resource/cash state available | ✅ | ✅ | `CoalitionBlackboard`, `CommandToolApi.GetEconomyState/GetProductionState`, `CoalitionMapAnalysis` | `CommandToolApiTest`, `MapAnalysisTest`, `WaterAreaTest`, `HeadlessSkirmishTest` | — |
| 57 | Power state available | ✅ | ✅ | `CoalitionBlackboard`, `CommandToolApi.GetEconomyState/GetProductionState`, `CoalitionMapAnalysis` | `CommandToolApiTest`, `MapAnalysisTest`, `WaterAreaTest`, `HeadlessSkirmishTest` | — |
| 58 | Tech/prerequisite availability | ✅ | ✅ | `CoalitionBlackboard`, `CommandToolApi.GetEconomyState/GetProductionState`, `CoalitionMapAnalysis` | `CommandToolApiTest`, `MapAnalysisTest`, `WaterAreaTest`, `HeadlessSkirmishTest` | — |
| 59 | Support-power readiness available | ✅ | ✅ | `CoalitionBlackboard`, `CommandToolApi.GetEconomyState/GetProductionState`, `CoalitionMapAnalysis` | `CommandToolApiTest`, `MapAnalysisTest`, `WaterAreaTest`, `HeadlessSkirmishTest` | — |
| 60 | Map dimensions available | ✅ | ✅ | `CoalitionBlackboard`, `CommandToolApi.GetEconomyState/GetProductionState`, `CoalitionMapAnalysis` | `CommandToolApiTest`, `MapAnalysisTest`, `WaterAreaTest`, `HeadlessSkirmishTest` | — |
| 61 | Terrain types available | ✅ | ✅ | `CoalitionBlackboard`, `CommandToolApi.GetEconomyState/GetProductionState`, `CoalitionMapAnalysis` | `CommandToolApiTest`, `MapAnalysisTest`, `WaterAreaTest`, `HeadlessSkirmishTest` | — |
| 62 | Water and land areas identifiable | ✅ | ✅ | `CoalitionBlackboard`, `CommandToolApi.GetEconomyState/GetProductionState`, `CoalitionMapAnalysis` | `CommandToolApiTest`, `MapAnalysisTest`, `WaterAreaTest`, `HeadlessSkirmishTest` | — |
| 63 | Rivers represented | ✅ | ✅ | `CoalitionBlackboard`, `CommandToolApi.GetEconomyState/GetProductionState`, `CoalitionMapAnalysis` | `CommandToolApiTest`, `MapAnalysisTest`, `WaterAreaTest`, `HeadlessSkirmishTest` | — |
| 64 | Bridges identified | ✅ | ✅ | `CoalitionBlackboard`, `CommandToolApi.GetEconomyState/GetProductionState`, `CoalitionMapAnalysis` | `CommandToolApiTest`, `MapAnalysisTest`, `WaterAreaTest`, `HeadlessSkirmishTest` | — |
| 65 | Impassable terrain identified | ✅ | ✅ | `CoalitionBlackboard`, `CommandToolApi.GetEconomyState/GetProductionState`, `CoalitionMapAnalysis` | `CommandToolApiTest`, `MapAnalysisTest`, `WaterAreaTest`, `HeadlessSkirmishTest` | — |
| 66 | Resource fields identified | ✅ | ✅ | `CoalitionBlackboard`, `CommandToolApi.GetEconomyState/GetProductionState`, `CoalitionMapAnalysis` | `CommandToolApiTest`, `MapAnalysisTest`, `WaterAreaTest`, `HeadlessSkirmishTest` | — |
| 67 | Expansion areas identified | ✅ | ✅ | `CoalitionBlackboard`, `CommandToolApi.GetEconomyState/GetProductionState`, `CoalitionMapAnalysis` | `CommandToolApiTest`, `MapAnalysisTest`, `WaterAreaTest`, `HeadlessSkirmishTest` | — |
| 68 | Building-placement areas analyzed | ✅ | ✅ | `CoalitionBlackboard`, `CommandToolApi.GetEconomyState/GetProductionState`, `CoalitionMapAnalysis` | `CommandToolApiTest`, `MapAnalysisTest`, `WaterAreaTest`, `HeadlessSkirmishTest` | — |
| 69 | Enemy actors exposed per visibility rules | ✅ | ✅ | `CoalitionBlackboard`, `CommandToolApi.GetEconomyState/GetProductionState`, `CoalitionMapAnalysis` | `CommandToolApiTest`, `MapAnalysisTest`, `WaterAreaTest`, `HeadlessSkirmishTest` | — |
| 70 | Significant events wake strategic reasoning | ✅ | ✅ | `CoalitionBlackboard`, `CommandToolApi.GetEconomyState/GetProductionState`, `CoalitionMapAnalysis` | `CommandToolApiTest`, `MapAnalysisTest`, `WaterAreaTest`, `HeadlessSkirmishTest` | — |

### §Fog of War & Intelligence Fairness  <span>(71–85)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 71 | Static terrain separated from dynamic intel | ✅ | ✅ | `CoalitionIntelTracker`, `EnemyIntel`/`IntelStatus` ladder, `CoalitionBlackboard.cs:616`, `CoalitionCommandCenterBotModule.cs:991` | `IntelTrackerTest`, `ExternalBrainSnapshotTest.FairFogRejectsInvisible`, `ExpandedCoverageTest` | — |
| 72 | Visible enemies tagged OBSERVED | ✅ | ✅ | `CoalitionIntelTracker`, `EnemyIntel`/`IntelStatus` ladder, `CoalitionBlackboard.cs:616`, `CoalitionCommandCenterBotModule.cs:991` | `IntelTrackerTest`, `ExternalBrainSnapshotTest.FairFogRejectsInvisible`, `ExpandedCoverageTest` | — |
| 73 | Visible buildings tagged OBSERVED | ✅ | ✅ | `CoalitionIntelTracker`, `EnemyIntel`/`IntelStatus` ladder, `CoalitionBlackboard.cs:616`, `CoalitionCommandCenterBotModule.cs:991` | `IntelTrackerTest`, `ExternalBrainSnapshotTest.FairFogRejectsInvisible`, `ExpandedCoverageTest` | — |
| 74 | Hidden enemies = LAST_KNOWN not current truth | ✅ | ✅ | `CoalitionIntelTracker`, `EnemyIntel`/`IntelStatus` ladder, `CoalitionBlackboard.cs:616`, `CoalitionCommandCenterBotModule.cs:991` | `IntelTrackerTest`, `ExternalBrainSnapshotTest.FairFogRejectsInvisible`, `ExpandedCoverageTest` | — |
| 75 | Inferred info tagged INFERRED | ✅ | ✅ | `CoalitionIntelTracker`, `EnemyIntel`/`IntelStatus` ladder, `CoalitionBlackboard.cs:616`, `CoalitionCommandCenterBotModule.cs:991` | `IntelTrackerTest`, `ExternalBrainSnapshotTest.FairFogRejectsInvisible`, `ExpandedCoverageTest` | — |
| 76 | Suspected info tagged SUSPECTED | ✅ | ✅ | `CoalitionIntelTracker`, `EnemyIntel`/`IntelStatus` ladder, `CoalitionBlackboard.cs:616`, `CoalitionCommandCenterBotModule.cs:991` | `IntelTrackerTest`, `ExternalBrainSnapshotTest.FairFogRejectsInvisible`, `ExpandedCoverageTest` | — |
| 77 | Unknown info remains UNKNOWN | ✅ | ✅ | `CoalitionIntelTracker`, `EnemyIntel`/`IntelStatus` ladder, `CoalitionBlackboard.cs:616`, `CoalitionCommandCenterBotModule.cs:991` | `IntelTrackerTest`, `ExternalBrainSnapshotTest.FairFogRejectsInvisible`, `ExpandedCoverageTest` | — |
| 78 | LLM cannot receive hidden positions in fair-fog | ✅ | ✅ | `CoalitionIntelTracker`, `EnemyIntel`/`IntelStatus` ladder, `CoalitionBlackboard.cs:616`, `CoalitionCommandCenterBotModule.cs:991` | `IntelTrackerTest`, `ExternalBrainSnapshotTest.FairFogRejectsInvisible`, `ExpandedCoverageTest` | — |
| 79 | Last-known positions retain timestamps | ✅ | ✅ | `CoalitionIntelTracker`, `EnemyIntel`/`IntelStatus` ladder, `CoalitionBlackboard.cs:616`, `CoalitionCommandCenterBotModule.cs:991` | `IntelTrackerTest`, `ExternalBrainSnapshotTest.FairFogRejectsInvisible`, `ExpandedCoverageTest` | — |
| 80 | Last-known info has confidence values | ✅ | ✅ | `CoalitionIntelTracker`, `EnemyIntel`/`IntelStatus` ladder, `CoalitionBlackboard.cs:616`, `CoalitionCommandCenterBotModule.cs:991` | `IntelTrackerTest`, `ExternalBrainSnapshotTest.FairFogRejectsInvisible`, `ExpandedCoverageTest` | — |
| 81 | Confidence decays as info ages | ✅ | ✅ | `CoalitionIntelTracker`, `EnemyIntel`/`IntelStatus` ladder, `CoalitionBlackboard.cs:616`, `CoalitionCommandCenterBotModule.cs:991` | `IntelTrackerTest`, `ExternalBrainSnapshotTest.FairFogRejectsInvisible`, `ExpandedCoverageTest` | — |
| 82 | Mobile position uncertainty grows with time | ✅ | ✅ | `CoalitionIntelTracker`, `EnemyIntel`/`IntelStatus` ladder, `CoalitionBlackboard.cs:616`, `CoalitionCommandCenterBotModule.cs:991` | `IntelTrackerTest`, `ExternalBrainSnapshotTest.FairFogRejectsInvisible`, `ExpandedCoverageTest` | — |
| 83 | Structures retain appropriate confidence | ✅ | ✅ | `CoalitionIntelTracker`, `EnemyIntel`/`IntelStatus` ladder, `CoalitionBlackboard.cs:616`, `CoalitionCommandCenterBotModule.cs:991` | `IntelTrackerTest`, `ExternalBrainSnapshotTest.FairFogRejectsInvisible`, `ExpandedCoverageTest` | — |
| 84 | Omniscient mode optionally enabled | ✅ | ✅ | `CoalitionIntelTracker`, `EnemyIntel`/`IntelStatus` ladder, `CoalitionBlackboard.cs:616`, `CoalitionCommandCenterBotModule.cs:991` | `IntelTrackerTest`, `ExternalBrainSnapshotTest.FairFogRejectsInvisible`, `ExpandedCoverageTest` | — |
| 85 | Fair-fog and omniscient independently testable | ✅ | ✅ | `CoalitionIntelTracker`, `EnemyIntel`/`IntelStatus` ladder, `CoalitionBlackboard.cs:616`, `CoalitionCommandCenterBotModule.cs:991` | `IntelTrackerTest`, `ExternalBrainSnapshotTest.FairFogRejectsInvisible`, `ExpandedCoverageTest` | — |

### §Map Analysis  <span>(86–106)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 86 | Map divided into strategic regions | ✅ | ✅ | `CoalitionMapAnalysis` (regions, adjacency, chokepoints, bridges, islands, `MovementClass`) | `MapAnalysisTest`, `WaterAreaTest`, `RoutePlannerTest` | — |
| 87 | Regions have stable IDs | ✅ | ✅ | `CoalitionMapAnalysis` (regions, adjacency, chokepoints, bridges, islands, `MovementClass`) | `MapAnalysisTest`, `WaterAreaTest`, `RoutePlannerTest` | — |
| 88 | Adjacent regions in a graph | ✅ | ✅ | `CoalitionMapAnalysis` (regions, adjacency, chokepoints, bridges, islands, `MovementClass`) | `MapAnalysisTest`, `WaterAreaTest`, `RoutePlannerTest` | — |
| 89 | Chokepoints detected | ✅ | ✅ | `CoalitionMapAnalysis` (regions, adjacency, chokepoints, bridges, islands, `MovementClass`) | `MapAnalysisTest`, `WaterAreaTest`, `RoutePlannerTest` | — |
| 90 | Bridges as strategic connectors | ✅ | ✅ | `CoalitionMapAnalysis` (regions, adjacency, chokepoints, bridges, islands, `MovementClass`) | `MapAnalysisTest`, `WaterAreaTest`, `RoutePlannerTest` | — |
| 91 | Narrow naval passages detected | ✅ | ✅ | `CoalitionMapAnalysis` (regions, adjacency, chokepoints, bridges, islands, `MovementClass`) | `MapAnalysisTest`, `WaterAreaTest`, `RoutePlannerTest` | — |
| 92 | Islands detected | ✅ | ✅ | `CoalitionMapAnalysis` (regions, adjacency, chokepoints, bridges, islands, `MovementClass`) | `MapAnalysisTest`, `WaterAreaTest`, `RoutePlannerTest` | — |
| 93 | Resource-rich areas scored | ✅ | ✅ | `CoalitionMapAnalysis` (regions, adjacency, chokepoints, bridges, islands, `MovementClass`) | `MapAnalysisTest`, `WaterAreaTest`, `RoutePlannerTest` | — |
| 94 | Expansion locations scored | ✅ | ✅ | `CoalitionMapAnalysis` (regions, adjacency, chokepoints, bridges, islands, `MovementClass`) | `MapAnalysisTest`, `WaterAreaTest`, `RoutePlannerTest` | — |
| 95 | Defensible positions identified | ✅ | ✅ | `CoalitionMapAnalysis` (regions, adjacency, chokepoints, bridges, islands, `MovementClass`) | `MapAnalysisTest`, `WaterAreaTest`, `RoutePlannerTest` | — |
| 96 | Rally/staging areas identified | ✅ | ✅ | `CoalitionMapAnalysis` (regions, adjacency, chokepoints, bridges, islands, `MovementClass`) | `MapAnalysisTest`, `WaterAreaTest`, `RoutePlannerTest` | — |
| 97 | Artillery positions identified | ✅ | ✅ | `CoalitionMapAnalysis` (regions, adjacency, chokepoints, bridges, islands, `MovementClass`) | `MapAnalysisTest`, `WaterAreaTest`, `RoutePlannerTest` | — |
| 98 | Attack corridors identified | ✅ | ✅ | `CoalitionMapAnalysis` (regions, adjacency, chokepoints, bridges, islands, `MovementClass`) | `MapAnalysisTest`, `WaterAreaTest`, `RoutePlannerTest` | — |
| 99 | Retreat corridors identified | ✅ | ✅ | `CoalitionMapAnalysis` (regions, adjacency, chokepoints, bridges, islands, `MovementClass`) | `MapAnalysisTest`, `WaterAreaTest`, `RoutePlannerTest` | — |
| 100 | Transport insertion zones identified | ✅ | ✅ | `CoalitionMapAnalysis` (regions, adjacency, chokepoints, bridges, islands, `MovementClass`) | `MapAnalysisTest`, `WaterAreaTest`, `RoutePlannerTest` | — |
| 101 | Ground movement graphs exist | ✅ | ✅ | `CoalitionMapAnalysis` (regions, adjacency, chokepoints, bridges, islands, `MovementClass`) | `MapAnalysisTest`, `WaterAreaTest`, `RoutePlannerTest` | — |
| 102 | Infantry movement graphs where different | ✅ | ✅ | `CoalitionMapAnalysis` (regions, adjacency, chokepoints, bridges, islands, `MovementClass`) | `MapAnalysisTest`, `WaterAreaTest`, `RoutePlannerTest` | — |
| 103 | Naval movement graphs exist | ✅ | ✅ | `CoalitionMapAnalysis` (regions, adjacency, chokepoints, bridges, islands, `MovementClass`) | `MapAnalysisTest`, `WaterAreaTest`, `RoutePlannerTest` | — |
| 104 | Aircraft routing represented separately | ✅ | ✅ | `CoalitionMapAnalysis` (regions, adjacency, chokepoints, bridges, islands, `MovementClass`) | `MapAnalysisTest`, `WaterAreaTest`, `RoutePlannerTest` | — |
| 105 | Transport routing uses appropriate constraints | ✅ | ✅ | `CoalitionMapAnalysis` (regions, adjacency, chokepoints, bridges, islands, `MovementClass`) | `MapAnalysisTest`, `WaterAreaTest`, `RoutePlannerTest` | — |
| 106 | LLM reasons about regions not raw cells | ✅ | ✅ | `CoalitionMapAnalysis` (regions, adjacency, chokepoints, bridges, islands, `MovementClass`) | `MapAnalysisTest`, `WaterAreaTest`, `RoutePlannerTest` | — |

### §Threat Modeling  <span>(107–125)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 107 | Ground anti-armor threat modeled | ✅ | ✅ | `CoalitionCapability` (14 axes), `CoalitionBlackboard.Threats`, `RouteWeights` profiles | `ThreatModelTest`, `MapAnalysisTest`, `RoutePlannerTest` | — |
| 108 | Ground anti-infantry threat modeled | ✅ | ✅ | `CoalitionCapability` (14 axes), `CoalitionBlackboard.Threats`, `RouteWeights` profiles | `ThreatModelTest`, `MapAnalysisTest`, `RoutePlannerTest` | — |
| 109 | Artillery threat modeled | ✅ | ✅ | `CoalitionCapability` (14 axes), `CoalitionBlackboard.Threats`, `RouteWeights` profiles | `ThreatModelTest`, `MapAnalysisTest`, `RoutePlannerTest` | — |
| 110 | Anti-air threat modeled | ✅ | ✅ | `CoalitionCapability` (14 axes), `CoalitionBlackboard.Threats`, `RouteWeights` profiles | `ThreatModelTest`, `MapAnalysisTest`, `RoutePlannerTest` | — |
| 111 | Air-to-air interception threat modeled | ✅ | ✅ | `CoalitionCapability` (14 axes), `CoalitionBlackboard.Threats`, `RouteWeights` profiles | `ThreatModelTest`, `MapAnalysisTest`, `RoutePlannerTest` | — |
| 112 | Naval threat modeled | ✅ | ✅ | `CoalitionCapability` (14 axes), `CoalitionBlackboard.Threats`, `RouteWeights` profiles | `ThreatModelTest`, `MapAnalysisTest`, `RoutePlannerTest` | — |
| 113 | Submarine threat modeled | ✅ | ✅ | `CoalitionCapability` (14 axes), `CoalitionBlackboard.Threats`, `RouteWeights` profiles | `ThreatModelTest`, `MapAnalysisTest`, `RoutePlannerTest` | — |
| 114 | Static-defense threat modeled | ✅ | ✅ | `CoalitionCapability` (14 axes), `CoalitionBlackboard.Threats`, `RouteWeights` profiles | `ThreatModelTest`, `MapAnalysisTest`, `RoutePlannerTest` | — |
| 115 | Enemy vision/exposure risk modeled | ✅ | ✅ | `CoalitionCapability` (14 axes), `CoalitionBlackboard.Threats`, `RouteWeights` profiles | `ThreatModelTest`, `MapAnalysisTest`, `RoutePlannerTest` | — |
| 116 | Detection risk modeled | ✅ | ✅ | `CoalitionCapability` (14 axes), `CoalitionBlackboard.Threats`, `RouteWeights` profiles | `ThreatModelTest`, `MapAnalysisTest`, `RoutePlannerTest` | — |
| 117 | Enemy reinforcement risk modeled | ✅ | ✅ | `CoalitionCapability` (14 axes), `CoalitionBlackboard.Threats`, `RouteWeights` profiles | `ThreatModelTest`, `MapAnalysisTest`, `RoutePlannerTest` | — |
| 118 | Support-power danger represented | ✅ | ✅ | `CoalitionCapability` (14 axes), `CoalitionBlackboard.Threats`, `RouteWeights` profiles | `ThreatModelTest`, `MapAnalysisTest`, `RoutePlannerTest` | — |
| 119 | Threat maps update as intel changes | ✅ | ✅ | `CoalitionCapability` (14 axes), `CoalitionBlackboard.Threats`, `RouteWeights` profiles | `ThreatModelTest`, `MapAnalysisTest`, `RoutePlannerTest` | — |
| 120 | Threat estimates account for confidence | ✅ | ✅ | `CoalitionCapability` (14 axes), `CoalitionBlackboard.Threats`, `RouteWeights` profiles | `ThreatModelTest`, `MapAnalysisTest`, `RoutePlannerTest` | — |
| 121 | Different unit types request different weightings | ✅ | ✅ | `CoalitionCapability` (14 axes), `CoalitionBlackboard.Threats`, `RouteWeights` profiles | `ThreatModelTest`, `MapAnalysisTest`, `RoutePlannerTest` | — |
| 122 | Aircraft heavily weight AA danger | ✅ | ✅ | `CoalitionCapability` (14 axes), `CoalitionBlackboard.Threats`, `RouteWeights` profiles | `ThreatModelTest`, `MapAnalysisTest`, `RoutePlannerTest` | — |
| 123 | Special ops weight visibility/detection | ✅ | ✅ | `CoalitionCapability` (14 axes), `CoalitionBlackboard.Threats`, `RouteWeights` profiles | `ThreatModelTest`, `MapAnalysisTest`, `RoutePlannerTest` | — |
| 124 | Ground assault weights chokepoints/threats | ✅ | ✅ | `CoalitionCapability` (14 axes), `CoalitionBlackboard.Threats`, `RouteWeights` profiles | `ThreatModelTest`, `MapAnalysisTest`, `RoutePlannerTest` | — |
| 125 | Naval units use naval-specific considerations | ✅ | ✅ | `CoalitionCapability` (14 axes), `CoalitionBlackboard.Threats`, `RouteWeights` profiles | `ThreatModelTest`, `MapAnalysisTest`, `RoutePlannerTest` | — |

### §Route Planning  <span>(126–143)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 126 | Routing not limited to shortest-path | ✅ | ✅ | `CoalitionRoutePlanner`, `RouteWeights` (12 cost terms; Stealth/Assault/Recon/Retreat profiles) | `RoutePlannerTest`, `MissionLifecycleTest.RouteDisruptions` | — |
| 127 | Route scoring includes travel distance | ✅ | ✅ | `CoalitionRoutePlanner`, `RouteWeights` (12 cost terms; Stealth/Assault/Recon/Retreat profiles) | `RoutePlannerTest`, `MissionLifecycleTest.RouteDisruptions` | — |
| 128 | Route scoring includes combat threat | ✅ | ✅ | `CoalitionRoutePlanner`, `RouteWeights` (12 cost terms; Stealth/Assault/Recon/Retreat profiles) | `RoutePlannerTest`, `MissionLifecycleTest.RouteDisruptions` | — |
| 129 | Route scoring includes AA threat | ✅ | ✅ | `CoalitionRoutePlanner`, `RouteWeights` (12 cost terms; Stealth/Assault/Recon/Retreat profiles) | `RoutePlannerTest`, `MissionLifecycleTest.RouteDisruptions` | — |
| 130 | Route scoring includes vision exposure | ✅ | ✅ | `CoalitionRoutePlanner`, `RouteWeights` (12 cost terms; Stealth/Assault/Recon/Retreat profiles) | `RoutePlannerTest`, `MissionLifecycleTest.RouteDisruptions` | — |
| 131 | Route scoring includes detection exposure | ✅ | ✅ | `CoalitionRoutePlanner`, `RouteWeights` (12 cost terms; Stealth/Assault/Recon/Retreat profiles) | `RoutePlannerTest`, `MissionLifecycleTest.RouteDisruptions` | — |
| 132 | Route scoring includes combat-zone proximity | ✅ | ✅ | `CoalitionRoutePlanner`, `RouteWeights` (12 cost terms; Stealth/Assault/Recon/Retreat profiles) | `RoutePlannerTest`, `MissionLifecycleTest.RouteDisruptions` | — |
| 133 | Route scoring includes chokepoint risk | ✅ | ✅ | `CoalitionRoutePlanner`, `RouteWeights` (12 cost terms; Stealth/Assault/Recon/Retreat profiles) | `RoutePlannerTest`, `MissionLifecycleTest.RouteDisruptions` | — |
| 134 | Route scoring includes congestion | ✅ | ✅ | `CoalitionRoutePlanner`, `RouteWeights` (12 cost terms; Stealth/Assault/Recon/Retreat profiles) | `RoutePlannerTest`, `MissionLifecycleTest.RouteDisruptions` | — |
| 135 | Route scoring includes reinforcement lanes | ✅ | ✅ | `CoalitionRoutePlanner`, `RouteWeights` (12 cost terms; Stealth/Assault/Recon/Retreat profiles) | `RoutePlannerTest`, `MissionLifecycleTest.RouteDisruptions` | — |
| 136 | Route scoring includes artillery exposure | ✅ | ✅ | `CoalitionRoutePlanner`, `RouteWeights` (12 cost terms; Stealth/Assault/Recon/Retreat profiles) | `RoutePlannerTest`, `MissionLifecycleTest.RouteDisruptions` | — |
| 137 | Different missions assign different weights | ✅ | ✅ | `CoalitionRoutePlanner`, `RouteWeights` (12 cost terms; Stealth/Assault/Recon/Retreat profiles) | `RoutePlannerTest`, `MissionLifecycleTest.RouteDisruptions` | — |
| 138 | Safe routes for transports | ✅ | ✅ | `CoalitionRoutePlanner`, `RouteWeights` (12 cost terms; Stealth/Assault/Recon/Retreat profiles) | `RoutePlannerTest`, `MissionLifecycleTest.RouteDisruptions` | — |
| 139 | Safe routes for special forces | ✅ | ✅ | `CoalitionRoutePlanner`, `RouteWeights` (12 cost terms; Stealth/Assault/Recon/Retreat profiles) | `RoutePlannerTest`, `MissionLifecycleTest.RouteDisruptions` | — |
| 140 | Main assault prioritizes combat efficiency | ✅ | ✅ | `CoalitionRoutePlanner`, `RouteWeights` (12 cost terms; Stealth/Assault/Recon/Retreat profiles) | `RoutePlannerTest`, `MissionLifecycleTest.RouteDisruptions` | — |
| 141 | Retreat routes planned separately | ✅ | ✅ | `CoalitionRoutePlanner`, `RouteWeights` (12 cost terms; Stealth/Assault/Recon/Retreat profiles) | `RoutePlannerTest`, `MissionLifecycleTest.RouteDisruptions` | — |
| 142 | Routes recalculated after threat changes | ✅ | ✅ | `CoalitionRoutePlanner`, `RouteWeights` (12 cost terms; Stealth/Assault/Recon/Retreat profiles) | `RoutePlannerTest`, `MissionLifecycleTest.RouteDisruptions` | — |
| 143 | Missions abort when no viable route | ✅ | ✅ | `CoalitionRoutePlanner`, `RouteWeights` (12 cost terms; Stealth/Assault/Recon/Retreat profiles) | `RoutePlannerTest`, `MissionLifecycleTest.RouteDisruptions` | — |

### §Combat Evaluation  <span>(144–159)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 144 | Deterministic combat estimator exists | ✅ | ✅ | `CombatEstimator`, `TargetEvaluator` | `CombatEstimatorTest`, `TargetEvaluatorTest`, `AcceptanceSuite.CombatEstimateInvariants` | — |
| 145 | Compare two force packages | ✅ | ✅ | `CombatEstimator`, `TargetEvaluator` | `CombatEstimatorTest`, `TargetEvaluatorTest`, `AcceptanceSuite.CombatEstimateInvariants` | — |
| 146 | Accounts for unit health | ✅ | ✅ | `CombatEstimator`, `TargetEvaluator` | `CombatEstimatorTest`, `TargetEvaluatorTest`, `AcceptanceSuite.CombatEstimateInvariants` | — |
| 147 | Accounts for weapon matchups | ✅ | ✅ | `CombatEstimator`, `TargetEvaluator` | `CombatEstimatorTest`, `TargetEvaluatorTest`, `AcceptanceSuite.CombatEstimateInvariants` | — |
| 148 | Accounts for anti-air coverage | ✅ | ✅ | `CombatEstimator`, `TargetEvaluator` | `CombatEstimatorTest`, `TargetEvaluatorTest`, `AcceptanceSuite.CombatEstimateInvariants` | — |
| 149 | Accounts for artillery/range advantage | ✅ | ✅ | `CombatEstimator`, `TargetEvaluator` | `CombatEstimatorTest`, `TargetEvaluatorTest`, `AcceptanceSuite.CombatEstimateInvariants` | — |
| 150 | Accounts for terrain | ✅ | ✅ | `CombatEstimator`, `TargetEvaluator` | `CombatEstimatorTest`, `TargetEvaluatorTest`, `AcceptanceSuite.CombatEstimateInvariants` | — |
| 151 | Accounts for reinforcement potential | ✅ | ✅ | `CombatEstimator`, `TargetEvaluator` | `CombatEstimatorTest`, `TargetEvaluatorTest`, `AcceptanceSuite.CombatEstimateInvariants` | — |
| 152 | Estimates probability of success | ✅ | ✅ | `CombatEstimator`, `TargetEvaluator` | `CombatEstimatorTest`, `TargetEvaluatorTest`, `AcceptanceSuite.CombatEstimateInvariants` | — |
| 153 | Estimates expected friendly losses | ✅ | ✅ | `CombatEstimator`, `TargetEvaluator` | `CombatEstimatorTest`, `TargetEvaluatorTest`, `AcceptanceSuite.CombatEstimateInvariants` | — |
| 154 | Estimates expected enemy losses | ✅ | ✅ | `CombatEstimator`, `TargetEvaluator` | `CombatEstimatorTest`, `TargetEvaluatorTest`, `AcceptanceSuite.CombatEstimateInvariants` | — |
| 155 | Identifies major matchup weaknesses | ✅ | ✅ | `CombatEstimator`, `TargetEvaluator` | `CombatEstimatorTest`, `TargetEvaluatorTest`, `AcceptanceSuite.CombatEstimateInvariants` | — |
| 156 | Tells LLM when capabilities required | ✅ | ✅ | `CombatEstimator`, `TargetEvaluator` | `CombatEstimatorTest`, `TargetEvaluatorTest`, `AcceptanceSuite.CombatEstimateInvariants` | — |
| 157 | LLM uses combat estimates not arbitrary ratios | ✅ | 🟡 | `CombatEstimator`, `TargetEvaluator` | `CombatEstimatorTest`, `TargetEvaluatorTest`, `AcceptanceSuite.CombatEstimateInvariants` | The prompt mandates tool-derived estimates and `estimate_engagement` is engine-computed, but no test verifies the model actually consults it before committing. |
| 158 | Estimator tested vs representative engagements | ✅ | 🟡 | `CombatEstimator`, `TargetEvaluator` | `CombatEstimatorTest`, `TargetEvaluatorTest`, `AcceptanceSuite.CombatEstimateInvariants` | `CombatEstimatorTest` uses synthetic force profiles and invariants, not captured OpenRA engagements. |
| 159 | Accuracy measured vs actual replay outcomes | 🟡 | 🟡 | `CombatEstimator`, `TargetEvaluator` | `ai/selfplay.py --combat-accuracy` (match-level correlation only) | `ai/selfplay.py --combat-accuracy` correlates predicted win ratio with match outcome. The source comment is explicit that this is coarse: *"a real accuracy benchmark needs recorded per-engagement outcomes, which the replay harness can add later"*. No per-engagement replay validation. |

### §Mission Framework  <span>(160–182)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 160 | Generic Mission base type exists | ✅ | ✅ | `CoalitionMission` (Id/Objective/DesiredEffects/LaunchConditions/Contingencies/Phase/Readiness/Progress/OutcomeReason), `MissionManager` | `MissionLifecycleTest`, `ExpandedCoverageTest` | — |
| 161 | Missions have unique IDs | ✅ | ✅ | `CoalitionMission` (Id/Objective/DesiredEffects/LaunchConditions/Contingencies/Phase/Readiness/Progress/OutcomeReason), `MissionManager` | `MissionLifecycleTest`, `ExpandedCoverageTest` | — |
| 162 | Missions have explicit objectives | ✅ | ✅ | `CoalitionMission` (Id/Objective/DesiredEffects/LaunchConditions/Contingencies/Phase/Readiness/Progress/OutcomeReason), `MissionManager` | `MissionLifecycleTest`, `ExpandedCoverageTest` | — |
| 163 | Missions have strategic desired effects | ✅ | ✅ | `CoalitionMission` (Id/Objective/DesiredEffects/LaunchConditions/Contingencies/Phase/Readiness/Progress/OutcomeReason), `MissionManager` | `MissionLifecycleTest`, `ExpandedCoverageTest` | — |
| 164 | Missions have priorities | ✅ | ✅ | `CoalitionMission` (Id/Objective/DesiredEffects/LaunchConditions/Contingencies/Phase/Readiness/Progress/OutcomeReason), `MissionManager` | `MissionLifecycleTest`, `ExpandedCoverageTest` | — |
| 165 | Missions have assigned forces | ✅ | ✅ | `CoalitionMission` (Id/Objective/DesiredEffects/LaunchConditions/Contingencies/Phase/Readiness/Progress/OutcomeReason), `MissionManager` | `MissionLifecycleTest`, `ExpandedCoverageTest` | — |
| 166 | Missions have target regions/actors/areas | ✅ | ✅ | `CoalitionMission` (Id/Objective/DesiredEffects/LaunchConditions/Contingencies/Phase/Readiness/Progress/OutcomeReason), `MissionManager` | `MissionLifecycleTest`, `ExpandedCoverageTest` | — |
| 167 | Missions can define staging regions | ✅ | ✅ | `CoalitionMission` (Id/Objective/DesiredEffects/LaunchConditions/Contingencies/Phase/Readiness/Progress/OutcomeReason), `MissionManager` | `MissionLifecycleTest`, `ExpandedCoverageTest` | — |
| 168 | Missions can define routes | ✅ | ✅ | `CoalitionMission` (Id/Objective/DesiredEffects/LaunchConditions/Contingencies/Phase/Readiness/Progress/OutcomeReason), `MissionManager` | `MissionLifecycleTest`, `ExpandedCoverageTest` | — |
| 169 | Missions can contain multiple phases | ✅ | ✅ | `CoalitionMission` (Id/Objective/DesiredEffects/LaunchConditions/Contingencies/Phase/Readiness/Progress/OutcomeReason), `MissionManager` | `MissionLifecycleTest`, `ExpandedCoverageTest` | — |
| 170 | Missions define launch conditions | ✅ | ✅ | `CoalitionMission` (Id/Objective/DesiredEffects/LaunchConditions/Contingencies/Phase/Readiness/Progress/OutcomeReason), `MissionManager` | `MissionLifecycleTest`, `ExpandedCoverageTest` | — |
| 171 | Missions define success conditions | ✅ | ✅ | `CoalitionMission` (Id/Objective/DesiredEffects/LaunchConditions/Contingencies/Phase/Readiness/Progress/OutcomeReason), `MissionManager` | `MissionLifecycleTest`, `ExpandedCoverageTest` | — |
| 172 | Missions define abort conditions | ✅ | ✅ | `CoalitionMission` (Id/Objective/DesiredEffects/LaunchConditions/Contingencies/Phase/Readiness/Progress/OutcomeReason), `MissionManager` | `MissionLifecycleTest`, `ExpandedCoverageTest` | — |
| 173 | Missions define contingency plans | ✅ | ✅ | `CoalitionMission` (Id/Objective/DesiredEffects/LaunchConditions/Contingencies/Phase/Readiness/Progress/OutcomeReason), `MissionManager` | `MissionLifecycleTest`, `ExpandedCoverageTest` | — |
| 174 | Missions define withdrawal/extraction | ✅ | ✅ | `CoalitionMission` (Id/Objective/DesiredEffects/LaunchConditions/Contingencies/Phase/Readiness/Progress/OutcomeReason), `MissionManager` | `MissionLifecycleTest`, `ExpandedCoverageTest` | — |
| 175 | Missions expose current phase | ✅ | ✅ | `CoalitionMission` (Id/Objective/DesiredEffects/LaunchConditions/Contingencies/Phase/Readiness/Progress/OutcomeReason), `MissionManager` | `MissionLifecycleTest`, `ExpandedCoverageTest` | — |
| 176 | Missions expose readiness | ✅ | ✅ | `CoalitionMission` (Id/Objective/DesiredEffects/LaunchConditions/Contingencies/Phase/Readiness/Progress/OutcomeReason), `MissionManager` | `MissionLifecycleTest`, `ExpandedCoverageTest` | — |
| 177 | Missions expose progress | ✅ | ✅ | `CoalitionMission` (Id/Objective/DesiredEffects/LaunchConditions/Contingencies/Phase/Readiness/Progress/OutcomeReason), `MissionManager` | `MissionLifecycleTest`, `ExpandedCoverageTest` | — |
| 178 | Missions expose failure reasons | ✅ | ✅ | `CoalitionMission` (Id/Objective/DesiredEffects/LaunchConditions/Contingencies/Phase/Readiness/Progress/OutcomeReason), `MissionManager` | `MissionLifecycleTest`, `ExpandedCoverageTest` | — |
| 179 | Missions persist without LLM replanning | ✅ | ✅ | `CoalitionMission` (Id/Objective/DesiredEffects/LaunchConditions/Contingencies/Phase/Readiness/Progress/OutcomeReason), `MissionManager` | `MissionLifecycleTest`, `ExpandedCoverageTest` | — |
| 180 | State changes trigger mission reconsideration | ✅ | ✅ | `CoalitionMission` (Id/Objective/DesiredEffects/LaunchConditions/Contingencies/Phase/Readiness/Progress/OutcomeReason), `MissionManager` | `MissionLifecycleTest`, `ExpandedCoverageTest` | — |
| 181 | Completed missions release forces | ✅ | ✅ | `CoalitionMission` (Id/Objective/DesiredEffects/LaunchConditions/Contingencies/Phase/Readiness/Progress/OutcomeReason), `MissionManager` | `MissionLifecycleTest`, `ExpandedCoverageTest` | — |
| 182 | Failed missions release/retreat forces | ✅ | ✅ | `CoalitionMission` (Id/Objective/DesiredEffects/LaunchConditions/Contingencies/Phase/Readiness/Progress/OutcomeReason), `MissionManager` | `MissionLifecycleTest`, `ExpandedCoverageTest` | — |

### §Offensive Mission Types  <span>(183–199)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 183 | Breakthrough mission | ✅ | ✅ | `MissionType` enum (38 values) + per-type directives in `CoalitionMission.cs`, executed via `TacticalControllers` | `MissionLifecycleTest`, `ExpandedCoverageTest.NewOffensiveTypes` | — |
| 184 | Frontal assault mission | ✅ | ✅ | `MissionType` enum (38 values) + per-type directives in `CoalitionMission.cs`, executed via `TacticalControllers` | `MissionLifecycleTest`, `ExpandedCoverageTest.NewOffensiveTypes` | — |
| 185 | Flanking mission | ✅ | ✅ | `MissionType` enum (38 values) + per-type directives in `CoalitionMission.cs`, executed via `TacticalControllers` | `MissionLifecycleTest`, `ExpandedCoverageTest.NewOffensiveTypes` | — |
| 186 | Pincer/double-envelopment | ✅ | ✅ | `MissionType` enum (38 values) + per-type directives in `CoalitionMission.cs`, executed via `TacticalControllers` | `MissionLifecycleTest`, `ExpandedCoverageTest.NewOffensiveTypes` | — |
| 187 | Exploitation mission | 🟡 | ✅ | `MissionType` enum (38 values) + per-type directives in `CoalitionMission.cs`, executed via `TacticalControllers` | `MissionLifecycleTest`, `ExpandedCoverageTest.NewOffensiveTypes` | Exploitation exists as `MissionPhase.Exploitation` (a phase inside Breakthrough/Attack), not as a standalone `MissionType`. Behaviourally covered; vocabulary differs from the checklist. |
| 188 | Base assault mission | ✅ | ✅ | `MissionType` enum (38 values) + per-type directives in `CoalitionMission.cs`, executed via `TacticalControllers` | `MissionLifecycleTest`, `ExpandedCoverageTest.NewOffensiveTypes` | — |
| 189 | Siege mission | ✅ | ✅ | `MissionType` enum (38 values) + per-type directives in `CoalitionMission.cs`, executed via `TacticalControllers` | `MissionLifecycleTest`, `ExpandedCoverageTest.NewOffensiveTypes` | — |
| 190 | Harassment mission | ✅ | ✅ | `MissionType` enum (38 values) + per-type directives in `CoalitionMission.cs`, executed via `TacticalControllers` | `MissionLifecycleTest`, `ExpandedCoverageTest.NewOffensiveTypes` | — |
| 191 | Economy/harvester raid mission | ✅ | ✅ | `MissionType` enum (38 values) + per-type directives in `CoalitionMission.cs`, executed via `TacticalControllers` | `MissionLifecycleTest`, `ExpandedCoverageTest.NewOffensiveTypes` | — |
| 192 | Production raid mission | ✅ | ✅ | `MissionType` enum (38 values) + per-type directives in `CoalitionMission.cs`, executed via `TacticalControllers` | `MissionLifecycleTest`, `ExpandedCoverageTest.NewOffensiveTypes` | — |
| 193 | Expansion denial mission | ✅ | ✅ | `MissionType` enum (38 values) + per-type directives in `CoalitionMission.cs`, executed via `TacticalControllers` | `MissionLifecycleTest`, `ExpandedCoverageTest.NewOffensiveTypes` | — |
| 194 | Chokepoint/bridge seizure mission | ✅ | ✅ | `MissionType` enum (38 values) + per-type directives in `CoalitionMission.cs`, executed via `TacticalControllers` | `MissionLifecycleTest`, `ExpandedCoverageTest.NewOffensiveTypes` | — |
| 195 | Naval blockade mission | ✅ | ✅ | `MissionType` enum (38 values) + per-type directives in `CoalitionMission.cs`, executed via `TacticalControllers` | `MissionLifecycleTest`, `ExpandedCoverageTest.NewOffensiveTypes` | — |
| 196 | Coastal bombardment mission | ✅ | ✅ | `MissionType` enum (38 values) + per-type directives in `CoalitionMission.cs`, executed via `TacticalControllers` | `MissionLifecycleTest`, `ExpandedCoverageTest.NewOffensiveTypes` | — |
| 197 | Air-strike mission | ✅ | ✅ | `MissionType` enum (38 values) + per-type directives in `CoalitionMission.cs`, executed via `TacticalControllers` | `MissionLifecycleTest`, `ExpandedCoverageTest.NewOffensiveTypes` | — |
| 198 | Coordinated mass-air attack | ✅ | 🟡 | `MissionType` enum (38 values) + per-type directives in `CoalitionMission.cs`, executed via `TacticalControllers` | `MissionLifecycleTest`, `ExpandedCoverageTest.NewOffensiveTypes` | `MissionType.AirStrike` is type-tested; mass/coordinated air concentration is not asserted. |
| 199 | Support-power strike mission | ✅ | ✅ | `MissionType` enum (38 values) + per-type directives in `CoalitionMission.cs`, executed via `TacticalControllers` | `MissionLifecycleTest`, `ExpandedCoverageTest.NewOffensiveTypes` | — |

### §Defensive Mission Types  <span>(200–214)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 200 | Local defense mission | ✅ | ✅ | `MissionType` (Defend/MobileDefense/Counterattack/AntiAirUmbrella/NavalScreen/Retreat/DelayingAction/Evacuation/Escort), `TacticalEngagement.DefenseCommitment` | `MissionLifecycleTest`, `ExpandedCoverageTest`, `CounterattackAssessmentTest` | — |
| 201 | Mobile defense mission | ✅ | ✅ | `MissionType` (Defend/MobileDefense/Counterattack/AntiAirUmbrella/NavalScreen/Retreat/DelayingAction/Evacuation/Escort), `TacticalEngagement.DefenseCommitment` | `MissionLifecycleTest`, `ExpandedCoverageTest`, `CounterattackAssessmentTest` | — |
| 202 | Emergency reinforcement mission | 🟡 | ✅ | `MissionType` (Defend/MobileDefense/Counterattack/AntiAirUmbrella/NavalScreen/Retreat/DelayingAction/Evacuation/Escort), `TacticalEngagement.DefenseCommitment` | `MissionLifecycleTest`, `ExpandedCoverageTest`, `CounterattackAssessmentTest` | No dedicated `EmergencyReinforcement` type. Served by `ReserveManager` commits + `MissionType.Defend` at emergency `ArbiterPriority`. Functional, but not a first-class mission type. |
| 203 | Counterattack mission | ✅ | ✅ | `MissionType` (Defend/MobileDefense/Counterattack/AntiAirUmbrella/NavalScreen/Retreat/DelayingAction/Evacuation/Escort), `TacticalEngagement.DefenseCommitment` | `MissionLifecycleTest`, `ExpandedCoverageTest`, `CounterattackAssessmentTest` | — |
| 204 | Interception mission | 🟡 | ✅ | `MissionType` (Defend/MobileDefense/Counterattack/AntiAirUmbrella/NavalScreen/Retreat/DelayingAction/Evacuation/Escort), `TacticalEngagement.DefenseCommitment` | `MissionLifecycleTest`, `ExpandedCoverageTest`, `CounterattackAssessmentTest` | No dedicated `Interception` type. Approximated by `AntiAirUmbrella`, `NavalScreen` and reserve intercepts (`TacticalEngagement.DefenseCommitment`). |
| 205 | Anti-air defensive umbrella | ✅ | ✅ | `MissionType` (Defend/MobileDefense/Counterattack/AntiAirUmbrella/NavalScreen/Retreat/DelayingAction/Evacuation/Escort), `TacticalEngagement.DefenseCommitment` | `MissionLifecycleTest`, `ExpandedCoverageTest`, `CounterattackAssessmentTest` | — |
| 206 | Naval screening defense | ✅ | ✅ | `MissionType` (Defend/MobileDefense/Counterattack/AntiAirUmbrella/NavalScreen/Retreat/DelayingAction/Evacuation/Escort), `TacticalEngagement.DefenseCommitment` | `MissionLifecycleTest`, `ExpandedCoverageTest`, `CounterattackAssessmentTest` | — |
| 207 | Retreat mission | ✅ | ✅ | `MissionType` (Defend/MobileDefense/Counterattack/AntiAirUmbrella/NavalScreen/Retreat/DelayingAction/Evacuation/Escort), `TacticalEngagement.DefenseCommitment` | `MissionLifecycleTest`, `ExpandedCoverageTest`, `CounterattackAssessmentTest` | — |
| 208 | Delaying-action mission | ✅ | ✅ | `MissionType` (Defend/MobileDefense/Counterattack/AntiAirUmbrella/NavalScreen/Retreat/DelayingAction/Evacuation/Escort), `TacticalEngagement.DefenseCommitment` | `MissionLifecycleTest`, `ExpandedCoverageTest`, `CounterattackAssessmentTest` | — |
| 209 | Evacuation mission | ✅ | ✅ | `MissionType` (Defend/MobileDefense/Counterattack/AntiAirUmbrella/NavalScreen/Retreat/DelayingAction/Evacuation/Escort), `TacticalEngagement.DefenseCommitment` | `MissionLifecycleTest`, `ExpandedCoverageTest`, `CounterattackAssessmentTest` | — |
| 210 | Escort/protection mission | ✅ | ✅ | `MissionType` (Defend/MobileDefense/Counterattack/AntiAirUmbrella/NavalScreen/Retreat/DelayingAction/Evacuation/Escort), `TacticalEngagement.DefenseCommitment` | `MissionLifecycleTest`, `ExpandedCoverageTest`, `CounterattackAssessmentTest` | — |
| 211 | Defense proportional to threat | ✅ | ✅ | `MissionType` (Defend/MobileDefense/Counterattack/AntiAirUmbrella/NavalScreen/Retreat/DelayingAction/Evacuation/Escort), `TacticalEngagement.DefenseCommitment` | `MissionLifecycleTest`, `ExpandedCoverageTest`, `CounterattackAssessmentTest` | `TacticalEngagement.DefenseCommitment` sizes the response to observed attackers; covered by contract tests and `ProductionContractTest` fair-fog interception rules. |
| 212 | Minor raids don't redirect whole army | ✅ | ✅ | `MissionType` (Defend/MobileDefense/Counterattack/AntiAirUmbrella/NavalScreen/Retreat/DelayingAction/Evacuation/Escort), `TacticalEngagement.DefenseCommitment` | `MissionLifecycleTest`, `ExpandedCoverageTest`, `CounterattackAssessmentTest` | Same: `DefenseCommitment` caps commitment proportionally so a minor raid cannot pull the army. |
| 213 | Critical structures higher defensive priority | ✅ | ✅ | `MissionType` (Defend/MobileDefense/Counterattack/AntiAirUmbrella/NavalScreen/Retreat/DelayingAction/Evacuation/Escort), `TacticalEngagement.DefenseCommitment` | `MissionLifecycleTest`, `ExpandedCoverageTest`, `CounterattackAssessmentTest` | — |
| 214 | Defense triggers counterattack evaluation | ✅ | ✅ | `MissionType` (Defend/MobileDefense/Counterattack/AntiAirUmbrella/NavalScreen/Retreat/DelayingAction/Evacuation/Escort), `TacticalEngagement.DefenseCommitment` | `MissionLifecycleTest`, `ExpandedCoverageTest`, `CounterattackAssessmentTest` | — |

### §Reconnaissance & Intelligence Missions  <span>(215–226)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 215 | General reconnaissance mission | ✅ | ✅ | `MissionType.Recon/DeepRecon/AirRecon/NavalRecon/RouteRecon/ExpansionSearch/DefenseProbe`, `CommandToolApi.GetUncertainties` | `MissionLifecycleTest.ReconObjective`, `HeadlessSkirmishTest.IntelligenceScouting` | — |
| 216 | Deep reconnaissance | ✅ | ✅ | `MissionType.Recon/DeepRecon/AirRecon/NavalRecon/RouteRecon/ExpansionSearch/DefenseProbe`, `CommandToolApi.GetUncertainties` | `MissionLifecycleTest.ReconObjective`, `HeadlessSkirmishTest.IntelligenceScouting` | — |
| 217 | Air reconnaissance | ✅ | ✅ | `MissionType.Recon/DeepRecon/AirRecon/NavalRecon/RouteRecon/ExpansionSearch/DefenseProbe`, `CommandToolApi.GetUncertainties` | `MissionLifecycleTest.ReconObjective`, `HeadlessSkirmishTest.IntelligenceScouting` | — |
| 218 | Naval reconnaissance | ✅ | ✅ | `MissionType.Recon/DeepRecon/AirRecon/NavalRecon/RouteRecon/ExpansionSearch/DefenseProbe`, `CommandToolApi.GetUncertainties` | `MissionLifecycleTest.ReconObjective`, `HeadlessSkirmishTest.IntelligenceScouting` | — |
| 219 | Route reconnaissance | ✅ | ✅ | `MissionType.Recon/DeepRecon/AirRecon/NavalRecon/RouteRecon/ExpansionSearch/DefenseProbe`, `CommandToolApi.GetUncertainties` | `MissionLifecycleTest.ReconObjective`, `HeadlessSkirmishTest.IntelligenceScouting` | — |
| 220 | Expansion-search mission | ✅ | ✅ | `MissionType.Recon/DeepRecon/AirRecon/NavalRecon/RouteRecon/ExpansionSearch/DefenseProbe`, `CommandToolApi.GetUncertainties` | `MissionLifecycleTest.ReconObjective`, `HeadlessSkirmishTest.IntelligenceScouting` | — |
| 221 | Defense-probing mission | ✅ | ✅ | `MissionType.Recon/DeepRecon/AirRecon/NavalRecon/RouteRecon/ExpansionSearch/DefenseProbe`, `CommandToolApi.GetUncertainties` | `MissionLifecycleTest.ReconObjective`, `HeadlessSkirmishTest.IntelligenceScouting` | — |
| 222 | Recon answers specific intel questions | ✅ | ✅ | `MissionType.Recon/DeepRecon/AirRecon/NavalRecon/RouteRecon/ExpansionSearch/DefenseProbe`, `CommandToolApi.GetUncertainties` | `MissionLifecycleTest.ReconObjective`, `HeadlessSkirmishTest.IntelligenceScouting` | `CommandToolApi.GetUncertainties` turns low-confidence intel into explicit scouting questions; covered by `CommandToolApiTest`. |
| 223 | Information requirements have priorities | ✅ | ✅ | `MissionType.Recon/DeepRecon/AirRecon/NavalRecon/RouteRecon/ExpansionSearch/DefenseProbe`, `CommandToolApi.GetUncertainties` | `MissionLifecycleTest.ReconObjective`, `HeadlessSkirmishTest.IntelligenceScouting` | — |
| 224 | Value-of-information approximated | ✅ | ✅ | `MissionType.Recon/DeepRecon/AirRecon/NavalRecon/RouteRecon/ExpansionSearch/DefenseProbe`, `CommandToolApi.GetUncertainties` | `MissionLifecycleTest.ReconObjective`, `HeadlessSkirmishTest.IntelligenceScouting` | — |
| 225 | Recon assets not risked for irrelevant info | ✅ | ✅ | `MissionType.Recon/DeepRecon/AirRecon/NavalRecon/RouteRecon/ExpansionSearch/DefenseProbe`, `CommandToolApi.GetUncertainties` | `MissionLifecycleTest.ReconObjective`, `HeadlessSkirmishTest.IntelligenceScouting` | — |
| 226 | New intel updates planning immediately | ✅ | ✅ | `MissionType.Recon/DeepRecon/AirRecon/NavalRecon/RouteRecon/ExpansionSearch/DefenseProbe`, `CommandToolApi.GetUncertainties` | `MissionLifecycleTest.ReconObjective`, `HeadlessSkirmishTest.IntelligenceScouting` | — |

### §Combined-Arms Operations  <span>(227–240)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 227 | Major attacks contain multiple components | ✅ | ✅ | `CoalitionCommandCenterBotModule` coordinated-force gate, `MissionManager`, per-domain `TacticalControllers` | `HeadlessSkirmishTest.CampaignLifecycle` (air+naval+land gate), `AcceptanceSuite` | `CampaignLifecycle` asserts the `Coordinated force:` gate evaluating air, naval and land together. |
| 228 | Ground armor coordinated with infantry | ✅ | 🟡 | `CoalitionCommandCenterBotModule` coordinated-force gate, `MissionManager`, per-domain `TacticalControllers` | `HeadlessSkirmishTest.CampaignLifecycle` (air+naval+land gate), `AcceptanceSuite` | — |
| 229 | Ground armor coordinated with artillery | ✅ | 🟡 | `CoalitionCommandCenterBotModule` coordinated-force gate, `MissionManager`, per-domain `TacticalControllers` | `HeadlessSkirmishTest.CampaignLifecycle` (air+naval+land gate), `AcceptanceSuite` | — |
| 230 | Ground forces coordinated with AA escorts | ✅ | 🟡 | `CoalitionCommandCenterBotModule` coordinated-force gate, `MissionManager`, per-domain `TacticalControllers` | `HeadlessSkirmishTest.CampaignLifecycle` (air+naval+land gate), `AcceptanceSuite` | — |
| 231 | Ground attacks coordinated with air strikes | ✅ | 🟡 | `CoalitionCommandCenterBotModule` coordinated-force gate, `MissionManager`, per-domain `TacticalControllers` | `HeadlessSkirmishTest.CampaignLifecycle` (air+naval+land gate), `AcceptanceSuite` | — |
| 232 | Ground attacks coordinated with naval support | ✅ | 🟡 | `CoalitionCommandCenterBotModule` coordinated-force gate, `MissionManager`, per-domain `TacticalControllers` | `HeadlessSkirmishTest.CampaignLifecycle` (air+naval+land gate), `AcceptanceSuite` | — |
| 233 | Ground attacks coordinated with special ops | ✅ | 🟡 | `CoalitionCommandCenterBotModule` coordinated-force gate, `MissionManager`, per-domain `TacticalControllers` | `HeadlessSkirmishTest.CampaignLifecycle` (air+naval+land gate), `AcceptanceSuite` | — |
| 234 | Reconnaissance precedes main operation | ✅ | 🟡 | `CoalitionCommandCenterBotModule` coordinated-force gate, `MissionManager`, per-domain `TacticalControllers` | `HeadlessSkirmishTest.CampaignLifecycle` (air+naval+land gate), `AcceptanceSuite` | — |
| 235 | Shaping attacks precede main breach | ✅ | 🟡 | `CoalitionCommandCenterBotModule` coordinated-force gate, `MissionManager`, per-domain `TacticalControllers` | `HeadlessSkirmishTest.CampaignLifecycle` (air+naval+land gate), `AcceptanceSuite` | — |
| 236 | Deception precedes/accompanies main op | ✅ | 🟡 | `CoalitionCommandCenterBotModule` coordinated-force gate, `MissionManager`, per-domain `TacticalControllers` | `HeadlessSkirmishTest.CampaignLifecycle` (air+naval+land gate), `AcceptanceSuite` | — |
| 237 | Breach forces separated from exploitation | ✅ | 🟡 | `CoalitionCommandCenterBotModule` coordinated-force gate, `MissionManager`, per-domain `TacticalControllers` | `HeadlessSkirmishTest.CampaignLifecycle` (air+naval+land gate), `AcceptanceSuite` | — |
| 238 | Reserve remains uncommitted during main attack | ✅ | 🟡 | `CoalitionCommandCenterBotModule` coordinated-force gate, `MissionManager`, per-domain `TacticalControllers` | `HeadlessSkirmishTest.CampaignLifecycle` (air+naval+land gate), `AcceptanceSuite` | — |
| 239 | Multiple allied players contribute to one op | ✅ | 🟡 | `CoalitionCommandCenterBotModule` coordinated-force gate, `MissionManager`, per-domain `TacticalControllers` | `HeadlessSkirmishTest.CampaignLifecycle` (air+naval+land gate), `AcceptanceSuite` | — |
| 240 | Multi-domain coordination without independence | ✅ | ✅ | `CoalitionCommandCenterBotModule` coordinated-force gate, `MissionManager`, per-domain `TacticalControllers` | `HeadlessSkirmishTest.CampaignLifecycle` (air+naval+land gate), `AcceptanceSuite` | Same coordinated-force gate; the arbiter keeps per-domain components under one mission id. |

### §Operational Phasing  <span>(241–250)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 241 | RECON phase | ✅ | ✅ | `MissionPhase` enum (Recon→Staging→Shaping→Deception→Breach→Exploitation→Consolidation→Withdrawal) | `MissionLifecycleTest`, `AcceptanceSuite.MissionPhaseForwardOnly` | — |
| 242 | STAGING phase | ✅ | ✅ | `MissionPhase` enum (Recon→Staging→Shaping→Deception→Breach→Exploitation→Consolidation→Withdrawal) | `MissionLifecycleTest`, `AcceptanceSuite.MissionPhaseForwardOnly` | — |
| 243 | SHAPING phase | ✅ | ✅ | `MissionPhase` enum (Recon→Staging→Shaping→Deception→Breach→Exploitation→Consolidation→Withdrawal) | `MissionLifecycleTest`, `AcceptanceSuite.MissionPhaseForwardOnly` | — |
| 244 | DECEPTION phase | ✅ | ✅ | `MissionPhase` enum (Recon→Staging→Shaping→Deception→Breach→Exploitation→Consolidation→Withdrawal) | `MissionLifecycleTest`, `AcceptanceSuite.MissionPhaseForwardOnly` | — |
| 245 | BREACH phase | ✅ | ✅ | `MissionPhase` enum (Recon→Staging→Shaping→Deception→Breach→Exploitation→Consolidation→Withdrawal) | `MissionLifecycleTest`, `AcceptanceSuite.MissionPhaseForwardOnly` | — |
| 246 | EXPLOITATION phase | ✅ | ✅ | `MissionPhase` enum (Recon→Staging→Shaping→Deception→Breach→Exploitation→Consolidation→Withdrawal) | `MissionLifecycleTest`, `AcceptanceSuite.MissionPhaseForwardOnly` | — |
| 247 | CONSOLIDATION/HOLD phase | ✅ | ✅ | `MissionPhase` enum (Recon→Staging→Shaping→Deception→Breach→Exploitation→Consolidation→Withdrawal) | `MissionLifecycleTest`, `AcceptanceSuite.MissionPhaseForwardOnly` | — |
| 248 | WITHDRAWAL phase | ✅ | ✅ | `MissionPhase` enum (Recon→Staging→Shaping→Deception→Breach→Exploitation→Consolidation→Withdrawal) | `MissionLifecycleTest`, `AcceptanceSuite.MissionPhaseForwardOnly` | — |
| 249 | Phase transitions have explicit conditions | ✅ | ✅ | `MissionPhase` enum (Recon→Staging→Shaping→Deception→Breach→Exploitation→Consolidation→Withdrawal) | `MissionLifecycleTest`, `AcceptanceSuite.MissionPhaseForwardOnly` | — |
| 250 | Phase transitions tested under disruptions | ✅ | ✅ | `MissionPhase` enum (Recon→Staging→Shaping→Deception→Breach→Exploitation→Consolidation→Withdrawal) | `MissionLifecycleTest`, `AcceptanceSuite.MissionPhaseForwardOnly` | — |

### §Synchronization & Time-on-Target  <span>(251–261)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 251 | Separate forces can stage before launch | ✅ | ✅ | `CoalitionMission.StagingRegion/LaunchConditions`, `CoalitionMatchMetrics.Synchronization` | `ExpandedCoverageTest.MatchMetricsSyncError`, `MatchMetricsTest` | `CoalitionMission.StagingRegion` + `MissionPhase.Staging`; covered by `MissionLifecycleTest`. |
| 252 | Forces can wait for launch conditions | ✅ | ✅ | `CoalitionMission.StagingRegion/LaunchConditions`, `CoalitionMatchMetrics.Synchronization` | `ExpandedCoverageTest.MatchMetricsSyncError`, `MatchMetricsTest` | `CoalitionMission.LaunchConditions` populated per type by `LaunchConditionsFor`; covered by `MissionLifecycleTest`. |
| 253 | Mission components have synchronized timing | ✅ | 🟡 | `CoalitionMission.StagingRegion/LaunchConditions`, `CoalitionMatchMetrics.Synchronization` | `ExpandedCoverageTest.MatchMetricsSyncError`, `MatchMetricsTest` | — |
| 254 | Air strikes precede ground by configured interval | ✅ | 🟡 | `CoalitionMission.StagingRegion/LaunchConditions`, `CoalitionMatchMetrics.Synchronization` | `ExpandedCoverageTest.MatchMetricsSyncError`, `MatchMetricsTest` | — |
| 255 | Naval bombardment synchronizes with ground | ✅ | 🟡 | `CoalitionMission.StagingRegion/LaunchConditions`, `CoalitionMatchMetrics.Synchronization` | `ExpandedCoverageTest.MatchMetricsSyncError`, `MatchMetricsTest` | — |
| 256 | Special ops launch during distraction windows | ✅ | 🟡 | `CoalitionMission.StagingRegion/LaunchConditions`, `CoalitionMatchMetrics.Synchronization` | `ExpandedCoverageTest.MatchMetricsSyncError`, `MatchMetricsTest` | — |
| 257 | Reserve movement synchronizes with breakthrough | ✅ | 🟡 | `CoalitionMission.StagingRegion/LaunchConditions`, `CoalitionMatchMetrics.Synchronization` | `ExpandedCoverageTest.MatchMetricsSyncError`, `MatchMetricsTest` | — |
| 258 | System accounts for different travel times | ✅ | 🟡 | `CoalitionMission.StagingRegion/LaunchConditions`, `CoalitionMatchMetrics.Synchronization` | `ExpandedCoverageTest.MatchMetricsSyncError`, `MatchMetricsTest` | — |
| 259 | Avoids one force arriving long before support | ✅ | 🟡 | `CoalitionMission.StagingRegion/LaunchConditions`, `CoalitionMatchMetrics.Synchronization` | `ExpandedCoverageTest.MatchMetricsSyncError`, `MatchMetricsTest` | — |
| 260 | Synchronization error measured in telemetry | ✅ | ✅ | `CoalitionMission.StagingRegion/LaunchConditions`, `CoalitionMatchMetrics.Synchronization` | `ExpandedCoverageTest.MatchMetricsSyncError`, `MatchMetricsTest` | `CoalitionMatchMetrics.Synchronization` (avg/max error ticks); covered by `ExpandedCoverageTest.MatchMetricsSyncError`. |
| 261 | Time-on-target has automated scenario tests | ✅ | 🟡 | `CoalitionMission.StagingRegion/LaunchConditions`, `CoalitionMatchMetrics.Synchronization` | `ExpandedCoverageTest.MatchMetricsSyncError`, `MatchMetricsTest` | — |

### §Deception Framework  <span>(262–276)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 262 | Feint mission | ✅ | ✅ | `MissionType.Feint/Bait/Demonstration/DecoyTransport/FakeBuildup`, `IntendedReaction`, `MissionManager` deception counters | `DeceptionTest`, `ExpandedCoverageTest.FakeBuildup*` | — |
| 263 | Demonstration mission | ✅ | ✅ | `MissionType.Feint/Bait/Demonstration/DecoyTransport/FakeBuildup`, `IntendedReaction`, `MissionManager` deception counters | `DeceptionTest`, `ExpandedCoverageTest.FakeBuildup*` | — |
| 264 | Probe attack | ✅ | ✅ | `MissionType.Feint/Bait/Demonstration/DecoyTransport/FakeBuildup`, `IntendedReaction`, `MissionManager` deception counters | `DeceptionTest`, `ExpandedCoverageTest.FakeBuildup*` | — |
| 265 | Fake buildup | ✅ | ✅ | `MissionType.Feint/Bait/Demonstration/DecoyTransport/FakeBuildup`, `IntendedReaction`, `MissionManager` deception counters | `DeceptionTest`, `ExpandedCoverageTest.FakeBuildup*` | — |
| 266 | Diversionary raid | ✅ | ✅ | `MissionType.Feint/Bait/Demonstration/DecoyTransport/FakeBuildup`, `IntendedReaction`, `MissionManager` deception counters | `DeceptionTest`, `ExpandedCoverageTest.FakeBuildup*` | — |
| 267 | Fake retreat/bait mission | ✅ | ✅ | `MissionType.Feint/Bait/Demonstration/DecoyTransport/FakeBuildup`, `IntendedReaction`, `MissionManager` deception counters | `DeceptionTest`, `ExpandedCoverageTest.FakeBuildup*` | — |
| 268 | Decoy transport mission | ✅ | ✅ | `MissionType.Feint/Bait/Demonstration/DecoyTransport/FakeBuildup`, `IntendedReaction`, `MissionManager` deception counters | `DeceptionTest`, `ExpandedCoverageTest.FakeBuildup*` | — |
| 269 | False multi-axis pressure | ✅ | ✅ | `MissionType.Feint/Bait/Demonstration/DecoyTransport/FakeBuildup`, `IntendedReaction`, `MissionManager` deception counters | `DeceptionTest`, `ExpandedCoverageTest.FakeBuildup*` | — |
| 270 | Deception defines intended enemy reaction | ✅ | ✅ | `MissionType.Feint/Bait/Demonstration/DecoyTransport/FakeBuildup`, `IntendedReaction`, `MissionManager` deception counters | `DeceptionTest`, `ExpandedCoverageTest.FakeBuildup*` | `CoalitionMission.IntendedReaction`; covered by `MissionLifecycleTest` and `ExpandedCoverageTest.FakeBuildupIntendedReaction`. |
| 271 | Feint forces have stricter loss limits | ✅ | ✅ | `MissionType.Feint/Bait/Demonstration/DecoyTransport/FakeBuildup`, `IntendedReaction`, `MissionManager` deception counters | `DeceptionTest`, `ExpandedCoverageTest.FakeBuildup*` | — |
| 272 | Feints withdraw early once purpose achieved | ✅ | ✅ | `MissionType.Feint/Bait/Demonstration/DecoyTransport/FakeBuildup`, `IntendedReaction`, `MissionManager` deception counters | `DeceptionTest`, `ExpandedCoverageTest.FakeBuildup*` | — |
| 273 | Deception success measured by enemy behavior | ✅ | ✅ | `MissionType.Feint/Bait/Demonstration/DecoyTransport/FakeBuildup`, `IntendedReaction`, `MissionManager` deception counters | `DeceptionTest`, `ExpandedCoverageTest.FakeBuildup*` | `DeceptionTest` measures enemy presence surge against a baseline and rejects a lone unit as noise. |
| 274 | Main op launches conditionally after feint | ✅ | ✅ | `MissionType.Feint/Bait/Demonstration/DecoyTransport/FakeBuildup`, `IntendedReaction`, `MissionManager` deception counters | `DeceptionTest`, `ExpandedCoverageTest.FakeBuildup*` | — |
| 275 | Fake retreat pulls enemies into kill zones | ✅ | ✅ | `MissionType.Feint/Bait/Demonstration/DecoyTransport/FakeBuildup`, `IntendedReaction`, `MissionManager` deception counters | `DeceptionTest`, `ExpandedCoverageTest.FakeBuildup*` | — |
| 276 | Bait force understands retreat = success | ✅ | ✅ | `MissionType.Feint/Bait/Demonstration/DecoyTransport/FakeBuildup`, `IntendedReaction`, `MissionManager` deception counters | `DeceptionTest`, `ExpandedCoverageTest.FakeBuildup*` | — |

### §Human Attention Exploitation  <span>(277–284)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 277 | Deliberately generate simultaneous threats | ✅ | 🟡 | `CoalitionCommandCenterBotModule` multi-mission planning, `OpponentModel.MovesWholeArmyToDefend` | `DeceptionTest`, `HeadlessSkirmishTest.CoalitionCoordinatedScenarios` | — |
| 278 | Simultaneous threats on different map parts | ✅ | 🟡 | `CoalitionCommandCenterBotModule` multi-mission planning, `OpponentModel.MovesWholeArmyToDefend` | `DeceptionTest`, `HeadlessSkirmishTest.CoalitionCoordinatedScenarios` | — |
| 279 | Simultaneous threats in different domains | ✅ | 🟡 | `CoalitionCommandCenterBotModule` multi-mission planning, `OpponentModel.MovesWholeArmyToDefend` | `DeceptionTest`, `HeadlessSkirmishTest.CoalitionCoordinatedScenarios` | — |
| 280 | Main assault + raid + air + naval + special | ✅ | 🟡 | `CoalitionCommandCenterBotModule` multi-mission planning, `OpponentModel.MovesWholeArmyToDefend` | `DeceptionTest`, `HeadlessSkirmishTest.CoalitionCoordinatedScenarios` | — |
| 281 | Simultaneous actions serve common purpose | ✅ | 🟡 | `CoalitionCommandCenterBotModule` multi-mission planning, `OpponentModel.MovesWholeArmyToDefend` | `DeceptionTest`, `HeadlessSkirmishTest.CoalitionCoordinatedScenarios` | — |
| 282 | Exploit observed human overreaction | ✅ | 🟡 | `CoalitionCommandCenterBotModule` multi-mission planning, `OpponentModel.MovesWholeArmyToDefend` | `DeceptionTest`, `HeadlessSkirmishTest.CoalitionCoordinatedScenarios` | — |
| 283 | Create distraction before high-value op | ✅ | 🟡 | `CoalitionCommandCenterBotModule` multi-mission planning, `OpponentModel.MovesWholeArmyToDefend` | `DeceptionTest`, `HeadlessSkirmishTest.CoalitionCoordinatedScenarios` | — |
| 284 | Force human to choose between targets | ✅ | 🟡 | `CoalitionCommandCenterBotModule` multi-mission planning, `OpponentModel.MovesWholeArmyToDefend` | `DeceptionTest`, `HeadlessSkirmishTest.CoalitionCoordinatedScenarios` | — |

### §Special Operations  <span>(285–301)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 285 | Special units excluded from generic squads | ✅ | ✅ | `SpecialOpsController`, `SpecialAsset`, `TargetEvaluator.TechnologyValue/EconomicValue`, `CommandToolApi.FindSpecialOpsRoutes` | `MissionLifecycleTest`, `TargetEvaluatorTest`, `CommandToolApiTest` | `ExcludeFromArmyTypes` in `ai.yaml` plus `SpecialTypes` routing to `SpecialOpsController`; covered by `ForceRegistryTest`. |
| 286 | Tanya as scarce strategic asset | ✅ | 🟡 | `SpecialOpsController`, `SpecialAsset`, `TargetEvaluator.TechnologyValue/EconomicValue`, `CommandToolApi.FindSpecialOpsRoutes` | `MissionLifecycleTest`, `TargetEvaluatorTest`, `CommandToolApiTest` | — |
| 287 | Spies as special-operation assets | ✅ | 🟡 | `SpecialOpsController`, `SpecialAsset`, `TargetEvaluator.TechnologyValue/EconomicValue`, `CommandToolApi.FindSpecialOpsRoutes` | `MissionLifecycleTest`, `TargetEvaluatorTest`, `CommandToolApiTest` | — |
| 288 | Engineers in deliberate capture operations | ✅ | 🟡 | `SpecialOpsController`, `SpecialAsset`, `TargetEvaluator.TechnologyValue/EconomicValue`, `CommandToolApi.FindSpecialOpsRoutes` | `MissionLifecycleTest`, `TargetEvaluatorTest`, `CommandToolApiTest` | — |
| 289 | Special-ops targets scored by consequence | ✅ | ✅ | `SpecialOpsController`, `SpecialAsset`, `TargetEvaluator.TechnologyValue/EconomicValue`, `CommandToolApi.FindSpecialOpsRoutes` | `MissionLifecycleTest`, `TargetEvaluatorTest`, `CommandToolApiTest` | `TargetEvaluator.TechnologyValue/EconomicValue` score strategic consequence; covered by `TargetEvaluatorTest`. |
| 290 | Production infrastructure targeted | ✅ | ✅ | `SpecialOpsController`, `SpecialAsset`, `TargetEvaluator.TechnologyValue/EconomicValue`, `CommandToolApi.FindSpecialOpsRoutes` | `MissionLifecycleTest`, `TargetEvaluatorTest`, `CommandToolApiTest` | Production structures carry explicit target value; covered by `TargetEvaluatorTest`. |
| 291 | Technology infrastructure targeted | ✅ | ✅ | `SpecialOpsController`, `SpecialAsset`, `TargetEvaluator.TechnologyValue/EconomicValue`, `CommandToolApi.FindSpecialOpsRoutes` | `MissionLifecycleTest`, `TargetEvaluatorTest`, `CommandToolApiTest` | `TargetEvaluator.TechnologyValue`; covered by `TargetEvaluatorTest`. |
| 292 | Economy infrastructure targeted | ✅ | ✅ | `SpecialOpsController`, `SpecialAsset`, `TargetEvaluator.TechnologyValue/EconomicValue`, `CommandToolApi.FindSpecialOpsRoutes` | `MissionLifecycleTest`, `TargetEvaluatorTest`, `CommandToolApiTest` | `TargetEvaluator.EconomicValue`; covered by `TargetEvaluatorTest`. |
| 293 | Support-power infrastructure targeted | ✅ | ✅ | `SpecialOpsController`, `SpecialAsset`, `TargetEvaluator.TechnologyValue/EconomicValue`, `CommandToolApi.FindSpecialOpsRoutes` | `MissionLifecycleTest`, `TargetEvaluatorTest`, `CommandToolApiTest` | `SupportPowerStructures` seeded into the blackboard and scored; covered by `TargetEvaluatorTest`. |
| 294 | Isolated/high-value rear targets | ✅ | ✅ | `SpecialOpsController`, `SpecialAsset`, `TargetEvaluator.TechnologyValue/EconomicValue`, `CommandToolApi.FindSpecialOpsRoutes` | `MissionLifecycleTest`, `TargetEvaluatorTest`, `CommandToolApiTest` | `CommandToolApi.FindSpecialOpsRoutes` ranks isolated rear targets; covered by `CommandToolApiTest`. |
| 295 | Special-ops evaluates probability of success | ✅ | 🟡 | `SpecialOpsController`, `SpecialAsset`, `TargetEvaluator.TechnologyValue/EconomicValue`, `CommandToolApi.FindSpecialOpsRoutes` | `MissionLifecycleTest`, `TargetEvaluatorTest`, `CommandToolApiTest` | — |
| 296 | Special-ops evaluates strategic value | ✅ | 🟡 | `SpecialOpsController`, `SpecialAsset`, `TargetEvaluator.TechnologyValue/EconomicValue`, `CommandToolApi.FindSpecialOpsRoutes` | `MissionLifecycleTest`, `TargetEvaluatorTest`, `CommandToolApiTest` | — |
| 297 | Special-ops evaluates asset-loss risk | ✅ | 🟡 | `SpecialOpsController`, `SpecialAsset`, `TargetEvaluator.TechnologyValue/EconomicValue`, `CommandToolApi.FindSpecialOpsRoutes` | `MissionLifecycleTest`, `TargetEvaluatorTest`, `CommandToolApiTest` | — |
| 298 | Special units wait for timing window | ✅ | 🟡 | `SpecialOpsController`, `SpecialAsset`, `TargetEvaluator.TechnologyValue/EconomicValue`, `CommandToolApi.FindSpecialOpsRoutes` | `MissionLifecycleTest`, `TargetEvaluatorTest`, `CommandToolApiTest` | — |
| 299 | Special ops synchronized with distractions | ✅ | 🟡 | `SpecialOpsController`, `SpecialAsset`, `TargetEvaluator.TechnologyValue/EconomicValue`, `CommandToolApi.FindSpecialOpsRoutes` | `MissionLifecycleTest`, `TargetEvaluatorTest`, `CommandToolApiTest` | — |
| 300 | Abort conditions for compromised operations | ✅ | 🟡 | `SpecialOpsController`, `SpecialAsset`, `TargetEvaluator.TechnologyValue/EconomicValue`, `CommandToolApi.FindSpecialOpsRoutes` | `MissionLifecycleTest`, `TargetEvaluatorTest`, `CommandToolApiTest` | — |
| 301 | Surviving special assets extracted and reused | ✅ | 🟡 | `SpecialOpsController`, `SpecialAsset`, `TargetEvaluator.TechnologyValue/EconomicValue`, `CommandToolApi.FindSpecialOpsRoutes` | `MissionLifecycleTest`, `TargetEvaluatorTest`, `CommandToolApiTest` | — |

### §Transport Operations  <span>(302–322)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 302 | Transport missions have explicit state machines | ✅ | ✅ | `TransportStateMachine` (11 states), `TransportController`, `RouteWeights.Stealth()` | `TransportStateMachineTest`, `MissionLifecycleTest` | — |
| 303 | ASSEMBLE state | ✅ | ✅ | `TransportStateMachine` (11 states), `TransportController`, `RouteWeights.Stealth()` | `TransportStateMachineTest`, `MissionLifecycleTest` | — |
| 304 | LOAD state | ✅ | ✅ | `TransportStateMachine` (11 states), `TransportController`, `RouteWeights.Stealth()` | `TransportStateMachineTest`, `MissionLifecycleTest` | — |
| 305 | WAIT_FOR_WINDOW state | ✅ | ✅ | `TransportStateMachine` (11 states), `TransportController`, `RouteWeights.Stealth()` | `TransportStateMachineTest`, `MissionLifecycleTest` | — |
| 306 | TRANSIT state | ✅ | ✅ | `TransportStateMachine` (11 states), `TransportController`, `RouteWeights.Stealth()` | `TransportStateMachineTest`, `MissionLifecycleTest` | — |
| 307 | APPROACH state | ✅ | ✅ | `TransportStateMachine` (11 states), `TransportController`, `RouteWeights.Stealth()` | `TransportStateMachineTest`, `MissionLifecycleTest` | — |
| 308 | UNLOAD state | ✅ | ✅ | `TransportStateMachine` (11 states), `TransportController`, `RouteWeights.Stealth()` | `TransportStateMachineTest`, `MissionLifecycleTest` | — |
| 309 | RETREAT/HOLD state | ✅ | ✅ | `TransportStateMachine` (11 states), `TransportController`, `RouteWeights.Stealth()` | `TransportStateMachineTest`, `MissionLifecycleTest` | — |
| 310 | EXTRACTION_REQUEST state | ✅ | ✅ | `TransportStateMachine` (11 states), `TransportController`, `RouteWeights.Stealth()` | `TransportStateMachineTest`, `MissionLifecycleTest` | — |
| 311 | RETURN_FOR_EXTRACTION | ✅ | ✅ | `TransportStateMachine` (11 states), `TransportController`, `RouteWeights.Stealth()` | `TransportStateMachineTest`, `MissionLifecycleTest` | — |
| 312 | RELOAD | ✅ | ✅ | `TransportStateMachine` (11 states), `TransportController`, `RouteWeights.Stealth()` | `TransportStateMachineTest`, `MissionLifecycleTest` | — |
| 313 | EXTRACT | ✅ | ✅ | `TransportStateMachine` (11 states), `TransportController`, `RouteWeights.Stealth()` | `TransportStateMachineTest`, `MissionLifecycleTest` | — |
| 314 | Transport routes prioritize safety | ✅ | ✅ | `TransportStateMachine` (11 states), `TransportController`, `RouteWeights.Stealth()` | `TransportStateMachineTest`, `MissionLifecycleTest` | — |
| 315 | Aircraft transports avoid AA | ✅ | ✅ | `TransportStateMachine` (11 states), `TransportController`, `RouteWeights.Stealth()` | `TransportStateMachineTest`, `MissionLifecycleTest` | — |
| 316 | Naval transports avoid naval threats | ✅ | ✅ | `TransportStateMachine` (11 states), `TransportController`, `RouteWeights.Stealth()` | `TransportStateMachineTest`, `MissionLifecycleTest` | — |
| 317 | Transport avoids combat zones for stealth | ✅ | ✅ | `TransportStateMachine` (11 states), `TransportController`, `RouteWeights.Stealth()` | `TransportStateMachineTest`, `MissionLifecycleTest` | — |
| 318 | Transport route replanned during transit | ✅ | ✅ | `TransportStateMachine` (11 states), `TransportController`, `RouteWeights.Stealth()` | `TransportStateMachineTest`, `MissionLifecycleTest` | — |
| 319 | Mission aborts when safe transit impossible | ✅ | ✅ | `TransportStateMachine` (11 states), `TransportController`, `RouteWeights.Stealth()` | `TransportStateMachineTest`, `MissionLifecycleTest` | — |
| 320 | Insertion route planned before launch | ✅ | ✅ | `TransportStateMachine` (11 states), `TransportController`, `RouteWeights.Stealth()` | `TransportStateMachineTest`, `MissionLifecycleTest` | — |
| 321 | Extraction route planned before launch | ✅ | ✅ | `TransportStateMachine` (11 states), `TransportController`, `RouteWeights.Stealth()` | `TransportStateMachineTest`, `MissionLifecycleTest` | — |
| 322 | Transport survival measured | ✅ | ✅ | `TransportStateMachine` (11 states), `TransportController`, `RouteWeights.Stealth()` | `TransportStateMachineTest`, `MissionLifecycleTest` | — |

### §Strategic Posture System  <span>(323–342)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 323 | Global strategic posture exists | ✅ | ✅ | `StrategicPosture` enum (13 postures + None), `StrategicPosture` policy, per-region posture on `CoalitionRegion` | `PostureSelectionTest`, `ExpandedCoverageTest.StrategicPosturesComplete` | — |
| 324 | OPENING posture | ✅ | ✅ | `StrategicPosture` enum (13 postures + None), `StrategicPosture` policy, per-region posture on `CoalitionRegion` | `PostureSelectionTest`, `ExpandedCoverageTest.StrategicPosturesComplete` | — |
| 325 | EXPANSION posture | ✅ | ✅ | `StrategicPosture` enum (13 postures + None), `StrategicPosture` policy, per-region posture on `CoalitionRegion` | `PostureSelectionTest`, `ExpandedCoverageTest.StrategicPosturesComplete` | — |
| 326 | PRESSURE posture | ✅ | ✅ | `StrategicPosture` enum (13 postures + None), `StrategicPosture` policy, per-region posture on `CoalitionRegion` | `PostureSelectionTest`, `ExpandedCoverageTest.StrategicPosturesComplete` | — |
| 327 | CONTAINMENT posture | ✅ | ✅ | `StrategicPosture` enum (13 postures + None), `StrategicPosture` policy, per-region posture on `CoalitionRegion` | `PostureSelectionTest`, `ExpandedCoverageTest.StrategicPosturesComplete` | — |
| 328 | ATTRITION posture | ✅ | ✅ | `StrategicPosture` enum (13 postures + None), `StrategicPosture` policy, per-region posture on `CoalitionRegion` | `PostureSelectionTest`, `ExpandedCoverageTest.StrategicPosturesComplete` | — |
| 329 | BREAKTHROUGH posture | ✅ | ✅ | `StrategicPosture` enum (13 postures + None), `StrategicPosture` policy, per-region posture on `CoalitionRegion` | `PostureSelectionTest`, `ExpandedCoverageTest.StrategicPosturesComplete` | — |
| 330 | SIEGE posture | ✅ | ✅ | `StrategicPosture` enum (13 postures + None), `StrategicPosture` policy, per-region posture on `CoalitionRegion` | `PostureSelectionTest`, `ExpandedCoverageTest.StrategicPosturesComplete` | — |
| 331 | RAIDING posture | ✅ | ✅ | `StrategicPosture` enum (13 postures + None), `StrategicPosture` policy, per-region posture on `CoalitionRegion` | `PostureSelectionTest`, `ExpandedCoverageTest.StrategicPosturesComplete` | — |
| 332 | DEFENSIVE posture | ✅ | ✅ | `StrategicPosture` enum (13 postures + None), `StrategicPosture` policy, per-region posture on `CoalitionRegion` | `PostureSelectionTest`, `ExpandedCoverageTest.StrategicPosturesComplete` | — |
| 333 | COUNTERATTACK posture | ✅ | ✅ | `StrategicPosture` enum (13 postures + None), `StrategicPosture` policy, per-region posture on `CoalitionRegion` | `PostureSelectionTest`, `ExpandedCoverageTest.StrategicPosturesComplete` | — |
| 334 | RECOVERY posture | ✅ | ✅ | `StrategicPosture` enum (13 postures + None), `StrategicPosture` policy, per-region posture on `CoalitionRegion` | `PostureSelectionTest`, `ExpandedCoverageTest.StrategicPosturesComplete` | — |
| 335 | DESPERATION posture | ✅ | ✅ | `StrategicPosture` enum (13 postures + None), `StrategicPosture` policy, per-region posture on `CoalitionRegion` | `PostureSelectionTest`, `ExpandedCoverageTest.StrategicPosturesComplete` | — |
| 336 | ALL_IN posture | ✅ | ✅ | `StrategicPosture` enum (13 postures + None), `StrategicPosture` policy, per-region posture on `CoalitionRegion` | `PostureSelectionTest`, `ExpandedCoverageTest.StrategicPosturesComplete` | — |
| 337 | Posture affects production priorities | ✅ | ✅ | `StrategicPosture` enum (13 postures + None), `StrategicPosture` policy, per-region posture on `CoalitionRegion` | `PostureSelectionTest`, `ExpandedCoverageTest.StrategicPosturesComplete` | — |
| 338 | Posture affects acceptable combat risk | ✅ | ✅ | `StrategicPosture` enum (13 postures + None), `StrategicPosture` policy, per-region posture on `CoalitionRegion` | `PostureSelectionTest`, `ExpandedCoverageTest.StrategicPosturesComplete` | — |
| 339 | Posture affects reserve requirements | ✅ | ✅ | `StrategicPosture` enum (13 postures + None), `StrategicPosture` policy, per-region posture on `CoalitionRegion` | `PostureSelectionTest`, `ExpandedCoverageTest.StrategicPosturesComplete` | — |
| 340 | Posture affects target-selection weights | ✅ | ✅ | `StrategicPosture` enum (13 postures + None), `StrategicPosture` policy, per-region posture on `CoalitionRegion` | `PostureSelectionTest`, `ExpandedCoverageTest.StrategicPosturesComplete` | — |
| 341 | Different theaters with different postures | ✅ | ✅ | `StrategicPosture` enum (13 postures + None), `StrategicPosture` policy, per-region posture on `CoalitionRegion` | `PostureSelectionTest`, `ExpandedCoverageTest.StrategicPosturesComplete` | — |
| 342 | Commander can change posture | ✅ | ✅ | `StrategicPosture` enum (13 postures + None), `StrategicPosture` policy, per-region posture on `CoalitionRegion` | `PostureSelectionTest`, `ExpandedCoverageTest.StrategicPosturesComplete` | — |

### §Main Effort & Force Concentration  <span>(343–350)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 343 | Commander identifies primary/main effort | ✅ | 🟡 | `CoalitionCommandCenterBotModule` main-effort selection, `CombatEstimator` local-superiority scoring | `PostureSelectionTest`, `CombatEstimatorTest`, `AcceptanceSuite` | — |
| 344 | Secondary operations support main effort | ✅ | 🟡 | `CoalitionCommandCenterBotModule` main-effort selection, `CombatEstimator` local-superiority scoring | `PostureSelectionTest`, `CombatEstimatorTest`, `AcceptanceSuite` | — |
| 345 | AI avoids attacking all fronts equally | ✅ | 🟡 | `CoalitionCommandCenterBotModule` main-effort selection, `CombatEstimator` local-superiority scoring | `PostureSelectionTest`, `CombatEstimatorTest`, `AcceptanceSuite` | — |
| 346 | Local superiority > total-map unit count | ✅ | ✅ | `CoalitionCommandCenterBotModule` main-effort selection, `CombatEstimator` local-superiority scoring | `PostureSelectionTest`, `CombatEstimatorTest`, `AcceptanceSuite` | `CombatEstimator` scores local matchups and `CoalitionMatchMetrics` records superior-engagement share; covered by `CombatEstimatorTest` + `MatchMetricsTest`. |
| 347 | AI can mass forces against vulnerable area | ✅ | 🟡 | `CoalitionCommandCenterBotModule` main-effort selection, `CombatEstimator` local-superiority scoring | `PostureSelectionTest`, `CombatEstimatorTest`, `AcceptanceSuite` | — |
| 348 | Other fronts retain defensive capability | ✅ | 🟡 | `CoalitionCommandCenterBotModule` main-effort selection, `CombatEstimator` local-superiority scoring | `PostureSelectionTest`, `CombatEstimatorTest`, `AcceptanceSuite` | — |
| 349 | Concentration creates counterattack exposure | ✅ | 🟡 | `CoalitionCommandCenterBotModule` main-effort selection, `CombatEstimator` local-superiority scoring | `PostureSelectionTest`, `CombatEstimatorTest`, `AcceptanceSuite` | — |
| 350 | Breakthrough followed by exploitation forces | ✅ | 🟡 | `CoalitionCommandCenterBotModule` main-effort selection, `CombatEstimator` local-superiority scoring | `PostureSelectionTest`, `CombatEstimatorTest`, `AcceptanceSuite` | — |

### §Strategic Reserve  <span>(351–362)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 351 | Coalition-level strategic reserve exists | ✅ | ✅ | `ReserveManager`, `CommandValidator.ValidateReserveFraction/ValidateReserveJustification` | `ReserveManagerTest`, `ExpandedCoverageTest.ValidateReserveFraction*` | — |
| 352 | Reserve size configurable/dynamic | ✅ | ✅ | `ReserveManager`, `CommandValidator.ValidateReserveFraction/ValidateReserveJustification` | `ReserveManagerTest`, `ExpandedCoverageTest.ValidateReserveFraction*` | — |
| 353 | ~10-25% reserve behavior | ✅ | ✅ | `ReserveManager`, `CommandValidator.ValidateReserveFraction/ValidateReserveJustification` | `ReserveManagerTest`, `ExpandedCoverageTest.ValidateReserveFraction*` | — |
| 354 | Reserve not casually consumed by routine missions | ✅ | ✅ | `ReserveManager`, `CommandValidator.ValidateReserveFraction/ValidateReserveJustification` | `ReserveManagerTest`, `ExpandedCoverageTest.ValidateReserveFraction*` | — |
| 355 | Reserve can stop counterattacks | ✅ | ✅ | `ReserveManager`, `CommandValidator.ValidateReserveFraction/ValidateReserveJustification` | `ReserveManagerTest`, `ExpandedCoverageTest.ValidateReserveFraction*` | — |
| 356 | Reserve can reinforce failing fronts | ✅ | ✅ | `ReserveManager`, `CommandValidator.ValidateReserveFraction/ValidateReserveJustification` | `ReserveManagerTest`, `ExpandedCoverageTest.ValidateReserveFraction*` | — |
| 357 | Reserve can exploit breakthroughs | ✅ | ✅ | `ReserveManager`, `CommandValidator.ValidateReserveFraction/ValidateReserveJustification` | `ReserveManagerTest`, `ExpandedCoverageTest.ValidateReserveFraction*` | — |
| 358 | Reserve can intercept transports/raids | ✅ | ✅ | `ReserveManager`, `CommandValidator.ValidateReserveFraction/ValidateReserveJustification` | `ReserveManagerTest`, `ExpandedCoverageTest.ValidateReserveFraction*` | — |
| 359 | Reserve can protect expansions | ✅ | ✅ | `ReserveManager`, `CommandValidator.ValidateReserveFraction/ValidateReserveJustification` | `ReserveManagerTest`, `ExpandedCoverageTest.ValidateReserveFraction*` | — |
| 360 | LLM must justify consuming last reserve | ✅ | ✅ | `ReserveManager`, `CommandValidator.ValidateReserveFraction/ValidateReserveJustification` | `ReserveManagerTest`, `ExpandedCoverageTest.ValidateReserveFraction*` | — |
| 361 | Reserve availability visible in context | ✅ | ✅ | `ReserveManager`, `CommandValidator.ValidateReserveFraction/ValidateReserveJustification` | `ReserveManagerTest`, `ExpandedCoverageTest.ValidateReserveFraction*` | — |
| 362 | Reserve usage tracked in telemetry | ✅ | ✅ | `ReserveManager`, `CommandValidator.ValidateReserveFraction/ValidateReserveJustification` | `ReserveManagerTest`, `ExpandedCoverageTest.ValidateReserveFraction*` | — |

### §Target Evaluation  <span>(363–377)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 363 | Target-scoring system exists | ✅ | ✅ | `TargetEvaluator`, `CommandToolApi.ScoreTargets`, posture-weighted scoring | `TargetEvaluatorTest`, `CommandToolApiTest.ScoreTargets*` | — |
| 364 | Strategic value considered | ✅ | ✅ | `TargetEvaluator`, `CommandToolApi.ScoreTargets`, posture-weighted scoring | `TargetEvaluatorTest`, `CommandToolApiTest.ScoreTargets*` | — |
| 365 | Economic damage considered | ✅ | ✅ | `TargetEvaluator`, `CommandToolApi.ScoreTargets`, posture-weighted scoring | `TargetEvaluatorTest`, `CommandToolApiTest.ScoreTargets*` | — |
| 366 | Production denial considered | ✅ | ✅ | `TargetEvaluator`, `CommandToolApi.ScoreTargets`, posture-weighted scoring | `TargetEvaluatorTest`, `CommandToolApiTest.ScoreTargets*` | — |
| 367 | Technology denial considered | ✅ | ✅ | `TargetEvaluator`, `CommandToolApi.ScoreTargets`, posture-weighted scoring | `TargetEvaluatorTest`, `CommandToolApiTest.ScoreTargets*` | — |
| 368 | Positional value considered | ✅ | ✅ | `TargetEvaluator`, `CommandToolApi.ScoreTargets`, posture-weighted scoring | `TargetEvaluatorTest`, `CommandToolApiTest.ScoreTargets*` | — |
| 369 | Information value considered | ✅ | ✅ | `TargetEvaluator`, `CommandToolApi.ScoreTargets`, posture-weighted scoring | `TargetEvaluatorTest`, `CommandToolApiTest.ScoreTargets*` | — |
| 370 | Follow-on opportunity considered | ✅ | ✅ | `TargetEvaluator`, `CommandToolApi.ScoreTargets`, posture-weighted scoring | `TargetEvaluatorTest`, `CommandToolApiTest.ScoreTargets*` | — |
| 371 | Expected friendly losses considered | ✅ | ✅ | `TargetEvaluator`, `CommandToolApi.ScoreTargets`, posture-weighted scoring | `TargetEvaluatorTest`, `CommandToolApiTest.ScoreTargets*` | — |
| 372 | Travel cost considered | ✅ | ✅ | `TargetEvaluator`, `CommandToolApi.ScoreTargets`, posture-weighted scoring | `TargetEvaluatorTest`, `CommandToolApiTest.ScoreTargets*` | — |
| 373 | Enemy reinforcement risk considered | ✅ | ✅ | `TargetEvaluator`, `CommandToolApi.ScoreTargets`, posture-weighted scoring | `TargetEvaluatorTest`, `CommandToolApiTest.ScoreTargets*` | — |
| 374 | Counterattack risk considered | ✅ | ✅ | `TargetEvaluator`, `CommandToolApi.ScoreTargets`, posture-weighted scoring | `TargetEvaluatorTest`, `CommandToolApiTest.ScoreTargets*` | — |
| 375 | Intelligence uncertainty considered | ✅ | ✅ | `TargetEvaluator`, `CommandToolApi.ScoreTargets`, posture-weighted scoring | `TargetEvaluatorTest`, `CommandToolApiTest.ScoreTargets*` | — |
| 376 | Target weights change with posture | ✅ | ✅ | `TargetEvaluator`, `CommandToolApi.ScoreTargets`, posture-weighted scoring | `TargetEvaluatorTest`, `CommandToolApiTest.ScoreTargets*` | — |
| 377 | AI avoids wasting force on low-value targets | ✅ | ✅ | `TargetEvaluator`, `CommandToolApi.ScoreTargets`, posture-weighted scoring | `TargetEvaluatorTest`, `CommandToolApiTest.ScoreTargets*` | — |

### §Production & Capability Planning  <span>(378–401)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 378 | Production planning at coalition level | ✅ | ✅ | `ProductionContract`, `FriendlyCapability` (15 capabilities), `CoalitionForceRegistry.FriendlyCapabilitiesFor` | `ProductionContractTest`, `ForceRegistryTest` | — |
| 379 | Reasons in capabilities not just unit names | ✅ | ✅ | `ProductionContract`, `FriendlyCapability` (15 capabilities), `CoalitionForceRegistry.FriendlyCapabilitiesFor` | `ProductionContractTest`, `ForceRegistryTest` | — |
| 380 | Anti-armor requirement tracked | ✅ | ✅ | `ProductionContract`, `FriendlyCapability` (15 capabilities), `CoalitionForceRegistry.FriendlyCapabilitiesFor` | `ProductionContractTest`, `ForceRegistryTest` | — |
| 381 | Anti-infantry requirement tracked | ✅ | ✅ | `ProductionContract`, `FriendlyCapability` (15 capabilities), `CoalitionForceRegistry.FriendlyCapabilitiesFor` | `ProductionContractTest`, `ForceRegistryTest` | — |
| 382 | Anti-air requirement tracked | ✅ | ✅ | `ProductionContract`, `FriendlyCapability` (15 capabilities), `CoalitionForceRegistry.FriendlyCapabilitiesFor` | `ProductionContractTest`, `ForceRegistryTest` | — |
| 383 | Artillery requirement tracked | ✅ | ✅ | `ProductionContract`, `FriendlyCapability` (15 capabilities), `CoalitionForceRegistry.FriendlyCapabilitiesFor` | `ProductionContractTest`, `ForceRegistryTest` | — |
| 384 | Recon requirement tracked | ✅ | ✅ | `ProductionContract`, `FriendlyCapability` (15 capabilities), `CoalitionForceRegistry.FriendlyCapabilitiesFor` | `ProductionContractTest`, `ForceRegistryTest` | — |
| 385 | Mobility requirement tracked | ✅ | ✅ | `ProductionContract`, `FriendlyCapability` (15 capabilities), `CoalitionForceRegistry.FriendlyCapabilitiesFor` | `ProductionContractTest`, `ForceRegistryTest` | — |
| 386 | Fast-raiding requirement tracked | ✅ | ✅ | `ProductionContract`, `FriendlyCapability` (15 capabilities), `CoalitionForceRegistry.FriendlyCapabilitiesFor` | `ProductionContractTest`, `ForceRegistryTest` | — |
| 387 | Naval requirement tracked | ✅ | ✅ | `ProductionContract`, `FriendlyCapability` (15 capabilities), `CoalitionForceRegistry.FriendlyCapabilitiesFor` | `ProductionContractTest`, `ForceRegistryTest` | — |
| 388 | Air-superiority requirement tracked | ✅ | ✅ | `ProductionContract`, `FriendlyCapability` (15 capabilities), `CoalitionForceRegistry.FriendlyCapabilitiesFor` | `ProductionContractTest`, `ForceRegistryTest` | — |
| 389 | Transport requirement tracked | ✅ | ✅ | `ProductionContract`, `FriendlyCapability` (15 capabilities), `CoalitionForceRegistry.FriendlyCapabilitiesFor` | `ProductionContractTest`, `ForceRegistryTest` | — |
| 390 | Special-operations requirement tracked | ✅ | ✅ | `ProductionContract`, `FriendlyCapability` (15 capabilities), `CoalitionForceRegistry.FriendlyCapabilitiesFor` | `ProductionContractTest`, `ForceRegistryTest` | — |
| 391 | Base-defense requirement tracked | ✅ | ✅ | `ProductionContract`, `FriendlyCapability` (15 capabilities), `CoalitionForceRegistry.FriendlyCapabilitiesFor` | `ProductionContractTest`, `ForceRegistryTest` | — |
| 392 | Capability requirements respond to enemy comp | ✅ | ✅ | `ProductionContract`, `FriendlyCapability` (15 capabilities), `CoalitionForceRegistry.FriendlyCapabilitiesFor` | `ProductionContractTest`, `ForceRegistryTest` | — |
| 393 | Allied players can specialize production | ✅ | ✅ | `ProductionContract`, `FriendlyCapability` (15 capabilities), `CoalitionForceRegistry.FriendlyCapabilitiesFor` | `ProductionContractTest`, `ForceRegistryTest` | — |
| 394 | Specialization respects tech tree | ✅ | ✅ | `ProductionContract`, `FriendlyCapability` (15 capabilities), `CoalitionForceRegistry.FriendlyCapabilitiesFor` | `ProductionContractTest`, `ForceRegistryTest` | — |
| 395 | Coalition avoids unnecessary duplication | ✅ | ✅ | `ProductionContract`, `FriendlyCapability` (15 capabilities), `CoalitionForceRegistry.FriendlyCapabilitiesFor` | `ProductionContractTest`, `ForceRegistryTest` | — |
| 396 | React to destroyed production infrastructure | ✅ | ✅ | `ProductionContract`, `FriendlyCapability` (15 capabilities), `CoalitionForceRegistry.FriendlyCapabilitiesFor` | `ProductionContractTest`, `ForceRegistryTest` | — |
| 397 | React to new enemy air/naval threats | ✅ | ✅ | `ProductionContract`, `FriendlyCapability` (15 capabilities), `CoalitionForceRegistry.FriendlyCapabilitiesFor` | `ProductionContractTest`, `ForceRegistryTest` | — |
| 398 | React to strategic posture | ✅ | ✅ | `ProductionContract`, `FriendlyCapability` (15 capabilities), `CoalitionForceRegistry.FriendlyCapabilitiesFor` | `ProductionContractTest`, `ForceRegistryTest` | — |
| 399 | Excessive resource floating detected | ✅ | ✅ | `ProductionContract`, `FriendlyCapability` (15 capabilities), `CoalitionForceRegistry.FriendlyCapabilitiesFor` | `ProductionContractTest`, `ForceRegistryTest` | — |
| 400 | Money reserved for planned infrastructure/tech | ✅ | ✅ | `ProductionContract`, `FriendlyCapability` (15 capabilities), `CoalitionForceRegistry.FriendlyCapabilitiesFor` | `ProductionContractTest`, `ForceRegistryTest` | — |
| 401 | Emergency replacement production | ✅ | ✅ | `ProductionContract`, `FriendlyCapability` (15 capabilities), `CoalitionForceRegistry.FriendlyCapabilitiesFor` | `ProductionContractTest`, `ForceRegistryTest` | — |

### §Economy & Expansion  <span>(402–412)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 402 | Understands each ally's economy separately | ✅ | ✅ | `CommandToolApi.GetEconomyState`, `CoalitionMapAnalysis.ExpansionValue`, `McvExpansionManagerBotModule`, `ResourceMapBotModule` | `CommandToolApiTest`, `MapAnalysisTest`, `ProductionContractTest` | `CommandToolApi.GetEconomyState` reports per-member cash separately; covered by `CommandToolApiTest`. |
| 403 | Refinery/resource capacity tracked | ✅ | ✅ | `CommandToolApi.GetEconomyState`, `CoalitionMapAnalysis.ExpansionValue`, `McvExpansionManagerBotModule`, `ResourceMapBotModule` | `CommandToolApiTest`, `MapAnalysisTest`, `ProductionContractTest` | Refinery/harvester state in `GetEconomyState`; covered by `CommandToolApiTest`. |
| 404 | Harvester status tracked | ✅ | ✅ | `CommandToolApi.GetEconomyState`, `CoalitionMapAnalysis.ExpansionValue`, `McvExpansionManagerBotModule`, `ResourceMapBotModule` | `CommandToolApiTest`, `MapAnalysisTest`, `ProductionContractTest` | Harvester status in `GetEconomyState`; covered by `CommandToolApiTest`. |
| 405 | Resource depletion influences planning | ✅ | ✅ | `CommandToolApi.GetEconomyState`, `CoalitionMapAnalysis.ExpansionValue`, `McvExpansionManagerBotModule`, `ResourceMapBotModule` | `CommandToolApiTest`, `MapAnalysisTest`, `ProductionContractTest` | `CoalitionRegion.ResourceCellsRemaining` feeds expansion scoring; covered by `MapAnalysisTest`. |
| 406 | Expansion opportunities scored | ✅ | ✅ | `CommandToolApi.GetEconomyState`, `CoalitionMapAnalysis.ExpansionValue`, `McvExpansionManagerBotModule`, `ResourceMapBotModule` | `CommandToolApiTest`, `MapAnalysisTest`, `ProductionContractTest` | `CoalitionMapAnalysis.ComputeExpansionValue`; covered by `MapAnalysisTest`. |
| 407 | Expansion risk evaluated | ✅ | 🟡 | `CommandToolApi.GetEconomyState`, `CoalitionMapAnalysis.ExpansionValue`, `McvExpansionManagerBotModule`, `ResourceMapBotModule` | `CommandToolApiTest`, `MapAnalysisTest`, `ProductionContractTest` | — |
| 408 | Expansion timing changes with posture | ✅ | 🟡 | `CommandToolApi.GetEconomyState`, `CoalitionMapAnalysis.ExpansionValue`, `McvExpansionManagerBotModule`, `ResourceMapBotModule` | `CommandToolApiTest`, `MapAnalysisTest`, `ProductionContractTest` | — |
| 409 | Ally assigned to expansion specialization | ✅ | 🟡 | `CommandToolApi.GetEconomyState`, `CoalitionMapAnalysis.ExpansionValue`, `McvExpansionManagerBotModule`, `ResourceMapBotModule` | `CommandToolApiTest`, `MapAnalysisTest`, `ProductionContractTest` | — |
| 410 | New expansions receive defensive planning | ✅ | 🟡 | `CommandToolApi.GetEconomyState`, `CoalitionMapAnalysis.ExpansionValue`, `McvExpansionManagerBotModule`, `ResourceMapBotModule` | `CommandToolApiTest`, `MapAnalysisTest`, `ProductionContractTest` | — |
| 411 | Economic vulnerability influences defense | ✅ | 🟡 | `CommandToolApi.GetEconomyState`, `CoalitionMapAnalysis.ExpansionValue`, `McvExpansionManagerBotModule`, `ResourceMapBotModule` | `CommandToolApiTest`, `MapAnalysisTest`, `ProductionContractTest` | — |
| 412 | Enemy economic weakness triggers raiding | ✅ | 🟡 | `CommandToolApi.GetEconomyState`, `CoalitionMapAnalysis.ExpansionValue`, `McvExpansionManagerBotModule`, `ResourceMapBotModule` | `CommandToolApiTest`, `MapAnalysisTest`, `ProductionContractTest` | — |

### §Opponent Modeling  <span>(413–432)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 413 | Opponent-model object exists | ✅ | ✅ | `OpponentModel` (biases, lanes, response rates, expansion timing, `Confidence`, `ShouldExploit`, `DerivePlaystyle`) | `OpponentModelTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 414 | Enemy army composition tendencies tracked | ✅ | ✅ | `OpponentModel` (biases, lanes, response rates, expansion timing, `Confidence`, `ShouldExploit`, `DerivePlaystyle`) | `OpponentModelTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 415 | Enemy armor bias tracked | ✅ | ✅ | `OpponentModel` (biases, lanes, response rates, expansion timing, `Confidence`, `ShouldExploit`, `DerivePlaystyle`) | `OpponentModelTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 416 | Enemy infantry bias tracked | ✅ | ✅ | `OpponentModel` (biases, lanes, response rates, expansion timing, `Confidence`, `ShouldExploit`, `DerivePlaystyle`) | `OpponentModelTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 417 | Enemy air bias tracked | ✅ | ✅ | `OpponentModel` (biases, lanes, response rates, expansion timing, `Confidence`, `ShouldExploit`, `DerivePlaystyle`) | `OpponentModelTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 418 | Enemy naval bias tracked | ✅ | ✅ | `OpponentModel` (biases, lanes, response rates, expansion timing, `Confidence`, `ShouldExploit`, `DerivePlaystyle`) | `OpponentModelTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 419 | Enemy static-defense bias tracked | ✅ | ✅ | `OpponentModel` (biases, lanes, response rates, expansion timing, `Confidence`, `ShouldExploit`, `DerivePlaystyle`) | `OpponentModelTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 420 | Preferred attack lanes tracked | ✅ | ✅ | `OpponentModel` (biases, lanes, response rates, expansion timing, `Confidence`, `ShouldExploit`, `DerivePlaystyle`) | `OpponentModelTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 421 | Human reaction speed estimated | ✅ | ✅ | `OpponentModel` (biases, lanes, response rates, expansion timing, `Confidence`, `ShouldExploit`, `DerivePlaystyle`) | `OpponentModelTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 422 | Response to harassment tracked | ✅ | ✅ | `OpponentModel` (biases, lanes, response rates, expansion timing, `Confidence`, `ShouldExploit`, `DerivePlaystyle`) | `OpponentModelTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 423 | Response to feints tracked | ✅ | ✅ | `OpponentModel` (biases, lanes, response rates, expansion timing, `Confidence`, `ShouldExploit`, `DerivePlaystyle`) | `OpponentModelTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 424 | Tendency to redeploy whole army tracked | ✅ | ✅ | `OpponentModel` (biases, lanes, response rates, expansion timing, `Confidence`, `ShouldExploit`, `DerivePlaystyle`) | `OpponentModelTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 425 | Expansion timing behavior tracked | ✅ | ✅ | `OpponentModel` (biases, lanes, response rates, expansion timing, `Confidence`, `ShouldExploit`, `DerivePlaystyle`) | `OpponentModelTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 426 | Defensive/turtling behavior tracked | ✅ | ✅ | `OpponentModel` (biases, lanes, response rates, expansion timing, `Confidence`, `ShouldExploit`, `DerivePlaystyle`) | `OpponentModelTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 427 | Aggressive/rush behavior tracked | ✅ | ✅ | `OpponentModel` (biases, lanes, response rates, expansion timing, `Confidence`, `ShouldExploit`, `DerivePlaystyle`) | `OpponentModelTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 428 | Opponent-model confidence values exist | ✅ | ✅ | `OpponentModel` (biases, lanes, response rates, expansion timing, `Confidence`, `ShouldExploit`, `DerivePlaystyle`) | `OpponentModelTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 429 | Commander exploits reliable patterns | ✅ | ✅ | `OpponentModel` (biases, lanes, response rates, expansion timing, `Confidence`, `ShouldExploit`, `DerivePlaystyle`) | `OpponentModelTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 430 | Commander doesn't treat history as guaranteed | ✅ | ✅ | `OpponentModel` (biases, lanes, response rates, expansion timing, `Confidence`, `ShouldExploit`, `DerivePlaystyle`) | `OpponentModelTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 431 | Opponent model updates during match | ✅ | ✅ | `OpponentModel` (biases, lanes, response rates, expansion timing, `Confidence`, `ShouldExploit`, `DerivePlaystyle`) | `OpponentModelTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 432 | Validated vs multiple scripted opponents | ✅ | ✅ | `OpponentModel` (biases, lanes, response rates, expansion timing, `Confidence`, `ShouldExploit`, `DerivePlaystyle`) | `OpponentModelTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |

### §Counterattack Intelligence  <span>(433–439)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 433 | Estimate where attackers originated | ✅ | ✅ | `CounterattackAssessment`, `MissionType.Counterattack`, `StrategicBrainBotModule` counter-pursuit | `CounterattackAssessmentTest`, `ExpandedCoverageTest.MatchMetricsCounterattack` | — |
| 434 | Evaluate what enemy exposed by attacking | ✅ | ✅ | `CounterattackAssessment`, `MissionType.Counterattack`, `StrategicBrainBotModule` counter-pursuit | `CounterattackAssessmentTest`, `ExpandedCoverageTest.MatchMetricsCounterattack` | — |
| 435 | Check if enemy reinforcements depleted | ✅ | ✅ | `CounterattackAssessment`, `MissionType.Counterattack`, `StrategicBrainBotModule` counter-pursuit | `CounterattackAssessmentTest`, `ExpandedCoverageTest.MatchMetricsCounterattack` | — |
| 436 | Consider immediate counterattack opportunities | ✅ | ✅ | `CounterattackAssessment`, `MissionType.Counterattack`, `StrategicBrainBotModule` counter-pursuit | `CounterattackAssessmentTest`, `ExpandedCoverageTest.MatchMetricsCounterattack` | — |
| 437 | Don't counterattack when unfavorable | ✅ | ✅ | `CounterattackAssessment`, `MissionType.Counterattack`, `StrategicBrainBotModule` counter-pursuit | `CounterattackAssessmentTest`, `ExpandedCoverageTest.MatchMetricsCounterattack` | — |
| 438 | Counterattack considers production windows | ✅ | ✅ | `CounterattackAssessment`, `MissionType.Counterattack`, `StrategicBrainBotModule` counter-pursuit | `CounterattackAssessmentTest`, `ExpandedCoverageTest.MatchMetricsCounterattack` | — |
| 439 | Counterattack scenarios covered by tests | ✅ | ✅ | `CounterattackAssessment`, `MissionType.Counterattack`, `StrategicBrainBotModule` counter-pursuit | `CounterattackAssessmentTest`, `ExpandedCoverageTest.MatchMetricsCounterattack` | — |

### §LLM World Context Compression  <span>(440–454)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 440 | Raw actor dumps not sent to LLM | ✅ | ✅ | `ExternalBrainBotModule` `TeamState`/`ArmyGroupState`/`EstimateState` summaries, bounded context sections | `ExternalBrainSnapshotTest`, `CommandToolApiTest` | — |
| 441 | Large groups summarized into army-group records | ✅ | ✅ | `ExternalBrainBotModule` `TeamState`/`ArmyGroupState`/`EstimateState` summaries, bounded context sections | `ExternalBrainSnapshotTest`, `CommandToolApiTest` | — |
| 442 | Army groups include unit composition | ✅ | ✅ | `ExternalBrainBotModule` `TeamState`/`ArmyGroupState`/`EstimateState` summaries, bounded context sections | `ExternalBrainSnapshotTest`, `CommandToolApiTest` | — |
| 443 | Army groups include strength | ✅ | ✅ | `ExternalBrainBotModule` `TeamState`/`ArmyGroupState`/`EstimateState` summaries, bounded context sections | `ExternalBrainSnapshotTest`, `CommandToolApiTest` | — |
| 444 | Army groups include readiness | ✅ | ✅ | `ExternalBrainBotModule` `TeamState`/`ArmyGroupState`/`EstimateState` summaries, bounded context sections | `ExternalBrainSnapshotTest`, `CommandToolApiTest` | — |
| 445 | Army groups include location | ✅ | ✅ | `ExternalBrainBotModule` `TeamState`/`ArmyGroupState`/`EstimateState` summaries, bounded context sections | `ExternalBrainSnapshotTest`, `CommandToolApiTest` | — |
| 446 | Army groups include mission assignment | ✅ | ✅ | `ExternalBrainBotModule` `TeamState`/`ArmyGroupState`/`EstimateState` summaries, bounded context sections | `ExternalBrainSnapshotTest`, `CommandToolApiTest` | — |
| 447 | Army groups include nearby known threats | ✅ | ✅ | `ExternalBrainBotModule` `TeamState`/`ArmyGroupState`/`EstimateState` summaries, bounded context sections | `ExternalBrainSnapshotTest`, `CommandToolApiTest` | — |
| 448 | Important unique units individually represented | ✅ | ✅ | `ExternalBrainBotModule` `TeamState`/`ArmyGroupState`/`EstimateState` summaries, bounded context sections | `ExternalBrainSnapshotTest`, `CommandToolApiTest` | — |
| 449 | Enemy force estimates contain min/expected/max | ✅ | ✅ | `ExternalBrainBotModule` `TeamState`/`ArmyGroupState`/`EstimateState` summaries, bounded context sections | `ExternalBrainSnapshotTest`, `CommandToolApiTest` | — |
| 450 | Confidence values exposed to LLM | ✅ | ✅ | `ExternalBrainBotModule` `TeamState`/`ArmyGroupState`/`EstimateState` summaries, bounded context sections | `ExternalBrainSnapshotTest`, `CommandToolApiTest` | — |
| 451 | Recent significant events summarized | ✅ | ✅ | `ExternalBrainBotModule` `TeamState`/`ArmyGroupState`/`EstimateState` summaries, bounded context sections | `ExternalBrainSnapshotTest`, `CommandToolApiTest` | — |
| 452 | Unresolved uncertainties summarized | ✅ | ✅ | `ExternalBrainBotModule` `TeamState`/`ArmyGroupState`/`EstimateState` summaries, bounded context sections | `ExternalBrainSnapshotTest`, `CommandToolApiTest` | — |
| 453 | Context-size limits handled gracefully | ✅ | ✅ | `ExternalBrainBotModule` `TeamState`/`ArmyGroupState`/`EstimateState` summaries, bounded context sections | `ExternalBrainSnapshotTest`, `CommandToolApiTest` | — |
| 454 | LLM receives only relevant information | ✅ | ✅ | `ExternalBrainBotModule` `TeamState`/`ArmyGroupState`/`EstimateState` summaries, bounded context sections | `ExternalBrainSnapshotTest`, `CommandToolApiTest` | — |

### §LLM Intelligence Tools  <span>(455–464)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 455 | get_global_summary() exists | ✅ | ✅ | `CommandToolApi` read tools; `ToolContext.EnemyIntel` is the only enemy source (no `world.Actors` access) | `CommandToolApiTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 456 | inspect_region() exists | ✅ | ✅ | `CommandToolApi` read tools; `ToolContext.EnemyIntel` is the only enemy source (no `world.Actors` access) | `CommandToolApiTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 457 | inspect_force() exists | ✅ | ✅ | `CommandToolApi` read tools; `ToolContext.EnemyIntel` is the only enemy source (no `world.Actors` access) | `CommandToolApiTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 458 | inspect_enemy_intelligence() exists | ✅ | ✅ | `CommandToolApi` read tools; `ToolContext.EnemyIntel` is the only enemy source (no `world.Actors` access) | `CommandToolApiTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 459 | get_recent_events() exists | ✅ | ✅ | `CommandToolApi` read tools; `ToolContext.EnemyIntel` is the only enemy source (no `world.Actors` access) | `CommandToolApiTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 460 | get_opponent_model() exists | ✅ | ✅ | `CommandToolApi` read tools; `ToolContext.EnemyIntel` is the only enemy source (no `world.Actors` access) | `CommandToolApiTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 461 | get_uncertainties() exists | ✅ | ✅ | `CommandToolApi` read tools; `ToolContext.EnemyIntel` is the only enemy source (no `world.Actors` access) | `CommandToolApiTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 462 | Tool outputs structured and machine-readable | ✅ | ✅ | `CommandToolApi` read tools; `ToolContext.EnemyIntel` is the only enemy source (no `world.Actors` access) | `CommandToolApiTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 463 | Tool outputs distinguish fact from inference | ✅ | ✅ | `CommandToolApi` read tools; `ToolContext.EnemyIntel` is the only enemy source (no `world.Actors` access) | `CommandToolApiTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 464 | Tool calls cannot expose hidden info in fair-fog | ✅ | ✅ | `CommandToolApi` read tools; `ToolContext.EnemyIntel` is the only enemy source (no `world.Actors` access) | `CommandToolApiTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |

### §LLM Analysis Tools  <span>(465–474)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 465 | estimate_engagement() exists | ✅ | ✅ | `CommandToolApi.EstimateEngagement/CompareForcePackages/PlanRoutes/ScoreTargets/EstimateEnemyResponse/FindAttackWindows/FindSpecialOpsRoutes` | `CommandToolApiTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 466 | compare_force_packages() exists | ✅ | ✅ | `CommandToolApi.EstimateEngagement/CompareForcePackages/PlanRoutes/ScoreTargets/EstimateEnemyResponse/FindAttackWindows/FindSpecialOpsRoutes` | `CommandToolApiTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 467 | plan_routes() exists | ✅ | ✅ | `CommandToolApi.EstimateEngagement/CompareForcePackages/PlanRoutes/ScoreTargets/EstimateEnemyResponse/FindAttackWindows/FindSpecialOpsRoutes` | `CommandToolApiTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 468 | score_targets() exists | ✅ | ✅ | `CommandToolApi.EstimateEngagement/CompareForcePackages/PlanRoutes/ScoreTargets/EstimateEnemyResponse/FindAttackWindows/FindSpecialOpsRoutes` | `CommandToolApiTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 469 | estimate_enemy_response() exists | ✅ | ✅ | `CommandToolApi.EstimateEngagement/CompareForcePackages/PlanRoutes/ScoreTargets/EstimateEnemyResponse/FindAttackWindows/FindSpecialOpsRoutes` | `CommandToolApiTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 470 | find_attack_windows() exists | ✅ | ✅ | `CommandToolApi.EstimateEngagement/CompareForcePackages/PlanRoutes/ScoreTargets/EstimateEnemyResponse/FindAttackWindows/FindSpecialOpsRoutes` | `CommandToolApiTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 471 | find_special_ops_routes() exists | ✅ | ✅ | `CommandToolApi.EstimateEngagement/CompareForcePackages/PlanRoutes/ScoreTargets/EstimateEnemyResponse/FindAttackWindows/FindSpecialOpsRoutes` | `CommandToolApiTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 472 | Analysis tools validate inputs | ✅ | ✅ | `CommandToolApi.EstimateEngagement/CompareForcePackages/PlanRoutes/ScoreTargets/EstimateEnemyResponse/FindAttackWindows/FindSpecialOpsRoutes` | `CommandToolApiTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 473 | Invalid IDs produce errors not hallucinations | ✅ | ✅ | `CommandToolApi.EstimateEngagement/CompareForcePackages/PlanRoutes/ScoreTargets/EstimateEnemyResponse/FindAttackWindows/FindSpecialOpsRoutes` | `CommandToolApiTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 474 | Tool results deterministic where expected | ✅ | ✅ | `CommandToolApi.EstimateEngagement/CompareForcePackages/PlanRoutes/ScoreTargets/EstimateEnemyResponse/FindAttackWindows/FindSpecialOpsRoutes` | `CommandToolApiTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |

### §LLM Economy/Production Tools  <span>(475–483)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 475 | get_economy_state() exists | ✅ | ✅ | `CommandToolApi.GetEconomyState/GetProductionState/SetProductionDirective/SetExpansionPriority/RequestCapability`, `CommandValidator` | `CommandToolApiTest`, `ExpandedCoverageTest.ValidateCapability*` | — |
| 476 | get_production_state() exists | ✅ | ✅ | `CommandToolApi.GetEconomyState/GetProductionState/SetProductionDirective/SetExpansionPriority/RequestCapability`, `CommandValidator` | `CommandToolApiTest`, `ExpandedCoverageTest.ValidateCapability*` | — |
| 477 | set_production_directive() exists | ✅ | ✅ | `CommandToolApi.GetEconomyState/GetProductionState/SetProductionDirective/SetExpansionPriority/RequestCapability`, `CommandValidator` | `CommandToolApiTest`, `ExpandedCoverageTest.ValidateCapability*` | — |
| 478 | set_expansion_priority() exists | ✅ | ✅ | `CommandToolApi.GetEconomyState/GetProductionState/SetProductionDirective/SetExpansionPriority/RequestCapability`, `CommandValidator` | `CommandToolApiTest`, `ExpandedCoverageTest.ValidateCapability*` | — |
| 479 | request_capability() exists | ✅ | ✅ | `CommandToolApi.GetEconomyState/GetProductionState/SetProductionDirective/SetExpansionPriority/RequestCapability`, `CommandValidator` | `CommandToolApiTest`, `ExpandedCoverageTest.ValidateCapability*` | — |
| 480 | Production directives respect prerequisites | ✅ | ✅ | `CommandToolApi.GetEconomyState/GetProductionState/SetProductionDirective/SetExpansionPriority/RequestCapability`, `CommandValidator` | `CommandToolApiTest`, `ExpandedCoverageTest.ValidateCapability*` | — |
| 481 | Production directives respect available cash | ✅ | ✅ | `CommandToolApi.GetEconomyState/GetProductionState/SetProductionDirective/SetExpansionPriority/RequestCapability`, `CommandValidator` | `CommandToolApiTest`, `ExpandedCoverageTest.ValidateCapability*` | — |
| 482 | Production directives respect queue availability | ✅ | ✅ | `CommandToolApi.GetEconomyState/GetProductionState/SetProductionDirective/SetExpansionPriority/RequestCapability`, `CommandValidator` | `CommandToolApiTest`, `ExpandedCoverageTest.ValidateCapability*` | — |
| 483 | Invalid production requests rejected with reasons | ✅ | ✅ | `CommandToolApi.GetEconomyState/GetProductionState/SetProductionDirective/SetExpansionPriority/RequestCapability`, `CommandValidator` | `CommandToolApiTest`, `ExpandedCoverageTest.ValidateCapability*` | — |

### §LLM Mission/Command Tools  <span>(484–499)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 484 | create_mission() exists | ✅ | ✅ | `CommandToolApi` mutation tools returning validated `plan_patch`; no `move_unit`/`attack_unit` surface exists | `CommandToolApiTest`, `CommandValidatorTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 485 | modify_mission() exists | ✅ | ✅ | `CommandToolApi` mutation tools returning validated `plan_patch`; no `move_unit`/`attack_unit` surface exists | `CommandToolApiTest`, `CommandValidatorTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 486 | cancel_mission() exists | ✅ | ✅ | `CommandToolApi` mutation tools returning validated `plan_patch`; no `move_unit`/`attack_unit` surface exists | `CommandToolApiTest`, `CommandValidatorTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 487 | assign_force() exists | ✅ | ✅ | `CommandToolApi` mutation tools returning validated `plan_patch`; no `move_unit`/`attack_unit` surface exists | `CommandToolApiTest`, `CommandValidatorTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 488 | release_force() exists | ✅ | ✅ | `CommandToolApi` mutation tools returning validated `plan_patch`; no `move_unit`/`attack_unit` surface exists | `CommandToolApiTest`, `CommandValidatorTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 489 | set_reserve() exists | ✅ | ✅ | `CommandToolApi` mutation tools returning validated `plan_patch`; no `move_unit`/`attack_unit` surface exists | `CommandToolApiTest`, `CommandValidatorTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 490 | request_recon() exists | ✅ | ✅ | `CommandToolApi` mutation tools returning validated `plan_patch`; no `move_unit`/`attack_unit` surface exists | `CommandToolApiTest`, `CommandValidatorTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 491 | set_strategic_posture() exists | ✅ | ✅ | `CommandToolApi` mutation tools returning validated `plan_patch`; no `move_unit`/`attack_unit` surface exists | `CommandToolApiTest`, `CommandValidatorTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 492 | get_mission_status() exists | ✅ | ✅ | `CommandToolApi` mutation tools returning validated `plan_patch`; no `move_unit`/`attack_unit` surface exists | `CommandToolApiTest`, `CommandValidatorTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 493 | get_force_readiness() exists | ✅ | ✅ | `CommandToolApi` mutation tools returning validated `plan_patch`; no `move_unit`/`attack_unit` surface exists | `CommandToolApiTest`, `CommandValidatorTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 494 | get_transport_status() exists | ✅ | ✅ | `CommandToolApi` mutation tools returning validated `plan_patch`; no `move_unit`/`attack_unit` surface exists | `CommandToolApiTest`, `CommandValidatorTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 495 | get_route_status() exists | ✅ | ✅ | `CommandToolApi` mutation tools returning validated `plan_patch`; no `move_unit`/`attack_unit` surface exists | `CommandToolApiTest`, `CommandValidatorTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 496 | LLM mission commands pass through validation | ✅ | ✅ | `CommandToolApi` mutation tools returning validated `plan_patch`; no `move_unit`/`attack_unit` surface exists | `CommandToolApiTest`, `CommandValidatorTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 497 | LLM does not require move_unit() control | ✅ | ✅ | `CommandToolApi` mutation tools returning validated `plan_patch`; no `move_unit`/`attack_unit` surface exists | `CommandToolApiTest`, `CommandValidatorTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 498 | LLM does not require attack_unit() control | ✅ | ✅ | `CommandToolApi` mutation tools returning validated `plan_patch`; no `move_unit`/`attack_unit` surface exists | `CommandToolApiTest`, `CommandValidatorTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |
| 499 | Emergency direct-control API tightly constrained | ✅ | ✅ | `CommandToolApi` mutation tools returning validated `plan_patch`; no `move_unit`/`attack_unit` surface exists | `CommandToolApiTest`, `CommandValidatorTest`, `AcceptanceSuite.ToolSurfaceComplete` | — |

### §LLM Commander Behavior  <span>(500–521)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 500 | System prompt: coalition victory = primary objective | ✅ | 🟡 | `ai/model_server.py` `SYSTEM_PROMPT` (lines 70–140); engine backstops in `CommandValidator`/`CoalitionOrderArbiter` | `ai/selfcheck.py::commander_contract_regression` (prompt substring contract only) | Expressed as an instruction in `SYSTEM_PROMPT` and verified only by substring assertion in `ai/selfcheck.py::commander_contract_regression`. No behavioural validation against a live model — the automated suite never runs an LLM. |
| 501 | LLM distinguishes known/observed/inferred/suspected/unknown | ✅ | 🟡 | `ai/model_server.py` `SYSTEM_PROMPT` (lines 70–140); engine backstops in `CommandValidator`/`CoalitionOrderArbiter` | `ai/selfcheck.py::commander_contract_regression` (prompt substring contract only) | Expressed as an instruction in `SYSTEM_PROMPT` and verified only by substring assertion in `ai/selfcheck.py::commander_contract_regression`. No behavioural validation against a live model — the automated suite never runs an LLM. |
| 502 | LLM instructed not to fabricate engine state | ✅ | 🟡 | `ai/model_server.py` `SYSTEM_PROMPT` (lines 70–140); engine backstops in `CommandValidator`/`CoalitionOrderArbiter` | `ai/selfcheck.py::commander_contract_regression` (prompt substring contract only) | Prompt rule *plus* a real engine backstop: fabricated state cannot survive `CommandValidator` (`REJECTED_UNKNOWN_*`). Prompt side is substring-tested only; the backstop is covered by `CommandValidatorTest`. |
| 503 | LLM checks available forces before assigning | ✅ | 🟡 | `ai/model_server.py` `SYSTEM_PROMPT` (lines 70–140); engine backstops in `CommandValidator`/`CoalitionOrderArbiter` | `ai/selfcheck.py::commander_contract_regression` (prompt substring contract only) | Prompt rule plus engine backstop: `CoalitionOrderArbiter.Assign` refuses an already-committed force (`REJECTED_CONFLICT`, covered by `OrderArbiterTest`). LLM-side compliance itself is untested. |
| 504 | LLM checks existing commitments before creating conflicts | ✅ | 🟡 | `ai/model_server.py` `SYSTEM_PROMPT` (lines 70–140); engine backstops in `CommandValidator`/`CoalitionOrderArbiter` | `ai/selfcheck.py::commander_contract_regression` (prompt substring contract only) | Prompt rule plus engine backstop: duplicate missions are rejected by `CommandValidator.ValidateMissions` (`REJECTED_CONFLICT`), covered by `CommandValidatorTest`. LLM-side compliance untested. |
| 505 | LLM identifies explicit strategic posture | ✅ | 🟡 | `ai/model_server.py` `SYSTEM_PROMPT` (lines 70–140); engine backstops in `CommandValidator`/`CoalitionOrderArbiter` | `ai/selfcheck.py::commander_contract_regression` (prompt substring contract only) | Expressed as an instruction in `SYSTEM_PROMPT` and verified only by substring assertion in `ai/selfcheck.py::commander_contract_regression`. No behavioural validation against a live model — the automated suite never runs an LLM. |
| 506 | LLM identifies explicit main effort | ✅ | 🟡 | `ai/model_server.py` `SYSTEM_PROMPT` (lines 70–140); engine backstops in `CommandValidator`/`CoalitionOrderArbiter` | `ai/selfcheck.py::commander_contract_regression` (prompt substring contract only) | Expressed as an instruction in `SYSTEM_PROMPT` and verified only by substring assertion in `ai/selfcheck.py::commander_contract_regression`. No behavioural validation against a live model — the automated suite never runs an LLM. |
| 507 | Major operations have explicit objectives | ✅ | 🟡 | `ai/model_server.py` `SYSTEM_PROMPT` (lines 70–140); engine backstops in `CommandValidator`/`CoalitionOrderArbiter` | `ai/selfcheck.py::commander_contract_regression` (prompt substring contract only) | Expressed as an instruction in `SYSTEM_PROMPT` and verified only by substring assertion in `ai/selfcheck.py::commander_contract_regression`. No behavioural validation against a live model — the automated suite never runs an LLM. |
| 508 | Major operations have launch conditions | ✅ | 🟡 | `ai/model_server.py` `SYSTEM_PROMPT` (lines 70–140); engine backstops in `CommandValidator`/`CoalitionOrderArbiter` | `ai/selfcheck.py::commander_contract_regression` (prompt substring contract only) | Expressed as an instruction in `SYSTEM_PROMPT` and verified only by substring assertion in `ai/selfcheck.py::commander_contract_regression`. No behavioural validation against a live model — the automated suite never runs an LLM. |
| 509 | Major operations have abort conditions | ✅ | 🟡 | `ai/model_server.py` `SYSTEM_PROMPT` (lines 70–140); engine backstops in `CommandValidator`/`CoalitionOrderArbiter` | `ai/selfcheck.py::commander_contract_regression` (prompt substring contract only) | Expressed as an instruction in `SYSTEM_PROMPT` and verified only by substring assertion in `ai/selfcheck.py::commander_contract_regression`. No behavioural validation against a live model — the automated suite never runs an LLM. |
| 510 | Major operations have contingencies | ✅ | 🟡 | `ai/model_server.py` `SYSTEM_PROMPT` (lines 70–140); engine backstops in `CommandValidator`/`CoalitionOrderArbiter` | `ai/selfcheck.py::commander_contract_regression` (prompt substring contract only) | Expressed as an instruction in `SYSTEM_PROMPT` and verified only by substring assertion in `ai/selfcheck.py::commander_contract_regression`. No behavioural validation against a live model — the automated suite never runs an LLM. |
| 511 | Major operations consider reserves | ✅ | 🟡 | `ai/model_server.py` `SYSTEM_PROMPT` (lines 70–140); engine backstops in `CommandValidator`/`CoalitionOrderArbiter` | `ai/selfcheck.py::commander_contract_regression` (prompt substring contract only) | Expressed as an instruction in `SYSTEM_PROMPT` and verified only by substring assertion in `ai/selfcheck.py::commander_contract_regression`. No behavioural validation against a live model — the automated suite never runs an LLM. |
| 512 | Major operations consider enemy responses | ✅ | 🟡 | `ai/model_server.py` `SYSTEM_PROMPT` (lines 70–140); engine backstops in `CommandValidator`/`CoalitionOrderArbiter` | `ai/selfcheck.py::commander_contract_regression` (prompt substring contract only) | Expressed as an instruction in `SYSTEM_PROMPT` and verified only by substring assertion in `ai/selfcheck.py::commander_contract_regression`. No behavioural validation against a live model — the automated suite never runs an LLM. |
| 513 | Major operations consider recon requirements | ✅ | 🟡 | `ai/model_server.py` `SYSTEM_PROMPT` (lines 70–140); engine backstops in `CommandValidator`/`CoalitionOrderArbiter` | `ai/selfcheck.py::commander_contract_regression` (prompt substring contract only) | Expressed as an instruction in `SYSTEM_PROMPT` and verified only by substring assertion in `ai/selfcheck.py::commander_contract_regression`. No behavioural validation against a live model — the automated suite never runs an LLM. |
| 514 | Major operations consider deception opportunities | ✅ | 🟡 | `ai/model_server.py` `SYSTEM_PROMPT` (lines 70–140); engine backstops in `CommandValidator`/`CoalitionOrderArbiter` | `ai/selfcheck.py::commander_contract_regression` (prompt substring contract only) | Expressed as an instruction in `SYSTEM_PROMPT` and verified only by substring assertion in `ai/selfcheck.py::commander_contract_regression`. No behavioural validation against a live model — the automated suite never runs an LLM. |
| 515 | Major operations consider combined arms | ✅ | 🟡 | `ai/model_server.py` `SYSTEM_PROMPT` (lines 70–140); engine backstops in `CommandValidator`/`CoalitionOrderArbiter` | `ai/selfcheck.py::commander_contract_regression` (prompt substring contract only) | Expressed as an instruction in `SYSTEM_PROMPT` and verified only by substring assertion in `ai/selfcheck.py::commander_contract_regression`. No behavioural validation against a live model — the automated suite never runs an LLM. |
| 516 | Major operations consider withdrawal/extraction | ✅ | 🟡 | `ai/model_server.py` `SYSTEM_PROMPT` (lines 70–140); engine backstops in `CommandValidator`/`CoalitionOrderArbiter` | `ai/selfcheck.py::commander_contract_regression` (prompt substring contract only) | Expressed as an instruction in `SYSTEM_PROMPT` and verified only by substring assertion in `ai/selfcheck.py::commander_contract_regression`. No behavioural validation against a live model — the automated suite never runs an LLM. |
| 517 | LLM adapts when assumptions become invalid | ✅ | 🟡 | `ai/model_server.py` `SYSTEM_PROMPT` (lines 70–140); engine backstops in `CommandValidator`/`CoalitionOrderArbiter` | `ai/selfcheck.py::commander_contract_regression` (prompt substring contract only) | Expressed as an instruction in `SYSTEM_PROMPT` and verified only by substring assertion in `ai/selfcheck.py::commander_contract_regression`. No behavioural validation against a live model — the automated suite never runs an LLM. |
| 518 | LLM doesn't constantly rewrite valid plans | ✅ | 🟡 | `ai/model_server.py` `SYSTEM_PROMPT` (lines 70–140); engine backstops in `CommandValidator`/`CoalitionOrderArbiter` | `ai/selfcheck.py::commander_contract_regression` (prompt substring contract only) | Expressed as an instruction in `SYSTEM_PROMPT` and verified only by substring assertion in `ai/selfcheck.py::commander_contract_regression`. No behavioural validation against a live model — the automated suite never runs an LLM. |
| 519 | LLM accepts strategically justified losses | ✅ | 🟡 | `ai/model_server.py` `SYSTEM_PROMPT` (lines 70–140); engine backstops in `CommandValidator`/`CoalitionOrderArbiter` | `ai/selfcheck.py::commander_contract_regression` (prompt substring contract only) | Expressed as an instruction in `SYSTEM_PROMPT` and verified only by substring assertion in `ai/selfcheck.py::commander_contract_regression`. No behavioural validation against a live model — the automated suite never runs an LLM. |
| 520 | LLM avoids strategically pointless losses | ✅ | 🟡 | `ai/model_server.py` `SYSTEM_PROMPT` (lines 70–140); engine backstops in `CommandValidator`/`CoalitionOrderArbiter` | `ai/selfcheck.py::commander_contract_regression` (prompt substring contract only) | Expressed as an instruction in `SYSTEM_PROMPT` and verified only by substring assertion in `ai/selfcheck.py::commander_contract_regression`. No behavioural validation against a live model — the automated suite never runs an LLM. |
| 521 | LLM exploits major enemy mistakes rapidly | ✅ | 🟡 | `ai/model_server.py` `SYSTEM_PROMPT` (lines 70–140); engine backstops in `CommandValidator`/`CoalitionOrderArbiter` | `ai/selfcheck.py::commander_contract_regression` (prompt substring contract only) | Expressed as an instruction in `SYSTEM_PROMPT` and verified only by substring assertion in `ai/selfcheck.py::commander_contract_regression`. No behavioural validation against a live model — the automated suite never runs an LLM. |

### §Event-Driven Strategic Reasoning  <span>(522–539)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 522 | LLM not called every game tick | ✅ | ✅ | `StrategicEventDetector`, `CoalitionCommandCenterBotModule.ReviewTrigger()` (debounced to `BlackboardInterval`) | `StrategicEventDetectorTest`, `HeadlessSkirmishTest` | — |
| 523 | Periodic strategic review exists | ✅ | ✅ | `StrategicEventDetector`, `CoalitionCommandCenterBotModule.ReviewTrigger()` (debounced to `BlackboardInterval`) | `StrategicEventDetectorTest`, `HeadlessSkirmishTest` | — |
| 524 | Enemy base discovery triggers review | ✅ | ✅ | `StrategicEventDetector`, `CoalitionCommandCenterBotModule.ReviewTrigger()` (debounced to `BlackboardInterval`) | `StrategicEventDetectorTest`, `HeadlessSkirmishTest` | — |
| 525 | Enemy expansion discovery triggers review | ✅ | ✅ | `StrategicEventDetector`, `CoalitionCommandCenterBotModule.ReviewTrigger()` (debounced to `BlackboardInterval`) | `StrategicEventDetectorTest`, `HeadlessSkirmishTest` | — |
| 526 | Major enemy composition changes trigger review | ✅ | ✅ | `StrategicEventDetector`, `CoalitionCommandCenterBotModule.ReviewTrigger()` (debounced to `BlackboardInterval`) | `StrategicEventDetectorTest`, `HeadlessSkirmishTest` | — |
| 527 | Major allied attack start triggers review | ✅ | ✅ | `StrategicEventDetector`, `CoalitionCommandCenterBotModule.ReviewTrigger()` (debounced to `BlackboardInterval`) | `StrategicEventDetectorTest`, `HeadlessSkirmishTest` | — |
| 528 | Major attack failure triggers review | ✅ | ✅ | `StrategicEventDetector`, `CoalitionCommandCenterBotModule.ReviewTrigger()` (debounced to `BlackboardInterval`) | `StrategicEventDetectorTest`, `HeadlessSkirmishTest` | — |
| 529 | Important allied base attack triggers review | ✅ | ✅ | `StrategicEventDetector`, `CoalitionCommandCenterBotModule.ReviewTrigger()` (debounced to `BlackboardInterval`) | `StrategicEventDetectorTest`, `HeadlessSkirmishTest` | — |
| 530 | Critical production-building loss triggers review | ✅ | ✅ | `StrategicEventDetector`, `CoalitionCommandCenterBotModule.ReviewTrigger()` (debounced to `BlackboardInterval`) | `StrategicEventDetectorTest`, `HeadlessSkirmishTest` | — |
| 531 | Special-unit availability triggers review | ✅ | ✅ | `StrategicEventDetector`, `CoalitionCommandCenterBotModule.ReviewTrigger()` (debounced to `BlackboardInterval`) | `StrategicEventDetectorTest`, `HeadlessSkirmishTest` | — |
| 532 | Transport readiness triggers review | ✅ | ✅ | `StrategicEventDetector`, `CoalitionCommandCenterBotModule.ReviewTrigger()` (debounced to `BlackboardInterval`) | `StrategicEventDetectorTest`, `HeadlessSkirmishTest` | — |
| 533 | Mission completion triggers review | ✅ | ✅ | `StrategicEventDetector`, `CoalitionCommandCenterBotModule.ReviewTrigger()` (debounced to `BlackboardInterval`) | `StrategicEventDetectorTest`, `HeadlessSkirmishTest` | — |
| 534 | Major route/bridge loss triggers review | ✅ | ✅ | `StrategicEventDetector`, `CoalitionCommandCenterBotModule.ReviewTrigger()` (debounced to `BlackboardInterval`) | `StrategicEventDetectorTest`, `HeadlessSkirmishTest` | — |
| 535 | Major economy change triggers review | ✅ | ✅ | `StrategicEventDetector`, `CoalitionCommandCenterBotModule.ReviewTrigger()` (debounced to `BlackboardInterval`) | `StrategicEventDetectorTest`, `HeadlessSkirmishTest` | — |
| 536 | Support-power readiness triggers review | ✅ | ✅ | `StrategicEventDetector`, `CoalitionCommandCenterBotModule.ReviewTrigger()` (debounced to `BlackboardInterval`) | `StrategicEventDetectorTest`, `HeadlessSkirmishTest` | — |
| 537 | High-value enemy structure discovery triggers review | ✅ | ✅ | `StrategicEventDetector`, `CoalitionCommandCenterBotModule.ReviewTrigger()` (debounced to `BlackboardInterval`) | `StrategicEventDetectorTest`, `HeadlessSkirmishTest` | — |
| 538 | Loss of contact with enemy army triggers review | ✅ | ✅ | `StrategicEventDetector`, `CoalitionCommandCenterBotModule.ReviewTrigger()` (debounced to `BlackboardInterval`) | `StrategicEventDetectorTest`, `HeadlessSkirmishTest` | — |
| 539 | Event storms throttled/debounced | ✅ | ✅ | `StrategicEventDetector`, `CoalitionCommandCenterBotModule.ReviewTrigger()` (debounced to `BlackboardInterval`) | `StrategicEventDetectorTest`, `HeadlessSkirmishTest` | — |

### §Tactical Execution Layer  <span>(540–555)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 540 | Ground controller exists | ✅ | ✅ | `TacticalController` base + `Ground`/`Air`/`Naval`/`Transport`/`SpecialOps` controllers; `TacticalFormation`, `TacticalEngagement` | `TacticalFormationTest`, `TransportStateMachineTest`, `HeadlessSkirmishTest` | — |
| 541 | Air controller exists | ✅ | ✅ | `TacticalController` base + `Ground`/`Air`/`Naval`/`Transport`/`SpecialOps` controllers; `TacticalFormation`, `TacticalEngagement` | `TacticalFormationTest`, `TransportStateMachineTest`, `HeadlessSkirmishTest` | — |
| 542 | Naval controller exists | ✅ | ✅ | `TacticalController` base + `Ground`/`Air`/`Naval`/`Transport`/`SpecialOps` controllers; `TacticalFormation`, `TacticalEngagement` | `TacticalFormationTest`, `TransportStateMachineTest`, `HeadlessSkirmishTest` | — |
| 543 | Transport controller exists | ✅ | ✅ | `TacticalController` base + `Ground`/`Air`/`Naval`/`Transport`/`SpecialOps` controllers; `TacticalFormation`, `TacticalEngagement` | `TacticalFormationTest`, `TransportStateMachineTest`, `HeadlessSkirmishTest` | — |
| 544 | Special-operations controller exists | ✅ | ✅ | `TacticalController` base + `Ground`/`Air`/`Naval`/`Transport`/`SpecialOps` controllers; `TacticalFormation`, `TacticalEngagement` | `TacticalFormationTest`, `TransportStateMachineTest`, `HeadlessSkirmishTest` | — |
| 545 | Formation/cohesion controller exists | ✅ | ✅ | `TacticalController` base + `Ground`/`Air`/`Naval`/`Transport`/`SpecialOps` controllers; `TacticalFormation`, `TacticalEngagement` | `TacticalFormationTest`, `TransportStateMachineTest`, `HeadlessSkirmishTest` | — |
| 546 | Micro/targeting controller exists | ✅ | ✅ | `TacticalController` base + `Ground`/`Air`/`Naval`/`Transport`/`SpecialOps` controllers; `TacticalFormation`, `TacticalEngagement` | `TacticalFormationTest`, `TransportStateMachineTest`, `HeadlessSkirmishTest` | — |
| 547 | Controllers execute without repeated LLM intervention | ✅ | ✅ | `TacticalController` base + `Ground`/`Air`/`Naval`/`Transport`/`SpecialOps` controllers; `TacticalFormation`, `TacticalEngagement` | `TacticalFormationTest`, `TransportStateMachineTest`, `HeadlessSkirmishTest` | — |
| 548 | Controllers can report inability | ✅ | ❌ | `TacticalController` base + `Ground`/`Air`/`Naval`/`Transport`/`SpecialOps` controllers; `TacticalFormation`, `TacticalEngagement` | none (indirect only, via headless matches) | Implemented and fully wired: `TacticalController.Unable(reason, requestReplan)` is raised from 8 sites across all five controllers and sets `FailureReason`/`NeedsReplan`. **No test references `Unable`, `FailureReason` or `NeedsReplan`.** |
| 549 | Controllers can request replanning | ✅ | ❌ | `TacticalController` base + `Ground`/`Air`/`Naval`/`Transport`/`SpecialOps` controllers; `TacticalFormation`, `TacticalEngagement` | none (indirect only, via headless matches) | `Unable(..., true)` → `StrategicBrainBotModule.RequestStrategicReplan` → `CoalitionCommandCenterBotModule.RequestReplan`. The path is real, but untested end-to-end. |
| 550 | Controllers handle pathfinding at engine speed | ✅ | ✅ | `TacticalController` base + `Ground`/`Air`/`Naval`/`Transport`/`SpecialOps` controllers; `TacticalFormation`, `TacticalEngagement` | `TacticalFormationTest`, `TransportStateMachineTest`, `HeadlessSkirmishTest` | — |
| 551 | Controllers handle target acquisition at engine speed | ✅ | ✅ | `TacticalController` base + `Ground`/`Air`/`Naval`/`Transport`/`SpecialOps` controllers; `TacticalFormation`, `TacticalEngagement` | `TacticalFormationTest`, `TransportStateMachineTest`, `HeadlessSkirmishTest` | — |
| 552 | Controllers handle formation movement at engine speed | ✅ | ✅ | `TacticalController` base + `Ground`/`Air`/`Naval`/`Transport`/`SpecialOps` controllers; `TacticalFormation`, `TacticalEngagement` | `TacticalFormationTest`, `TransportStateMachineTest`, `HeadlessSkirmishTest` | — |
| 553 | Controllers handle transport loading/unloading reliably | ✅ | ✅ | `TacticalController` base + `Ground`/`Air`/`Naval`/`Transport`/`SpecialOps` controllers; `TacticalFormation`, `TacticalEngagement` | `TacticalFormationTest`, `TransportStateMachineTest`, `HeadlessSkirmishTest` | — |
| 554 | Controllers preserve mission-critical assets | ✅ | ✅ | `TacticalController` base + `Ground`/`Air`/`Naval`/`Transport`/`SpecialOps` controllers; `TacticalFormation`, `TacticalEngagement` | `TacticalFormationTest`, `TransportStateMachineTest`, `HeadlessSkirmishTest` | — |
| 555 | Controllers avoid obvious suicide behavior | ✅ | ✅ | `TacticalController` base + `Ground`/`Air`/`Naval`/`Transport`/`SpecialOps` controllers; `TacticalFormation`, `TacticalEngagement` | `TacticalFormationTest`, `TransportStateMachineTest`, `HeadlessSkirmishTest` | — |

### §Formation & Force Cohesion  <span>(556–564)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 556 | Assault groups avoid arriving as isolated units | ✅ | ✅ | `TacticalFormation` (`ArtilleryPullbackTarget`, `IsAheadOfCenter`, `ProjectBeyondContact`), `ForceGroup.Cohesion` | `TacticalFormationTest`, `MatchMetricsTest` | — |
| 557 | Artillery remains behind screening forces | ✅ | ✅ | `TacticalFormation` (`ArtilleryPullbackTarget`, `IsAheadOfCenter`, `ProjectBeyondContact`), `ForceGroup.Cohesion` | `TacticalFormationTest`, `MatchMetricsTest` | — |
| 558 | AA units within useful coverage of valuable forces | ✅ | ✅ | `TacticalFormation` (`ArtilleryPullbackTarget`, `IsAheadOfCenter`, `ProjectBeyondContact`), `ForceGroup.Cohesion` | `TacticalFormationTest`, `MatchMetricsTest` | — |
| 559 | Fast units do not outrun required support | ✅ | ✅ | `TacticalFormation` (`ArtilleryPullbackTarget`, `IsAheadOfCenter`, `ProjectBeyondContact`), `ForceGroup.Cohesion` | `TacticalFormationTest`, `MatchMetricsTest` | — |
| 560 | Groups pause/regroup when cohesion falls too low | ✅ | ✅ | `TacticalFormation` (`ArtilleryPullbackTarget`, `IsAheadOfCenter`, `ProjectBeyondContact`), `ForceGroup.Cohesion` | `TacticalFormationTest`, `MatchMetricsTest` | — |
| 561 | Retreating forces maintain withdrawal behavior | ✅ | ✅ | `TacticalFormation` (`ArtilleryPullbackTarget`, `IsAheadOfCenter`, `ProjectBeyondContact`), `ForceGroup.Cohesion` | `TacticalFormationTest`, `MatchMetricsTest` | — |
| 562 | Formation/cohesion thresholds configurable | ✅ | ✅ | `TacticalFormation` (`ArtilleryPullbackTarget`, `IsAheadOfCenter`, `ProjectBeyondContact`), `ForceGroup.Cohesion` | `TacticalFormationTest`, `MatchMetricsTest` | — |
| 563 | Force cohesion included in readiness calculations | ✅ | ✅ | `TacticalFormation` (`ArtilleryPullbackTarget`, `IsAheadOfCenter`, `ProjectBeyondContact`), `ForceGroup.Cohesion` | `TacticalFormationTest`, `MatchMetricsTest` | — |
| 564 | Force cohesion logged in telemetry | ✅ | ✅ | `TacticalFormation` (`ArtilleryPullbackTarget`, `IsAheadOfCenter`, `ProjectBeyondContact`), `ForceGroup.Cohesion` | `TacticalFormationTest`, `MatchMetricsTest` | — |

### §Support Powers  <span>(565–571)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 565 | Support-power readiness visible to strategic command | ✅ | ✅ | `SupportPowerPolicy` (`Classify`/`ShouldFire`), `SupportPowerBotModule`, `MissionType.SupportPowerStrike` | `SupportPowerTest` | — |
| 566 | Support powers integrated into mission planning | ✅ | ✅ | `SupportPowerPolicy` (`Classify`/`ShouldFire`), `SupportPowerBotModule`, `MissionType.SupportPowerStrike` | `SupportPowerTest` | — |
| 567 | Support powers participate in shaping attacks | ✅ | ✅ | `SupportPowerPolicy` (`Classify`/`ShouldFire`), `SupportPowerBotModule`, `MissionType.SupportPowerStrike` | `SupportPowerTest` | — |
| 568 | Target scoring avoids unacceptable friendly-fire | ✅ | ✅ | `SupportPowerPolicy` (`Classify`/`ShouldFire`), `SupportPowerBotModule`, `MissionType.SupportPowerStrike` | `SupportPowerTest` | — |
| 569 | Support powers synchronized with attacks | ✅ | ✅ | `SupportPowerPolicy` (`Classify`/`ShouldFire`), `SupportPowerBotModule`, `MissionType.SupportPowerStrike` | `SupportPowerTest` | — |
| 570 | AI avoids wasting powers on low-value targets | ✅ | ✅ | `SupportPowerPolicy` (`Classify`/`ShouldFire`), `SupportPowerBotModule`, `MissionType.SupportPowerStrike` | `SupportPowerTest` | — |
| 571 | Support-power behavior covered by tests per RA power | 🟡 | ✅ | `SupportPowerPolicy` (`Classify`/`ShouldFire`), `SupportPowerBotModule`, `MissionType.SupportPowerStrike` | `SupportPowerTest` | Only 4 of RA's 6 support powers are supported: `SupportPowerPolicy.Classify` maps SpyPlane/Paratroopers/Parabombs/Nuke; **Chronoshift, AdvancedChronoshift and Iron Curtain return `Unsupported` and are never fired**. The limitation is deliberate and asserted in `SupportPowerTest.RaPowerClassification`, but two RA powers are unimplemented. |

### §Failure Handling & Fallback  <span>(572–585)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 572 | LLM timeouts do not freeze game AI | ✅ | ✅ | `ExternalBrainBotModule` (120 s timeout, catch-all → scripted brain), `CommandValidator.IsStale`, `StrategicBrainBotModule` deterministic commander | `CommandValidatorTest`, `HeadlessSkirmishTest.DeterministicFallback` | — |
| 573 | LLM API failures do not freeze active missions | ✅ | ✅ | `ExternalBrainBotModule` (120 s timeout, catch-all → scripted brain), `CommandValidator.IsStale`, `StrategicBrainBotModule` deterministic commander | `CommandValidatorTest`, `HeadlessSkirmishTest.DeterministicFallback` | — |
| 574 | Invalid LLM JSON/tool output safely rejected | ✅ | ✅ | `ExternalBrainBotModule` (120 s timeout, catch-all → scripted brain), `CommandValidator.IsStale`, `StrategicBrainBotModule` deterministic commander | `CommandValidatorTest`, `HeadlessSkirmishTest.DeterministicFallback` | — |
| 575 | Hallucinated force IDs rejected | ✅ | ✅ | `ExternalBrainBotModule` (120 s timeout, catch-all → scripted brain), `CommandValidator.IsStale`, `StrategicBrainBotModule` deterministic commander | `CommandValidatorTest`, `HeadlessSkirmishTest.DeterministicFallback` | — |
| 576 | Hallucinated targets rejected | ✅ | ✅ | `ExternalBrainBotModule` (120 s timeout, catch-all → scripted brain), `CommandValidator.IsStale`, `StrategicBrainBotModule` deterministic commander | `CommandValidatorTest`, `HeadlessSkirmishTest.DeterministicFallback` | — |
| 577 | Impossible routes rejected | ✅ | ✅ | `ExternalBrainBotModule` (120 s timeout, catch-all → scripted brain), `CommandValidator.IsStale`, `StrategicBrainBotModule` deterministic commander | `CommandValidatorTest`, `HeadlessSkirmishTest.DeterministicFallback` | — |
| 578 | Impossible production orders rejected | ✅ | ✅ | `ExternalBrainBotModule` (120 s timeout, catch-all → scripted brain), `CommandValidator.IsStale`, `StrategicBrainBotModule` deterministic commander | `CommandValidatorTest`, `HeadlessSkirmishTest.DeterministicFallback` | — |
| 579 | Existing valid missions continue during LLM failure | ✅ | ✅ | `ExternalBrainBotModule` (120 s timeout, catch-all → scripted brain), `CommandValidator.IsStale`, `StrategicBrainBotModule` deterministic commander | `CommandValidatorTest`, `HeadlessSkirmishTest.DeterministicFallback` | — |
| 580 | Deterministic strategic fallback activates | ✅ | ✅ | `ExternalBrainBotModule` (120 s timeout, catch-all → scripted brain), `CommandValidator.IsStale`, `StrategicBrainBotModule` deterministic commander | `CommandValidatorTest`, `HeadlessSkirmishTest.DeterministicFallback` | — |
| 581 | Fallback can defend the coalition | ✅ | ✅ | `ExternalBrainBotModule` (120 s timeout, catch-all → scripted brain), `CommandValidator.IsStale`, `StrategicBrainBotModule` deterministic commander | `CommandValidatorTest`, `HeadlessSkirmishTest.DeterministicFallback` | — |
| 582 | Fallback can continue production | ✅ | ✅ | `ExternalBrainBotModule` (120 s timeout, catch-all → scripted brain), `CommandValidator.IsStale`, `StrategicBrainBotModule` deterministic commander | `CommandValidatorTest`, `HeadlessSkirmishTest.DeterministicFallback` | — |
| 583 | Fallback can create basic attacks/counterattacks | ✅ | ✅ | `ExternalBrainBotModule` (120 s timeout, catch-all → scripted brain), `CommandValidator.IsStale`, `StrategicBrainBotModule` deterministic commander | `CommandValidatorTest`, `HeadlessSkirmishTest.DeterministicFallback` | — |
| 584 | Recovery from fallback to LLM command is safe | ✅ | ✅ | `ExternalBrainBotModule` (120 s timeout, catch-all → scripted brain), `CommandValidator.IsStale`, `StrategicBrainBotModule` deterministic commander | `CommandValidatorTest`, `HeadlessSkirmishTest.DeterministicFallback` | — |
| 585 | Failure cases covered by automated tests | ✅ | ✅ | `ExternalBrainBotModule` (120 s timeout, catch-all → scripted brain), `CommandValidator.IsStale`, `StrategicBrainBotModule` deterministic commander | `CommandValidatorTest`, `HeadlessSkirmishTest.DeterministicFallback` | — |

### §Strategic Decision Logging  <span>(586–598)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 586 | Every major LLM strategic decision logged | ✅ | ✅ | `CoalitionTelemetry`, `ai/brain.log` (prompt/tool-trace/reply/plan), `format_tool_trace` | `ai/selfcheck.py` (lossless tool trace), `LlmEvalTest` | — |
| 587 | Strategic-posture changes logged | ✅ | ✅ | `CoalitionTelemetry`, `ai/brain.log` (prompt/tool-trace/reply/plan), `format_tool_trace` | `ai/selfcheck.py` (lossless tool trace), `LlmEvalTest` | — |
| 588 | Mission creation logged | ✅ | ✅ | `CoalitionTelemetry`, `ai/brain.log` (prompt/tool-trace/reply/plan), `format_tool_trace` | `ai/selfcheck.py` (lossless tool trace), `LlmEvalTest` | — |
| 589 | Mission cancellation logged | ✅ | ✅ | `CoalitionTelemetry`, `ai/brain.log` (prompt/tool-trace/reply/plan), `format_tool_trace` | `ai/selfcheck.py` (lossless tool trace), `LlmEvalTest` | — |
| 590 | Mission failure reasons logged | ✅ | ✅ | `CoalitionTelemetry`, `ai/brain.log` (prompt/tool-trace/reply/plan), `format_tool_trace` | `ai/selfcheck.py` (lossless tool trace), `LlmEvalTest` | — |
| 591 | Production-priority changes logged | ✅ | ✅ | `CoalitionTelemetry`, `ai/brain.log` (prompt/tool-trace/reply/plan), `format_tool_trace` | `ai/selfcheck.py` (lossless tool trace), `LlmEvalTest` | — |
| 592 | Reserve commitments logged | ✅ | ✅ | `CoalitionTelemetry`, `ai/brain.log` (prompt/tool-trace/reply/plan), `format_tool_trace` | `ai/selfcheck.py` (lossless tool trace), `LlmEvalTest` | — |
| 593 | Intelligence discoveries affecting plans logged | ✅ | ✅ | `CoalitionTelemetry`, `ai/brain.log` (prompt/tool-trace/reply/plan), `format_tool_trace` | `ai/selfcheck.py` (lossless tool trace), `LlmEvalTest` | — |
| 594 | Significant opponent-model changes logged | ✅ | ✅ | `CoalitionTelemetry`, `ai/brain.log` (prompt/tool-trace/reply/plan), `format_tool_trace` | `ai/selfcheck.py` (lossless tool trace), `LlmEvalTest` | — |
| 595 | Combat-estimator results for major decisions logged | ✅ | ✅ | `CoalitionTelemetry`, `ai/brain.log` (prompt/tool-trace/reply/plan), `format_tool_trace` | `ai/selfcheck.py` (lossless tool trace), `LlmEvalTest` | — |
| 596 | LLM tool calls reconstructable from logs | ✅ | ✅ | `CoalitionTelemetry`, `ai/brain.log` (prompt/tool-trace/reply/plan), `format_tool_trace` | `ai/selfcheck.py` (lossless tool trace), `LlmEvalTest` | — |
| 597 | LLM responses correlated with game timestamps | ✅ | ✅ | `CoalitionTelemetry`, `ai/brain.log` (prompt/tool-trace/reply/plan), `format_tool_trace` | `ai/selfcheck.py` (lossless tool trace), `LlmEvalTest` | — |
| 598 | Logs usable for debugging bad decisions | ✅ | ✅ | `CoalitionTelemetry`, `ai/brain.log` (prompt/tool-trace/reply/plan), `format_tool_trace` | `ai/selfcheck.py` (lossless tool trace), `LlmEvalTest` | — |

### §Telemetry & Quality Metrics  <span>(599–622)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 599 | Win/loss recorded | ✅ | ✅ | `CoalitionMatchMetrics.Summary()`, `MissionManager.MissionSummary()` | `MatchMetricsTest`, `ExpandedCoverageTest.MatchMetrics*` | — |
| 600 | Match duration recorded | ✅ | ✅ | `CoalitionMatchMetrics.Summary()`, `MissionManager.MissionSummary()` | `MatchMetricsTest`, `ExpandedCoverageTest.MatchMetrics*` | — |
| 601 | Friendly combat value lost recorded | ✅ | ✅ | `CoalitionMatchMetrics.Summary()`, `MissionManager.MissionSummary()` | `MatchMetricsTest`, `ExpandedCoverageTest.MatchMetrics*` | — |
| 602 | Enemy combat value destroyed recorded | ✅ | ✅ | `CoalitionMatchMetrics.Summary()`, `MissionManager.MissionSummary()` | `MatchMetricsTest`, `ExpandedCoverageTest.MatchMetrics*` | — |
| 603 | Exchange ratio recorded | ✅ | ✅ | `CoalitionMatchMetrics.Summary()`, `MissionManager.MissionSummary()` | `MatchMetricsTest`, `ExpandedCoverageTest.MatchMetrics*` | — |
| 604 | Economic damage caused recorded | 🟡 | ✅ | `CoalitionMatchMetrics.Summary()`, `MissionManager.MissionSummary()` | `MatchMetricsTest`, `ExpandedCoverageTest.MatchMetrics*` | Economic damage is counted as **refinery losses** (`FriendlyRefineryLosses`/`EnemyRefineryLosses`), not destroyed economic *value*. Coarse proxy. |
| 605 | Economic damage suffered recorded | 🟡 | ✅ | `CoalitionMatchMetrics.Summary()`, `MissionManager.MissionSummary()` | `MatchMetricsTest`, `ExpandedCoverageTest.MatchMetrics*` | Same proxy as 604 — refinery counts, not credits/value denied. |
| 606 | Production idle time recorded | ✅ | ✅ | `CoalitionMatchMetrics.Summary()`, `MissionManager.MissionSummary()` | `MatchMetricsTest`, `ExpandedCoverageTest.MatchMetrics*` | — |
| 607 | Excess resource floating recorded | ✅ | ✅ | `CoalitionMatchMetrics.Summary()`, `MissionManager.MissionSummary()` | `MatchMetricsTest`, `ExpandedCoverageTest.MatchMetrics*` | — |
| 608 | Expansion timing recorded | ✅ | ✅ | `CoalitionMatchMetrics.Summary()`, `MissionManager.MissionSummary()` | `MatchMetricsTest`, `ExpandedCoverageTest.MatchMetrics*` | — |
| 609 | Army idle time recorded | ✅ | ✅ | `CoalitionMatchMetrics.Summary()`, `MissionManager.MissionSummary()` | `MatchMetricsTest`, `ExpandedCoverageTest.MatchMetrics*` | — |
| 610 | Force cohesion recorded | ✅ | ✅ | `CoalitionMatchMetrics.Summary()`, `MissionManager.MissionSummary()` | `MatchMetricsTest`, `ExpandedCoverageTest.MatchMetrics*` | — |
| 611 | Mission success rate recorded | ✅ | ✅ | `CoalitionMatchMetrics.Summary()`, `MissionManager.MissionSummary()` | `MatchMetricsTest`, `ExpandedCoverageTest.MatchMetrics*` | — |
| 612 | Synchronization error recorded | ✅ | ✅ | `CoalitionMatchMetrics.Summary()`, `MissionManager.MissionSummary()` | `MatchMetricsTest`, `ExpandedCoverageTest.MatchMetrics*` | — |
| 613 | Local combat superiority at engagement recorded | ✅ | ✅ | `CoalitionMatchMetrics.Summary()`, `MissionManager.MissionSummary()` | `MatchMetricsTest`, `ExpandedCoverageTest.MatchMetrics*` | — |
| 614 | Retreat timing/effectiveness recorded | ✅ | ✅ | `CoalitionMatchMetrics.Summary()`, `MissionManager.MissionSummary()` | `MatchMetricsTest`, `ExpandedCoverageTest.MatchMetrics*` | — |
| 615 | Strategic reserve availability recorded | ✅ | ✅ | `CoalitionMatchMetrics.Summary()`, `MissionManager.MissionSummary()` | `MatchMetricsTest`, `ExpandedCoverageTest.MatchMetrics*` | — |
| 616 | Recon efficiency recorded | ✅ | ✅ | `CoalitionMatchMetrics.Summary()`, `MissionManager.MissionSummary()` | `MatchMetricsTest`, `ExpandedCoverageTest.MatchMetrics*` | — |
| 617 | Transport survival recorded | ✅ | ✅ | `CoalitionMatchMetrics.Summary()`, `MissionManager.MissionSummary()` | `MatchMetricsTest`, `ExpandedCoverageTest.MatchMetrics*` | — |
| 618 | Tanya/Spy/special-op success recorded | ✅ | ✅ | `CoalitionMatchMetrics.Summary()`, `MissionManager.MissionSummary()` | `MatchMetricsTest`, `ExpandedCoverageTest.MatchMetrics*` | — |
| 619 | Feint effectiveness recorded | ✅ | ✅ | `CoalitionMatchMetrics.Summary()`, `MissionManager.MissionSummary()` | `MatchMetricsTest`, `ExpandedCoverageTest.MatchMetrics*` | — |
| 620 | Counterattack effectiveness recorded | ✅ | ✅ | `CoalitionMatchMetrics.Summary()`, `MissionManager.MissionSummary()` | `MatchMetricsTest`, `ExpandedCoverageTest.MatchMetrics*` | — |
| 621 | Base-defense response time recorded | ✅ | ✅ | `CoalitionMatchMetrics.Summary()`, `MissionManager.MissionSummary()` | `MatchMetricsTest`, `ExpandedCoverageTest.MatchMetrics*` | — |
| 622 | Opponent-model prediction accuracy recorded | 🟡 | 🟡 | `CoalitionMatchMetrics.Summary()`, `MissionManager.MissionSummary()` | `MatchMetricsTest`, `ExpandedCoverageTest.MatchMetrics*` | `LastWinRatioEstimate` vs result measures **combat-estimator** prediction accuracy, not `OpponentModel` prediction accuracy. No per-prediction scoring of the opponent model's own forecasts. |

### §Deception Metrics  <span>(623–628)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 623 | Feint effectiveness measures enemy value redeployed | ✅ | ✅ | `MissionManager` deception counters, `DeceptionEnemiesDrawn`, `RecordFeintOpenedWindow` | `DeceptionTest`, `ExpandedCoverageTest` | — |
| 624 | Feint compares enemy reaction vs friendly value risked | ✅ | ✅ | `MissionManager` deception counters, `DeceptionEnemiesDrawn`, `RecordFeintOpenedWindow` | `DeceptionTest`, `ExpandedCoverageTest` | — |
| 625 | AI distinguishes distraction from tactical success | ✅ | ✅ | `MissionManager` deception counters, `DeceptionEnemiesDrawn`, `RecordFeintOpenedWindow` | `DeceptionTest`, `ExpandedCoverageTest` | — |
| 626 | Enemy reaction to bait operations recorded | ✅ | ✅ | `MissionManager` deception counters, `DeceptionEnemiesDrawn`, `RecordFeintOpenedWindow` | `DeceptionTest`, `ExpandedCoverageTest` | — |
| 627 | Whether feint opened main-attack window recorded | ✅ | ✅ | `MissionManager` deception counters, `DeceptionEnemiesDrawn`, `RecordFeintOpenedWindow` | `DeceptionTest`, `ExpandedCoverageTest` | — |
| 628 | Repeatedly ineffective deception deprioritized | ✅ | ✅ | `MissionManager` deception counters, `DeceptionEnemiesDrawn`, `RecordFeintOpenedWindow` | `DeceptionTest`, `ExpandedCoverageTest` | — |

### §Difficulty Configuration  <span>(629–639)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 629 | Command-quality configurable independently | ✅ | ✅ | `CoalitionDifficulty`, `mods/ra/rules/ai.yaml` (Difficulty/MicroPrecision/Intelligence/ReserveFraction), `HeadlessSkirmish.CommanderIntelligence` | `DifficultyTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 630 | Reaction speed configurable independently | ✅ | ✅ | `CoalitionDifficulty`, `mods/ra/rules/ai.yaml` (Difficulty/MicroPrecision/Intelligence/ReserveFraction), `HeadlessSkirmish.CommanderIntelligence` | `DifficultyTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 631 | Economic bonus configurable independently | ✅ | ✅ | `CoalitionDifficulty`, `mods/ra/rules/ai.yaml` (Difficulty/MicroPrecision/Intelligence/ReserveFraction), `HeadlessSkirmish.CommanderIntelligence` | `DifficultyTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 632 | Intelligence/fog advantage configurable independently | ✅ | ✅ | `CoalitionDifficulty`, `mods/ra/rules/ai.yaml` (Difficulty/MicroPrecision/Intelligence/ReserveFraction), `HeadlessSkirmish.CommanderIntelligence` | `DifficultyTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 633 | Micro precision configurable independently | ✅ | ✅ | `CoalitionDifficulty`, `mods/ra/rules/ai.yaml` (Difficulty/MicroPrecision/Intelligence/ReserveFraction), `HeadlessSkirmish.CommanderIntelligence` | `DifficultyTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 634 | Coalition coordination strength configurable independently | ✅ | ✅ | `CoalitionDifficulty`, `mods/ra/rules/ai.yaml` (Difficulty/MicroPrecision/Intelligence/ReserveFraction), `HeadlessSkirmish.CommanderIntelligence` | `DifficultyTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 635 | Strong fair mode with zero economic cheating | ✅ | ✅ | `CoalitionDifficulty`, `mods/ra/rules/ai.yaml` (Difficulty/MicroPrecision/Intelligence/ReserveFraction), `HeadlessSkirmish.CommanderIntelligence` | `DifficultyTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 636 | Fair-fog Supreme Command mode exists | ✅ | ✅ | `CoalitionDifficulty`, `mods/ra/rules/ai.yaml` (Difficulty/MicroPrecision/Intelligence/ReserveFraction), `HeadlessSkirmish.CommanderIntelligence` | `DifficultyTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 637 | Optional omniscient mode exists | ✅ | ✅ | `CoalitionDifficulty`, `mods/ra/rules/ai.yaml` (Difficulty/MicroPrecision/Intelligence/ReserveFraction), `HeadlessSkirmish.CommanderIntelligence` | `DifficultyTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 638 | Difficulty settings exposed via YAML/config | ✅ | ✅ | `CoalitionDifficulty`, `mods/ra/rules/ai.yaml` (Difficulty/MicroPrecision/Intelligence/ReserveFraction), `HeadlessSkirmish.CommanderIntelligence` | `DifficultyTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |
| 639 | Difficulty settings verified to change runtime behavior | ✅ | ✅ | `CoalitionDifficulty`, `mods/ra/rules/ai.yaml` (Difficulty/MicroPrecision/Intelligence/ReserveFraction), `HeadlessSkirmish.CommanderIntelligence` | `DifficultyTest`, `HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent` | — |

### §Fair-but-Brutal Target Configuration  <span>(640–645)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 640 | Test config approximating brutal fair AI | ✅ | ✅ | `ai.yaml` (Difficulty 3 / MicroPrecision 3 / Intelligence 0 / 0% bonus), `ai/selfplay.py --intelligence --bot-type --vs` | `AUDIT_REPORT.md` fixed-seed batches; `HeadlessSkirmishTest.MixedBotHeadToHead` | The shipped `ai.yaml` default *is* this configuration (Difficulty 3, MicroPrecision 3, Intelligence 0, no income bonus). |
| 641 | Tested without hidden economic advantages | ✅ | ✅ | `ai.yaml` (Difficulty 3 / MicroPrecision 3 / Intelligence 0 / 0% bonus), `ai/selfplay.py --intelligence --bot-type --vs` | `AUDIT_REPORT.md` fixed-seed batches; `HeadlessSkirmishTest.MixedBotHeadToHead` | Fair preset carries no economic bonus; `DifficultyTest` asserts the axes are independent and 0% is strictly fair. |
| 642 | Tested without hidden enemy-position access | ✅ | ✅ | `ai.yaml` (Difficulty 3 / MicroPrecision 3 / Intelligence 0 / 0% bonus), `ai/selfplay.py --intelligence --bot-type --vs` | `AUDIT_REPORT.md` fixed-seed batches; `HeadlessSkirmishTest.MixedBotHeadToHead` | Backed by `ExternalBrainSnapshotTest.FairFogRejectsInvisible` + `IntelTrackerTest` (no live actor retained). |
| 643 | Performance vs standard bots measured | ✅ | 🟡 | `ai.yaml` (Difficulty 3 / MicroPrecision 3 / Intelligence 0 / 0% bonus), `ai/selfplay.py --intelligence --bot-type --vs` | `AUDIT_REPORT.md` fixed-seed batches; `HeadlessSkirmishTest.MixedBotHeadToHead` | Measured in `AUDIT_REPORT.md` over 3 fixed seeds vs Rush (0W/2L/1D, 1.39 mean exchange) and a 1-seed matrix vs Turtle/Naval/Normal. Small sample; **the AI loses more of these matches than it wins**. |
| 644 | Performance vs multiple allied standard bots measured | ✅ | 🟡 | `ai.yaml` (Difficulty 3 / MicroPrecision 3 / Intelligence 0 / 0% bonus), `ai/selfplay.py --intelligence --bot-type --vs` | `AUDIT_REPORT.md` fixed-seed batches; `HeadlessSkirmishTest.MixedBotHeadToHead` | `--vs` supports multiple scripted opponents, but the recorded matrix is one seed per opponent — not a statistically meaningful multi-bot evaluation. |
| 645 | Performance vs experienced human players evaluated | 🟡 | ❌ | `ai.yaml` (Difficulty 3 / MicroPrecision 3 / Intelligence 0 / 0% bonus), `ai/selfplay.py --intelligence --bot-type --vs` | none — outstanding manual work per `TESTING.md` | No replay ingestion and no recorded human playtest. `TESTING.md` lists this as outstanding manual work. |

### §Automated Testing — Unit Level  <span>(646–662)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 646 | World-state extractor unit tests exist | ✅ | ✅ | subsystems under `Coalition/` are pure/static and directly unit-testable | 30+ NUnit fixtures in `OpenRA.Test/OpenRA.Game/` | — |
| 647 | Visibility/intelligence classification tests exist | ✅ | ✅ | subsystems under `Coalition/` are pure/static and directly unit-testable | 30+ NUnit fixtures in `OpenRA.Test/OpenRA.Game/` | — |
| 648 | Confidence-decay tests exist | ✅ | ✅ | subsystems under `Coalition/` are pure/static and directly unit-testable | 30+ NUnit fixtures in `OpenRA.Test/OpenRA.Game/` | — |
| 649 | Map-region analysis tests exist | ✅ | ✅ | subsystems under `Coalition/` are pure/static and directly unit-testable | 30+ NUnit fixtures in `OpenRA.Test/OpenRA.Game/` | — |
| 650 | Threat-map tests exist | ✅ | ✅ | subsystems under `Coalition/` are pure/static and directly unit-testable | 30+ NUnit fixtures in `OpenRA.Test/OpenRA.Game/` | — |
| 651 | Route-planner tests exist | ✅ | ✅ | subsystems under `Coalition/` are pure/static and directly unit-testable | 30+ NUnit fixtures in `OpenRA.Test/OpenRA.Game/` | — |
| 652 | Combat-estimator tests exist | ✅ | ✅ | subsystems under `Coalition/` are pure/static and directly unit-testable | 30+ NUnit fixtures in `OpenRA.Test/OpenRA.Game/` | — |
| 653 | Target-scoring tests exist | ✅ | ✅ | subsystems under `Coalition/` are pure/static and directly unit-testable | 30+ NUnit fixtures in `OpenRA.Test/OpenRA.Game/` | — |
| 654 | Force-registry tests exist | ✅ | ✅ | subsystems under `Coalition/` are pure/static and directly unit-testable | 30+ NUnit fixtures in `OpenRA.Test/OpenRA.Game/` | — |
| 655 | Order-arbitration tests exist | ✅ | ✅ | subsystems under `Coalition/` are pure/static and directly unit-testable | 30+ NUnit fixtures in `OpenRA.Test/OpenRA.Game/` | — |
| 656 | Mission-state-machine tests exist | ✅ | ✅ | subsystems under `Coalition/` are pure/static and directly unit-testable | 30+ NUnit fixtures in `OpenRA.Test/OpenRA.Game/` | — |
| 657 | Production-directive tests exist | ✅ | ✅ | subsystems under `Coalition/` are pure/static and directly unit-testable | 30+ NUnit fixtures in `OpenRA.Test/OpenRA.Game/` | — |
| 658 | Strategic-reserve tests exist | ✅ | ✅ | subsystems under `Coalition/` are pure/static and directly unit-testable | 30+ NUnit fixtures in `OpenRA.Test/OpenRA.Game/` | — |
| 659 | Opponent-model tests exist | ✅ | ✅ | subsystems under `Coalition/` are pure/static and directly unit-testable | 30+ NUnit fixtures in `OpenRA.Test/OpenRA.Game/` | — |
| 660 | LLM command-validation tests exist | ✅ | ✅ | subsystems under `Coalition/` are pure/static and directly unit-testable | 30+ NUnit fixtures in `OpenRA.Test/OpenRA.Game/` | — |
| 661 | Invalid-tool-input tests exist | ✅ | ✅ | subsystems under `Coalition/` are pure/static and directly unit-testable | 30+ NUnit fixtures in `OpenRA.Test/OpenRA.Game/` | — |
| 662 | Serialization/deserialization tests for LLM schemas | ✅ | ✅ | subsystems under `Coalition/` are pure/static and directly unit-testable | 30+ NUnit fixtures in `OpenRA.Test/OpenRA.Game/` | — |

### §Automated Testing — Mission Scenarios  <span>(663–690)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 663 | Basic coordinated ground attack scenario | ✅ | 🟡 | `MissionType`/`MissionPhase` machinery + `TacticalControllers` | contract fixtures (`MissionLifecycleTest`, `DeceptionTest`, `TransportStateMachineTest`, `ProductionContractTest`) + generic headless matches | — |
| 664 | Multi-player coalition attack | ✅ | 🟡 | `MissionType`/`MissionPhase` machinery + `TacticalControllers` | contract fixtures (`MissionLifecycleTest`, `DeceptionTest`, `TransportStateMachineTest`, `ProductionContractTest`) + generic headless matches | — |
| 665 | Ground + artillery attack | ✅ | 🟡 | `MissionType`/`MissionPhase` machinery + `TacticalControllers` | contract fixtures (`MissionLifecycleTest`, `DeceptionTest`, `TransportStateMachineTest`, `ProductionContractTest`) + generic headless matches | — |
| 666 | Ground + air attack | ✅ | 🟡 | `MissionType`/`MissionPhase` machinery + `TacticalControllers` | contract fixtures (`MissionLifecycleTest`, `DeceptionTest`, `TransportStateMachineTest`, `ProductionContractTest`) + generic headless matches | — |
| 667 | Ground + naval support | ✅ | 🟡 | `MissionType`/`MissionPhase` machinery + `TacticalControllers` | contract fixtures (`MissionLifecycleTest`, `DeceptionTest`, `TransportStateMachineTest`, `ProductionContractTest`) + generic headless matches | — |
| 668 | Ground + air + naval combined operation | ✅ | 🟡 | `MissionType`/`MissionPhase` machinery + `TacticalControllers` | contract fixtures (`MissionLifecycleTest`, `DeceptionTest`, `TransportStateMachineTest`, `ProductionContractTest`) + generic headless matches | — |
| 669 | Feint followed by main assault | ✅ | 🟡 | `MissionType`/`MissionPhase` machinery + `TacticalControllers` | contract fixtures (`MissionLifecycleTest`, `DeceptionTest`, `TransportStateMachineTest`, `ProductionContractTest`) + generic headless matches | — |
| 670 | Fake retreat into ambush | ✅ | 🟡 | `MissionType`/`MissionPhase` machinery + `TacticalControllers` | contract fixtures (`MissionLifecycleTest`, `DeceptionTest`, `TransportStateMachineTest`, `ProductionContractTest`) + generic headless matches | — |
| 671 | Harvester raid | ✅ | 🟡 | `MissionType`/`MissionPhase` machinery + `TacticalControllers` | contract fixtures (`MissionLifecycleTest`, `DeceptionTest`, `TransportStateMachineTest`, `ProductionContractTest`) + generic headless matches | — |
| 672 | Expansion denial | ✅ | 🟡 | `MissionType`/`MissionPhase` machinery + `TacticalControllers` | contract fixtures (`MissionLifecycleTest`, `DeceptionTest`, `TransportStateMachineTest`, `ProductionContractTest`) + generic headless matches | — |
| 673 | Emergency base defense | ✅ | 🟡 | `MissionType`/`MissionPhase` machinery + `TacticalControllers` | contract fixtures (`MissionLifecycleTest`, `DeceptionTest`, `TransportStateMachineTest`, `ProductionContractTest`) + generic headless matches | — |
| 674 | Reserve reinforcement | ✅ | 🟡 | `MissionType`/`MissionPhase` machinery + `TacticalControllers` | contract fixtures (`MissionLifecycleTest`, `DeceptionTest`, `TransportStateMachineTest`, `ProductionContractTest`) + generic headless matches | — |
| 675 | Immediate counterattack after defense | ✅ | 🟡 | `MissionType`/`MissionPhase` machinery + `TacticalControllers` | contract fixtures (`MissionLifecycleTest`, `DeceptionTest`, `TransportStateMachineTest`, `ProductionContractTest`) + generic headless matches | — |
| 676 | Air transport insertion | ✅ | 🟡 | `MissionType`/`MissionPhase` machinery + `TacticalControllers` | contract fixtures (`MissionLifecycleTest`, `DeceptionTest`, `TransportStateMachineTest`, `ProductionContractTest`) + generic headless matches | — |
| 677 | Naval transport insertion | ✅ | 🟡 | `MissionType`/`MissionPhase` machinery + `TacticalControllers` | contract fixtures (`MissionLifecycleTest`, `DeceptionTest`, `TransportStateMachineTest`, `ProductionContractTest`) + generic headless matches | — |
| 678 | Tanya operation | ✅ | 🟡 | `MissionType`/`MissionPhase` machinery + `TacticalControllers` | contract fixtures (`MissionLifecycleTest`, `DeceptionTest`, `TransportStateMachineTest`, `ProductionContractTest`) + generic headless matches | — |
| 679 | Spy operation | ✅ | 🟡 | `MissionType`/`MissionPhase` machinery + `TacticalControllers` | contract fixtures (`MissionLifecycleTest`, `DeceptionTest`, `TransportStateMachineTest`, `ProductionContractTest`) + generic headless matches | — |
| 680 | Engineer capture operation | ✅ | 🟡 | `MissionType`/`MissionPhase` machinery + `TacticalControllers` | contract fixtures (`MissionLifecycleTest`, `DeceptionTest`, `TransportStateMachineTest`, `ProductionContractTest`) + generic headless matches | — |
| 681 | Transport rerouting after new threat | ✅ | 🟡 | `MissionType`/`MissionPhase` machinery + `TacticalControllers` | contract fixtures (`MissionLifecycleTest`, `DeceptionTest`, `TransportStateMachineTest`, `ProductionContractTest`) + generic headless matches | — |
| 682 | Transport abort | ✅ | 🟡 | `MissionType`/`MissionPhase` machinery + `TacticalControllers` | contract fixtures (`MissionLifecycleTest`, `DeceptionTest`, `TransportStateMachineTest`, `ProductionContractTest`) + generic headless matches | — |
| 683 | Special-unit extraction | ✅ | 🟡 | `MissionType`/`MissionPhase` machinery + `TacticalControllers` | contract fixtures (`MissionLifecycleTest`, `DeceptionTest`, `TransportStateMachineTest`, `ProductionContractTest`) + generic headless matches | — |
| 684 | Simultaneous multi-front pressure | ✅ | 🟡 | `MissionType`/`MissionPhase` machinery + `TacticalControllers` | contract fixtures (`MissionLifecycleTest`, `DeceptionTest`, `TransportStateMachineTest`, `ProductionContractTest`) + generic headless matches | — |
| 685 | Enemy composition switch triggers production response | ✅ | ✅ | `MissionType`/`MissionPhase` machinery + `TacticalControllers` | contract fixtures (`MissionLifecycleTest`, `DeceptionTest`, `TransportStateMachineTest`, `ProductionContractTest`) + generic headless matches | `ProductionContractTest` covers threat-driven counter-contracting and the capability-weight response directly. |
| 686 | Destroyed production facility triggers replanning | ✅ | ✅ | `MissionType`/`MissionPhase` machinery + `TacticalControllers` | contract fixtures (`MissionLifecycleTest`, `DeceptionTest`, `TransportStateMachineTest`, `ProductionContractTest`) + generic headless matches | `ProductionContractTest`: "Destroyed production infrastructure triggers the first valid emergency replacement." |
| 687 | Loss of bridge/route triggers replanning | ✅ | ✅ | `MissionType`/`MissionPhase` machinery + `TacticalControllers` | contract fixtures (`MissionLifecycleTest`, `DeceptionTest`, `TransportStateMachineTest`, `ProductionContractTest`) + generic headless matches | `ReviewTrigger()` hashes bridge damage state into `routeSignature`; `MissionLifecycleTest` covers "Route disruptions replan twice before a deterministic abort." |
| 688 | Enemy main army disappearance triggers recon | ✅ | ✅ | `MissionType`/`MissionPhase` machinery + `TacticalControllers` | contract fixtures (`MissionLifecycleTest`, `DeceptionTest`, `TransportStateMachineTest`, `ProductionContractTest`) + generic headless matches | `StrategicEventDetectorTest`: "Losing all contact with the enemy triggers an intelligence review", plus `HeadlessSkirmishTest.IntelligenceScouting`. |
| 689 | LLM failure during major operation | ✅ | 🟡 | `MissionType`/`MissionPhase` machinery + `TacticalControllers` | contract fixtures (`MissionLifecycleTest`, `DeceptionTest`, `TransportStateMachineTest`, `ProductionContractTest`) + generic headless matches | `HeadlessSkirmishTest.DeterministicFallback` runs with **no** model server for the whole match. It never tests the LLM dropping out *mid-operation* with missions already executing — the transition is the untested part. |
| 690 | Invalid LLM mission tested | ✅ | ✅ | `MissionType`/`MissionPhase` machinery + `TacticalControllers` | contract fixtures (`MissionLifecycleTest`, `DeceptionTest`, `TransportStateMachineTest`, `ProductionContractTest`) + generic headless matches | `CommandValidatorTest` + `ExpandedCoverageTest.Validate*` cover unknown types, out-of-bounds targets, conflicts and invalid priorities with machine-readable reasons. |

### §Stress & Scale Testing  <span>(691–705)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 691 | AI tested on small maps | ✅ | ❌ | `HeadlessSkirmish`, bounded scans/intervals, listener disposal | none | The whole automated suite runs on **one map** (`TestMapUid 9d94535c…`, Shattered Mountain). 141 maps ship with the mod; no small-map case is exercised. |
| 692 | AI tested on large maps | ✅ | ❌ | `HeadlessSkirmish`, bounded scans/intervals, listener disposal | none | Same single-map limitation — no large-map case is exercised automatically. |
| 693 | AI tested with many allied players | ✅ | ✅ | `HeadlessSkirmish`, bounded scans/intervals, listener disposal | `HeadlessSkirmishTest.StressScale` (4 bots/3000 ticks), `RepeatedMatchesReleaseResources` | `StressScale`/`UnifiedCoalitionCommand`/`CampaignLifecycle` run 4 allied bots; the harness cap is 8 per team. |
| 694 | AI tested with many enemy players | ✅ | 🟡 | `HeadlessSkirmish`, bounded scans/intervals, listener disposal | `HeadlessSkirmishTest.StressScale` (4 bots/3000 ticks), `RepeatedMatchesReleaseResources` | Only 2 teams are supported by `HeadlessSkirmish.Run` (a third throws). 'Many enemy players' is limited to 4-bot/2-team matches. |
| 695 | AI tested with hundreds of units | ✅ | ❌ | `HeadlessSkirmish`, bounded scans/intervals, listener disposal | `StressScale` (does not assert scale) | No unit-count assertion anywhere. `StressScale` asserts only `ActorCount > 0` after 3000 ticks — it never establishes that hundreds of units were ever alive. |
| 696 | AI tested with heavy air activity | ✅ | ❌ | `HeadlessSkirmish`, bounded scans/intervals, listener disposal | none | No air-activity-specific scenario or assertion. |
| 697 | AI tested with heavy naval activity | ✅ | ❌ | `HeadlessSkirmish`, bounded scans/intervals, listener disposal | none | No naval-activity-specific scenario or assertion. |
| 698 | AI tested with many simultaneous missions | ✅ | ❌ | `HeadlessSkirmish`, bounded scans/intervals, listener disposal | none | No assertion on concurrent mission count. |
| 699 | AI tested with frequent world-state changes | ✅ | ❌ | `HeadlessSkirmish`, bounded scans/intervals, listener disposal | none | No scenario that deliberately churns world state. |
| 700 | AI does not cause unacceptable frame/tick degradation | ✅ | ❌ | `HeadlessSkirmish`, bounded scans/intervals, listener disposal | none | **No tick/frame timing is measured or asserted anywhere.** Tests assert tick *counts* completed, never their cost. |
| 701 | LLM context generation remains bounded | ✅ | ✅ | `HeadlessSkirmish`, bounded scans/intervals, listener disposal | `HeadlessSkirmishTest.StressScale` (4 bots/3000 ticks), `RepeatedMatchesReleaseResources` | Enforced structurally: `ExternalBrainBotModule` sends fixed-shape `ArmyGroupState` summaries, asserted by `ExternalBrainSnapshotTest.ExternalContextSectionsAreDeterministicallyBounded`. |
| 702 | Threat-map updates remain performant | ✅ | 🟡 | `HeadlessSkirmish`, bounded scans/intervals, listener disposal | `HeadlessSkirmishTest.StressScale` (4 bots/3000 ticks), `RepeatedMatchesReleaseResources` | — |
| 703 | Route planning remains performant | ✅ | 🟡 | `HeadlessSkirmish`, bounded scans/intervals, listener disposal | `HeadlessSkirmishTest.StressScale` (4 bots/3000 ticks), `RepeatedMatchesReleaseResources` | — |
| 704 | Mission-management complexity remains bounded | ✅ | 🟡 | `HeadlessSkirmish`, bounded scans/intervals, listener disposal | `HeadlessSkirmishTest.StressScale` (4 bots/3000 ticks), `RepeatedMatchesReleaseResources` | — |
| 705 | Memory leaks/actor-reference leaks tested | ✅ | 🟡 | `HeadlessSkirmish`, bounded scans/intervals, listener disposal | `HeadlessSkirmishTest.StressScale` (4 bots/3000 ticks), `RepeatedMatchesReleaseResources` | `RepeatedMatchesReleaseResources` covers **listener/port and telemetry-writer leaks** across 3 back-to-back matches. No managed-memory or actor-reference leak profiling. |

### §Replay & Regression Testing  <span>(706–714)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 706 | Deterministic/reproducible test seeds available | ✅ | 🟡 | fixed-seed determinism in `HeadlessSkirmish`; timestamped `ai-telemetry.log` | `HeadlessSkirmishTest.DeterministicSameSeed`, `ToolApiReleasesPortBetweenGames` | — |
| 707 | Known battle scenarios replayable automatically | 🟡 | 🟡 | fixed-seed determinism in `HeadlessSkirmish`; timestamped `ai-telemetry.log` | `HeadlessSkirmishTest.DeterministicSameSeed`, `ToolApiReleasesPortBetweenGames` | Satisfied by **fixed-seed headless re-runs**, not by OpenRA replay files. There is no `.orarep` ingestion anywhere in the AI stack or harness. |
| 708 | AI decisions inspectable alongside replay timestamps | 🟡 | 🟡 | fixed-seed determinism in `HeadlessSkirmish`; timestamped `ai-telemetry.log` | `HeadlessSkirmishTest.DeterministicSameSeed`, `ToolApiReleasesPortBetweenGames` | `ai-telemetry.log` lines carry wall-clock timestamps and tick numbers, so decisions align with a *simulation*; they are not correlated to an OpenRA replay timeline. |
| 709 | Previously discovered strategic bugs have regression tests | ✅ | ❌ | fixed-seed determinism in `HeadlessSkirmish`; timestamped `ai-telemetry.log` | none | No regression test is labelled to a previously discovered strategic bug; the suite is feature-contract shaped, not regression shaped. |
| 710 | Previously discovered transport bugs have regression tests | ✅ | ❌ | fixed-seed determinism in `HeadlessSkirmish`; timestamped `ai-telemetry.log` | none | No transport-specific regression test. `TransportStateMachineTest` is a fresh contract suite, not a regression of a known defect. |
| 711 | Previously discovered order-conflict bugs have regression tests | ✅ | ✅ | fixed-seed determinism in `HeadlessSkirmish`; timestamped `ai-telemetry.log` | `HeadlessSkirmishTest.DeterministicSameSeed`, `ToolApiReleasesPortBetweenGames` | The one genuine regression test in the repo: `ToolApiReleasesPortBetweenGames`/`RepeatedMatchesReleaseResources` pin the tool-API listener leak that caused a per-tick retry storm. |
| 712 | Previously discovered fog-of-war leaks have regression tests | ✅ | ✅ | fixed-seed determinism in `HeadlessSkirmish`; timestamped `ai-telemetry.log` | `HeadlessSkirmishTest.DeterministicSameSeed`, `ToolApiReleasesPortBetweenGames` | `ExternalBrainSnapshotTest.FairFogRejectsInvisible` and `IntelTrackerTest.RememberedIntelRetainsNoActor` are exactly the fog-leak regressions closed by the previous audit pass. |
| 713 | Combat estimation benchmarked against historical scenarios | ✅ | ❌ | fixed-seed determinism in `HeadlessSkirmish`; timestamped `ai-telemetry.log` | none | No historical engagement corpus exists to benchmark against; see 159. |
| 714 | Strategic behavior compared against baseline win rates | ✅ | 🟡 | fixed-seed determinism in `HeadlessSkirmish`; timestamped `ai-telemetry.log` | `HeadlessSkirmishTest.DeterministicSameSeed`, `ToolApiReleasesPortBetweenGames` | `ai/selfplay.py --bot-type normal` produces the scripted baseline, and `AUDIT_REPORT.md` records a 3-seed comparison. Not wired into CI, so a strategic regression would not fail a build. |

### §Self-Play & Optimization  <span>(715–727)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 715 | AI-vs-AI self-play runs automatically | ✅ | ✅ | `ai/selfplay.py` (`--maps`, `--vs`, `--bot-type`, 8 `--sweep-*` axes, `--combat-accuracy`, `--details`) | `ai/selfcheck.py::selfplay_failure_regression` | `ai/selfplay.py` batches seeded headless matches; failure/parse handling covered by `ai/selfcheck.py::selfplay_failure_regression`. |
| 716 | Multiple maps in batch evaluation | ✅ | 🟡 | `ai/selfplay.py` (`--maps`, `--vs`, `--bot-type`, 8 `--sweep-*` axes, `--combat-accuracy`, `--details`) | `ai/selfcheck.py::selfplay_failure_regression` | `--maps` implemented; not exercised by any automated run. |
| 717 | Multiple factions/configurations included | 🟡 | ❌ | `ai/selfplay.py` (`--maps`, `--vs`, `--bot-type`, 8 `--sweep-*` axes, `--combat-accuracy`, `--details`) | `ai/selfcheck.py::selfplay_failure_regression` | `HeadlessSkirmish` assigns factions round-robin (`factions[i % factions.Length]`) and reports them, but neither `SimulateCommand` nor `selfplay.py` exposes a `FACTION=` selector, so a faction-controlled experiment cannot be expressed. |
| 718 | Strategic parameters varied experimentally | ✅ | 🟡 | `ai/selfplay.py` (`--maps`, `--vs`, `--bot-type`, 8 `--sweep-*` axes, `--combat-accuracy`, `--details`) | `ai/selfcheck.py::selfplay_failure_regression` | — |
| 719 | Threat weights tunable | ✅ | 🟡 | `ai/selfplay.py` (`--maps`, `--vs`, `--bot-type`, 8 `--sweep-*` axes, `--combat-accuracy`, `--details`) | `ai/selfcheck.py::selfplay_failure_regression` | — |
| 720 | Retreat thresholds tunable | ✅ | 🟡 | `ai/selfplay.py` (`--maps`, `--vs`, `--bot-type`, 8 `--sweep-*` axes, `--combat-accuracy`, `--details`) | `ai/selfcheck.py::selfplay_failure_regression` | — |
| 721 | Reserve percentages tunable | ✅ | 🟡 | `ai/selfplay.py` (`--maps`, `--vs`, `--bot-type`, 8 `--sweep-*` axes, `--combat-accuracy`, `--details`) | `ai/selfcheck.py::selfplay_failure_regression` | — |
| 722 | Production capability weights tunable | ✅ | 🟡 | `ai/selfplay.py` (`--maps`, `--vs`, `--bot-type`, 8 `--sweep-*` axes, `--combat-accuracy`, `--details`) | `ai/selfcheck.py::selfplay_failure_regression` | — |
| 723 | Target-scoring weights tunable | ✅ | 🟡 | `ai/selfplay.py` (`--maps`, `--vs`, `--bot-type`, 8 `--sweep-*` axes, `--combat-accuracy`, `--details`) | `ai/selfcheck.py::selfplay_failure_regression` | — |
| 724 | Feint commitment thresholds tunable | ✅ | 🟡 | `ai/selfplay.py` (`--maps`, `--vs`, `--bot-type`, 8 `--sweep-*` axes, `--combat-accuracy`, `--details`) | `ai/selfcheck.py::selfplay_failure_regression` | — |
| 725 | Special-ops risk thresholds tunable | ✅ | 🟡 | `ai/selfplay.py` (`--maps`, `--vs`, `--bot-type`, 8 `--sweep-*` axes, `--combat-accuracy`, `--details`) | `ai/selfcheck.py::selfplay_failure_regression` | — |
| 726 | Changes evaluated on more than raw win rate | ✅ | ✅ | `ai/selfplay.py` (`--maps`, `--vs`, `--bot-type`, 8 `--sweep-*` axes, `--combat-accuracy`, `--details`) | `ai/selfcheck.py::selfplay_failure_regression` | `--details` plus the exchange/duration/outcome report evaluate on more than win rate. |
| 727 | Overfitting to one map/opponent checked | ✅ | 🟡 | `ai/selfplay.py` (`--maps`, `--vs`, `--bot-type`, 8 `--sweep-*` axes, `--combat-accuracy`, `--details`) | `ai/selfcheck.py::selfplay_failure_regression` | `--maps` reports per-map win rates and flags map-specific overfitting. Implemented but never run automatically — and the C# suite's single map means overfitting is currently *likely*, not excluded. |

### §LLM Strategic Evaluation  <span>(728–738)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 728 | Same game state replayed through multiple decisions | ✅ | ✅ | `ai/llm_eval.py` (11 `score_*` functions + `replay_same_state`) | `ai/selfcheck.py::repeat_state_regression`; `LlmEvalTest` (C# re-implementation, see notes) | `replay_same_state` replays one immutable snapshot through N commander decisions and reports snapshot hash, per-plan hashes and unique-decision count. Directly covered by `ai/selfcheck.py::repeat_state_regression`. |
| 729 | LLM plans scored for legality | ✅ | 🟡 | `ai/llm_eval.py` (11 `score_*` functions + `replay_same_state`) | `ai/selfcheck.py::repeat_state_regression`; `LlmEvalTest` (C# re-implementation, see notes) | `score_legality` exists in `ai/llm_eval.py`, but the only test (`LlmEvalTest.Legality*`) is a **private C# re-implementation of the same algorithm inside the test file** — it never executes the shipped Python. The Python function could regress silently. |
| 730 | LLM plans scored for force availability | ✅ | ❌ | `ai/llm_eval.py` (11 `score_*` functions + `replay_same_state`) | none | `score_force_availability()` is implemented in `ai/llm_eval.py` but has **no test at all** — neither `ai/selfcheck.py` nor `LlmEvalTest` touches it. |
| 731 | LLM plans scored for mission completeness | ✅ | ❌ | `ai/llm_eval.py` (11 `score_*` functions + `replay_same_state`) | none | `score_mission_completeness()` is implemented in `ai/llm_eval.py` but has **no test at all** — neither `ai/selfcheck.py` nor `LlmEvalTest` touches it. |
| 732 | LLM plans scored for unnecessary risk | ✅ | ❌ | `ai/llm_eval.py` (11 `score_*` functions + `replay_same_state`) | none | `score_unnecessary_risk()` is implemented in `ai/llm_eval.py` but has **no test at all** — neither `ai/selfcheck.py` nor `LlmEvalTest` touches it. |
| 733 | LLM plans compared with deterministic baseline | ✅ | ❌ | `ai/llm_eval.py` (11 `score_*` functions + `replay_same_state`) | none | `score_baseline_comparison()` is implemented in `ai/llm_eval.py` but has **no test at all** — neither `ai/selfcheck.py` nor `LlmEvalTest` touches it. |
| 734 | LLM decisions checked for strategic oscillation | ✅ | 🟡 | `ai/llm_eval.py` (11 `score_*` functions + `replay_same_state`) | `ai/selfcheck.py::repeat_state_regression`; `LlmEvalTest` (C# re-implementation, see notes) | Same duplication problem as 729: `LlmEvalTest.Oscillation*` re-implements `score_strategic_oscillation` in C# instead of exercising `ai/llm_eval.py`. |
| 735 | LLM decisions checked for repeated impossible commands | ✅ | ❌ | `ai/llm_eval.py` (11 `score_*` functions + `replay_same_state`) | none | `score_repeated_impossible()` is implemented in `ai/llm_eval.py` but has **no test at all** — neither `ai/selfcheck.py` nor `LlmEvalTest` touches it. |
| 736 | LLM decisions checked for misuse of uncertain intel | ✅ | ❌ | `ai/llm_eval.py` (11 `score_*` functions + `replay_same_state`) | none | `score_uncertain_intelligence()` is implemented in `ai/llm_eval.py` but has **no test at all** — neither `ai/selfcheck.py` nor `LlmEvalTest` touches it. |
| 737 | LLM decisions checked for failing to maintain reserves | ✅ | ❌ | `ai/llm_eval.py` (11 `score_*` functions + `replay_same_state`) | none | `score_reserves()` is implemented in `ai/llm_eval.py` but has **no test at all** — neither `ai/selfcheck.py` nor `LlmEvalTest` touches it. |
| 738 | LLM decisions checked for excessive idle forces | ✅ | 🟡 | `ai/llm_eval.py` (11 `score_*` functions + `replay_same_state`) | `ai/selfcheck.py::repeat_state_regression`; `LlmEvalTest` (C# re-implementation, see notes) | Same duplication problem as 729: `LlmEvalTest.Idle*` re-implements `score_idle_forces` in C#. |

### §Information-Security / Game-Rule Integrity  <span>(739–747)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 739 | Fair-fog cannot access hidden enemies via engine refs | ✅ | ✅ | `CommandToolApi` reads only `ToolContext.EnemyIntel`; `EnemyIntel` holds no `Actor`; `CommandValidator` re-validates on the game thread | `ExternalBrainSnapshotTest.FairFogRejectsInvisible`, `IntelTrackerTest`, `CommandValidatorTest` | — |
| 740 | Tool APIs enforce visibility restrictions | ✅ | ✅ | `CommandToolApi` reads only `ToolContext.EnemyIntel`; `EnemyIntel` holds no `Actor`; `CommandValidator` re-validates on the game thread | `ExternalBrainSnapshotTest.FairFogRejectsInvisible`, `IntelTrackerTest`, `CommandValidatorTest` | — |
| 741 | LLM cannot bypass production prerequisites | ✅ | ✅ | `CommandToolApi` reads only `ToolContext.EnemyIntel`; `EnemyIntel` holds no `Actor`; `CommandValidator` re-validates on the game thread | `ExternalBrainSnapshotTest.FairFogRejectsInvisible`, `IntelTrackerTest`, `CommandValidatorTest` | — |
| 742 | LLM cannot spend money a player doesn't have | ✅ | ✅ | `CommandToolApi` reads only `ToolContext.EnemyIntel`; `EnemyIntel` holds no `Actor`; `CommandValidator` re-validates on the game thread | `ExternalBrainSnapshotTest.FairFogRejectsInvisible`, `IntelTrackerTest`, `CommandValidatorTest` | — |
| 743 | LLM cannot issue orders to enemy units | ✅ | ✅ | `CommandToolApi` reads only `ToolContext.EnemyIntel`; `EnemyIntel` holds no `Actor`; `CommandValidator` re-validates on the game thread | `ExternalBrainSnapshotTest.FairFogRejectsInvisible`, `IntelTrackerTest`, `CommandValidatorTest` | — |
| 744 | LLM cannot issue orders to ungranted allied units | ✅ | ✅ | `CommandToolApi` reads only `ToolContext.EnemyIntel`; `EnemyIntel` holds no `Actor`; `CommandValidator` re-validates on the game thread | `ExternalBrainSnapshotTest.FairFogRejectsInvisible`, `IntelTrackerTest`, `CommandValidatorTest` | — |
| 745 | LLM cannot teleport or bypass movement rules | ✅ | ✅ | `CommandToolApi` reads only `ToolContext.EnemyIntel`; `EnemyIntel` holds no `Actor`; `CommandValidator` re-validates on the game thread | `ExternalBrainSnapshotTest.FairFogRejectsInvisible`, `IntelTrackerTest`, `CommandValidatorTest` | — |
| 746 | LLM cannot create nonexistent units/buildings | ✅ | ✅ | `CommandToolApi` reads only `ToolContext.EnemyIntel`; `EnemyIntel` holds no `Actor`; `CommandValidator` re-validates on the game thread | `ExternalBrainSnapshotTest.FairFogRejectsInvisible`, `IntelTrackerTest`, `CommandValidatorTest` | — |
| 747 | Commands validated engine-side not trusted from LLM | ✅ | ✅ | `CommandToolApi` reads only `ToolContext.EnemyIntel`; `EnemyIntel` holds no `Actor`; `CommandValidator` re-validates on the game thread | `ExternalBrainSnapshotTest.FairFogRejectsInvisible`, `IntelTrackerTest`, `CommandValidatorTest` | — |

### §Code Organization  <span>(748–770)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 748 | Coalition command isolated module | ✅ | ✅ | one type per concern under `Traits/BotModules/Coalition/` (23 files) + `TacticalControllers.cs` | whole suite compiles clean (0 warnings) and each unit is directly testable | — |
| 749 | Coalition blackboard isolated | ✅ | ✅ | one type per concern under `Traits/BotModules/Coalition/` (23 files) + `TacticalControllers.cs` | whole suite compiles clean (0 warnings) and each unit is directly testable | — |
| 750 | Force registry isolated | ✅ | ✅ | one type per concern under `Traits/BotModules/Coalition/` (23 files) + `TacticalControllers.cs` | whole suite compiles clean (0 warnings) and each unit is directly testable | — |
| 751 | Order arbiter isolated | ✅ | ✅ | one type per concern under `Traits/BotModules/Coalition/` (23 files) + `TacticalControllers.cs` | whole suite compiles clean (0 warnings) and each unit is directly testable | — |
| 752 | Intelligence tracker isolated | ✅ | ✅ | one type per concern under `Traits/BotModules/Coalition/` (23 files) + `TacticalControllers.cs` | whole suite compiles clean (0 warnings) and each unit is directly testable | — |
| 753 | Opponent model isolated | ✅ | ✅ | one type per concern under `Traits/BotModules/Coalition/` (23 files) + `TacticalControllers.cs` | whole suite compiles clean (0 warnings) and each unit is directly testable | — |
| 754 | Map analyzer isolated | ✅ | ✅ | one type per concern under `Traits/BotModules/Coalition/` (23 files) + `TacticalControllers.cs` | whole suite compiles clean (0 warnings) and each unit is directly testable | — |
| 755 | Threat analysis isolated | ✅ | ✅ | one type per concern under `Traits/BotModules/Coalition/` (23 files) + `TacticalControllers.cs` | whole suite compiles clean (0 warnings) and each unit is directly testable | — |
| 756 | Combat evaluator isolated | ✅ | ✅ | one type per concern under `Traits/BotModules/Coalition/` (23 files) + `TacticalControllers.cs` | whole suite compiles clean (0 warnings) and each unit is directly testable | — |
| 757 | Route planner isolated | ✅ | ✅ | one type per concern under `Traits/BotModules/Coalition/` (23 files) + `TacticalControllers.cs` | whole suite compiles clean (0 warnings) and each unit is directly testable | — |
| 758 | Target evaluator isolated | ✅ | ✅ | one type per concern under `Traits/BotModules/Coalition/` (23 files) + `TacticalControllers.cs` | whole suite compiles clean (0 warnings) and each unit is directly testable | — |
| 759 | Mission manager isolated | ✅ | ✅ | one type per concern under `Traits/BotModules/Coalition/` (23 files) + `TacticalControllers.cs` | whole suite compiles clean (0 warnings) and each unit is directly testable | — |
| 760 | Ground controller isolated | ✅ | ✅ | one type per concern under `Traits/BotModules/Coalition/` (23 files) + `TacticalControllers.cs` | whole suite compiles clean (0 warnings) and each unit is directly testable | — |
| 761 | Air controller isolated | ✅ | ✅ | one type per concern under `Traits/BotModules/Coalition/` (23 files) + `TacticalControllers.cs` | whole suite compiles clean (0 warnings) and each unit is directly testable | — |
| 762 | Naval controller isolated | ✅ | ✅ | one type per concern under `Traits/BotModules/Coalition/` (23 files) + `TacticalControllers.cs` | whole suite compiles clean (0 warnings) and each unit is directly testable | — |
| 763 | Transport controller isolated | ✅ | ✅ | one type per concern under `Traits/BotModules/Coalition/` (23 files) + `TacticalControllers.cs` | whole suite compiles clean (0 warnings) and each unit is directly testable | — |
| 764 | Special-ops controller isolated | ✅ | ✅ | one type per concern under `Traits/BotModules/Coalition/` (23 files) + `TacticalControllers.cs` | whole suite compiles clean (0 warnings) and each unit is directly testable | — |
| 765 | Production director isolated | ✅ | ✅ | one type per concern under `Traits/BotModules/Coalition/` (23 files) + `TacticalControllers.cs` | whole suite compiles clean (0 warnings) and each unit is directly testable | — |
| 766 | Strategic reserve manager isolated | ✅ | ✅ | one type per concern under `Traits/BotModules/Coalition/` (23 files) + `TacticalControllers.cs` | whole suite compiles clean (0 warnings) and each unit is directly testable | — |
| 767 | LLM adapter isolated from core gameplay | ✅ | ✅ | one type per concern under `Traits/BotModules/Coalition/` (23 files) + `TacticalControllers.cs` | whole suite compiles clean (0 warnings) and each unit is directly testable | — |
| 768 | Command validator isolated and testable | ✅ | ✅ | one type per concern under `Traits/BotModules/Coalition/` (23 files) + `TacticalControllers.cs` | whole suite compiles clean (0 warnings) and each unit is directly testable | — |
| 769 | Deterministic fallback isolated and testable | ✅ | ✅ | one type per concern under `Traits/BotModules/Coalition/` (23 files) + `TacticalControllers.cs` | whole suite compiles clean (0 warnings) and each unit is directly testable | — |
| 770 | Telemetry/logging isolated from decision logic | ✅ | ✅ | one type per concern under `Traits/BotModules/Coalition/` (23 files) + `TacticalControllers.cs` | whole suite compiles clean (0 warnings) and each unit is directly testable | — |

### §Documentation  <span>(771–788)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 771 | Architecture documented | ✅ | ✅ | `README.md`, `ai/README.md`, `ai/COMMAND_API.md` (9 numbered schema sections), `TESTING.md`, XML doc comments | documentation/source consistency pass | — |
| 772 | Coalition-control model documented | ✅ | ✅ | `README.md`, `ai/README.md`, `ai/COMMAND_API.md` (9 numbered schema sections), `TESTING.md`, XML doc comments | documentation/source consistency pass | — |
| 773 | Fog-of-war information policy documented | ✅ | ✅ | `README.md`, `ai/README.md`, `ai/COMMAND_API.md` (9 numbered schema sections), `TESTING.md`, XML doc comments | documentation/source consistency pass | — |
| 774 | LLM tool API documented | ✅ | ✅ | `README.md`, `ai/README.md`, `ai/COMMAND_API.md` (9 numbered schema sections), `TESTING.md`, XML doc comments | documentation/source consistency pass | — |
| 775 | Mission schema documented | ✅ | ✅ | `README.md`, `ai/README.md`, `ai/COMMAND_API.md` (9 numbered schema sections), `TESTING.md`, XML doc comments | documentation/source consistency pass | — |
| 776 | Force/army-group schema documented | ✅ | ✅ | `README.md`, `ai/README.md`, `ai/COMMAND_API.md` (9 numbered schema sections), `TESTING.md`, XML doc comments | documentation/source consistency pass | — |
| 777 | Enemy-intelligence schema documented | ✅ | ✅ | `README.md`, `ai/README.md`, `ai/COMMAND_API.md` (9 numbered schema sections), `TESTING.md`, XML doc comments | documentation/source consistency pass | — |
| 778 | Threat-map model documented | ✅ | ✅ | `README.md`, `ai/README.md`, `ai/COMMAND_API.md` (9 numbered schema sections), `TESTING.md`, XML doc comments | documentation/source consistency pass | — |
| 779 | Route-cost model documented | ✅ | ✅ | `README.md`, `ai/README.md`, `ai/COMMAND_API.md` (9 numbered schema sections), `TESTING.md`, XML doc comments | documentation/source consistency pass | — |
| 780 | Combat-estimator assumptions documented | ✅ | ✅ | `README.md`, `ai/README.md`, `ai/COMMAND_API.md` (9 numbered schema sections), `TESTING.md`, XML doc comments | documentation/source consistency pass | — |
| 781 | Strategic-posture behavior documented | ✅ | ✅ | `README.md`, `ai/README.md`, `ai/COMMAND_API.md` (9 numbered schema sections), `TESTING.md`, XML doc comments | documentation/source consistency pass | — |
| 782 | Production/capability system documented | ✅ | ✅ | `README.md`, `ai/README.md`, `ai/COMMAND_API.md` (9 numbered schema sections), `TESTING.md`, XML doc comments | documentation/source consistency pass | — |
| 783 | Opponent-model features documented | ✅ | ✅ | `README.md`, `ai/README.md`, `ai/COMMAND_API.md` (9 numbered schema sections), `TESTING.md`, XML doc comments | documentation/source consistency pass | — |
| 784 | Failure/fallback behavior documented | ✅ | ✅ | `README.md`, `ai/README.md`, `ai/COMMAND_API.md` (9 numbered schema sections), `TESTING.md`, XML doc comments | documentation/source consistency pass | — |
| 785 | Difficulty settings documented | ✅ | ✅ | `README.md`, `ai/README.md`, `ai/COMMAND_API.md` (9 numbered schema sections), `TESTING.md`, XML doc comments | documentation/source consistency pass | — |
| 786 | Testing instructions documented | ✅ | ✅ | `README.md`, `ai/README.md`, `ai/COMMAND_API.md` (9 numbered schema sections), `TESTING.md`, XML doc comments | documentation/source consistency pass | — |
| 787 | Batch/self-play evaluation documented | ✅ | ✅ | `README.md`, `ai/README.md`, `ai/COMMAND_API.md` (9 numbered schema sections), `TESTING.md`, XML doc comments | documentation/source consistency pass | — |
| 788 | Decision-log format documented | ✅ | ✅ | `README.md`, `ai/README.md`, `ai/COMMAND_API.md` (9 numbered schema sections), `TESTING.md`, XML doc comments | documentation/source consistency pass | — |

### §Final Acceptance Tests  <span>(789–804)</span>

| # | Requirement | Impl | Test | Code | Tests | Notes |
|---|---|:--:|:--:|---|---|---|
| 789 | Unified Coalition Test (3+ allied AI as one command) | ✅ | 🟡 | end-to-end behaviour of the whole stack | `HeadlessSkirmishTest` acceptance scenarios + `AcceptanceSuite` contracts | `UnifiedCoalitionCommand` asserts 4 bots enabled + a `Posture ` line + a `Match metrics:` line. Asserted by **telemetry-marker presence**, not by behavioural outcome. It does not demonstrate that the bots acted as one command rather than four. |
| 790 | Combined Arms Test (ground+artillery+air+naval sync) | ✅ | 🟡 | end-to-end behaviour of the whole stack | `HeadlessSkirmishTest` acceptance scenarios + `AcceptanceSuite` contracts | `CampaignLifecycle` asserts a `Coordinated force:` line naming air, naval and land arms — i.e. the *gate evaluated* all three domains. It does not assert a synchronized multi-domain operation actually executed. |
| 791 | Deception Test (feint→enemy reaction→real attack) | ✅ | 🟡 | end-to-end behaviour of the whole stack | `HeadlessSkirmishTest` acceptance scenarios + `AcceptanceSuite` contracts | `DeceptionTest` covers attempt counting and response measurement as pure functions. No end-to-end run asserts feint → measured reaction → conditional real attack. |
| 792 | Special Operations Test (Tanya/Spy transport+extract) | ✅ | 🟡 | end-to-end behaviour of the whole stack | `HeadlessSkirmishTest` acceptance scenarios + `AcceptanceSuite` contracts | `SpecialOpsController` + `TransportStateMachineTest` cover the machinery; no scenario asserts a full insert → act → extract cycle in a live match. |
| 793 | Human Attention Test (simultaneous coordinated threats) | ✅ | 🟡 | end-to-end behaviour of the whole stack | `HeadlessSkirmishTest` acceptance scenarios + `AcceptanceSuite` contracts | No test asserts simultaneous coordinated threats; only that coordinated telemetry is emitted. |
| 794 | Counter-Composition Test (enemy comp switch→production) | ✅ | 🟡 | end-to-end behaviour of the whole stack | `HeadlessSkirmishTest` acceptance scenarios + `AcceptanceSuite` contracts | `ProductionContractTest` covers the counter-composition response at contract level for one coalition; multi-player propagation is not asserted end-to-end. |
| 795 | Reserve Test (reserve remains, then reacts/exploits) | ✅ | 🟡 | end-to-end behaviour of the whole stack | `HeadlessSkirmishTest` acceptance scenarios + `AcceptanceSuite` contracts | `ReserveManagerTest` covers commit rules and justification; no live scenario asserts a reserve surviving a major attack and later reacting. |
| 796 | Counterattack Test (enemy overcommits→AI counterattacks) | ✅ | 🟡 | end-to-end behaviour of the whole stack | `HeadlessSkirmishTest` acceptance scenarios + `AcceptanceSuite` contracts | `CounterattackAssessmentTest` covers the windowing decision; no live scenario drives a failed enemy attack into a timed counterattack. |
| 797 | Intelligence Test (loses sight→uncertain→recon not cheat) | ✅ | ✅ | end-to-end behaviour of the whole stack | `HeadlessSkirmishTest` acceptance scenarios + `AcceptanceSuite` contracts | Genuinely covered: `IntelTrackerTest` (ladder + decay + no-actor snapshots), `ExternalBrainSnapshotTest.FairFogRejectsInvisible`, and `HeadlessSkirmishTest.IntelligenceScouting` (asserts a scout/probe is actually dispatched). |
| 798 | Fairness Test (fair fog + 0% bonus = no cheating) | ✅ | ✅ | end-to-end behaviour of the whole stack | `HeadlessSkirmishTest` acceptance scenarios + `AcceptanceSuite` contracts | The strongest-evidenced acceptance case: fog honesty is enforced structurally (the tool API has no `world.Actors` access at all) and asserted by `ExternalBrainSnapshotTest` + `IntelTrackerTest`; 0% economic bonus is the shipped default and covered by `DifficultyTest`. |
| 799 | LLM Failure Test (LLM down mid-battle→missions continue) | ✅ | 🟡 | end-to-end behaviour of the whole stack | `HeadlessSkirmishTest` acceptance scenarios + `AcceptanceSuite` contracts | See 689 — the fallback is proven from a cold start, not across a mid-battle LLM dropout. |
| 800 | Invalid Commander Test (impossible op→reject→replan) | ✅ | ✅ | end-to-end behaviour of the whole stack | `HeadlessSkirmishTest` acceptance scenarios + `AcceptanceSuite` contracts | `CommandValidatorTest` + `OrderArbiterTest` cover rejection with machine-readable reasons, and the commander continues from the last valid plan. |
| 801 | Adaptation Test (valid strategy→poor→cancel/modify) | ✅ | 🟡 | end-to-end behaviour of the whole stack | `HeadlessSkirmishTest` acceptance scenarios + `AcceptanceSuite` contracts | `StrategicEventDetectorTest` proves the *trigger* fires; `MissionLifecycleTest` proves outmatched missions withdraw. No scenario asserts a full valid→invalid→cancelled arc under live conditions. |
| 802 | Withdrawal Test (losing engagement→preserve forces) | ✅ | 🟡 | end-to-end behaviour of the whole stack | `HeadlessSkirmishTest` acceptance scenarios + `AcceptanceSuite` contracts | `MissionLifecycleTest`: "Outmatched missions enter withdrawal before becoming terminal", plus `RetreatEffectiveness` telemetry. Not asserted in a live match. |
| 803 | Campaign Test (full match: recon→econ→pressure→ops→win) | ✅ | 🟡 | end-to-end behaviour of the whole stack | `HeadlessSkirmishTest` acceptance scenarios + `AcceptanceSuite` contracts | `CampaignLifecycle` (5000 ticks) asserts posture, metrics, production/mission planning, recon and the combined-arms gate all appear. Marker-based; it does not assert *victory* behaviour — and the recorded matches are mostly losses. |
| 804 | Brutal Fair-AI Test (extreme/supreme/fair/0% > standard) | 🟡 | 🟡 | end-to-end behaviour of the whole stack | `HeadlessSkirmishTest` acceptance scenarios + `AcceptanceSuite` contracts | **The weakest row.** `AUDIT_REPORT.md`'s own figures show 0W/2L/1D vs Rush at 1.39 mean exchange. That is ~136% better *combat exchange* than the scripted Normal baseline (0.59), which is a real and measured result — but it is not 'demonstrably much stronger than standard OpenRA bots', because it still loses the matches. Strategic timing/economy remains the open problem, as the report states. |
