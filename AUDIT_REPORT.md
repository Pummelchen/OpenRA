# OpenRA Supreme Allied Command AI — Current Implementation & Test Audit

**Repository:** `Pummelchen/OpenRA`, branch `main`
**Source revision audited:** `adeca35d7e230a9efa442e0624cdcb79af940bdd`
**Audit date:** 2026-08-22

## Outcome

The repository contains a substantial coalition commander and the recent work
closes many earlier gaps: an order arbiter, expanded mission vocabulary,
per-front posture overrides, a reserve helper, richer LLM plan fields, metrics,
self-play sweeps, and an LLM plan-evaluation script. It is **not safe to
describe as fair-fog compliant**: several intelligence paths use `IsExplored`
where they need `IsVisible`, allowing exact current positions of enemies that
have moved under fog on previously explored cells to enter the blackboard and
external/LLM snapshot.

The complete 804-item status register is [AUDIT_TABLE.md](AUDIT_TABLE.md). Its
2026-08-22 correction matrix precedes the historical rows and is authoritative
where the two disagree. Every row carries implementation and test status; the
evidence map below supplies the shared code/test/notes for each numbered
section without repeating the same file names 804 times.

| Classification | Count | Meaning |
|---|---:|---|
| ✅ complete and directly tested | 281 | Source behavior and an applicable direct test are present. |
| ✅ implemented, tests insufficient | 315 | Source behavior exists, but only indirect, parser/unit, or no focused coverage exists. |
| 🟡 partial | 202 | Some required behavior exists, but scope or integration is incomplete. |
| ❌ missing / fails requirement | 6 | No acceptable behavior, including known safety failures. |
| **Total** | **804** | |

## Evidence map for every checklist section

| Checklist IDs | Primary implementation evidence | Primary test/evaluation evidence | Scope note |
|---|---|---|---|
| 1–46 | `CoalitionCommandCenterBotModule`, `CoalitionBlackboard`, `CoalitionOrderArbiter` | `AcceptanceSuite`, `OrderArbiterTest`, `CommandValidatorTest` | Coalition-level coordination exists; local tactical claims still do not consult the coalition arbiter. |
| 47–159 | `CoalitionIntelTracker`, `CoalitionMapAnalysis`, `CoalitionRoutePlanner`, `CombatEstimator` | `ExpandedCoverageTest`, `HeadlessSkirmishTest`, `ai/selfplay.py` | Intel-history capability is present, but its observation gate has the critical fog defect described below. |
| 160–322 | `CoalitionMission`, `TacticalControllers`, `StrategicBrainBotModule` | `ExpandedCoverageTest`, mission/acceptance coverage | New pincer, blockade, and fake-buildup values are recognized, but not yet distinct, scenario-proven tactics. |
| 323–439 | `StrategicPosture`, `ReserveManager`, `ProductionContract`, `TargetEvaluator` | `ReserveManagerTest`, `TacticalFormationTest`, expanded coverage | Local posture and reserve code are limited helpers/overrides rather than full theatre management. |
| 440–521 | `ExternalBrainBotModule`, `CommandToolApi`, `CommandValidator`, `ai/model_server.py` | `LlmEvalTest`, `CommandValidatorTest` | Richer controls are JSON plan fields, not independently callable engine tools; hidden-info safety is currently broken. |
| 522–628 | `CoalitionCommandCenterBotModule`, `StrategicBrainBotModule`, `CoalitionMatchMetrics`, `TacticalFormation` | `MatchMetricsTest`, `TacticalFormationTest`, `ExpandedCoverageTest` | Many metrics are recorded, but several lack outcome-level assertions. |
| 629–747 | `mods/ra/rules/ai.yaml`, `ai/selfplay.py`, `ai/llm_eval.py` | self-check, parser/pure-logic tests, deterministic headless tests | Evaluation scripts exist, but there is no live LLM-to-engine validation or replay decision harness. |
| 748–804 | module boundaries, `HeadlessSkirmish`, test suites, documentation | `make tests`, acceptance/headless suites | The final fairness/intelligence acceptance requirements remain invalid until fog gating is repaired. |

## Top 20 gaps and risks

1. **78, 464, 739, 740, 797, 798 — fair-fog violation.** Replace all
   current-observation gates with `Shroud.IsVisible`; keep historical intel as
   immutable last-known snapshots, not live actor references.
2. **10 — tactical conflict boundary.** `StrategicBrainBotModule.Claim` is
   local and does not consult `CoalitionOrderArbiter`, so local controllers can
   still take actors notionally committed by coalition strategy.
3. **341 — local posture is shallow.** Current overrides cover home/enemy
   regions and use global strength instead of robust per-theatre evaluation.
4. **359–360 — reserve policy lacks scenario proof.** Protection and
   last-reserve justification need live mission tests.
5. **186, 195, 265 — mission names exceed execution.** Pincer, blockade, and
   fake buildup are enum/effect-level, not dedicated tactical implementations.
6. **401 — replacement production.** Logic exists but lacks regression coverage
   around a destroyed critical capability.
7. **445, 447 — external snapshot coverage.** Location and threat fields are
   populated but need direct snapshot contract tests.
8. **477–479, 485–489 — tool contract mismatch.** Controls are plan fields,
   not the callable tool API named by the checklist.
9. **519 — justified-loss reasoning.** No explicit acceptance/learning policy.
10. **521 — rapid exploitation of enemy mistakes.** Event-driven review is
    generic; no targeted mechanism or test.
11. **557 — artillery screening.** Formation helper has no full live integration
    test.
12. **559 — support synchronization.** Formation helper has no full live
    integration test.
13. **591, 599 — telemetry proof.** Events are recorded but lack focused
    end-to-end assertions.
14. **604–605 — economic-damage semantics.** Metrics exist, but the measurement
    needs scenario validation and a stable definition.
15. **612, 614 — outcome metrics.** Collection is present but value/effectiveness
    is not asserted in the public summary or a battle outcome.
16. **719, 723–725 — tunable weights.** Configuration exists without sweep-based
    evidence that adjustments have predictable effects.
17. **728 — replaying decisions.** `ai/llm_eval.py` scores plans, but does not
    re-run an engine game state through successive decisions.
18. **729–738 — LLM evaluation only at parser/pure-logic level.** No controlled
    live-model run establishes that generated plans are legal and stable.
19. **Self-play scope.** Headless simulation disables the external brain, so it
    measures the deterministic commander, not LLM strategic quality.
20. **CI health.** The most recent source CI is red: Linux `make check` reports
    style/static-analysis failures and Windows headless tests report sprite-cache
    token resolution failures. This documentation-only update does not mask it.

## Difficult architecture and bottlenecks

- **Ownership versus coalition authority:** actor ownership intentionally stays
  with each player. A single, shared reservation boundary must be used by both
  coalition and tactical controllers to prevent conflicting commands.
- **Fog-safe intelligence:** never carry a live `Actor` across an observation
  boundary. Store a time-stamped location/type/value snapshot and expose exact
  data only while currently visible.
- **LLM isolation:** deterministic validation is a good boundary, but named
  tools, JSON schema, sanitization, and execution must remain one contract.
- **Evaluation throughput:** tick-driven rebuilds, full-world scans, snapshot
  construction, and Python subprocess/model calls are the principal pressure
  points. Cache bounded summaries and profile them under headless self-play;
  do not cache live actor references.

## Prioritized remediation plan

1. Fix all explored-versus-visible gates in coalition, external-brain, radar,
   resource-map, and raid-contact paths; remove live `Actor` from remembered
   enemy intel; add regression tests that move an enemy under fog.
2. Route tactical `Claim` and release decisions through `CoalitionOrderArbiter`;
   add cross-controller contention tests.
3. Build true per-region posture/strength/mission policy and test different
   simultaneous fronts.
4. Give pincer, blockade, fake buildup, artillery screens, and fast-support
   coordination distinctive executors with headless scenario tests.
5. Choose either real callable LLM tools or a documented plan-field API; then
   test schema, execution, cancellation, reserve validation, and hidden-info
   boundaries end to end.
6. Connect `ai/llm_eval.py` to reproducible engine-state fixtures/live model
   runs, and make metric tests assert values and outcomes.
7. Resolve existing CI failures before treating the current branch as release
   quality.

## Validation performed for this audit

- Re-read coalition commander, blackboard/intel, external snapshot, tool API,
  strategic/tactical controllers, metrics, headless simulation, Python
  evaluation, and directly related tests.
- Parsed all 804 original rows and recomputed the summary counts after the
  60 current-source corrections.
- Re-ran the repository C# test suite and the Python 3.13 AI self-check after
  publishing this documentation update (results recorded in the commit).
