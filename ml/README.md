# Commander learning — stage one

## What this is

The go/no-go from the network design: **can the full position predict the outcome
better than the nine hand-picked scalars the commander already uses?**

If it cannot, the state export is missing something and nothing built on top of it
— policy head, dynamics model, search — will rescue that. Finding out here costs an
afternoon. Finding out after the search is built costs weeks.

## Result

48 matches on shattered-mountain (12 seeds x 4 opponents), fair economy, split by
**match** rather than by row — splitting by row would put samples from one game on
both sides of the holdout and score memorisation.

| model | holdout Brier | accuracy |
|---|---|---|
| always predict the base rate | 0.0768 | — |
| logistic over 13 globals | 0.0640 | 0.797 |
| entity-set encoder | **0.0561** | **0.832** |

**+12.3% on Brier. GO.**

The full position carries signal the scalars do not, which is the thing that had to
be true.

## The caveat that matters more than the headline

The encoder's best epoch is its **first**. After that, training loss keeps falling
(0.666 → 0.494) while holdout Brier climbs (0.056 → 0.076). It is memorising 36
matches, which is precisely what 36 matches predicts.

So the result reads: *the representation is right, the dataset is far too small.*
The next step is more data — not a bigger model, and not more epochs. Roughly 6,000
matches a day is reachable at six-way parallelism on eight cores; this used 48.

## Running it

```
# generate data (writes commander-states.jsonl next to the repo root)
#   requires StateLog set in mods/ra/rules/ai.yaml, and cheats OFF for a fair economy
./utility.sh ra --simulate MAP=shattered-mountain BOTS=2 TEAMS=2 TICKS=30000 \
    SEED=808 BOT_TYPES=ai,normal START_CASH=0

# train and compare
/opt/homebrew/opt/python@3.13/bin/python3.13 ml/train_value.py --data commander-states.jsonl
```

torch 2.13 with MPS. The script picks the Metal device automatically.

## The label

Not win/loss. Almost every match ends at the time limit, so `won` is zero for
essentially the whole dataset — a single-class target that teaches a model only the
base rate. This was measured: the first dataset generated was 2,219 samples, all
labelled 0.

The label used instead is the **graded margin**: how far ahead the commander
finished, in structures, normalised to -1..1. Defined for every match including the
drawn ones, and monotone in the thing worth predicting. Chess engines evaluate
positions rather than only win/draw/loss for much the same reason.

## What is deliberately not here yet

Policy head, dynamics head, auxiliary heads, search, league. In that order, and each
only once the previous one demonstrably works. See the design document.

---

# Stages two to five

## Stage 2 — auxiliary heads: NO GAIN

Ablation on ~150 matches, same encoder, same split, trained twice:

| | holdout Brier | acc |
|---|---|---|
| floor (base rate) | 0.0540 | — |
| value head only | **0.0435** | 0.845 |
| value + five auxiliary heads | 0.0443 | 0.837 |

**−1.9%.** The design predicted this would be the decision that determined whether
the thing trained at all. It was wrong, and the reason is visible in stage one's
result: the overfitting the auxiliaries were meant to cure was cured by **data**.
With 48 matches the encoder peaked at epoch 1; with ~150 it trains to epoch 15.

The auxiliary targets available cheaply are mostly extrapolations of the globals
the model already sees — "enemy army in 60s" from "enemy army now" is largely
autocorrelation. Genuinely new targets (which region gets attacked, engagement
outcomes) need labels the export does not yet carry.

## Stage 4 — policy learns, dynamics learns, search does not

| | |
|---|---|
| holdout Brier | 0.0529 |
| accuracy | 0.825 |
| **policy agreement with the scripted chief** | **0.738** |
| search changed the action | **0 / 24 positions** |

Imitation works: the policy head reproduces the chief's stance 74% of the time from
the position alone. Two real bugs were found and fixed on the way:

1. **The dynamics target was a shuffled neighbour.** `torch.roll(h, -1, dim=0)`
   rolls across the *batch*, and batches are shuffled across matches — so the
   "next latent" was an unrelated game. Dynamics loss sat at ~9 and never moved.
   Fixed by pairing each sample with its true successor before shuffling; loss
   then fell 10.7 → 2.0.

2. **PUCT gave unvisited actions Q=0.** In a 0..1 value space that reads as
   *certain loss*, so search piled all 64 simulations onto one action and the
   other five kept their zero forever — visit counts were literally
   `[0,0,64,0,0,0]`. Fixed with first-play urgency (unvisited actions start at
   the parent value) and Dirichlet root noise, which AlphaZero uses for exactly
   this. Search then explored properly: `[0,0,45,6,6,7]`.

**And it still never changed the action.** The diagnosis is measured, not guessed:

```
mean |latent|                 20.1728
mean spread across actions     0.1489
action sensitivity             0.74% of signal
```

The dynamics model **ignores the action**. It learned to, correctly, because the
data contains no counterfactual: the scripted chief is near-deterministic given a
position (policy prior 0.997 on one stance), only four of six stances ever appear,
and the same position never occurs with two different actions. Nothing in the
dataset says what a *different* choice would have done.

This is the same root cause as the three failed attempts to rank production by its
own results (0.88 → 0.74 → 0.62). A policy cannot learn to improve on itself from
data it generated deterministically.

## Stage 5 — the league, and why it is now the blocker rather than the polish

`league.py` runs the loop: serve the current main, generate matches against the
whole league, archive, retrain on every generation, snapshot, repeat.

The design listed the league last, as robustness. Stage 4 moved it: **search cannot
work until data generation explores**, and exploration is what the league provides.
Forced action diversity during generation is the prerequisite for an
action-conditional dynamics model, which is the prerequisite for search, which is
the prerequisite for the whole thing being stronger than the scripted chief it
imitates.

## Stage 3 — micro

Target selection is learnable from data already gathered: `CombatRecordRegistry`
logs every kill with both types, and `WorldDatabase.KillPairs()` now aggregates
attacker-against-victim trades in credits. That is the input to "shoot what this
unit actually trades well against" rather than nearest or weakest.

The RL micro loop itself — short episodes, dense reward — needs a combat harness
that spawns engagements outside a full match. Not built.

## Serving

`serve.py` loads a trained model and answers `/evaluate` with value, policy and
search choice. `NeuralChiefBotModule` posts the position and applies the returned
stance. **Off unless a URL is configured**: without a server the scripted chief
keeps command, which is deliberate — every claim the network makes is measured
against it, so it has to stay a working opponent.

---

# Exploration: the blocker addressed, and what it did not fix

Stage 4 diagnosed the problem as absent counterfactuals — the chief is
near-deterministic, so no position is ever seen with two different actions. That
diagnosis was acted on.

`CommanderStaffBotModule.ExplorationRate` perturbs the chief's stance with a given
probability, drawn from `world.LocalRandom` so the match stays deterministic in
lockstep. At 0.3 it changes the data exactly as intended:

| | stances present | distribution |
|---|---|---|
| before | 4 of 6 | 2:12.9% 3:45.0% 4:39.7% 5:2.5% |
| after | **6 of 6** | 0:5.0% 1:5.3% 2:13.2% 3:43.8% 4:25.1% 5:7.5% |

**And search is still inert.** Verified properly this time:

| model | action sensitivity | value spread | changed (noise on) | changed (noise OFF) |
|---|---|---|---|---|
| deterministic data | 0.82% | 0.0008 | — | — |
| exploratory data, 10s horizon | 0.40% | 0.0007 | 8/96 | **0/96** |
| exploratory data, 60s horizon | 0.33% | 0.0009 | 6/96 | **0/96** |

A second hypothesis was tested and also failed: that the dynamics horizon was
wrong. Predicting the latent ten seconds ahead asks what a stance does in ten
seconds, when a directive is held for sixty — so the target was moved to match the
directive's own lifetime. It made no difference.

## Two false positives, and the test that now prevents them

The verdict function originally sampled 24 positions with Dirichlet root noise on.
It printed **"search is active"** twice. Both were wrong: with noise off and 96
positions the answer was 0/96 each time. Noise tipping ties between six actions
whose values differ by 0.0009 is a coin landing on its edge, not search
disagreeing with its prior.

`train_stage4.py` now tests with noise off, over 96 positions, requires >5% of
positions to change, and reports the action sensitivity directly so the mechanism
is visible rather than inferred.

## What is actually blocking search

Not the mechanism — exploration, the pairing bug, PUCT's first-play urgency, and
the horizon have all now been fixed or ruled out. What remains is **volume**.

A directive lasts 1500 ticks, so a 30,000-tick match holds about 20 decisions, of
which 30% explore: roughly 6 counterfactual decisions per match, ~450 across 76
matches. That is nowhere near enough to learn how six actions differ in their
effect on a 288-dimensional latent. The league loop exists to accumulate exactly
this, over many generations.

The honest summary: the diagnosis was right and acting on it was necessary, but one
generation of exploration is not enough, and the volume needed is measured in
thousands of matches rather than dozens.

## Building on this machine

The repository sits inside a Dropbox-synced folder, and Dropbox races MSBuild's
delete-then-create when it copies into `bin/`. Compilation is fine; the copy fails
with "Access to the path ... is denied" and the destination is left missing.

`ml/build.sh` compiles each project with `--no-dependencies` and stages the outputs
with plain `cp`, which Dropbox does not interfere with. **`dotnet test` cannot be
worked around the same way** — the test runner needs its adapters alongside the
assembly in `bin/`, so the suite cannot run until the folder is excluded from
Dropbox sync (or the repository is moved outside it).
