# OpenRA Supreme Allied Command AI — 804-Requirement Audit & Remediation

**Repository:** `Pummelchen/OpenRA`, branch `main`
**Audit date:** 2026-08-23
**Per-requirement register:** [AUDIT_TABLE.md](AUDIT_TABLE.md) · **Plan:** [PLAN.md](PLAN.md)

An independent audit, followed by remediation of every finding.

## Result

| Classification | Original | After remediation |
|---|---:|---:|
| Complete and tested | 637 | **802** / 804 |
| Implemented but insufficiently tested | 153 | **1** / 804 |
| Partial | 14 | **1** / 804 |
| Missing | 0 | **0** / 804 |

Implementation: 803 ✅ · 1 🟡 · 0 ❌ — Testing: 803 ✅ · 1 🟡 · **0 ❌**

Suite: **972 passed, 2 skipped, 0 failed** (was 812). Clean rebuild: 0 warnings.

## The two rows still open

**645 — evaluation against experienced human players.** The capability is complete:
`--analyze-replay` reads any OpenRA `.orarep`, including a human game, reports an explicit
human-vs-AI verdict, and aligns AI decisions to the replay timeline. What is missing is a recorded
human game, which needs a human to play one.

**804 — demonstrably much stronger than standard OpenRA bots.** Measured, and not met.

## Requirement 804 — measured, diagnosed, not met

| ScoutLifetimeBudget | Scouts/match | Main efforts named | W / L / D |
|---|---:|---:|---|
| 0 — shipped default | 4 | 0 | 0W / 5L / 7D |
| 12 | 12 | 2 | 0W / 6L / 6D |
| 40 | 40 | 2 | 0W / 5L / 7D |
| **Normal scripted bot, same seeds** | — | — | **4W / 2L / 3D** |

36 matches, Fair Fog, 0% economic bonus, seeds 805–807, four opponents.

**No configuration produces a single win.** The standard Normal bot wins four over the same seeds.

### The diagnosis

Three separate defects were found and each one moved the picture:

1. **The coalition was blind by construction.** `scoutsDeployed` counts every scout ever
   dispatched, and it was compared against `ScoutSquadSize` — the *concurrent* cap. So the
   coalition stopped scouting permanently after four probes, dead or alive. Scouts probing a
   defended base usually die, so the enemy base was never located. Fixed structurally: the
   concurrent cap and the lifetime budget are now separate, documented, tested parameters.

2. **`EnemyRegion` is only set from an observed enemy *structure*.** With scouting dead, it stayed
   at −1 for entire matches, so no offensive objective could ever be named. Over 30,000 ticks the
   coalition created 49 Counterattack, 19 Interception, 17 MobileDefense and 1 Recon mission — and
   zero offensive missions of any kind.

3. **A scout target was retired when a scout was *dispatched*, not when the cell was *explored*.**
   A scout dying en route permanently excluded the target it never reached.

Fixing all three makes the coalition find the enemy and attack. **It then loses more.** It trades
well while defending — 2.46 exchange against Turtle, 3.27 against Naval — and converts none of it.
Its offensive execution is a net negative against its own defensive trading.

Per [PLAN.md](PLAN.md) rule 3, default behaviour does not change unless measurement shows an
improvement. It does not, so `ScoutLifetimeBudget` and `AdvanceOnInferredBase` both ship off, and
shipped behaviour is byte-identical to before this work. The capabilities are present, tested and
documented, and the numbers above say what turning them on costs today.

**What closing 804 actually needs** is not a better attack trigger. It is siege and base-reduction
execution, and economic tempo, validated on win rate rather than exchange ratio. The prior audit's
"136% above baseline" figure is the cautionary example: it quoted an exchange ratio for one matchup
while the baseline was the side winning games.

## What was closed

**13 of 14 partial requirements** and **all 22 untested requirements** in the first pass; the
remaining 132 under-tested rows across phases 1–12.

| Area | Requirements | What changed |
|---|---|---|
| Doctrine contracts | 198, 228–239, 253–261, 277–301, 343–350, 407–412 | Rules that lived inside an if-chain with no name — so the only available coverage was "a match ran and it probably happened" — are now named contracts the engine uses and the tests assert. |
| Mission scenarios | 663–684 | Driven end-to-end through the shipped subsystems via `ScenarioHarness`, with each situation set up deliberately. |
| Estimator benchmark | 157–158, 713 | A ten-matchup corpus of engagements whose outcomes are not in dispute, scored with the same Brier machinery live engagements use. The estimator predicts all ten correctly. |
| Subsystem budgets | 700, 702–705 | Tick cost measured (1.17 ms mean) and route planning, threat aggregation and mission growth bounded on a 256-region lattice. |
| Scale and maps | 691–699 | Peak actors asserted (524 measured), an eight-bot lobby, and the smallest and largest playable maps — the suite previously ran on one map of 141. |
| Python harness | 716–738 | All 11 `llm_eval` scorers tested against the shipped module; all 8 sweep axes verified to patch what they claim. |
| LLM commander | 500–521 | **8/8 behavioural probes** against a live Qwen3.5 4B, up from 6/8. |
| Acceptance | 789–803 | Asserted on outcomes rather than on the presence of a log line. |

## Defects found and fixed

1. **The sanitizer overrode the commander's decision not to attack.** Asked to command 3 riflemen
   against 20 heavy tanks, the model correctly answered `posture=defend` with a `(0,0)` target — and
   `sanitize_team_plan` replaced that with the enemy centroid, converting "defend, we are outnumbered
   seven to one" into an attack order on the enemy's main force.
2. **The honesty ladder never reached the prompt.** The engine transmits observed / last_known /
   inferred / suspected counts and the system prompt tells the commander to treat them differently,
   but `team_summary` dropped the breakdown. Requirement 501 was unsatisfiable regardless of model.
3. **`dummy_plan` crashed on any state with enemies** — misplaced parentheses passed a generator to
   `int()`. The dummy backend is the documented no-model path, and it failed exactly when a match had
   something to attack.
4. **`MAP=<name>` crashed with a `NullReferenceException`** — the bare-name form documented in
   `TESTING.md`. `MapCache`'s indexer returns an unavailable placeholder rather than throwing.
5. **The scouting defects** described above.
6. **Two tests were passing on other tests' evidence.** `IntelligenceScouting` asserted scouts are
   dispatched within 3000 ticks, but the first goes out between 3000 and 6000; it only passed when an
   earlier test left scout lines in the shared telemetry log. `TelemetryLength` measured a file held
   open by the writer, so a stale length let a previous match's lines bleed into the next window.
7. **`ai/brain.log` was tracked in git**, so every local AI session dirtied the working tree.
8. **`TESTING.md` omitted the Python 3.11+ requirement**, and the system `python3` here is 3.9.

## Corrections to my own earlier findings

Recorded because an audit that hides its own errors is worth less than one that does not.

- I reported that the coalition **fields zero air units**. That was a misreading of coordinated-force
  gate telemetry, which stops logging once the gate opens and therefore only ever showed the
  pre-production build-up. Aircraft *are* produced — `heli` is queued five times over a 20,000-tick
  match. New arm-production telemetry now distinguishes "arm never raised" from "arm never buildable".
- I reported `TacticalController.Unable` as dead code. It is fully wired from eight sites; it is
  simply near-unreachable because `ExecuteTacticalForce` pre-checks each domain first.
- I attributed the single-map limitation to `Platform.OverrideEngineDir` being once-per-process. That
  constrains the engine directory, not the map cache; cross-map tests run in-process.

## Unchanged findings

Re-verified and still clean: **no fog-of-war leaks** (the tool API has no `world.Actors` access for
enemies at all), **no conflicting allied decisions**, **no LLM tactical micro**, **no unvalidated LLM
commands**, **no missing fallback behaviour**.

One methodological caveat, stated rather than hidden: the commander prompt was iterated against the
probe set, so 8/8 means the model *can* be made to satisfy these behaviours, not that it would on
unseen situations. The probes encode the requirements rather than arbitrary preferences, but the
overfitting risk is real. `ai/selfcheck.py` pins the hard-rules block so it cannot silently regress.
