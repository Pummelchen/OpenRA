# OpenRA Supreme Allied Command AI — 804-Requirement Audit & Remediation

**Repository:** `Pummelchen/OpenRA`, branch `main`
**Audit date:** 2026-08-23
**Per-requirement register:** [AUDIT_TABLE.md](AUDIT_TABLE.md)

An independent re-audit, followed by remediation of every finding.

## Result

| Classification | Before | After |
|---|---:|---:|
| Complete and tested | 637 | **671** / 804 |
| Implemented but insufficiently tested | 153 | **132** / 804 |
| Partial | 14 | **1** / 804 |
| Missing | 0 | **0** / 804 |

Implementation: 803 ✅ · 1 🟡 · 0 ❌ — Testing: 672 ✅ · 132 🟡 · **0 ❌**

**Thirteen of fourteen partial requirements are closed and all 22 untested requirements now have
tests.** The one still open is 804, which is an empirical outcome, not a code gap.

## Validation executed

| Check | Result |
|---|---|
| `dotnet build` (clean rebuild) | succeeded, 0 warnings, 0 errors |
| `dotnet test bin/OpenRA.Test.dll` | 863 passed, 2 skipped, 0 failed (was 812) |
| `.venv-ai/bin/python ai/selfcheck.py` | passed, including the new 11-scorer Python suite |
| Fixed-seed opponent matrix | 12 matches, Fair Fog, 0% bonus |
| Tick-cost budget | 1.17 ms mean, ~70 ms slowest, 524 peak actors |

---

## 1. What was closed

### The 13 partial requirements

| # | Requirement | What was built |
|---|---|---|
| 26 | Mixed-owner force groups | `CoalitionForcePackage`: every allied contingent on one mission aggregated into one object with combined strength and capabilities, so a package short on anti-air is short *coalition-wide*. Orders stay per-owner because OpenRA forbids otherwise. Exposed as `inspect_force_package`. |
| 187 | Exploitation mission | First-class `MissionType.Exploitation` starting in `MissionPhase.Exploitation`, created once a breakthrough actually opens a breach — the follow-on force is distinct from the breaching force. |
| 202 | Emergency reinforcement | `MissionType.EmergencyReinforcement` with its own `relief` defense kind, fired when observed attackers outnumber the defenders already covering an asset. Proportional, so a raid the garrison can handle does not pull the main effort. |
| 204 | Interception | `MissionType.Interception` with an `intercept` defense kind, cutting off a mobile enemy inside an approach band; an enemy already at the base is base defense instead. |
| 571 | Support-power coverage | Chronosphere, Advanced Chronoshift and Iron Curtain now have `Redeployment`/`Protection` roles and are wired into `ai.yaml`. Force multipliers invert the friendly-fire rule — they need a committed friendly force at the target, not an empty cell. |
| 604/605 | Economic damage | Measured in credits across refineries, harvesters and silos rather than refinery counts. A peak that only ratchets downward means an enemy rebuild cannot erase damage already recorded. |
| 622 | Opponent-model accuracy | `OpponentPredictionLog` scores the profile's own forecasts against later observation — distinct from the combat estimator. Unresolved predictions never score as correct; repeated forecasts are not double-counted; resolution is final; calibration reports whether the model is more confident when right. |
| 159 | Combat-estimator accuracy | `EngagementOutcomeLog` records each mission's prediction at commit and resolves it against the real outcome, scored with a Brier score. Confident-and-wrong scores 1.0 where hedged-and-wrong scores 0.25. Hindsight cannot improve the score. |
| 645/707/708 | Replay evaluation | `--analyze-replay` reads any OpenRA `.orarep`, including a human game, reports an explicit human-vs-AI verdict, and aligns tick-stamped decisions onto the replay timeline. |
| 717 | Faction selection | `FACTION=` / `--faction` / `CommanderFaction`. An unknown faction is a hard error, not a silent fallback that would mislabel a batch. |

### The 22 untested requirements

- **700 — tick cost.** Nothing measured timing anywhere; a performance regression was invisible.
  Now recorded and asserted against the 40 ms real-time budget (measured 1.17 ms mean).
- **695/698 — scale.** `StressScale` asserted `ActorCount > 0`, which passes on a match that never
  grew. Now asserts 100+ peak simultaneous actors (measured 524).
- **691/692/727 — cross-map.** The suite ran on one map of 141. The blocker was assumed to be
  `Platform.OverrideEngineDir` being once-per-process; that constrains the *engine directory*, not
  the map cache. Smallest and largest playable maps now run in-process.
- **730–737 — seven `llm_eval.py` scorers with no test at all**, plus **729/734/738** which were
  covered only by a C# re-implementation *inside the test file*. `ai/test_llm_eval.py` now covers
  all 11 scorers against the shipped module, and `LlmEvalTest` runs it in a subprocess.
- **548/549 — controller replan.** Debounce extracted as pure `MayReplan` and tested.
- **689/799 — mid-battle LLM dropout.** Now tested as a transition, not a cold start.
- **709/710/712 — regression tests.** `RegressionTest` pins defects that actually failed.

## 2. Bugs found and fixed

1. **`MAP=<name>` crashed with a `NullReferenceException`.** The bare-name form documented in
   `TESTING.md` and `ai/README.md` was broken: `MapCache`'s indexer returns an unavailable
   placeholder for an unknown key instead of throwing, so `ToMap()` dereferenced null and the
   existing `KeyNotFoundException` handler never ran. Now resolves by uid or title with an
   actionable error.
2. **`IntelligenceScouting` was passing on another test's evidence.** It ran 3000 ticks and
   asserted scouts are dispatched, but the first scout goes out between 3000 and 6000 on that map.
   It only passed when an earlier test left scout lines in the shared telemetry log.
3. **Telemetry offsets were measured on a file held open by the writer**, so a stale length let a
   previous match's lines bleed into the next test's window — the cause of `DeterministicSameSeed`
   failing only in full-suite order.
4. **`ai/brain.log` was tracked in git** while its sibling logs were ignored, so every local AI
   session dirtied the working tree.
5. **`TESTING.md` did not record the Python 3.11+ requirement**, and the system `python3` here is
   3.9, so the documented self-check command fails outright.

## 3. Requirement 804 — measured, still not met

This is the honest headline. Over a 12-match fixed-seed matrix (3 seeds × 4 opponents, Fair Fog,
0% economic bonus):

| Team 1 | vs Rush | vs Normal | vs Turtle | vs Naval | Total |
|---|---|---|---|---|---|
| **Supreme (`ai`)** | 0W/2L/1D · 1.19 | 0W/3L/0D · 0.39 | 0W/0L/3D · 2.46 | 0W/0L/3D · 3.27 | **0W / 5L / 7D** |
| **Normal baseline** | 0W/2L/1D · 0.59 | — | 1W/0L/2D · 1.15 | 3W/0L/0D · 5.64 | **4W / 2L / 3D** |

**Supreme wins no matches. The standard Normal bot wins four over the same seeds, and beats
Supreme 3–0 head-to-head at 0.39 exchange.** Supreme trades better against Turtle and Naval but
converts none of it — all six are time-limit draws. The previous claim of "~136% above baseline"
measured *exchange ratio* on a single matchup while the baseline was the side actually winning.

### Root cause, identified

`CoalitionBlackboard.EnemyRegion` is set **only from an observed enemy structure**. Under fair fog
a coalition whose scouts never reach a base leaves it at −1 for the whole match. The
deliberate-assault gate additionally needs a 33% strength edge, while the enemy estimate carries a
fog floor proportional to the *unexplored* map — so an army that never advances can never earn that
edge, because it assumes a large hidden enemy precisely as a consequence of not having looked.

Traced over a 30,000-tick match, the coalition created **49 Counterattack, 19 Interception, 17
MobileDefense, 1 Recon — and zero offensive missions of any kind.** It out-traded its opponent and
never threatened it. That is exactly the shape of the results table: good exchange, no wins.

### Fix built, and deliberately left disabled

An offensive objective inferred from public map starting locations (preferring the unexplored spawn
nearest the axis enemy forces arrive along), plus a reconnaissance-in-force rule that advances only
with an overwhelming force after scouting has demonstrably failed.

Measured, it **did not work**: no wins, and it cost reconnaissance because the army it commits is
drawn from the same pool the scouting probes come from. Against Rush the ground-truth exchange fell
from 1.25 to 0.79 at a 1× force threshold, recovering only to 1.19 at 3×. Shipping it enabled would
ship a measured regression, so it is kept, tested and documented behind `AdvanceOnInferredBase`
(default off). **Default behaviour is byte-identical to before this work.**

### What would actually be required

The problem is not the attack trigger; it is that the coalition cannot convert a material advantage
into a base kill. Closing 804 needs siege/base-reduction execution and economic tempo work,
validated on win rate rather than exchange ratio, across more than three seeds per matchup. The
instrumentation to measure that now exists — per-engagement estimator scoring, tick-cost budgets,
peak-scale assertions, faction pinning and cross-map runs — which is what makes the next attempt
falsifiable in a way this one would not have been.

## 4. Unchanged findings from the original audit

Re-verified and still clean: **no fog-of-war leaks** (the tool API has no `world.Actors` access for
enemies at all), **no conflicting allied decisions**, **no LLM tactical micro**, **no unvalidated
LLM commands**, **no missing fallback behaviour in kind**.

The 132 rows still marked "insufficiently tested" are concentrated in §34 (LLM commander behaviour,
verified by prompt contract because the suite never runs a model), §46 (end-to-end mission
scenarios, covered at contract level), and the acceptance cases, which remain telemetry-marker
assertions rather than behavioural outcomes.
