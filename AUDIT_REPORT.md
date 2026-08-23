# OpenRA Supreme Allied Command AI — 804-Requirement Audit Report

**Repository:** `Pummelchen/OpenRA`, branch `main`
**Audited revision:** `cb2f999a7f`
**Audit date:** 2026-08-23
**Per-requirement register:** [AUDIT_TABLE.md](AUDIT_TABLE.md)

This is an independent re-audit. The build, the full test suite and the Python
self-check were executed; every subsystem was read at source level and every claim
traced to a named test. It supersedes the previous register, which recorded 804/804
with no per-row evidence.

## Result

| Classification | Count |
|---|---:|
| Complete and tested | **637** / 804 |
| Implemented but insufficiently tested | **153** / 804 |
| Partial | **14** / 804 |
| Missing | **0** / 804 |

Implementation: 790 ✅ · 14 🟡 · 0 ❌ — Testing: 644 ✅ · 138 🟡 · 22 ❌

**The headline finding is not that anything is missing — essentially nothing is.
It is that the test suite is weighted towards contract and unit coverage, while the
behavioural, scale and LLM-facing layers rest on marker-presence smoke tests.**

## Validation executed

| Check | Result |
|---|---|
| `dotnet build OpenRA.Test/OpenRA.Test.csproj -c Debug` | succeeded, 0 warnings, 0 errors |
| `dotnet test bin/OpenRA.Test.dll --test-adapter-path:.` | 812 passed, 2 skipped, 0 failed |
| `HeadlessSkirmishTest` in isolation | 15/15 passed — RA content present, nothing silently skipped |
| `.venv-ai/bin/python ai/selfcheck.py` | passed |
| Qwen3.5 4B MLX runtime | operational (`mlx_vlm` 0.6.15, 4.8 GB cache) |

The 2 skips are upstream PNG tests. `ai/selfcheck.py` needs Python ≥ 3.11 and must run
via `.venv-ai/bin/python` — the system `python3` here is 3.9. `TESTING.md` omits this.

---

## 1. The 20 most important missing/partial requirements

Ranked by risk to the project's stated goal (a fair-fog AI that is much stronger than
the stock bots).

| Rank | # | Requirement | Status | Why it matters |
|---:|---|---|---|---|
| 1 | 804 | Brutal Fair-AI Test | 🟡/🟡 | The central claim. Measured record is **0W/2L/1D vs Rush**. Combat exchange (1.39) is genuinely 136% above the scripted baseline (0.59), but the AI still loses. Strategic timing/economy, not tactics, is the open problem. |
| 2 | 700 | No unacceptable tick degradation | ✅/❌ | **No timing is measured anywhere.** Tests assert tick *counts*, never tick *cost*. A performance regression cannot be detected. |
| 3 | 730–737 | 7 `llm_eval.py` scorers | ✅/❌ | `score_force_availability`, `score_mission_completeness`, `score_unnecessary_risk`, `score_baseline_comparison`, `score_repeated_impossible`, `score_uncertain_intelligence`, `score_reserves` have **no test at all**. |
| 4 | 729/734/738 | Legality, oscillation, idle scoring | ✅/🟡 | `LlmEvalTest.cs` re-implements these **in C# inside the test file** and tests its own copy. The shipped Python can regress silently. |
| 5 | 691–692 | Small / large map coverage | ✅/❌ | The entire automated suite runs on **one map** of the 141 that ship. Map-specific overfitting is currently likely, not excluded. |
| 6 | 500–521 | LLM commander behaviour (22 rows) | ✅/🟡 | Verified only by **substring assertions on the prompt text**. The automated suite never runs a model, so no behavioural claim is validated. |
| 7 | 695 | Hundreds of units | ✅/❌ | `StressScale` asserts `ActorCount > 0` after 3000 ticks — it never establishes that large-scale combat occurred. |
| 8 | 548–549 | Controller inability / replan request | ✅/❌ | Fully wired (`Unable()` fires from 8 sites → `RequestReplan`), but **no test touches `Unable`, `FailureReason` or `NeedsReplan`**. |
| 9 | 571 | Support-power coverage | 🟡/✅ | Only 4 of RA's 6 powers are usable. **Chronosphere, Advanced Chronoshift and Iron Curtain are never fired.** Deliberate and asserted, but a real capability gap. |
| 10 | 26 | Mixed-owner force groups | 🟡/✅ | `ForceGroup` is strictly one-per-owner. Correct given OpenRA's ownership rules, but the literal requirement is unmet; coordination is emergent from shared mission ids. |
| 11 | 789–796, 799, 801–803 | Acceptance cases | ✅/🟡 | Asserted by **telemetry-marker presence**, not behavioural outcome. `UnifiedCoalitionCommand` proves a `Posture` line was logged, not that four bots acted as one command. |
| 12 | 159 | Combat-estimator accuracy vs replays | 🟡/🟡 | Only a match-level win-ratio correlation. The source itself says a real benchmark "needs recorded per-engagement outcomes". |
| 13 | 709–710, 713 | Regression tests for past bugs | ✅/❌ | Exactly **one** genuine regression test exists (the tool-API listener leak). No strategic or transport regression pins. |
| 14 | 689/799 | LLM failure mid-operation | ✅/🟡 | `DeterministicFallback` runs with **no** model server for the whole match. The dropout *transition* with missions already executing is untested. |
| 15 | 696–699 | Heavy air/naval, many missions, churn | ✅/❌ | No scenario or assertion for any of these load profiles. |
| 16 | 705 | Memory/actor-reference leaks | ✅/🟡 | Covers listener and telemetry-writer leaks across 3 matches. No managed-memory or actor-reference profiling. |
| 17 | 707–708 | Replay-based inspection | 🟡/🟡 | Satisfied by fixed-seed re-runs. **No `.orarep` ingestion exists anywhere** in the AI stack. |
| 18 | 645 | Evaluation against human players | 🟡/❌ | No replay ingestion, no recorded playtest. |
| 19 | 717 | Multiple factions in evaluation | 🟡/❌ | Factions are assigned round-robin and reported, but no `FACTION=` selector exists, so faction-controlled experiments cannot be expressed. |
| 20 | 604–605, 622 | Economic-damage and prediction metrics | 🟡/✅ | Economic damage is **refinery counts**, not value. 622 measures combat-estimator accuracy, not opponent-model accuracy. |

## 2. Architecture decisions that make requirements impossible or difficult

1. **OpenRA forbids ordering another player's actors.** This makes requirement 26
   (mixed-owner force groups) unachievable as literally written. The chosen workaround —
   per-owner `ForceGroup`s bound by a shared mission id and arbiter — is the correct
   design, and it is what makes 1–17 work at all.
2. **The LLM lives out-of-process behind HTTP.** This buys a clean fallback and a hard
   validation boundary, but it puts commander behaviour (§34) permanently outside
   `dotnet test`. Validating 500–521 needs a separate model-in-the-loop harness.
3. **Every allied bot recomputes the identical blackboard** rather than exchanging
   messages. This is what keeps the coalition deterministic and desync-free, at the cost
   of N× redundant full-world scans — the main performance exposure (see §9).
4. **`Platform.OverrideEngineDir` may be called only once per process.** All headless
   tests therefore share one mod/map load, which is the direct structural cause of the
   single-map limitation in 691/692/727.
5. No architectural decision blocks any remaining requirement.

## 3. Hidden-information / fog-of-war leaks

**None found.** This is the most rigorously built part of the codebase, and it is
enforced structurally rather than by convention:

- `CommandToolApi` has **no access to `world.Actors` for enemies at all** — every enemy
  answer is served from `ToolContext.EnemyIntel`, the already-filtered snapshot. A leak
  would require adding a new data source, not forgetting a check.
- `EnemyIntel` holds **no `Actor` reference**; the constructor extracts the type name and
  discards the actor. Pinned by `IntelTrackerTest`.
- Every observation site is gated: `CoalitionBlackboard.cs:616`,
  `CoalitionCommandCenterBotModule.cs:991`, `StrategicBrainBotModule.cs:465,1368,1828`,
  `ExternalBrainBotModule.cs:284`.
- The honesty ladder is complete (Observed / LastKnown / Inferred / Suspected / Unknown)
  with timestamps, decaying confidence and growing `PositionErrorCells`.
- `ExternalBrainSnapshotTest.FairFogRejectsInvisible` asserts the payload sent to the
  model excludes non-visible actors.

Omniscience exists only as an explicit opt-in axis (`Intelligence >= 3`) and is not the
default.

## 4. Conflicting allied strategic decisions

**None found.** One team plan is computed identically by every member; the
`CoalitionOrderArbiter` gives each committed unit exactly one mission owner, one role and
one release condition, and rejects double-commitment with `REJECTED_CONFLICT`.
`CommandValidator.ValidateMissions` additionally rejects duplicate missions at the same
target before execution. Covered by `OrderArbiterTest` and `CommandValidatorTest`.

## 5. LLM performing tactical micro

**None.** The separation is clean and structural. The LLM's entire surface is strategic —
there is no `move_unit` or `attack_unit` tool, and mutation tools return validated
`plan_patch` objects rather than orders. Every actor order originates in
`TacticalControllers.cs` (`Ground`/`Air`/`Naval`/`Transport`/`SpecialOps`), which run
identically whether the model is present or not.

## 6. Untested mission types

All 38 `MissionType` values exist, carry per-type directives and desired effects, and
reach execution code. The gap is scenario-level, not vocabulary-level:

- **Type-level coverage is complete** — `MissionLifecycleTest` and `ExpandedCoverageTest`
  assert existence, directive mapping, desired effects and intended reactions per type.
- **End-to-end scenario coverage is largely absent** — reqs 663–684. No test drives a
  feint→reaction→real-attack arc, a Tanya insert→act→extract cycle, a naval transport
  insertion, or a reserve-reinforcement sequence in a live match and asserts the outcome.
- Two vocabulary gaps: **Exploitation** (187) exists as a phase, not a type;
  **Emergency reinforcement** (202) and **Interception** (204) have no dedicated types.

## 7. LLM commands not engine-side validated

**None.** Validation is layered and cannot be bypassed:
read tools resolve references and error on unknown ids; mutation tools return validated
patches; `CommandValidator` re-validates the merged plan **on the game thread** before
execution, emitting machine-readable `REJECTED_*` reasons (unknown type, out of bounds,
invalid priority, conflict, unknown unit/capability/posture, invalid reserve fraction,
unjustified reserve commitment). `IsStale` discards late replies. Prerequisites, cash and
queue availability are enforced by the engine's own production path, not by the model.

## 8. Missing fallback behaviour

**None missing in kind, one untested transition.** Timeout (120 s), malformed output,
unreachable server, stale round and rejected commands all fall back to the deterministic
commander, which defends, produces and attacks alone. The gap is coverage, not code:
`DeterministicFallback` proves a cold start without a model, never a **mid-battle
dropout** with missions already executing (689/799).

## 9. Performance bottlenecks

1. **N× redundant full-world scans.** Every allied bot rebuilds the identical blackboard
   every 40 ticks. Deterministic by design, but cost scales with bots × actors.
2. **Unmeasured.** No tick-time assertion exists anywhere (700), so any regression here
   is invisible to the suite. This is the single most valuable missing test.
3. Mitigations already in place and verified: bounded context sections
   (`ExternalBrainSnapshotTest`), event debouncing to `BlackboardInterval`, bounded
   scouting, immutable summaries, and a released tool-API listener between matches.
4. Headless match duration dominates suite runtime (~54 s for 15 scenarios).

## 10. Prioritized plan to full compliance

**P0 — close the measurement blind spots (highest value per unit of work)**
1. Add tick-time budget assertions to `HeadlessSkirmishTest` (req 700). Without this,
   every other performance claim is unfalsifiable.
2. Test the 7 untested `llm_eval.py` scorers **in Python** (730–737), and convert
   `LlmEvalTest.cs` from a C# re-implementation into a subprocess call against the real
   `ai/llm_eval.py` (729/734/738).
3. Assert scale in `StressScale`: peak concurrent actors, peak concurrent missions
   (695, 698).

**P1 — break the single-map monoculture**
4. Parameterise `HeadlessSkirmishTest` over a small map, a large map and a naval map. The
   `Platform.OverrideEngineDir` once-per-process constraint means this needs either
   separate fixtures per process or a map-cache-level workaround (691, 692, 697, 727).
5. Wire `ai/selfplay.py --bot-type normal` into CI as a tracked baseline so strategic
   regressions fail a build (714).

**P2 — make the acceptance cases behavioural**
6. Replace marker-presence assertions with outcome assertions for 789–804: assert a
   feint measurably drew enemy units before the main attack (791), that a special asset
   completed insert→act→extract (792), that reserve units were still uncommitted at
   attack launch and later engaged (795).
7. Add a mid-operation LLM dropout test: run with a stub server, kill it once missions
   are `Executing`, assert missions continue (689, 799).
8. Add a direct test for `TacticalController.Unable` → `RequestReplan` (548, 549).

**P3 — close the real capability gaps**
9. Implement Chronosphere and Iron Curtain in `SupportPowerPolicy` (571).
10. Add `FACTION=` to `SimulateCommand`/`selfplay.py` (717).
11. Measure economic damage in credits rather than refinery counts (604, 605), and score
    opponent-model predictions against outcomes (622).

**P4 — the actual strategic problem**
12. **This is the one that decides req 804.** The tactical executor is measurably
    efficient; the losses come from strategic timing and economy. Use the existing
    `--sweep-*` axes across multiple maps and seeds to tune attack timing and expansion
    cadence, and track win rate — not exchange ratio — as the gate. Everything above is
    instrumentation that makes this work measurable; this is the work itself.

**Standing gate:** treat any fairness, analyzer, contract, acceptance or remote-parity
regression as release-blocking, and keep the register honest — the previous all-✅
register concealed exactly the gaps that matter most.
