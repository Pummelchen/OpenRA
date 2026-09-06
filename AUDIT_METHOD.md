# How to re-run the 804-requirement audit

The register lives in [AUDIT_TABLE.md](AUDIT_TABLE.md); the standing findings in
[AUDIT_REPORT.md](AUDIT_REPORT.md). This file is the *method*, so a re-audit reaches
comparable verdicts instead of new opinions.

## The rule that decides a verdict

A requirement is **complete and tested** only if all three hold:

1. **Exists** — the capability is implemented.
2. **Reachable** — it is consulted by a running match, not merely by tests.
3. **Covered** — a test exists that would fail if it broke.

Miss (2) and the verdict is **Partial**, however good the code and however green the test.

This rule is the whole point. The 2026-08-23 audit recorded 802/804 complete by checking (1)
and (3) only, and the 2026-09-06 re-audit put it at 486. Neither audit was careless; they asked
different questions. Row 260 ("synchronization error measured in telemetry") was marked complete
citing `OperationSchedule.SynchronizationError` and `CoalitionMatchMetrics.RecordSyncError` —
`OperationSchedule` has no production caller and `RecordSyncError` is defined twice and called
from no production code, so the metric is structurally always zero. Rows 187, 197, 341 and 630
fail the same way.

## Step 1 — the cheap mechanical sweep

Most reachability failures are found by grep, before any reading.

```bash
# A subsystem whose only consumers are tests is not reachable.
for sym in OperationSchedule SpecialOpsPlan ThreatDispersion ConcentrationPolicy \
           ExpansionPolicy HarvesterEconomics ProductionBalance; do
  printf "%-22s production=%s tests=%s\n" "$sym" \
    "$(grep -rl "$sym" OpenRA.Mods.Common/ | grep -v "$sym.cs" | wc -l | tr -d ' ')" \
    "$(grep -rl "$sym" OpenRA.Test/ | wc -l | tr -d ' ')"
done
```

`production=0` is a Partial, no matter what the tests say. Run the same check for any
type a requirement cites as its evidence.

Two subtler shapes worth grepping for by hand:

- **Defined but never called.** `grep -rn RecordSyncError` shows two definitions and zero
  production call sites. A definition is not a caller.
- **Declared but never assigned.** `OpponentModel.AttacksHarvesters` is read twice and
  assigned nowhere, so it is permanently false and gates a dead branch.

## Step 2 — the empirical channel audit

Reading cannot prove a value reaches a decision; the simulation is deterministic, so severing
a channel and comparing outcomes can.

```bash
./ml/channels.sh     # drops one intent type, one match, compares the result
./ml/directive.sh    # flattens one directive field, same comparison
```

An identical outcome means the decision never reached the game. This is how the reserve
conversion bug was found after reading the call site had not revealed it.

Two rules learned the hard way, both encoded in the scripts:

- **The probe match must exercise the channel.** The first run used a 20,000-tick game that
  ended `buildings_lost=0` on both sides, so the repair and relocate channels showed as
  "never emitted" when the truth was "never needed". Use a rush at 30,000 ticks.
- **Count what you severed.** "Suppressing it changed nothing" means a dead channel *or* an
  unused one, and those are different verdicts. `OPENRA_SUPPRESS_INTENTS` logs a running count
  so the two can be told apart.

One deterministic match answers *whether* a channel is connected. It is no evidence at all about
*which way* a change is better — severing staff production looked like an improvement on one
match and measured 0.658 → 0.020 over twenty-four.

## Step 3 — the reading pass, in six independent domains

Split so the domains do not overlap, and require file:line for every claim:

1. Coalition architecture, force registry, order arbitration, world state, map analysis
2. Fog of war and intel fairness, threat modelling, route planning, combat evaluation
3. Mission framework and types, recon, phasing, synchronisation, combined arms, deception, transport
4. LLM tool API, validation, fairness enforcement, context compression, events, failure handling, logging
5. Production and capability planning, economy, posture, main effort, reserve, target evaluation, opponent model, counterattack, telemetry, difficulty
6. Test suite, stress and scale, replay and regression, self-play, code organisation, documentation

Each brief must say: *distinguish "code exists" from "code affects the game"; do not speculate;
if you cannot find it, say ABSENT; an enum value with no behaviour is Partial — say so.*

Then **re-verify every headline claim by hand** before it goes in the report. Of the twenty
findings in the last pass, all twenty were re-checked directly; two agent claims needed
correction on inspection.

## Step 4 — measure what the checklist cannot see

```bash
OUT=bench_x.txt ./ml/bench.sh                      # 24 fair-economy matches
OPENRA_LOG_BELIEF_ERROR=1 OPP=rush ./ml/one.sh     # believed vs actual enemy strength
```

Standing gaps in the measurement itself, which no code audit will surface:

- Every benchmark script runs `BOTS=2 TEAMS=2` — a **1v1**. Nothing about multi-player
  coordination has ever been measured, so acceptance test 789 cannot currently be evaluated.
- `ScenarioHarness` has no World, no map and no units. No scenario test is a simulation, so it
  cannot catch an integration fault — which is what every top finding turned out to be.
- The shipped `ai.yaml` has cheats **on**. `ml/bench.sh` toggles them off and restores on exit;
  any figure quoted from a run that skipped that is not a fair-play result.

## Reporting

Give the four tallies, then the twenty findings ranked by consequence, then the ten analysis
sections. State plainly which rows were verified individually and which inherited a group
verdict — per-row confidence is not uniform, and saying so is part of the result.
