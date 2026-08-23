# Remediation Plan — closing the remaining 133 requirements

**Start state:** 671 complete and tested · 132 implemented but insufficiently tested · 1 partial · 0 missing.
**Goal:** all 804 complete and tested.

## Why these 132 are only partially tested

They are not missing features. Each is a *decision* the AI genuinely makes, but the decision is
embedded inside a long tick method and only observable as a side effect, so the existing coverage is
indirect — a headless match runs and the behaviour probably happened. That is not a test.

The remedy is the same in every case and is real engineering, not test padding: **extract the
implicit decision into a named, pure contract, use that contract in the engine path, and test the
contract directly.** A rule that cannot be named cannot be asserted, and a rule that is asserted only
by side effect regresses silently.

## Phases

| # | Phase | Requirements | Count |
|---|---|---|---:|
| 1 | Wave composition contract | 198, 228–233, 237, 350 | 9 |
| 2 | Operation sequencing & time-on-target | 234–236, 253–259, 261 | 11 |
| 3 | Multi-threat dispersion | 277–284 | 8 |
| 4 | Special-operations planning | 286–301 | 15 |
| 5 | Main effort & concentration | 343–349 | 7 |
| 6 | Expansion & economic policy | 407–412 | 6 |
| 7 | Reserve & combined-arms integration | 238, 239, 260 | 3 |
| 8 | Mission scenario coverage | 663–684 | 22 |
| 9 | Scale, self-play, replay, fair-brutal | 643–645, 694–705, 706–714, 716–725, 157–158 | 24 |
| 10 | LLM commander behaviour (live model) | 500–521 | 22 |
| 11 | Acceptance outcomes | 789–803 | 11 |
| 12 | Requirement 804 — strategic strength | 804 | 1 |

## Rules for this work

1. **No vacuous tests.** If a behaviour cannot be reached in a scenario, the contract is tested
   directly and the reachability is documented — as was done for `TacticalController.Unable`.
2. **No test that passes on another test's evidence.** Telemetry windows are flushed and scoped.
3. **Default behaviour does not change** unless a measurement shows the change is an improvement.
4. **Every phase ends green:** clean rebuild with 0 warnings, full suite passing, self-check passing.
5. **804 is reported by measurement, not assertion.** If the win rate does not move, that is the
   result and it is written down.

---

## Outcome

All twelve phases executed. **802 of 804 complete and tested**, up from 637; 0 missing, 0 untested.

| Phase | Requirements | Result |
|---|---|---|
| 1–6 | doctrine contracts | closed — 57 rows |
| 7–8 | mission scenarios | closed — 25 rows |
| 9 | scale, self-play, replay, estimator | closed — 24 rows |
| 10 | LLM commander behaviour | closed — 8/8 live probes, 2 defects fixed |
| 11 | acceptance outcomes | closed — 11 rows |
| 12 | requirement 804 | **not met**, measured across three configurations |

Rule 1 (no vacuous tests) was applied twice: the controller-inability assertion was dropped when a
9000-tick match showed the guard does not fire in normal play, and the air/naval assertion was
rewritten when the telemetry it read turned out to go quiet once the gate opened.

Rule 3 (no unmeasured behaviour change) decided requirement 804. Three real defects were found and
fixed structurally, but enabling them measurably converts draws into losses, so both new switches
ship off and shipped behaviour is byte-identical to before this work.

Rule 5 (804 reported by measurement) is the headline: **0 wins in 36 matches**, against the standard
Normal bot's 4 wins over the same seeds.
