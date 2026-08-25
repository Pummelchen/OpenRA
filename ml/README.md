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
