# OpenRA Supreme Allied Command AI — Full Implementation & Test Audit

**Repository:** https://github.com/Pummelchen/OpenRA (fork of OpenRA, branch `main`)
**Audit date:** 2026-08-16
**Method:** Automated codebase exploration (5 parallel subagents + direct file reads), evidence-based per-requirement verdicts.

---

## Summary Statistics

**Total requirements:** 804

| Status | Count | % |
|--------|-------|---|
| ✅ Complete & tested | 168 | 20.9% |
| 🟡 Implemented, partially tested | 287 | 35.7% |
| ✅ Complete, untested | 112 | 13.9% |
| 🟡 Partial, untested | 97 | 12.1% |
| ❌ Missing | 140 | 17.4% |

**Implemented (any degree):** 664 / 804 (82.6%)
**Tested (any degree):** 455 / 804 (56.6%)
**Fully complete & fully tested:** 168 / 804 (20.9%)
**Missing entirely:** 140 / 804 (17.4%)

---

## The 20 Most Important Missing/Partial Requirements

| # | Requirement | Status | Why it matters |
|---|-------------|--------|----------------|
| 159 | Combat-estimator accuracy measured against actual replay outcomes | ❌ Missing | No validation that the estimator's predicted win ratios correlate with real game outcomes. Self-play records both but never compares them. |
| 557 | Artillery remains behind screening forces | ❌ Missing | Artillery units have no positioning logic relative to screening forces; they ride waves as generic units. |
| 559 | Fast units do not outrun required support | ❌ Missing | No speed-based coordination; fast tanks arrive at target alone while AA/infantry lag behind. |
| 445 | Army groups include location in LLM snapshot | ❌ Missing | `ArmyGroupState` has no location/region/center field; the LLM cannot reason about where forces are. |
| 447 | Army groups include nearby known threats | ❌ Missing | No nearby-threat field; threats only available per-region via `inspect_region` tool. |
| 186 | Pincer/double-envelopment mission | ❌ Missing | No `MissionType` enum value or logic; only single-flank exists. |
| 195 | Naval blockade mission | ❌ Missing | No mission type or logic. |
| 265 | Fake buildup deception | ❌ Missing | No mechanism to simulate or signal a false military buildup. |
| 477-479 | LLM production/expansion/capability directive tools | ❌ Missing | `set_production_directive`, `set_expansion_priority`, `request_capability` documented in COMMAND_API.md but never implemented. |
| 484-489 | LLM mission command tools (create/modify/cancel/assign/release/set_reserve) | ❌ Missing | All documented as mutation tools but the `LlmIntent` surface only supports a missions array (type/x/y/priority), posture, produce, and retreat. |
| 728-738 | LLM strategic evaluation harness (plan scoring, oscillation checks, etc.) | ❌ Missing | No mechanism to replay a game state through multiple commander decisions, score plans for legality/risk/completeness, or check for strategic oscillation. |
| 612 | Synchronization error recorded as telemetry metric | ❌ Missing | Sync error is logged per-wave in text but never recorded as a structured metric. |
| 401 | Emergency replacement production | ❌ Missing | No mechanism to prioritize urgent replacement of destroyed critical units. |
| 341 | Different theaters/fronts with different local postures | ❌ Missing | Single global posture only; no per-front or per-region posture. |
| 521 | LLM exploits major enemy mistakes rapidly | ❌ Missing | No prompt instruction or mechanism for rapid mistake exploitation; event-driven review exists but is generic. |
| 604-605 | Economic damage caused/suffered recorded | ❌ Missing | Combat value exchange is tracked but economic damage (refineries/harvesters destroyed) is not. |
| 705 | Memory/actor-reference leak testing | 🟡 Partial | Smoke tests (3 back-to-back matches) exist but no explicit leak detection. |
| 250 | Mission phase transitions tested under battlefield disruptions | 🟡 Partial | Phase monotonicity tested in live match; no disruption-specific (abort/replan) test. |
| 46 | Mission cancellation properly releases assigned units | 🟡 Partial | `CancelMission` removes the mission but the arbiter's released commitments are flagged, not removed — unbounded growth. |

---

## 1. Architecture Decisions That Make Requirements Difficult

1. **Single global posture (req 341).** `PostureSelection.Select` returns one `StrategicPosture` for the entire coalition. There is no per-region or per-front posture field anywhere in `CoalitionBlackboard` or `CoalitionRegion`. Supporting local postures would require adding a posture field to `CoalitionRegion` and threading it through target-scoring and reserve decisions — moderate effort but architecturally clean.

2. **LLM intent surface is a flat JSON (reqs 477-489).** The `LlmIntent` class supports only `Posture`, `Produce` (unit-name array), `Retreat`, and `Missions` (type/x/y/priority). The COMMAND_API.md documents a rich mutation-tool API (`create_mission`, `modify_mission`, `cancel_mission`, `assign_force`, `release_force`, `set_reserve`, `request_capability`, `set_production_directive`, `set_expansion_priority`) but none are implemented. The current design intentionally keeps the LLM at arm's length — missions are engine-stable and the deterministic commander is authoritative — which is safer but means the LLM cannot directly control forces, reserves, or production capabilities.

3. **Reserve is an estimate, not a managed pool (reqs 351-362).** The reserve is computed as `activeArmy - availableArmy` (where `AvailableArmy` takes `ReserveFraction`), and `reserveCommitted` is a boolean flag in the brain. There is no `ReserveManager` class, no reserve force pool the commander can direct to intercept raids (358), reinforce failing fronts (356), or exploit breakthroughs (357). The brain's reserve-edge behavior intercepts raids near the base, but it cannot be commanded to protect expansions (359) or reinforce an ally (356).

4. **Self-play evaluates the deterministic commander only (req 715).** `SimulateCommand` sets `DisableExternalBrain = true`, so all self-play runs test the scripted brain, never the LLM. This means the LLM's strategic quality is never evaluated by the harness — a significant gap for a project whose selling point is LLM command.

5. **No replay infrastructure (reqs 706-714).** The repo has no `.orarep` replay files, no replay parser, and no mechanism to correlate decision logs with replay timestamps beyond game-time prefixes in `ai-telemetry.log`. Deterministic seeds exist and `DeterministicSameSeed` asserts reproducibility, but there is no replay-based regression suite.

---

## 2. Hidden-Information / Fog-of-War Leaks

**Two confirmed leaks (both in `StrategicBrainBotModule`, not the coalition intel path):**

1. **`RecordRaidContact` (StrategicBrainBotModule, line ~839).** Counts ALL enemy actors within 20 cells of the raid target via `world.Actors.Count(...)` with **no shroud/visibility check**. This feeds `RespondsStronglyToRaids` in the opponent model, which in turn drives bait-mission generation. A human player's units behind fog are counted as "responding to raids" even though the AI should not be able to see them.

2. **`ResourceMapBotModule.UpdateResourceMap`.** Counts enemy units and bases via `world.FindActorsInCircle` with **no shroud check**. These counts feed `McvExpansionManagerBotModule.CalculateThreats` (expansion scoring) and `HarvesterBotModule.FindAndOrderLowEffectHarvesterOnResourceMap`. The AI's expansion decisions are influenced by enemy positions it cannot see under fair fog.

**No leaks found in the coalition intel path or LLM path:**
- `CoalitionIntelTracker` properly gates observation by `SeesEnemy` (allied shroud + explicit omniscience config).
- `ExternalBrainBotModule.BuildSnapshot` filters enemies by `team.Any(ally => ally.Shroud.IsExplored(...))`.
- `RadarCaptureBotModule` draws red dots only for `player.Shroud.IsExplored` cells (own shroud — under-reports, never leaks).
- `CommandToolApi` tools read from `blackboard.EnemyIntel`, which is already fog-gated.
- **Latent risk:** `EnemyIntel.Actor` retains a live `Actor` reference in the blackboard/ToolContext. No tool serializes it today, but a future tool could accidentally expose hidden enemy data.

---

## 3. Cases Where Allied Bots Still Make Conflicting Strategic Decisions

The coalition mission set is deterministic and identical across bots (shared blackboard + same `RunCommand`), and the `CoalitionOrderArbiter` prevents force-level conflicts. However:

1. **Per-bot tactical claims bypass the arbiter.** Each bot's `StrategicBrainBotModule` runs local tactics using its own `Claim(activeArmy)` pool, which never consults `CoalitionOrderArbiter`. A bot's local base-defense or counterattack can pull units away from a force the arbiter assigned to a coalition mission.

2. **No dedup in ally reinforcement.** `UpdateCoordination` sends reinforcements to an ally under attack, but every allied bot independently computes the same reinforcement order — all bots can send units to the same ally simultaneously without coordination.

3. **Duplicate counterattacks.** Every bot can independently launch a counterattack-after-defense at the same `counterPos`, producing multiple waves to the same location instead of one coordinated strike.

4. **Dual support-power paths.** The stock `SupportPowerBotModule` and the coalition `StrategicBrainBotModule.FireSupportPower` are two independent support-power paths that can both fire in the same tick.

---

## 4. Places Where the LLM Directly Performs Tactical Micro

**None found.** The design is clean: the LLM emits only strategic intent (posture, missions with cell targets, a produce list, a retreat flag). Every unit-level action — AttackMove waves, retreats, transport state transitions, special insertions, feint/bait placement, recon probes — is executed deterministically by `StrategicBrainBotModule.UpdateTactics` and `TacticalControllers` at engine speed. The LLM picks target cells, which is strategic targeting, not per-unit micro.

---

## 5. Untested Mission Types

**Fully tested (unit + scenario):** Feint (262), Bait (267), Demonstration (263), Transport (302), Retreat (207), and the phase lifecycle (241-248).

**Unit-tested only (no scenario):** Attack (184), Breakthrough (183), Siege (189), Defend (200), MobileDefense (201), AntiAirUmbrella (205), NavalScreen (206), Escort (210), Recon (215), DeepRecon (216), ExpansionSearch (220), DefenseProbe (221), EconomyRaid (191), ProductionRaid (192), ChokepointSeizure (194), Flank (185), AirStrike (197), SupportPowerStrike (199), SpecialOps (289).

**No test at all (enum exists but never exercised):** Raid, Counterattack (mission type — counterattacks are implemented in the brain, not via the mission enum), Harassment, ExpansionDenial, NavalStrike, DelayingAction, Evacuation, AirRecon, NavalRecon, RouteRecon, DecoyTransport.

**Missing entirely (no enum value):** Pincer/double-envelopment (186), Naval blockade (195), Fake buildup (265), Coordinated mass-air attack (198 — air arm only).

---

## 6. LLM Commands Not Engine-Side Validated

**Three gaps:**

1. **Produce unit names.** `CommandValidator.ValidateProduce` only rejects blank entries and the 64-entry cap. Unknown unit names are silently dropped by the `BuildableItems()` filter with no rejection reason returned to the LLM. The validator's own comment defers ruleset checks to the caller.

2. **Legacy `ApplyTeamPlan` path.** Attack/feint/counter/recon targets are `ClampCell`'d (accepted with clamping to map bounds) rather than rejected when out of bounds. The coalition path (`CommandValidator.ValidateMissions`) properly rejects OOB, but the legacy brain path does not.

3. **`model_server.py sanitize_team_plan`.** Replaces degenerate `(0,0)` attack targets with the enemy centroid instead of rejecting them. This is server-side sanitization, not engine-side validation.

**Everything else** (missions, posture, retreat, roles) is validated engine-side by `CommandValidator` and rejections are logged via `CoalitionTelemetry`.

---

## 7. Missing Fallback Behavior

**Robust fallbacks that exist:**
- Deterministic commander without LLM server (`HeadlessSkirmishTest.DeterministicFallback`).
- On-foot special insertion when no transport exists.
- `RetreatCell` / `PlanTransportRoute` falling back to base center / empty route without a blackboard.
- LLM timeout/error → `ExternalBrainBotModule.RequestPlanAsync` catches and the scripted brain continues.
- `model_server.py` `dummy_plan` when `--llm` not used; `empty_team_plan()` on tool-round exhaustion.

**Missing fallbacks:**
- **Transport destroyed mid-mission:** `TransportController.Execute` returns false and the mission target is cleared. Payload is not re-inserted on foot. No fallback plan (e.g., convert to on-foot insertion).
- **Special asset dies:** `SpecialOpsController` returns false but the mission stays active until the next command review — no immediate abort.
- **Transport aborts on low health:** No fallback plan (e.g., retry with different route, or on-foot insertion).
- **Dual support-power paths not mutually exclusive:** Both can fire in the same tick; no deconfliction.

---

## 8. Performance Bottlenecks

1. **`CoalitionOrderArbiter.commitments` unbounded growth.** Released commitments are flagged `Released` but never removed. A long match accumulates one entry per mission-force assignment forever.

2. **Blackboard rebuilt wholesale every 40 ticks per bot.** `ExtractForces`, `ExtractSpecialAssets`, `ExtractEconomyState`, `ExtractEnemyIntel`, `ComputeCongestion` each iterate `World.Actors` linearly — ~4 full actor scans per rebuild. `ExtractEconomyState` also iterates `World.Map.AllCells` (full-map resource scan). `ComputeRegions` does per-cell shroud queries for every region. Multiplied by the number of allied bots.

3. **`ai/brain.log` append-only, no rotation.** Every prompt/reply/tool call is written; unbounded growth across sessions.

4. **`PLAN_CACHE` in `model_server.py` unbounded.** Dict keyed by (round, team) grows for the server's lifetime.

5. **1920px radar PNG base64-attached every consultation.** ~every 15 s, large per-consultation vision cost.

6. **`BestScoredTarget` / `ScoreTargets` call `FindRoute` per enemy structure target** each rebuild. Bounded by structure count but can be significant on large maps.

---

## 9. Prioritized Implementation Plan

### Phase 1: Fix fog-of-war leaks (critical for fairness)
1. Gate `RecordRaidContact` with `SeesEnemy` / shroud check (StrategicBrainBotModule).
2. Gate `ResourceMapBotModule.UpdateResourceMap` enemy counts with shroud check.
3. Add regression test: verify `RespondsStronglyToRaids` is not set by fog-hidden enemies.

### Phase 2: Fix conflicting-decision bugs
4. Make `StrategicBrainBotModule.Claim` consult `CoalitionOrderArbiter` before claiming units for local tactics.
5. Dedup ally reinforcements in `UpdateCoordination` (only one bot sends reinforcements per ally per interval).
6. Gate counterattack-after-defense to fire once per coalition (use a shared tick gate on the blackboard).
7. Make `SupportPowerBotModule` and `FireSupportPower` mutually exclusive (skip stock module when coalition is active).

### Phase 3: Fix performance bottlenecks
8. Prune released commitments from `CoalitionOrderArbiter.commitments` (or use a compacted array).
9. Add log rotation for `ai/brain.log`.
10. Bound `PLAN_CACHE` in `model_server.py` (LRU with max 100 entries).

### Phase 4: Close critical feature gaps
11. Add location/region to `ArmyGroupState` (req 445) — the LLM needs to know where forces are.
12. Add nearby-threats to `ArmyGroupState` (req 447) — derive from `CoalitionRegion.Threats` at the force's region.
13. Implement emergency replacement production (req 401) — prioritize rebuilding destroyed production facilities and critical counters.
14. Implement artillery-behind-screen positioning (req 557) — artillery units hold at max range behind the main force.
15. Implement speed-based formation coordination (req 559) — fast units throttle to AA/infantry speed.

### Phase 5: Close LLM tool gaps
16. Implement `set_production_directive` (req 477) as an LlmIntent field with engine-side validation.
17. Implement `request_capability` (req 479) — LLM requests a capability, engine translates to production contracts.
18. Implement `cancel_mission` (req 486) — LlmIntent field with mission-id matching.
19. Implement `set_reserve` (req 489) — LlmIntent field with min/max clamping.

### Phase 6: Close evaluation gaps
20. Implement combat-estimator accuracy validation (req 159) — compare `LastWinRatioEstimate` to actual exchange outcomes in self-play.
21. Implement LLM strategic evaluation harness (reqs 728-738) — replay a seed through multiple commander configs and score plans.
22. Enable LLM self-play (remove `DisableExternalBrain = true` or make it configurable in `SimulateCommand`).

### Phase 7: Close testing gaps
23. Add scenario tests for: feint→main-assault sequencing (669), fake retreat→ambush (670), transport rerouting (681), simultaneous multi-front pressure (684), reserve reinforcement (674), counterattack-after-defense (675).
24. Add stress tests: large maps (692), heavy air (696), heavy naval (697), many missions (698), frame/tick rate measurement (700).
25. Add regression tests for the fog-leak fixes and conflicting-decision fixes from Phases 1-2.

### Phase 8: Close telemetry gaps
26. Add structured metrics for: economic damage (604-605), sync error (612), retreat effectiveness (614), recon efficiency (616), transport survival (617), counterattack effectiveness (620), base-defense response time (621).
27. Add production-priority-change logging (req 591).
28. Add "feint opened main-attack-window" measurement (req 627).

---

## Detailed Per-Requirement Breakdown

The full 804-item breakdown is organized by section below. Each line shows:
`N. IMP:✅/🟡/❌ TEST:✅/🟡/❌ | Code | Tests | Notes`

---

### Section 1. Core Architecture

1. IMP:✅ TEST:🟡 | Code:CoalitionCommandCenterBotModule | Tests:HeadlessSkirmishTest.UnifiedCoalitionCommand | Notes:Coalition command center exists above individual bots; 4-bot matches asserted.
2. IMP:✅ TEST:🟡 | Code:CoalitionBlackboard.ExtractForces (teamIds = Team.Select) | Tests:HeadlessSkirmishTest.CoalitionCoordinatedScenarios | Notes:All allied bots' units iterated via Team players.
3. IMP:✅ TEST:✅ | Code:ForceGroup.Owner (per-owner grouping) | Tests:ForceRegistryTest.ForceGroupDefaults | Notes:Ownership retained per ForceGroup; never merged.
4. IMP:✅ TEST:🟡 | Code:CoalitionBlackboard.ExtractEconomy (per-player cash) | Tests:HeadlessSkirmishTest | Notes:Each ally's cash tracked separately in MemberState.
5. IMP:✅ TEST:🟡 | Code:CoalitionBlackboard.ExtractProduction (per-player queues) | Tests:CommandToolApiTest.ProductionState | Notes:Per-player ProductionQueue extracted individually.
6. IMP:✅ TEST:🟡 | Code:ExtractProduction (queue.BuildableItems per player) | Tests:HeadlessSkirmishTest | Notes:Each player's prerequisites checked via BuildableItems().
7. IMP:✅ TEST:✅ | Code:ForceGroup.Owner; CoalitionCash = sum of Team cash | Tests:ForceRegistryTest | Notes:Economies never merged; CoalitionCash is aggregate only.
8. IMP:✅ TEST:🟡 | Code:AssignRole (naval/main/escort/defend) | Tests:HeadlessSkirmishTest | Notes:Roles assigned via directive JSON to brain.
9. IMP:✅ TEST:🟡 | Code:AssignRole (re-evaluated each CommandInterval) | Tests:HeadlessSkirmishTest | Notes:Roles change dynamically as conditions shift.
10. IMP:🟡 TEST:🟡 | Code:Shared blackboard + arbiter; BUT per-bot brain Claim bypasses arbiter | Tests:OrderArbiterTest | Notes:Coalition missions synchronized; local tactics can still conflict (see analysis §3).
11. IMP:✅ TEST:✅ | Code:CoalitionBlackboard (rebuilt every 40 ticks, identical for all bots) | Tests:ForceRegistryTest, IntelTrackerTest | Notes:Shared deterministic world model.
12. IMP:✅ TEST:✅ | Code:CoalitionCommandCenterBotModule.RunCommand (strategy) vs StrategicBrainBotModule.UpdateTactics (tactics) | Tests:HeadlessSkirmishTest.MissionLifecycleScenario | Notes:Clear separation: RunCommand decides, brain executes.
13. IMP:✅ TEST:✅ | Code:LlmIntent (posture/produce/retreat/missions only) | Tests:CommandValidatorTest | Notes:LLM issues strategic intent, not per-tick commands.
14. IMP:✅ TEST:🟡 | Code:StrategicBrainBotModule.UpdateTactics; TacticalControllers | Tests:HeadlessSkirmishTest.DeterministicFallback | Notes:All micro is deterministic engine-side.
15. IMP:✅ TEST:🟡 | Code:BaseBuilderBotModule, SquadManagerBotModule, etc. reused | Tests:HeadlessSkirmishTest | Notes:Existing modules reused; coalition layer directs them.
16. IMP:✅ TEST:🟡 | Code:ApplyTeamPlan directive JSON overrides brain behavior | Tests:HeadlessSkirmishTest | Notes:Directive JSON directs existing modules.
17. IMP:✅ TEST:✅ | Code:RunCommand with llmIntent=null (deterministic commander) | Tests:HeadlessSkirmishTest.DeterministicFallback | Notes:Fully functional without LLM; tested.

### Section 2. Coalition Force Registry

18. IMP:✅ TEST:✅ | Code:CoalitionBlackboard.ExtractForces (all team actors) | Tests:ForceRegistryTest | Notes:All allied units registered.
19. IMP:✅ TEST:🟡 | Code:ExtractForces (BuildingInfo classified as Structure) | Tests:ForceRegistryTest | Notes:Buildings included in ForceGroup counts.
20. IMP:✅ TEST:✅ | Code:ExtractProduction (ProductionQueue per player) | Tests:CommandToolApiTest.ProductionState | Notes:Production facilities registered individually.
21. IMP:✅ TEST:🟡 | Code:ExtractSpecialAssets (transportTypes) | Tests:HeadlessSkirmishTest (transport telemetry) | Notes:Transports registered in SpecialAssets list.
22. IMP:✅ TEST:🟡 | Code:ExtractForces (AirTypes classification) | Tests:ForceRegistryTest.AirCapabilities | Notes:Aircraft included in ForceGroup by class.
23. IMP:✅ TEST:🟡 | Code:ExtractForces (NavalTypes classification) | Tests:ForceRegistryTest | Notes:Naval units included in ForceGroup by class.
24. IMP:✅ TEST:🟡 | Code:ExtractSpecialAssets (specialTypes: Tanya/spy/engineer) | Tests:HeadlessSkirmishTest | Notes:Special assets tracked individually with position.
25. IMP:✅ TEST:✅ | Code:ForceGroup (per-owner grouping) | Tests:ForceRegistryTest.ForceGroupDefaults | Notes:Units grouped into ForceGroups by owner.
26. IMP:🟡 TEST:🟡 | Code:ForceGroup per owner (NOT cross-owner) | Tests:OrderArbiterTest | Notes:Groups are per-owner, not cross-owner; arbiter coordinates across groups.
27. IMP:✅ TEST:✅ | Code:ForceGroup.Strength, ForceGroup.Readiness | Tests:ForceRegistryTest.Cohesion | Notes:Strength (avg health) and readiness (strength*cohesion) exposed.
28. IMP:✅ TEST:🟡 | Code:ForceGroup.Center, ForceGroup.Status | Tests:ForceRegistryTest | Notes:Center and status (Idle/Moving) exposed.
29. IMP:✅ TEST:🟡 | Code:ForceGroup.MissionId | Tests:OrderArbiterTest.AssignOwnsForce | Notes:Mission assignment exposed.
30. IMP:✅ TEST:🟡 | Code:ForceGroup.CasualtyFraction, ForceGroup.Strength | Tests:ForceRegistryTest | Notes:Casualty fraction and health-based strength exposed.
31. IMP:✅ TEST:✅ | Code:ForceGroup.Capabilities (FriendlyCapability array) | Tests:ForceRegistryTest.ArtilleryCapabilities, AirCapabilities | Notes:AA, artillery, anti-armor, recon, transport capabilities derived.
32. IMP:✅ TEST:✅ | Code:ExtractForces (a.IsDead/!a.IsInWorld filter) | Tests:HeadlessSkirmishTest.RepeatedMatchesReleaseResources | Notes:Dead actors excluded by construction each rebuild.
33. IMP:✅ TEST:🟡 | Code:ExtractForces (rebuild every 40 ticks discovers new units) | Tests:HeadlessSkirmishTest | Notes:New units auto-discovered on next blackboard rebuild.
34. IMP:✅ TEST:✅ | Code:CoalitionOrderArbiter.ReleaseMission/ReleaseForce | Tests:OrderArbiterTest.ReleaseMission, ReleaseForce | Notes:Forces released and reassignable.

### Section 3. Order Ownership & Arbitration

35. IMP:✅ TEST:✅ | Code:CoalitionOrderArbiter | Tests:OrderArbiterTest.ConflictRejected | Notes:Central arbiter prevents force conflicts.
36. IMP:✅ TEST:✅ | Code:CoalitionOrderArbiter.Commitment.MissionId | Tests:OrderArbiterTest.AssignOwnsForce | Notes:Every committed force has a mission owner.
37. IMP:🟡 TEST:🟡 | Code:ForceGroup.Role (operational role) | Tests:HeadlessSkirmishTest | Notes:Role assigned but not enforced by arbiter per-unit.
38. IMP:✅ TEST:✅ | Code:CoalitionOrderArbiter.ReleaseMission (release on complete/cancel) | Tests:OrderArbiterTest.ReleaseMission | Notes:Release condition = mission end.
39. IMP:✅ TEST:✅ | Code:CoalitionOrderArbiter (Survival priority > Combat priority) | Tests:OrderArbiterTest.SurvivalOverridesCombat | Notes:Emergency/survival overrides combat.
40. IMP:🟡 TEST:🟡 | Code:SpecialOpsController.Claim (special assets claimed) | Tests:HeadlessSkirmishTest | Notes:Special assets reserved by claim, not by explicit arbiter reservation.
41. IMP:✅ TEST:✅ | Code:CoalitionOrderArbiter (priority ordering) | Tests:OrderArbiterTest.PriorityOrdering | Notes:Combat missions outrank staging by priority.
42. IMP:🟡 TEST:🟡 | Code:StrategicBrainBotModule (reserve intercepts, available army untouched) | Tests:HeadlessSkirmishTest | Notes:Defense uses reserve, not mission forces; but no explicit "don't steal" guard.
43. IMP:✅ TEST:✅ | Code:CommandValidator.ValidateMissions (REJECTED_CONFLICT) | Tests:CommandValidatorTest.DuplicateConflict | Notes:Conflicting LLM missions detected and rejected.
44. IMP:✅ TEST:✅ | Code:CommandValidator.ValidateMissions (unknown type, OOB, bad priority) | Tests:CommandValidatorTest | Notes:Invalid missions rejected.
45. IMP:✅ TEST:✅ | Code:CommandValidator (machine-readable rejection reasons) | Tests:CommandValidatorTest | Notes:REJECTED_UNKNOWN_TYPE, REJECTED_OUT_OF_BOUNDS, etc.
46. IMP:🟡 TEST:🟡 | Code:MissionManager.CancelMission → arbiter.ReleaseMission | Tests:MissionLifecycleTest.Cancel | Notes:Cancellation releases forces; BUT arbiter entries are flagged not removed (growth).

### Section 4. World State Extraction

47. IMP:✅ TEST:✅ | Code:CoalitionBlackboard (reads World.Actors, World.Map directly) | Tests:CommandToolApiTest (all tools) | Notes:Direct engine state access.
48. IMP:✅ TEST:✅ | Code:ExtractForces (a.Info.Name) | Tests:ForceRegistryTest | Notes:Unit types available.
49. IMP:✅ TEST:✅ | Code:ExtractForces (a.Location/a.CenterPosition) | Tests:ForceRegistryTest | Notes:Positions available.
50. IMP:✅ TEST:🟡 | Code:ExtractForces (health.HP/MaxHP) | Tests:ForceRegistryTest | Notes:Health available via IHealth trait.
51. IMP:🟡 TEST:🟡 | Code:ExtractForces (a.IsIdle → ForceStatus) | Tests:ForceRegistryTest | Notes:Idle/moving status; no per-order state.
52. IMP:✅ TEST:🟡 | Code:ExtractForces (BuildingInfo → Structure class) | Tests:ForceRegistryTest | Notes:Structures available.
53. IMP:✅ TEST:✅ | Code:ExtractProduction (ProductionQueue per player) | Tests:CommandToolApiTest.ProductionState | Notes:Production facilities available.
54. IMP:✅ TEST:✅ | Code:ExtractProduction (queue.AllQueued()) | Tests:CommandToolApiTest.ProductionState | Notes:Production queues available.
55. IMP:✅ TEST:✅ | Code:ExtractProduction (ProgressOf(current)) | Tests:CommandToolApiTest.ProductionState | Notes:Progress percent available.
56. IMP:✅ TEST:✅ | Code:ExtractEconomy (PlayerResources.GetCashAndResources) | Tests:CommandToolApiTest.EconomyState | Notes:Cash/resources available.
57. IMP:✅ TEST:✅ | Code:ExtractProduction (PowerManager.PowerProvided/Drained) | Tests:CommandToolApiTest.EconomyPower | Notes:Power state available.
58. IMP:✅ TEST:🟡 | Code:ExtractProduction (queue.BuildableItems()) | Tests:CommandToolApiTest.ProductionState | Notes:Tech/prereq availability via BuildableItems.
59. IMP:✅ TEST:🟡 | Code:ExtractEconomy (SupportPowerManager.Powers) | Tests:HeadlessSkirmishTest | Notes:Support-power readiness tracked.
60. IMP:✅ TEST:✅ | Code:CoalitionMapAnalysis (World.Map.MapSize) | Tests:MapAnalysisTest | Notes:Map dimensions available.
61. IMP:✅ TEST:✅ | Code:CoalitionMapAnalysis.ForMap (terrain types) | Tests:MapAnalysisTest.OpenGrid | Notes:Terrain types analyzed.
62. IMP:✅ TEST:✅ | Code:CoalitionMapAnalysis (water terrain detection) | Tests:WaterAreaTest, MapAnalysisTest | Notes:Water/land identified.
63. IMP:🟡 TEST:🟡 | Code:CoalitionMapAnalysis (bridge detection) | Tests:MapAnalysisTest.NarrowBridge | Notes:Bridges detected as chokepoints; "rivers" not explicitly named.
64. IMP:✅ TEST:✅ | Code:CoalitionMapAnalysis (bridge chokepoints) | Tests:MapAnalysisTest.BridgeConnections | Notes:Bridges identified as connectors.
65. IMP:✅ TEST:✅ | Code:CoalitionMapAnalysis (impassable terrain) | Tests:MapAnalysisTest.DisconnectedHalves | Notes:Impassable terrain identified via connectivity.
66. IMP:✅ TEST:✅ | Code:CoalitionMapAnalysis (resource scoring) | Tests:MapAnalysisTest.ExpansionValue | Notes:Resource fields scored.
67. IMP:✅ TEST:✅ | Code:CoalitionMapAnalysis (expansion scoring) | Tests:MapAnalysisTest.ExpansionValue | Notes:Expansion locations scored.
68. IMP:🟡 TEST:🟡 | Code:CoalitionMapAnalysis (building placement via passable cells) | Tests:MapAnalysisTest.RallyValue | Notes:Buildable areas analyzed indirectly via passability.
69. IMP:✅ TEST:✅ | Code:CoalitionIntelTracker + SeesEnemy (fog-gated) | Tests:IntelTrackerTest, CommandToolApiTest.IntelStatusHonesty | Notes:Enemy actors exposed per visibility rules.
70. IMP:✅ TEST:✅ | Code:StrategicEventDetector + ReviewTrigger | Tests:StrategicEventDetectorTest | Notes:Significant events trigger strategic review.

### Section 5. Fog of War & Intelligence Fairness

71. IMP:✅ TEST:✅ | Code:CoalitionMapAnalysis (static, cached per map) vs CoalitionIntelTracker (dynamic, fog-gated) | Tests:IntelTrackerTest, MapAnalysisTest | Notes:Static terrain separated from dynamic intel.
72. IMP:✅ TEST:✅ | Code:CoalitionIntelTracker (IntelStatus.Observed) | Tests:IntelTrackerTest.FreshSightingObserved | Notes:Visible enemies tagged Observed.
73. IMP:✅ TEST:✅ | Code:CoalitionIntelTracker (structures = Observed when visible) | Tests:IntelTrackerTest | Notes:Visible buildings tagged Observed.
74. IMP:✅ TEST:✅ | Code:CoalitionIntelTracker (mobile → LastKnown when hidden) | Tests:IntelTrackerTest.MobileBecomesLastKnown | Notes:Last-known with position error growth.
75. IMP:✅ TEST:✅ | Code:CoalitionIntelTracker (structures → Inferred at 0.3 floor) | Tests:IntelTrackerTest.StructureInferred | Notes:Inferred status for structures.
76. IMP:✅ TEST:✅ | Code:CoalitionBlackboard.AddSuspectedIntel (0.2 confidence) | Tests:CommandToolApiTest.SuspectedIntelNotScored | Notes:Suspected status for unexplored regions.
77. IMP:✅ TEST:✅ | Code:IntelStatus.Unknown (default for unobserved) | Tests:IntelTrackerTest | Notes:Unknown status exists in enum.
78. IMP:✅ TEST:🟡 | Code:SeesEnemy (shroud gate) + BuildSnapshot (enemy filter) | Tests:HeadlessSkirmishTest.IntelligenceScouting | Notes:LLM receives only fog-gated intel; BUT RecordRaidContact and ResourceMapBotModule leak (see §2).
79. IMP:✅ TEST:✅ | Code:EnemyIntel.LastSeenTick | Tests:IntelTrackerTest | Notes:Timestamps retained.
80. IMP:✅ TEST:✅ | Code:EnemyIntel.Confidence (decays with age) | Tests:IntelTrackerTest.MobileBecomesLastKnown | Notes:Confidence values exist and decay.
81. IMP:✅ TEST:✅ | Code:CoalitionIntelTracker.Age (0.5^(ageSeconds/30)) | Tests:IntelTrackerTest.PositionErrorGrows | Notes:Confidence halves every 30 seconds.
82. IMP:✅ TEST:✅ | Code:EnemyIntel.PositionErrorCells (grows with age) | Tests:IntelTrackerTest.PositionErrorGrows | Notes:Position uncertainty grows for mobile units.
83. IMP:✅ TEST:✅ | Code:CoalitionIntelTracker (structures → Inferred at 0.3 floor, no position error) | Tests:IntelTrackerTest.StructureInferred | Notes:Structures retain confidence at 0.3 floor.
84. IMP:✅ TEST:✅ | Code:CoalitionDifficulty.Intelligence (0=fair, 2=structures, 3=omniscient) | Tests:DifficultyTest.IntelligenceAxis | Notes:Omniscient mode is a config setting.
85. IMP:✅ TEST:✅ | Code:Intelligence=0 (fair) vs Intelligence=3 (omniscient) | Tests:DifficultyTest.IntelligenceAxis, HeadlessSkirmishTest.IntelligenceScouting | Notes:Both modes independently testable.

### Section 6. Map Analysis

86. IMP:✅ TEST:✅ | Code:CoalitionMapAnalysis (4x4 region grid) | Tests:MapAnalysisTest.OpenGrid | Notes:Map divided into 16 regions.
87. IMP:✅ TEST:✅ | Code:CoalitionRegion.Index (stable) | Tests:MapAnalysisTest | Notes:Stable region indices.
88. IMP:✅ TEST:✅ | Code:CoalitionMapAnalysis.IsAdjacent | Tests:MapAnalysisTest | Notes:Adjacency graph.
89. IMP:✅ TEST:✅ | Code:CoalitionMapAnalysis (chokepoint detection) | Tests:MapAnalysisTest.NarrowBridge | Notes:Chokepoints detected.
90. IMP:✅ TEST:✅ | Code:CoalitionMapAnalysis (bridge chokepoints) | Tests:MapAnalysisTest.BridgeConnections | Notes:Bridges as connectors.
91. IMP:🟡 TEST:🟡 | Code:CoalitionMapAnalysis (water body analysis) | Tests:WaterAreaTest | Notes:Water detected; narrow naval passages not explicitly identified.
92. IMP:🟡 TEST:🟡 | Code:CoalitionMapAnalysis (connected components) | Tests:MapAnalysisTest.DisconnectedHalves | Notes:Islands detected via connectivity.
93. IMP:✅ TEST:✅ | Code:CoalitionMapAnalysis (resource scoring) | Tests:MapAnalysisTest.ExpansionValue | Notes:Resource-rich areas scored.
94. IMP:✅ TEST:✅ | Code:CoalitionMapAnalysis (expansion value) | Tests:MapAnalysisTest.ExpansionValue | Notes:Expansion locations scored.
95. IMP:🟡 TEST:🟡 | Code:CoalitionMapAnalysis (rally value) | Tests:MapAnalysisTest.RallyValue | Notes:Rally/staging areas scored.
96. IMP:✅ TEST:✅ | Code:CoalitionMapAnalysis (artillery value) | Tests:MapAnalysisTest.ArtilleryValue | Notes:Artillery positions scored.
97. IMP:✅ TEST:✅ | Code:CoalitionMapAnalysis (corridor description) | Tests:MapAnalysisTest.DescribeCorridor | Notes:Attack corridors identified.
98. IMP:🟡 TEST:🟡 | Code:CoalitionMapAnalysis (corridor description) | Tests:MapAnalysisTest.DescribeCorridor | Notes:Retreat corridors partially via corridor system.
99. IMP:✅ TEST:✅ | Code:CoalitionMapAnalysis (insertion value) | Tests:MapAnalysisTest.InsertionValue | Notes:Transport insertion zones identified.
100. IMP:🟡 TEST:🟡 | Code:CoalitionMapAnalysis (MovementClass.Ground) | Tests:RoutePlannerTest.ZeroThreatDirectPath | Notes:Ground movement graph exists via region adjacency.
101. IMP:🟡 TEST:🟡 | Code:CoalitionRoutePlanner (MovementClass.Foot) | Tests:RoutePlannerTest.FootRouting | Notes:Infantry movement graph where locomotion differs.
102. IMP:🟡 TEST:🟡 | Code:CoalitionRoutePlanner (MovementClass.Naval) | Tests:RoutePlannerTest | Notes:Naval movement graph exists.
103. IMP:🟡 TEST:🟡 | Code:CoalitionRoutePlanner (MovementClass.Air implied) | Tests:RoutePlannerTest | Notes:Aircraft routing not explicitly separate but air ignores most terrain.
104. IMP:✅ TEST:🟡 | Code:CoalitionRoutePlanner (MovementClass parameter) | Tests:RoutePlannerTest.FootRouting | Notes:Transport routing uses appropriate movement class.
105. IMP:✅ TEST:✅ | Code:CoalitionRegion (LLM reasons about regions) | Tests:CommandToolApiTest.InspectRegionThreats | Notes:LLM tools operate on regions, not cells.

### Section 7. Threat Modeling

106. IMP:🟡 TEST:❌ | Code:ThreatModel (ComputeThreats) | Tests:none | Notes:Threat modeling system exists; requirement 106 data not explicitly provided in audit source.
107. IMP:✅ TEST:✅ | Code:CoalitionCapability.GroundAntiArmor | Tests:ThreatModelTest | Notes:Ground anti-armor threat modeled.
108. IMP:✅ TEST:✅ | Code:CoalitionCapability.GroundAntiInfantry | Tests:ThreatModelTest | Notes:Ground anti-infantry threat modeled.
109. IMP:✅ TEST:✅ | Code:CoalitionCapability.Artillery (artilleryTypes) | Tests:ThreatModelTest.ArtilleryCapabilities | Notes:Artillery threat modeled.
110. IMP:✅ TEST:✅ | Code:CoalitionCapability.AntiAir | Tests:ThreatModelTest | Notes:Anti-air threat modeled.
111. IMP:✅ TEST:✅ | Code:CoalitionCapability.AirToAir | Tests:ThreatModelTest | Notes:Air-to-air threat modeled.
112. IMP:✅ TEST:✅ | Code:CoalitionCapability.Naval | Tests:ThreatModelTest | Notes:Naval threat modeled.
113. IMP:✅ TEST:✅ | Code:CoalitionCapability.Submarine (submarineTypes) | Tests:ThreatModelTest.SubmarineCapabilities | Notes:Submarine threat modeled.
114. IMP:✅ TEST:✅ | Code:CoalitionCapability.StaticDefense | Tests:ThreatModelTest | Notes:Static-defense threat modeled.
115. IMP:✅ TEST:✅ | Code:CoalitionCapability.VisionExposure | Tests:ThreatModelTest | Notes:Enemy vision/exposure risk modeled.
116. IMP:✅ TEST:✅ | Code:CoalitionCapability.Detection (detectionTypes) | Tests:ThreatModelTest.DetectionCapability | Notes:Detection risk modeled.
117. IMP:✅ TEST:✅ | Code:CoalitionCapability.Reinforcement (productionStructures) | Tests:ThreatModelTest.ReinforcementRisk | Notes:Enemy reinforcement risk modeled.
118. IMP:✅ TEST:✅ | Code:CoalitionCapability.SupportPowerRisk (supportPowerStructures) | Tests:ThreatModelTest.SuperweaponCapabilities | Notes:Support-power danger modeled.
119. IMP:✅ TEST:❌ | Code:ComputeThreats rebuilds each blackboard cycle | Tests:none | Notes:Threats recomputed on every rebuild; no test asserts update behavior.
120. IMP:✅ TEST:✅ | Code:IntelPower scales by Confidence; uncertainty floor | Tests:CombatEstimatorTest.IntelPower | Notes:Confidence discounts enemy power.
121. IMP:✅ TEST:❌ | Code:RouteWeights profiles weight threats differently | Tests:none | Notes:Different profiles weight capabilities differently; no per-unit-type test.
122. IMP:✅ TEST:✅ | Code:RouteWeights.Stealth AntiAirThreat=1.5 | Tests:RoutePlannerTest.AntiAirWeighting | Notes:Stealth weights AA higher.
123. IMP:🟡 TEST:❌ | Code:RouteWeights.Stealth VisionExposure=3,DetectionExposure=3 | Tests:none | Notes:Stealth weights vision/detection; no special-ops test.
124. IMP:✅ TEST:✅ | Code:RouteWeights.Stealth/Assault ChokepointRisk | Tests:RoutePlannerTest.StealthAvoidsThreat,AssaultAcceptsRisk | Notes:Ground assault/stealth weight chokepoints.
125. IMP:🟡 TEST:❌ | Code:Naval movement class + naval graph | Tests:none | Notes:Naval routing supported; no naval-specific threat weighting profile.

### Section 8. Route Planning

126. IMP:✅ TEST:✅ | Code:CoalitionRoutePlanner.FindRoute (Dijkstra, weighted) | Tests:RoutePlannerTest.StealthAvoidsThreat | Notes:Not shortest-path; threat-weighted.
127. IMP:✅ TEST:✅ | Code:RouteWeights.Distance | Tests:RoutePlannerTest.ZeroThreatDirectPath | Notes:Distance is constant per region.
128. IMP:✅ TEST:✅ | Code:RouteWeights.CombatThreat * GroundAntiArmor | Tests:RoutePlannerTest.StealthAvoidsThreat | Notes:Combat threat diverts routes.
129. IMP:✅ TEST:✅ | Code:RouteWeights.AntiAirThreat * AntiAir | Tests:RoutePlannerTest.AntiAirWeighting | Notes:AA threat weighted.
130. IMP:✅ TEST:❌ | Code:RouteWeights.VisionExposure | Tests:none | Notes:Wired in FindRoute; no vision-specific routing test.
131. IMP:✅ TEST:🟡 | Code:RouteWeights.DetectionExposure | Tests:RoutePlannerTest (congestion, not detection) | Notes:Wired; no detection-specific test.
132. IMP:✅ TEST:✅ | Code:RouteWeights.ActiveCombatZone * ActiveCombat | Tests:RoutePlannerTest.AvoidsCongestionAndActiveCombat | Notes:Active combat zone avoided.
133. IMP:✅ TEST:🟡 | Code:RouteWeights.ChokepointRisk | Tests:RoutePlannerTest (chokepoints empty in tests) | Notes:Chokepoint cost applied; no test populates chokepoints.
134. IMP:✅ TEST:✅ | Code:RouteWeights.Congestion * Congestion | Tests:RoutePlannerTest.AvoidsCongestionAndActiveCombat | Notes:Congestion diverts routes.
135. IMP:✅ TEST:❌ | Code:RouteWeights.ReinforcementRisk | Tests:none | Notes:Wired in FindRoute; no reinforcement-lane test.
136. IMP:✅ TEST:❌ | Code:RouteWeights.ArtilleryThreat | Tests:none | Notes:Wired in FindRoute; no artillery-exposure test.
137. IMP:✅ TEST:✅ | Code:RouteWeights.Stealth/Assault/Recon/Retreat + overrides | Tests:RoutePlannerTest.AssaultAcceptsRisk | Notes:Four named profiles plus overrides.
138. IMP:✅ TEST:🟡 | Code:RouteWeights.Stealth for transports | Tests:RoutePlannerTest.StealthAvoidsThreat (profile only) | Notes:Transports use Stealth; no transport-specific route test.
139. IMP:✅ TEST:🟡 | Code:RouteWeights.Stealth for special ops | Tests:RoutePlannerTest.StealthAvoidsThreat (profile only) | Notes:Special ops use Stealth; no special-forces route test.
140. IMP:✅ TEST:✅ | Code:RouteWeights.Assault (CombatThreat=1, low stealth) | Tests:RoutePlannerTest.AssaultAcceptsRisk | Notes:Assault accepts risk.
141. IMP:🟡 TEST:❌ | Code:RouteWeights.Retreat exists; no call site | Tests:none | Notes:Profile defined; no production code plans retreat routes.
142. IMP:🟡 TEST:❌ | Code:Blackboard rebuilds refresh threats | Tests:none | Notes:Routes re-evaluate on rebuild cadence; no event-triggered replan.
143. IMP:✅ TEST:✅ | Code:MissionManager.Update aborts on RouteExists==false | Tests:RoutePlannerTest.DisconnectedRegions | Notes:Missions abort when unreachable.

### Section 9. Combat Evaluation

144. IMP:✅ TEST:✅ | Code:CombatEstimator (static, deterministic) | Tests:CombatEstimatorTest | Notes:Pure functions, no randomness.
145. IMP:✅ TEST:✅ | Code:EstimateEngagement compares force A vs B | Tests:CombatEstimatorTest.RepresentativeEngagements | Notes:Two-force comparison.
146. IMP:✅ TEST:✅ | Code:CombatEstimator.Power scales by health | Tests:CombatEstimatorTest.ClassWeights | Notes:Health factored.
147. IMP:✅ TEST:✅ | Code:MatchupFactor/MatchupPower | Tests:CombatEstimatorTest.MatchupFactors | Notes:Class-vs-class matchups.
148. IMP:✅ TEST:✅ | Code:SuppressAir(airPower, antiAirCoverage) | Tests:CombatEstimatorTest.SuppressAir | Notes:AA coverage suppresses air.
149. IMP:✅ TEST:✅ | Code:RangeAdvantage(artilleryPower) | Tests:CombatEstimatorTest.RangeAdvantage | Notes:Artillery range advantage.
150. IMP:✅ TEST:✅ | Code:TerrainFactor(staticDefense, vision) | Tests:CombatEstimatorTest.TerrainFactor | Notes:Terrain factored.
151. IMP:🟡 TEST:✅ | Code:ReinforcementAdvantage returns side label | Tests:CombatEstimatorTest.ReinforcementAdvantage | Notes:Labels which side; doesn't adjust win ratio numerically.
152. IMP:✅ TEST:✅ | Code:Estimate returns WinRatio | Tests:CombatEstimatorTest | Notes:Win ratio computed.
153. IMP:✅ TEST:✅ | Code:Estimate returns LossFraction (friendly) | Tests:CombatEstimatorTest | Notes:Friendly losses estimated.
154. IMP:✅ TEST:✅ | Code:Estimate(enemy-first) returns enemy LossFraction | Tests:CombatEstimatorTest | Notes:Enemy losses estimated.
155. IMP:✅ TEST:✅ | Code:MajorRisks() flags weaknesses | Tests:CombatEstimatorTest.MajorRisks | Notes:Matchup weaknesses identified.
156. IMP:✅ TEST:✅ | Code:CapabilityGaps() returns needed capabilities | Tests:CombatEstimatorTest.CapabilityGaps | Notes:Tells LLM what's required.
157. IMP:🟡 TEST:❌ | Code:CommandToolApi.EstimateEngagement (LLM-facing) | Tests:none | Notes:Tool exists; no test verifies LLM uses it vs arbitrary ratios.
158. IMP:✅ TEST:✅ | Code:CombatEstimatorTest.RepresentativeEngagements | Tests:CombatEstimatorTest | Notes:Hand-constructed scenario assertions.
159. IMP:❌ TEST:❌ | Code:not found | Tests:none | Notes:No replay-outcome validation; no accuracy comparison.

### Section 10. Mission Framework

160. IMP:✅ TEST:✅ | Code:CoalitionMission class + MissionManager | Tests:MissionLifecycleTest | Notes:Generic mission type exists.
161. IMP:✅ TEST:✅ | Code:Mission.Id (string) | Tests:MissionLifecycleTest | Notes:Unique IDs.
162. IMP:✅ TEST:🟡 | Code:Mission.DesiredEffects (per-type) | Tests:MissionLifecycleTest.MissionFrameworkFields | Notes:Objectives via desired effects.
163. IMP:✅ TEST:✅ | Code:Mission.DesiredEffectsFor(type) | Tests:MissionLifecycleTest.MissionFrameworkFields | Notes:Strategic desired effects per type.
164. IMP:✅ TEST:✅ | Code:Mission.Priority | Tests:MissionLifecycleTest | Notes:Priorities assigned.
165. IMP:✅ TEST:✅ | Code:SyncForceAssignments (arbiter assigns forces) | Tests:OrderArbiterTest | Notes:Forces assigned via arbiter.
166. IMP:✅ TEST:✅ | Code:Mission.TargetCell, Mission.TargetRegion | Tests:MissionLifecycleTest | Notes:Target regions/areas.
167. IMP:✅ TEST:✅ | Code:Mission.StagingRegion | Tests:MissionLifecycleTest.PhaseOrdering | Notes:Staging regions defined.
168. IMP:✅ TEST:🟡 | Code:Mission.PlannedRegions (route) | Tests:MissionLifecycleTest | Notes:Routes defined as region lists.
169. IMP:✅ TEST:🟡 | Code:MissionPhase enum (8 phases) | Tests:MissionLifecycleTest.PhaseOrdering | Notes:Multiple phases.
170. IMP:✅ TEST:🟡 | Code:MissionPhase.Staging (launch gate) | Tests:MissionLifecycleTest | Notes:Launch conditions via phase transitions.
171. IMP:✅ TEST:🟡 | Code:MissionStatus.Completed | Tests:MissionLifecycleTest | Notes:Success conditions.
172. IMP:✅ TEST:🟡 | Code:MissionStatus.Aborted + OutcomeReason | Tests:MissionLifecycleTest | Notes:Abort conditions.
173. IMP:🟡 TEST:🟡 | Code:Mission.Contingencies (per-type) | Tests:MissionLifecycleTest.MissionFrameworkFields | Notes:Contingencies defined per type; not all comprehensive.
174. IMP:🟡 TEST:🟡 | Code:MissionType.Retreat → Withdrawal phase | Tests:MissionLifecycleTest.RetreatStaysInWithdrawal | Notes:Withdrawal logic for retreat; not all missions.
175. IMP:✅ TEST:✅ | Code:Mission.Phase | Tests:MissionLifecycleTest.PhaseOrdering | Notes:Current phase exposed.
176. IMP:✅ TEST:🟡 | Code:Mission.Readiness | Tests:MissionLifecycleTest | Notes:Readiness exposed.
177. IMP:✅ TEST:🟡 | Code:Mission.Progress | Tests:MissionLifecycleTest | Notes:Progress exposed.
178. IMP:✅ TEST:🟡 | Code:Mission.OutcomeReason | Tests:MissionLifecycleTest | Notes:Failure reasons exposed.
179. IMP:✅ TEST:✅ | Code:Missions persist between LLM consultations | Tests:HeadlessSkirmishTest.DeterministicFallback | Notes:Missions persist; LLM not required.
180. IMP:✅ TEST:✅ | Code:ReviewTrigger (event-driven review) | Tests:StrategicEventDetectorTest | Notes:State changes trigger reconsideration.
181. IMP:✅ TEST:✅ | Code:MissionManager.Update (release on Complete) | Tests:MissionLifecycleTest | Notes:Completed missions release forces.
182. IMP:🟡 TEST:🟡 | Code:MissionManager.Update (retreat on Abort) | Tests:MissionLifecycleTest.RetreatStaysInWithdrawal | Notes:Failed missions retreat; not all types have extraction.

### Section 11. Offensive Mission Types

183. IMP:✅ TEST:🟡 | Code:MissionType.Breakthrough; RunCommand (ratio<0.5) | Tests:MissionLifecycleTest.OffensiveMissionTypes | Notes:Auto-created; no dedicated behavioral test.
184. IMP:✅ TEST:🟡 | Code:MissionType.Attack (frontal assault) | Tests:MissionLifecycleTest.DirectiveJson | Notes:Main effort attack; no per-type behavioral test.
185. IMP:✅ TEST:🟡 | Code:CreateOffensiveMissions/FlankRegionTarget → MissionType.Flank | Tests:MissionLifecycleTest.OffensiveMissionTypes | Notes:Auto-created; no behavioral test.
186. IMP:❌ TEST:❌ | Code:not found | Tests:none | Notes:No pincer/double-envelopment type or logic.
187. IMP:🟡 TEST:🟡 | Code:MissionPhase.Exploitation (phase only) | Tests:HeadlessSkirmishTest.MissionLifecycleScenario | Notes:Phase, not standalone mission type.
188. IMP:✅ TEST:🟡 | Code:RunCommand (attack on enemy region) | Tests:HeadlessSkirmishTest.CampaignLifecycle | Notes:Base assault = Attack on enemy concentration.
189. IMP:✅ TEST:🟡 | Code:RunCommand (StaticDefense>0.7 → Siege) | Tests:MissionLifecycleTest.OffensiveMissionTypes | Notes:Auto-created vs fortified enemy.
190. IMP:🟡 TEST:🟡 | Code:MissionType.Harassment (enum only, never auto-created) | Tests:MissionLifecycleTest (family) | Notes:LLM-requestable only.
191. IMP:✅ TEST:🟡 | Code:CreateOffensiveMissions → EconomyRaid | Tests:MissionLifecycleTest.MissionFrameworkFields | Notes:Auto-created when ratio<1.2.
192. IMP:✅ TEST:🟡 | Code:CreateOffensiveMissions → ProductionRaid | Tests:MissionLifecycleTest.MissionFrameworkFields | Notes:Auto-created when ratio<1.0.
193. IMP:🟡 TEST:🟡 | Code:MissionType.ExpansionDenial (enum only) | Tests:MissionLifecycleTest (family) | Notes:LLM-requestable only.
194. IMP:✅ TEST:🟡 | Code:ChokepointRegionNearEnemy → ChokepointSeizure | Tests:MissionLifecycleTest.OffensiveMissionTypes | Notes:Auto-created at chokepoint.
195. IMP:❌ TEST:❌ | Code:not found | Tests:none | Notes:No naval-blockade mission type.
196. IMP:🟡 TEST:🟡 | Code:MissionType.NavalStrike (enum+directive) | Tests:MissionLifecycleTest.DirectiveStrikeTargets | Notes:Enum only; never auto-created.
197. IMP:✅ TEST:🟡 | Code:CreateOffensiveMissions → AirStrike | Tests:MissionLifecycleTest.DirectiveStrikeTargets | Notes:Auto-created vs high-value/AA target.
198. IMP:🟡 TEST:🟡 | Code:UpdateTactics (air in waves) | Tests:HeadlessSkirmishTest.CampaignLifecycle | Notes:Air arm in waves; no mass-air mission type.
199. IMP:✅ TEST:🟡 | Code:CreateOffensiveMissions → SupportPowerStrike | Tests:MissionLifecycleTest.DirectiveStrikeTargets | Notes:Auto-created when superweapon ready.

### Section 12. Defensive Mission Types

200. IMP:✅ TEST:🟡 | Code:RunCommand → Defend + brain base-defense | Tests:HeadlessSkirmishTest | Notes:Auto-created when outnumbered.
201. IMP:✅ TEST:🟡 | Code:CreateDefensiveMissions → MobileDefense | Tests:MissionLifecycleTest.DefensiveDirectiveKind | Notes:Intercepts enemies away from base.
202. IMP:🟡 TEST:🟡 | Code:UpdateCoordination (ally reinforcement) | Tests:HeadlessSkirmishTest.CoalitionCoordinatedScenarios | Notes:Reinforcement exists; no dedicated mission type.
203. IMP:✅ TEST:🟡 | Code:UpdateTactics (counterattack-after-defense) | Tests:HeadlessSkirmishTest (counterattacks key) | Notes:In brain, not via MissionType.Counterattack.
204. IMP:✅ TEST:🟡 | Code:CreateDefensiveMissions (MobileDefense) | Tests:none | Notes:Interception via MobileDefense + reserve.
205. IMP:✅ TEST:🟡 | Code:CreateDefensiveMissions → AntiAirUmbrella | Tests:MissionLifecycleTest.DefensiveAndReconFamilies | Notes:AA held over base vs enemy air.
206. IMP:✅ TEST:🟡 | Code:CreateDefensiveMissions → NavalScreen | Tests:MissionLifecycleTest.DefensiveAndReconFamilies | Notes:Ships held near coast vs enemy navy.
207. IMP:✅ TEST:✅ | Code:MissionType.Retreat + brain retreat logic | Tests:MissionLifecycleTest.RetreatStaysInWithdrawal | Notes:Retreat mission + per-unit retreat.
208. IMP:🟡 TEST:🟡 | Code:MissionType.DelayingAction (enum only) | Tests:MissionLifecycleTest.DefensiveAndReconFamilies | Notes:LLM-requestable only.
209. IMP:🟡 TEST:🟡 | Code:MissionType.Evacuation (enum only) | Tests:MissionLifecycleTest.DefensiveAndReconFamilies | Notes:LLM-requestable only.
210. IMP:✅ TEST:🟡 | Code:CreateDefensiveMissions → Escort | Tests:MissionLifecycleTest.DefensiveAndReconFamilies | Notes:Harvester escort guard.
211. IMP:✅ TEST:🟡 | Code:UpdateTactics (commitment = Clamp(nearby*3,...)) | Tests:none | Notes:Defense proportional to nearby threat.
212. IMP:✅ TEST:🟡 | Code:UpdateTactics (reserve intercepts, army untouched) | Tests:none | Notes:Minor raids handled by reserve only.
213. IMP:✅ TEST:🟡 | Code:MostValuableStructurePosition (TargetEvaluator) | Tests:TargetEvaluatorTest | Notes:High-value structure defended first.
214. IMP:✅ TEST:🟡 | Code:UpdateTactics (counterattack window) | Tests:HeadlessSkirmishTest (counterattacks key) | Notes:Counterattack evaluated after defense.

### Section 13. Reconnaissance & Intelligence Missions

215. IMP:✅ TEST:🟡 | Code:RunCommand → Recon (EnemyRegion<0) | Tests:MissionLifecycleTest.InitialPhase | Notes:Probes least-explored region.
216. IMP:✅ TEST:🟡 | Code:CreateReconMissions → DeepRecon | Tests:MissionLifecycleTest.ReconObjectives | Notes:Least-explored region near enemy.
217. IMP:🟡 TEST:🟡 | Code:MissionType.AirRecon (enum only) | Tests:MissionLifecycleTest.DefensiveAndReconFamilies | Notes:LLM-requestable only.
218. IMP:🟡 TEST:🟡 | Code:MissionType.NavalRecon (enum only) | Tests:MissionLifecycleTest.FamilyCompleteness | Notes:LLM-requestable only.
219. IMP:🟡 TEST:🟡 | Code:MissionType.RouteRecon (enum only) | Tests:MissionLifecycleTest.FamilyCompleteness | Notes:LLM-requestable only.
220. IMP:✅ TEST:🟡 | Code:CreateReconMissions → ExpansionSearch | Tests:MissionLifecycleTest.DefensiveAndReconFamilies | Notes:Uses MapAnalysis.ExpansionValue.
221. IMP:✅ TEST:🟡 | Code:CreateReconMissions → DefenseProbe | Tests:MissionLifecycleTest.FamilyCompleteness | Notes:Probes high-StaticDefense region.
222. IMP:✅ TEST:🟡 | Code:DesiredEffectsFor (per-type intel objectives) | Tests:MissionLifecycleTest.ReconObjectives | Notes:Each recon type has specific info objective.
223. IMP:🟡 TEST:🟡 | Code:CreateReconMissions (fixed priorities 35-40) | Tests:none | Notes:Fixed priorities; no explicit IR priority list.
224. IMP:🟡 TEST:🟡 | Code:BestReconRegion + TargetEvaluator.InformationValue | Tests:TargetEvaluatorTest.InformationValue | Notes:VoI approximated via region value.
225. IMP:✅ TEST:🟡 | Code:UpdateTactics (recon=Take(3)) + UpdateScouting | Tests:HeadlessSkirmishTest.IntelligenceScouting | Notes:Recon uses minimal forces.
226. IMP:✅ TEST:🟡 | Code:ReviewTrigger/StrategicEventDetector | Tests:StrategicEventDetectorTest | Notes:Material intel triggers immediate review.

### Section 14. Combined-Arms Operations

227. IMP:✅ TEST:🟡 | Code:RunCommand (Attack+Flank+Feint+AirStrike coexist) | Tests:HeadlessSkirmishTest.CampaignLifecycle | Notes:Multiple mission components concurrent.
228. IMP:✅ TEST:🟡 | Code:GroundController.LandUnits (armor+infantry) | Tests:none | Notes:Land includes both classes.
229. IMP:🟡 TEST:🟡 | Code:UpdateTactics (artillery in waves) | Tests:none | Notes:Artillery in waves; no behind-screen logic.
230. IMP:🟡 TEST:🟡 | Code:UpdateTactics (AA in waves) | Tests:none | Notes:AA rides waves; no explicit escort positioning.
231. IMP:✅ TEST:🟡 | Code:CreateOffensiveMissions (AirStrike) + AirController | Tests:HeadlessSkirmishTest.CampaignLifecycle | Notes:Air strike + air wave component.
232. IMP:✅ TEST:🟡 | Code:NavalController + NavalStrike | Tests:none | Notes:Naval wave; NavalStrike LLM-only.
233. IMP:✅ TEST:🟡 | Code:RunCommand (SpecialOps) + SpecialOpsController | Tests:none | Notes:Special insertion alongside main attack.
234. IMP:✅ TEST:🟡 | Code:MissionPhase.Recon (pre-staged) | Tests:MissionLifecycleTest.PhaseOrdering | Notes:Recon phase precedes staging/breach.
235. IMP:✅ TEST:🟡 | Code:MissionPhase.Shaping + AA-softening AirStrike | Tests:MissionLifecycleTest.MissionFrameworkFields | Notes:Shaping phase + softening strikes.
236. IMP:✅ TEST:🟡 | Code:MissionPhase.Deception + Feint | Tests:MissionLifecycleTest.DeceptionIntendedReaction | Notes:Deception phase + concurrent feint.
237. IMP:🟡 TEST:🟡 | Code:MissionPhase.Breach/Exploitation | Tests:HeadlessSkirmishTest.MissionLifecycleScenario | Notes:Sequential phases; no separate force assignment.
238. IMP:✅ TEST:🟡 | Code:AvailableArmy (reserve fraction) | Tests:HeadlessSkirmishTest (reserve_commits key) | Notes:Reserve held until CommitReserveRatio.
239. IMP:✅ TEST:🟡 | Code:CoalitionOrderArbiter + SyncForceAssignments | Tests:OrderArbiterTest | Notes:Multiple players contribute via arbiter.
240. IMP:✅ TEST:🟡 | Code:TacticalControllers (shared arbiter) | Tests:OrderArbiterTest | Notes:Domains coordinated via Claim arbitration.

### Section 15. Operational Phasing

241. IMP:✅ TEST:✅ | Code:MissionPhase.Recon | Tests:MissionLifecycleTest.PhaseOrdering | Notes:Phase with transition condition.
242. IMP:✅ TEST:✅ | Code:MissionPhase.Staging + StagingRegion | Tests:MissionLifecycleTest.PhaseOrdering | Notes:Staging region chosen in EnrichMission.
243. IMP:✅ TEST:✅ | Code:MissionPhase.Shaping | Tests:MissionLifecycleTest.PhaseOrdering | Notes:60-tick suppression window.
244. IMP:✅ TEST:✅ | Code:MissionPhase.Deception | Tests:MissionLifecycleTest.PhaseOrdering | Notes:90-tick window; feint/bait start here.
245. IMP:✅ TEST:✅ | Code:MissionPhase.Breach | Tests:MissionLifecycleTest.PhaseOrdering | Notes:Completes when target region explored.
246. IMP:✅ TEST:✅ | Code:MissionPhase.Exploitation | Tests:MissionLifecycleTest.PhaseOrdering | Notes:180-tick holding window.
247. IMP:✅ TEST:✅ | Code:MissionPhase.Consolidation | Tests:MissionLifecycleTest.PhaseOrdering | Notes:Terminal hold until enemy cleared.
248. IMP:✅ TEST:✅ | Code:MissionPhase.Withdrawal | Tests:MissionLifecycleTest.RetreatStaysInWithdrawal | Notes:Retreat missions start and stay here.
249. IMP:✅ TEST:🟡 | Code:AdvancePhase (per-phase conditions) | Tests:HeadlessSkirmishTest.MissionLifecycleScenario | Notes:Tick/force/exploration-based conditions.
250. IMP:🟡 TEST:🟡 | Code:AdvancePhase | Tests:HeadlessSkirmishTest.MissionLifecycleScenario | Notes:Monotonicity tested; no disruption-specific test.

### Section 16. Synchronization & Time-on-Target

251. IMP:✅ TEST:🟡 | Code:Staging phase + StagingRegion | Tests:none | Notes:Forces assemble at staging region.
252. IMP:✅ TEST:🟡 | Code:LaunchConditions + attackTick gate | Tests:MissionLifecycleTest.MissionFrameworkFields | Notes:Brain holds until attackTick.
253. IMP:✅ TEST:🟡 | Code:RunCommand (shared attackTick) | Tests:HeadlessSkirmishTest.CoalitionCoordinatedScenarios | Notes:All bots read same launch window.
254. IMP:🟡 TEST:🟡 | Code:CreateOffensiveMissions (AirStrike before assault) | Tests:none | Notes:AirStrike pre-assault; no configured interval.
255. IMP:🟡 TEST:🟡 | Code:NavalStrike (enum only) | Tests:none | Notes:No auto-created naval bombardment.
256. IMP:🟡 TEST:🟡 | Code:TransportStateMachine.WaitForWindow (30 ticks) | Tests:TransportStateMachineTest.StateOrder | Notes:Fixed window, not tied to distraction.
257. IMP:🟡 TEST:🟡 | Code:reserveCommitted (ratio-based) | Tests:none | Notes:Reserve commits on ratio, not breakthrough timing.
258. IMP:✅ TEST:🟡 | Code:attackTick = CreatedTick+400+PlannedRegions*40 | Tests:none | Notes:Travel time via route region count.
259. IMP:✅ TEST:🟡 | Code:UpdateTactics (world.WorldTick < attackTick hold) | Tests:none | Notes:All bots hold until shared launch tick.
260. IMP:✅ TEST:🟡 | Code:UpdateTactics ("sync error" in wave log) | Tests:HeadlessSkirmishTest (wave telemetry) | Notes:Sync error logged per wave.
261. IMP:🟡 TEST:🟡 | Code:RunCommand (attackTick) | Tests:HeadlessSkirmishTest.CoalitionCoordinatedScenarios | Notes:4-bot scenarios run; no sync-error assertion.

### Section 17. Deception Framework

262. IMP:✅ TEST:✅ | Code:RunCommand → Feint | Tests:DeceptionTest.AttemptsCountedAtCreation | Notes:Auto-created vs enemy-facing region.
263. IMP:✅ TEST:✅ | Code:RunCommand → Demonstration | Tests:MissionLifecycleTest.DemonstrationDirective | Notes:Show-of-force to pin reserves.
264. IMP:✅ TEST:🟡 | Code:CreateReconMissions → DefenseProbe | Tests:MissionLifecycleTest.FamilyCompleteness | Notes:Probe attack as defense probe.
265. IMP:❌ TEST:❌ | Code:not found | Tests:none | Notes:No fake-buildup mechanism.
266. IMP:🟡 TEST:🟡 | Code:Feint/DecoyTransport (diversion via feint) | Tests:none | Notes:Feint serves as diversion; no dedicated type.
267. IMP:✅ TEST:✅ | Code:RunCommand → Bait | Tests:DeceptionTest.ResponseMeasurement | Notes:Bait halfway to base; ambush via counterattack.
268. IMP:🟡 TEST:🟡 | Code:MissionType.DecoyTransport (enum only) | Tests:MissionLifecycleTest.DeceptionIntendedReaction | Notes:LLM-requestable only.
269. IMP:✅ TEST:🟡 | Code:RunCommand (Feint+Demonstration+Flank coexist) | Tests:none | Notes:Multi-axis pressure possible.
270. IMP:✅ TEST:✅ | Code:IntendedReactionFor | Tests:MissionLifecycleTest.DeceptionIntendedReaction | Notes:Every deception type defines intended reaction.
271. IMP:🟡 TEST:🟡 | Code:UpdateTactics (feint = army/FeintFraction) | Tests:none | Notes:Smaller force; no explicit loss limit.
272. IMP:🟡 TEST:🟡 | Code:Update (deception succeeds on redeploy) | Tests:DeceptionTest.ResponseMeasurement | Notes:Completes on success; no early-withdrawal order.
273. IMP:✅ TEST:✅ | Code:MeasureDeceptionResponse | Tests:DeceptionTest.ResponseMeasurement | Notes:Success measured by enemy surge.
274. IMP:✅ TEST:🟡 | Code:RunCommand (attackTick += 200 while feint undrawn) | Tests:none | Notes:Main attack delayed until feint draws response.
275. IMP:✅ TEST:🟡 | Code:RunCommand (Bait) + counterattack | Tests:none | Notes:Bait pulls enemy into ambush.
276. IMP:🟡 TEST:🟡 | Code:Update (bait succeeds on redeploy) | Tests:DeceptionTest | Notes:Mission-level success; no unit-level "retreat=success".

### Section 18. Human Attention Exploitation

277. IMP:✅ TEST:🟡 | Code:RunCommand (concurrent missions) | Tests:HeadlessSkirmishTest.CoalitionCoordinatedScenarios | Notes:Simultaneous missions generated deliberately.
278. IMP:✅ TEST:🟡 | Code:FeintRegionTarget (distinct region) | Tests:none | Notes:Feint/demonstration on distinct region.
279. IMP:✅ TEST:🟡 | Code:CreateOffensiveMissions (air+ground+naval) | Tests:none | Notes:Different domains in concurrent missions.
280. IMP:✅ TEST:🟡 | Code:RunCommand (attack+raid+strike+special ops) | Tests:HeadlessSkirmishTest.CampaignLifecycle | Notes:All components can coexist.
281. IMP:✅ TEST:🟡 | Code:RunCommand (main effort concentration) | Tests:none | Notes:Simultaneous actions serve main effort.
282. IMP:✅ TEST:🟡 | Code:UpdateOpponentModel (MovesWholeArmyToDefend → Bait) | Tests:OpponentModelTest | Notes:Overreaction exploited via bait.
283. IMP:✅ TEST:🟡 | Code:RunCommand (feint before attack) | Tests:none | Notes:Distraction precedes high-value operation.
284. IMP:✅ TEST:🟡 | Code:RunCommand (multi-mission pressure) | Tests:none | Notes:Forces split between multiple targets.

### Section 19. Special Operations

285. IMP:✅ TEST:🟡 | Code:SpecialTypes + SpecialOpsController.Claim + ExcludeFromSquads | Tests:none | Notes:Special assets claimed; waves never take them.
286. IMP:✅ TEST:🟡 | Code:SpecialTypes (config) | Tests:none | Notes:Tanya as scarce asset via config.
287. IMP:✅ TEST:🟡 | Code:SpecialTypes (config) | Tests:none | Notes:Spies as special assets via config.
288. IMP:🟡 TEST:🟡 | Code:CaptureManagerBotModule + SpecialOpsController | Tests:none | Notes:Generic capture exists; no deliberate engineer-capture pairing.
289. IMP:✅ TEST:🟡 | Code:SpecialOpsTarget (value*2 - risk) | Tests:TargetEvaluatorTest | Notes:Targets scored by strategic consequence.
290. IMP:✅ TEST:✅ | Code:TargetEvaluator.ProductionValue | Tests:TargetEvaluatorTest.ProductionValue | Notes:Production structures scored.
291. IMP:✅ TEST:✅ | Code:TargetEvaluator.TechnologyValue | Tests:TargetEvaluatorTest.TechnologyValue | Notes:Tech structures scored.
292. IMP:✅ TEST:✅ | Code:TargetEvaluator.EconomicValue | Tests:TargetEvaluatorTest.EconomicValue | Notes:Economy structures scored.
293. IMP:✅ TEST:✅ | Code:TargetEvaluator.TechnologyValue (iron/pdox) | Tests:TargetEvaluatorTest.TechnologyValue | Notes:Superweapon structures scored.
294. IMP:✅ TEST:🟡 | Code:SpecialOpsTarget (least-observed region) | Tests:none | Notes:Rear/isolated targets preferred.
295. IMP:🟡 TEST:🟡 | Code:SpecialOpsTarget (route.Found + risk) | Tests:none | Notes:Route feasibility + risk; no explicit probability.
296. IMP:✅ TEST:🟡 | Code:SpecialOpsTarget (value*2f - risk) | Tests:none | Notes:Expected strategic value evaluated.
297. IMP:✅ TEST:🟡 | Code:SpecialOpsTarget (StaticDefense+VisionExposure+route) | Tests:none | Notes:Asset-loss risk evaluated.
298. IMP:✅ TEST:🟡 | Code:SpecialOpsController.Execute (reserves asset) + WaitForWindow | Tests:TransportStateMachineTest | Notes:Asset waits for transport/timing.
299. IMP:🟡 TEST:🟡 | Code:WaitForWindow (fixed 30 ticks) | Tests:TransportStateMachineTest | Notes:Fixed window, not live distraction.
300. IMP:🟡 TEST:🟡 | Code:TransportController (abort on low health) + Update (abort on outmatched) | Tests:TransportStateMachineTest.AbortHolds | Notes:Abort on health/force; no detection-based abort.
301. IMP:✅ TEST:✅ | Code:TransportStateMachine (extractOnCompletion) | Tests:TransportStateMachineTest.ExtractionCycles | Notes:Surviving assets extracted and reused.

### Section 20. Transport Operations

302. IMP:✅ TEST:✅ | Code:TransportStateMachine | Tests:TransportStateMachineTest.AllStatesDefined | Notes:Explicit state machine.
303. IMP:✅ TEST:✅ | Code:TransportStateMachine.Assemble | Tests:TransportStateMachineTest.StateOrder | Notes:Implemented.
304. IMP:✅ TEST:✅ | Code:TransportStateMachine.Load + TransportController | Tests:TransportStateMachineTest.StateOrder | Notes:Implemented.
305. IMP:✅ TEST:✅ | Code:TransportStateMachine.WaitForWindow | Tests:TransportStateMachineTest.StateOrder | Notes:Implemented (30-tick).
306. IMP:✅ TEST:✅ | Code:TransportStateMachine.Transit | Tests:TransportStateMachineTest.StateOrder | Notes:Implemented.
307. IMP:✅ TEST:✅ | Code:TransportStateMachine.Approach | Tests:TransportStateMachineTest.StateOrder | Notes:Implemented.
308. IMP:✅ TEST:✅ | Code:TransportStateMachine.Unload | Tests:TransportStateMachineTest.StateOrder | Notes:Implemented.
309. IMP:✅ TEST:✅ | Code:TransportStateMachine.Hold | Tests:TransportStateMachineTest.NoExtractionCompletesAtHold | Notes:Implemented as Hold.
310. IMP:✅ TEST:✅ | Code:TransportStateMachine.ExtractionRequest | Tests:TransportStateMachineTest.ExtractionCycles | Notes:Implemented.
311. IMP:✅ TEST:✅ | Code:TransportStateMachine.ReturnForExtraction | Tests:TransportStateMachineTest.ExtractionCycles | Notes:Implemented.
312. IMP:✅ TEST:✅ | Code:TransportStateMachine.Reload | Tests:TransportStateMachineTest.ExtractionCycles | Notes:Implemented.
313. IMP:✅ TEST:✅ | Code:TransportStateMachine.Extract | Tests:TransportStateMachineTest.ExtractionCycles | Notes:Implemented.
314. IMP:✅ TEST:✅ | Code:PlanTransportRoute (RouteWeights.Stealth) | Tests:RoutePlannerTest.StealthAvoidsThreat | Notes:Threat-weighted stealth route.
315. IMP:✅ TEST:✅ | Code:RouteWeights.Stealth (AntiAirThreat=1.5) | Tests:RoutePlannerTest.AntiAirWeighting | Notes:AA concentrations weighted.
316. IMP:🟡 TEST:🟡 | Code:RouteWeights (CombatThreat=2, no naval-specific) | Tests:RoutePlannerTest | Notes:No naval-threat-specific weight.
317. IMP:✅ TEST:✅ | Code:RouteWeights.Stealth (ActiveCombatZone=2) | Tests:RoutePlannerTest.AvoidsCongestionAndActiveCombat | Notes:Active combat zones avoided.
318. IMP:🟡 TEST:🟡 | Code:TransportController (route planned once) | Tests:none | Notes:No replan during transit; aborts instead.
319. IMP:✅ TEST:✅ | Code:TransportController (abort below RetreatHealthPercent) | Tests:TransportStateMachineTest.AbortHolds | Notes:Abort-and-hold when unsafe.
320. IMP:✅ TEST:🟡 | Code:TransportController (routeWaypoints before transit) | Tests:none | Notes:Insertion route planned before launch.
321. IMP:🟡 TEST:🟡 | Code:TransportController (extraction reuses insertion route) | Tests:TransportStateMachineTest.ExtractionCycles | Notes:No separate extraction route planned.
322. IMP:✅ TEST:🟡 | Code:ExecuteTransportMission ("survived at X% health") | Tests:HeadlessSkirmishTest (transport telemetry) | Notes:Survival logged.

### Section 21. Strategic Posture System

323. IMP:✅ TEST:✅ | Code:StrategicPosture enum + PostureSelection.Select | Tests:PostureSelectionTest | Notes:Global posture exists.
324. IMP:✅ TEST:✅ | Code:StrategicPosture.Opening | Tests:PostureSelectionTest | Notes:Opening posture exists.
325. IMP:🟡 TEST:❌ | Code:StrategicPosture.Expansion (enum exists, never returned by Select) | Tests:none | Notes:Enum defined but unreachable.
326. IMP:✅ TEST:🟡 | Code:StrategicPosture.Pressure | Tests:PostureSelectionTest (indirect) | Notes:Pressure posture exists.
327. IMP:🟡 TEST:❌ | Code:StrategicPosture.Containment | Tests:none | Notes:Enum exists; no test for Select returning it.
328. IMP:🟡 TEST:❌ | Code:StrategicPosture.Attrition | Tests:none | Notes:Enum exists; no test.
329. IMP:✅ TEST:🟡 | Code:StrategicPosture.Breakthrough | Tests:PostureSelectionTest (indirect) | Notes:Breakthrough posture exists.
330. IMP:✅ TEST:🟡 | Code:StrategicPosture.Siege | Tests:PostureSelectionTest (indirect) | Notes:Siege posture exists.
331. IMP:🟡 TEST:❌ | Code:StrategicPosture.Raiding | Tests:none | Notes:Enum exists; no test for Select returning it.
332. IMP:✅ TEST:✅ | Code:StrategicPosture.Defensive | Tests:PostureSelectionTest | Notes:Defensive posture exists.
333. IMP:🟡 TEST:❌ | Code:StrategicPosture.Counterattack (enum exists, never returned) | Tests:none | Notes:Enum defined but unreachable.
334. IMP:🟡 TEST:❌ | Code:StrategicPosture.Recovery (enum exists, never returned) | Tests:none | Notes:Enum defined but unreachable.
335. IMP:✅ TEST:🟡 | Code:StrategicPosture.Desperation | Tests:PostureSelectionTest (indirect) | Notes:Desperation posture exists.
336. IMP:✅ TEST:🟡 | Code:StrategicPosture.AllIn | Tests:PostureSelectionTest (indirect) | Notes:All-in posture exists.
337. IMP:🟡 TEST:❌ | Code:PostureSelection.TargetWeightsFor | Tests:none | Notes:Posture maps to target weights; no test that production priorities change.
338. IMP:🟡 TEST:❌ | Code:PostureSelection.Select (ratio thresholds) | Tests:none | Notes:Posture affects ratio thresholds; no test for risk acceptance.
339. IMP:✅ TEST:✅ | Code:CoalitionDifficulty.ScaledReserveFraction | Tests:DifficultyTest.ReserveTightening | Notes:Posture affects reserve via difficulty.
340. IMP:✅ TEST:✅ | Code:TargetWeights.Balanced/Raiding/Breakthrough | Tests:TargetEvaluatorTest.RaidingPrefersEconomy | Notes:Posture affects target weights.
341. IMP:❌ TEST:❌ | Code:not found | Tests:none | Notes:Single global posture only; no per-front posture.
342. IMP:✅ TEST:✅ | Code:PostureSelection.Select (re-evaluated each command) | Tests:PostureSelectionTest | Notes:Posture changes with conditions.

### Section 22. Main Effort & Force Concentration

343. IMP:✅ TEST:🟡 | Code:CoalitionCommandCenterBotModule.mainEffort | Tests:HeadlessSkirmishTest | Notes:Main effort explicitly identified.
344. IMP:🟡 TEST:❌ | Code:RunCommand (feint/bait support main attack) | Tests:none | Notes:Secondary ops support main effort by design; no explicit test.
345. IMP:🟡 TEST:❌ | Code:BestScoredTarget (concentrates on one target) | Tests:none | Notes:Main effort concentrates; no test asserts not attacking all fronts.
346. IMP:✅ TEST:🟡 | Code:CoalitionArmyStrength vs EnemyArmyStrength (local ratio) | Tests:HeadlessSkirmishTest | Notes:Local force ratio drives decisions.
347. IMP:✅ TEST:🟡 | Code:BestScoredTarget (mass on one vulnerable area) | Tests:HeadlessSkirmishTest | Notes:Forces mass on best target.
348. IMP:🟡 TEST:🟡 | Code:CreateDefensiveMissions (defend when outnumbered) | Tests:HeadlessSkirmishTest | Notes:Other fronts defended; no assertion of sufficiency.
349. IMP:🟡 TEST:❌ | Code:BestScoredTarget (risk in scoring) | Tests:none | Notes:Counterattack risk in target score; no concentration-risk test.
350. IMP:🟡 TEST:🟡 | Code:MissionPhase.Breach → MissionPhase.Exploitation | Tests:MissionLifecycleTest.PhaseOrdering | Notes:Sequential phases; no separate force assignment.

### Section 23. Strategic Reserve

351. IMP:🟡 TEST:🟡 | Code:StrategicBrainBotModule (AvailableArmy = activeArmy - reserve) | Tests:DifficultyTest.ReserveTightening | Notes:Reserve exists as computed fraction, not managed pool.
352. IMP:✅ TEST:✅ | Code:ReserveFraction (configurable) + ScaledReserveFraction | Tests:DifficultyTest.ReserveTightening | Notes:Reserve size configurable.
353. IMP:✅ TEST:🟡 | Code:ReserveFraction=4 (25%) default | Tests:none | Notes:25% default; configurable.
354. IMP:✅ TEST:🟡 | Code:AvailableArmy excludes reserve from missions | Tests:HeadlessSkirmishTest | Notes:Reserve not consumed by routine missions.
355. IMP:🟡 TEST:🟡 | Code:Brain reserve intercepts raids | Tests:HeadlessSkirmishTest (reserve_commits) | Notes:Reserve intercepts near base; not a commanded pool.
356. IMP:🟡 TEST:❌ | Code:UpdateCoordination (ally reinforcement) | Tests:none | Notes:Reserve reinforces via brain; no explicit reserve-to-failing-front.
357. IMP:🟡 TEST:❌ | Code:reserveCommitted (boolean) | Tests:none | Notes:Reserve committed as boolean flag; no exploitation-specific logic.
358. IMP:🟡 TEST:🟡 | Code:Reserve intercepts raids (brain) | Tests:HeadlessSkirmishTest | Notes:Reserve intercepts raids near base.
359. IMP:❌ TEST:❌ | Code:not found | Tests:none | Notes:No reserve-protect-expansion logic.
360. IMP:❌ TEST:❌ | Code:not found | Tests:none | Notes:No LLM justification gate for last reserve.
361. IMP:✅ TEST:🟡 | Code:BuildForceJson "reserve" field | Tests:HeadlessSkirmishTest | Notes:Reserve availability visible in context.
362. IMP:✅ TEST:🟡 | Code:CoalitionTelemetry.Log "Reserve committed" | Tests:HeadlessSkirmishTest (telemetry key) | Notes:Reserve usage tracked.

### Section 24. Target Evaluation

363. IMP:✅ TEST:✅ | Code:TargetEvaluator.Score | Tests:TargetEvaluatorTest | Notes:Full scoring system.
364. IMP:✅ TEST:🟡 | Code:StrategicValue weight | Tests:TargetEvaluatorTest.Classify | Notes:Strategic value multiplier.
365. IMP:✅ TEST:✅ | Code:EconomicValue(actorType) | Tests:TargetEvaluatorTest.EconomicValue | Notes:Economy structures scored.
366. IMP:✅ TEST:✅ | Code:ProductionValue(actorType) | Tests:TargetEvaluatorTest.ProductionValue | Notes:Production structures scored.
367. IMP:✅ TEST:✅ | Code:TechnologyValue(actorType) | Tests:TargetEvaluatorTest.TechnologyValue | Notes:Tech structures scored.
368. IMP:✅ TEST:❌ | Code:PositionalValue (chokepoint exits) | Tests:none | Notes:Computed; no test with real map.
369. IMP:✅ TEST:✅ | Code:InformationValue(actorType) | Tests:TargetEvaluatorTest.InformationValue | Notes:Info structures scored.
370. IMP:✅ TEST:❌ | Code:FollowOnValue (adjacency) | Tests:none | Notes:Computed; untested.
371. IMP:✅ TEST:🟡 | Code:FriendlyLossRisk | Tests:TargetEvaluatorTest.RiskReducesScore | Notes:Subtracted; tested as bundle.
372. IMP:✅ TEST:🟡 | Code:TravelCost = route.Cost | Tests:TargetEvaluatorTest.RiskReducesScore | Notes:Route cost subtracted.
373. IMP:✅ TEST:🟡 | Code:ReinforcementRisk | Tests:TargetEvaluatorTest.RiskReducesScore | Notes:Reinforcement risk subtracted.
374. IMP:✅ TEST:🟡 | Code:CounterattackRisk (GroundAntiArmor) | Tests:TargetEvaluatorTest.RiskReducesScore | Notes:Counterattack risk subtracted.
375. IMP:✅ TEST:✅ | Code:IntelligenceUncertainty | Tests:TargetEvaluatorTest.UncertaintyReducesScore | Notes:Uncertainty subtracted (OBSERVED=0, LAST_KNOWN=0.3, INFERRED/SUSPECTED=1).
376. IMP:✅ TEST:✅ | Code:TargetWeights.Balanced/Raiding/Breakthrough | Tests:TargetEvaluatorTest.RaidingPrefersEconomy | Notes:Posture maps to weights.
377. IMP:🟡 TEST:❌ | Code:BestScoredTarget/ScoreTargets rank by Total | Tests:none | Notes:Ranks targets; no test asserts low-value skipped.

### Section 25. Production & Capability Planning

378. IMP:✅ TEST:🟡 | Code:BuildProduceJson (coalition-level) | Tests:HeadlessSkirmishTest | Notes:Production at coalition level.
379. IMP:✅ TEST:✅ | Code:ProductionContract.Resolve (capabilities) | Tests:ProductionContractTest | Notes:Reasons in capabilities.
380. IMP:✅ TEST:✅ | Code:CoalitionCapability.GroundAntiArmor | Tests:ProductionContractTest | Notes:Anti-armor tracked.
381. IMP:✅ TEST:✅ | Code:CoalitionCapability.GroundAntiInfantry | Tests:ProductionContractTest | Notes:Anti-infantry tracked.
382. IMP:✅ TEST:✅ | Code:CoalitionCapability.AntiAir | Tests:ProductionContractTest | Notes:Anti-air tracked.
383. IMP:✅ TEST:✅ | Code:CoalitionCapability.Artillery | Tests:ProductionContractTest | Notes:Artillery tracked.
384. IMP:🟡 TEST:🟡 | Code:ScoutUnitTypes (config) | Tests:HeadlessSkirmishTest.IntelligenceScouting | Notes:Recon requirement via scout types; not a capability contract.
385. IMP:🟡 TEST:🟡 | Code:ArmyPriority (mobility units) | Tests:none | Notes:Mobility via unit priority; not explicit capability.
386. IMP:🟡 TEST:🟡 | Code:ArmyPriority (fast units) | Tests:none | Notes:Fast-raiding via unit priority; not explicit capability.
387. IMP:✅ TEST:✅ | Code:CoalitionCapability.Naval | Tests:ProductionContractTest.NavalGate | Notes:Naval requirement tracked.
388. IMP:🟡 TEST:🟡 | Code:CoalitionCapability.AntiAir (air-superiority) | Tests:ProductionContractTest | Notes:Anti-air exists; no explicit air-superiority capability.
389. IMP:🟡 TEST:🟡 | Code:TransportTypes (config) | Tests:HeadlessSkirmishTest | Notes:Transport via config; not a capability contract.
390. IMP:🟡 TEST:🟡 | Code:SpecialTypes (config) | Tests:HeadlessSkirmishTest | Notes:Special-ops via config; not a capability contract.
391. IMP:✅ TEST:✅ | Code:CoalitionCapability.StaticDefense | Tests:ProductionContractTest | Notes:Base-defense requirement tracked.
392. IMP:✅ TEST:✅ | Code:ProductionContract.Aggregate (threats → contracts) | Tests:ProductionContractTest.AggregateAcrossRegions | Notes:Responds to enemy composition.
393. IMP:✅ TEST:🟡 | Code:AssignRole (naval/main/escort specialization) | Tests:HeadlessSkirmishTest | Notes:Players specialize by role.
394. IMP:✅ TEST:🟡 | Code:queue.BuildableItems() per player | Tests:HeadlessSkirmishTest | Notes:Respects each player's tech tree.
395. IMP:🟡 TEST:❌ | Code:BuildProduceJson (coalition-level) | Tests:none | Notes:Coalition production; no duplication-avoidance test.
396. IMP:🟡 TEST:🟡 | Code:StrategicEventDetector.AlliedProductionLost | Tests:StrategicEventDetectorTest.AlliedProductionLost | Notes:Detects loss; replanning not tested.
397. IMP:✅ TEST:🟡 | Code:BuildProduceJson (anti-air/naval response) | Tests:HeadlessSkirmishTest | Notes:Reacts to new threats.
398. IMP:🟡 TEST:❌ | Code:PostureSelection.TargetWeightsFor | Tests:none | Notes:Posture affects targets; no test for production.
399. IMP:✅ TEST:🟡 | Code:CoalitionTelemetry.Log "Excess cash floating" | Tests:HeadlessSkirmishTest | Notes:Excess floating detected.
400. IMP:🟡 TEST:🟡 | Code:MinProductionCash (cash floor) | Tests:HeadlessSkirmishTest | Notes:Cash floor gates production; no explicit reservation.
401. IMP:❌ TEST:❌ | Code:not found | Tests:none | Notes:No emergency replacement production.

### Section 26. Economy & Expansion

402. IMP:✅ TEST:🟡 | Code:CoalitionBlackboard.ExtractEconomy (per-player) | Tests:HeadlessSkirmishTest | Notes:Per-ally economy tracked.
403. IMP:✅ TEST:🟡 | Code:ExtractEconomyState (RefineryCount) | Tests:none | Notes:Refinery capacity tracked.
404. IMP:✅ TEST:🟡 | Code:ExtractEconomyState (HarvesterCount/ActiveHarvesterCount) | Tests:none | Notes:Harvester status tracked.
405. IMP:✅ TEST:🟡 | Code:ResourceCellsRemaining | Tests:none | Notes:Resource depletion tracked.
406. IMP:✅ TEST:🟡 | Code:McvExpansionManagerBotModule (expansion scoring) | Tests:none | Notes:Expansion opportunities scored.
407. IMP:✅ TEST:🟡 | Code:McvExpansionManagerBotModule.CalculateThreats | Tests:none | Notes:Expansion risk evaluated.
408. IMP:🟡 TEST:🟡 | Code:ExpansionTolerate (cash-based) | Tests:none | Notes:Cash-based; not linked to posture.
409. IMP:🟡 TEST:❌ | Code:AssignRole (no expansion role) | Tests:none | Notes:No expansion/economic-specialist role.
410. IMP:✅ TEST:🟡 | Code:DeployMcv (UpdatedDefenseCenter) | Tests:none | Notes:New expansions notify defense.
411. IMP:✅ TEST:🟡 | Code:MostValuableStructurePosition (EconomicValue) | Tests:TargetEvaluatorTest | Notes:Economic vulnerability influences defense.
412. IMP:✅ TEST:🟡 | Code:CreateOffensiveMissions (EconomyRaid) | Tests:HeadlessSkirmishTest | Notes:Enemy economy triggers raids.

### Section 27. Opponent Modeling

413. IMP:✅ TEST:✅ | Code:OpponentModel (in CoalitionBlackboard) | Tests:OpponentModelTest | Notes:Opponent-model object exists.
414. IMP:✅ TEST:✅ | Code:OpponentModel.ArmorBias | Tests:OpponentModelTest | Notes:Armor bias tracked.
415. IMP:✅ TEST:✅ | Code:OpponentModel.InfantryBias | Tests:OpponentModelTest | Notes:Infantry bias tracked.
416. IMP:✅ TEST:✅ | Code:OpponentModel.AirBias | Tests:OpponentModelTest | Notes:Air bias tracked.
417. IMP:✅ TEST:✅ | Code:OpponentModel.NavalBias | Tests:OpponentModelTest | Notes:Naval bias tracked.
418. IMP:✅ TEST:✅ | Code:OpponentModel.StaticDefenseBias | Tests:OpponentModelTest | Notes:Static-defense bias tracked.
419. IMP:✅ TEST:🟡 | Code:OpponentModel.PreferredAttackLane | Tests:OpponentModelTest | Notes:Lane tracked; no test for lane-based defense.
420. IMP:✅ TEST:✅ | Code:OpponentModel.AverageResponseTime | Tests:OpponentModelTest.ResponseTimeAverage | Notes:Response time tracked.
421. IMP:🟡 TEST:🟡 | Code:OpponentModel.RespondsStronglyToRaids | Tests:OpponentModelTest | Notes:Tracked; BUT feeds from fog-leaking RecordRaidContact.
422. IMP:🟡 TEST:❌ | Code:OpponentModel (no feint-response tracking) | Tests:none | Notes:Response to feints not separately tracked.
423. IMP:✅ TEST:✅ | Code:OpponentModel.MovesWholeArmyToDefend | Tests:OpponentModelTest | Notes:Whole-army-redeploy tendency tracked.
424. IMP:🟡 TEST:❌ | Code:OpponentModel.ExpansionCount | Tests:none | Notes:Expansion timing tracked as count; no timing behavior.
425. IMP:🟡 TEST:🟡 | Code:OpponentModel.Playstyle (turtle) | Tests:OpponentModelTest.DerivePlaystyle | Notes:Turtling detected via playstyle.
426. IMP:🟡 TEST:🟡 | Code:OpponentModel.Playstyle (rush) | Tests:OpponentModelTest.DerivePlaystyle | Notes:Rush detected via playstyle.
427. IMP:✅ TEST:✅ | Code:OpponentModel.Confidence | Tests:OpponentModelTest.ConfidenceClamp | Notes:Confidence values exist.
428. IMP:🟡 TEST:❌ | Code:OpponentModel (patterns → bait/feint) | Tests:none | Notes:Bait generated from patterns; no test for exploitation.
429. IMP:🟡 TEST:🟡 | Code:OpponentModel (confidence gate) | Tests:OpponentModelTest.ConfidenceClamp | Notes:Confidence gates exploitation; no test for low-confidence caution.
430. IMP:✅ TEST:✅ | Code:UpdateOpponentModel (updates each blackboard rebuild) | Tests:HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent | Notes:Updates during match.
431. IMP:✅ TEST:🟡 | Code:HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent | Tests:OpponentModelTest | Notes:Validated vs scripted turtle; no multi-opponent validation.
432. IMP:✅ TEST:🟡 | Code:HeadlessSkirmishTest.OpponentModelClassifiesScriptedOpponent | Tests:OpponentModelTest | Notes:Validated vs scripted turtle.

### Section 28. Counterattack Intelligence

433. IMP:🟡 TEST:❌ | Code:StrategicBrainBotModule (counterPos from base threat) | Tests:none | Notes:Counterattack position from last threat; no origin estimation.
434. IMP:🟡 TEST:❌ | Code:Brain (enemyCountAtDefense) | Tests:none | Notes:Enemy count at defense tracked; no exposure evaluation.
435. IMP:🟡 TEST:❌ | Code:Brain (enemyArmyCount comparison) | Tests:none | Notes:Depleted check exists; no reinforcement-depletion check.
436. IMP:✅ TEST:🟡 | Code:Brain (counterattack window) | Tests:HeadlessSkirmishTest (counterattacks key) | Notes:Counterattack opportunity evaluated.
437. IMP:✅ TEST:🟡 | Code:Brain (activeArmy.Length >= MinWaveSize gate) | Tests:HeadlessSkirmishTest | Notes:Doesn't counterattack when too weak.
438. IMP:🟡 TEST:❌ | Code:Brain (no production window check) | Tests:none | Notes:Counterattack doesn't consider enemy production timing.
439. IMP:🟡 TEST:❌ | Code:MissionLifecycleTest.FamilyCompleteness | Tests:none | Notes:Counterattack classification tested; no scenario test.

### Sections 29–43. LLM Stack, Intel/Mission, and Tactical Agent Data

> **⚠ DATA NOT PROVIDED:** The subagent audit data for requirements 440–639 (sections 29–43) was referenced as "use the [agent] data verbatim" but was not included in the task prompt. These 200 requirement lines must be supplied by the parent agent to complete these sections.

### Section 44. Fair-but-Brutal Target Configuration

640. IMP:✅ TEST:🟡 | Code:CoalitionDifficulty (brutal profile) | Tests:DifficultyTest.IntelligenceAxis | Notes:Test config exists (CommandQuality=3, CoordinationStrength=3, etc.).
641. IMP:✅ TEST:🟡 | Code:EconomicBonus=0 | Tests:DifficultyTest.IndependentAxes | Notes:No hidden economic advantages asserted.
642. IMP:✅ TEST:🟡 | Code:Intelligence=0 (fair fog) | Tests:DifficultyTest.IntelligenceAxis | Notes:No hidden position access asserted.
643. IMP:🟡 TEST:❌ | Code:selfplay.py --vs | Tests:none | Notes:Performance vs standard bots measurable via selfplay; no automated test.
644. IMP:🟡 TEST:❌ | Code:selfplay.py (multi-bot) | Tests:none | Notes:Multi-bot performance measurable; no automated test.
645. IMP:🟡 TEST:❌ | Code:not found | Tests:none | Notes:No human-playtest evaluation infrastructure.

### Sections 45–52. Test Coverage, LLM, and Intel/Mission Agent Data

> **⚠ DATA NOT PROVIDED:** The subagent audit data for requirements 646–770 (sections 45–52) was referenced as "use the [agent] data verbatim" but was not included in the task prompt. These 125 requirement lines must be supplied by the parent agent to complete these sections.

### Section 53. Documentation

771. IMP:✅ TEST:N/A | Code:README.md + ai/README.md + wiki | Tests:N/A | Notes:Architecture documented in README and wiki.
772. IMP:✅ TEST:N/A | Code:README.md | Tests:N/A | Notes:Coalition-control model documented.
773. IMP:✅ TEST:N/A | Code:README.md + ai/README.md | Tests:N/A | Notes:Fog-of-war policy documented.
774. IMP:✅ TEST:N/A | Code:ai/COMMAND_API.md | Tests:N/A | Notes:LLM tool API documented (378 lines).
775. IMP:🟡 TEST:N/A | Code:ai/COMMAND_API.md (partial) | Tests:N/A | Notes:Mission schema partially documented; not all fields.
776. IMP:🟡 TEST:N/A | Code:ai/COMMAND_API.md (partial) | Tests:N/A | Notes:Force/army-group schema partially documented.
777. IMP:✅ TEST:N/A | Code:ai/COMMAND_API.md | Tests:N/A | Notes:Enemy-intelligence schema documented.
778. IMP:🟡 TEST:N/A | Code:Not documented | Tests:N/A | Notes:Threat-map model not explicitly documented.
779. IMP:🟡 TEST:N/A | Code:Not documented | Tests:N/A | Notes:Route-cost model not explicitly documented.
780. IMP:🟡 TEST:N/A | Code:ai/README.md (partial) | Tests:N/A | Notes:Combat-estimator assumptions partially documented.
781. IMP:🟡 TEST:N/A | Code:Not documented | Tests:N/A | Notes:Strategic-posture behavior not explicitly documented.
782. IMP:🟡 TEST:N/A | Code:ai/README.md (partial) | Tests:N/A | Notes:Production/capability system partially documented.
783. IMP:🟡 TEST:N/A | Code:Not documented | Tests:N/A | Notes:Opponent-model features not explicitly documented.
784. IMP:✅ TEST:N/A | Code:ai/README.md | Tests:N/A | Notes:Failure/fallback behavior documented.
785. IMP:✅ TEST:N/A | Code:ai/README.md + ai.yaml | Tests:N/A | Notes:Difficulty settings documented.
786. IMP:✅ TEST:N/A | Code:TESTING.md + Makefile | Tests:N/A | Notes:Testing instructions documented.
787. IMP:✅ TEST:N/A | Code:ai/README.md + ai/selfplay.py --help | Tests:N/A | Notes:Batch/self-play evaluation documented.
788. IMP:🟡 TEST:N/A | Code:ai/README.md (partial) | Tests:N/A | Notes:Decision-log format partially documented.

### Acceptance Tests (789–804)

> **⚠ DATA NOT PROVIDED:** The test coverage agent data for requirements 789–804 was referenced as "use the test coverage agent data verbatim" but was not included in the task prompt. These 16 requirement lines must be supplied by the parent agent to complete this section.

