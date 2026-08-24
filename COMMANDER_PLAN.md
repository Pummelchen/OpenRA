# The Commander, Re-planned

The first plan is superseded. This one is built from what measurement actually showed, and it is
organised differently on purpose. [`COMMANDER_ARCHITECTURE.md`](COMMANDER_ARCHITECTURE.md) remains
correct about the *mathematics*; it was wrong about the *order*, and about which layer was broken.

---

## 1. What the first plan got wrong

The original plan was a stack: terrain, then a forward model, then an evaluator, then search, then
belief, then game theory, then build orders. Seven phases, each a technique, each gated on its own
correctness. All seven were built. All seven pass their tests. **The commander did not get better.**

Three failures, and they are failures of the plan rather than of the code.

### It was organised by technique instead of by the chain that produces a win

Winning is one causal chain: *find the enemy → commit a force → destroy their base → and be able to
tell that you did*. The plan built capabilities in the order they appear in a textbook, not in the
order that chain requires them. So each phase was verifiable in isolation and none of them moved the
outcome, because the chain was broken at a link no phase was looking at.

### It assumed the executor was sound, on bad evidence

The architecture said the execution layer "is not the problem and should not be rewritten", citing a
2.46 exchange ratio as proof it does what it is told. Trading well is not evidence of being able to
take a base, and the rebuild proved it: with the entire decision layer replaced and the commander
committing to twelve assaults a match, kills were unchanged (220k against 223k) and **economic
damage to the enemy was zero credits and zero refineries** — while the mission system reported
65 of 65 missions successful.

Two defects underneath that, neither a tuning question:

- **An assault succeeded when the commander could no longer *see* an enemy in the target region** —
  not when it destroyed anything. Under fog that is true most of the time, *including before the
  force has set off*, so assaults were declared won on departure.
- **Reconnaissance never found the enemy base.** Forty scouts a match, sent to map edges by a
  geometric sweep, located it exactly zero times. Every assault after that took empty ground.

A plan is only as good as the executor's definition of done, and **a success criterion that can be
satisfied by not looking will be satisfied constantly.**

### It let components ship unwired

Of everything built, three whole phases are referenced by nothing at all: the opponent posterior,
regret matching, and build-order search. Min-cut — a headline result of phase 1 — informs no
decision. They have tests, which makes them look like capability. They are shelfware, and shelfware
is worse than an empty directory because it reads as progress on a status report.

---

## 2. The measurements this plan is built on

| Finding | Evidence |
|---|---|
| Every resource injection made closing *less* likely | 5k → 3/4 mirrors decisive; 20k → 1/4; 20k + cheats → 0/4 |
| At 20k the commander cannot close a mirror at all | 0 of 3 decisive in 60,000 ticks — 40 minutes of game time |
| Assaults destroy nothing | 0 credits, 0 refineries of enemy economy across a 30,000-tick match |
| The executor reports success regardless | 65 of 65 missions "succeeded" during that match |
| Reconnaissance never finds the base | 40 scouts a match, base located 0 times |
| The new decision layer is a regression | 9/24 mirrors decisive without it, 5/24 with it, 7/24 after two fixes |
| Fog turns "advantages" into constants | 6 of 9 evaluator features were constant or near-constant over 16,000 states |
| Army value at 30 s is near a random walk | Every extrapolated trend scored worse than assuming no change |

---

## 3. The spine: one Objective, end to end

The re-design has a single organising idea. Everything the commander does that is meant to change
the world is an **Objective**, and an Objective carries its own definition of done — stated in terms
of *effect*, never of what happens to be visible.

```
Objective
├── Kind        Destroy · Hold · Observe · Deny
├── Where       region
├── What        structures · army · ground
├── Committed   until tick, with declared abort conditions
├── Done when   an effect on the world, measured
└── Outcome     Accomplished | Failed | Abandoned, + credits actually destroyed
```

Everything else in the architecture hangs off that spine and earns its place by serving it:

| Component | What the spine needs it for |
|---|---|
| Region graph, min-cut | Naming *where* — and which chokes an objective must pass |
| Belief state | Choosing **Observe** objectives, and estimating what a **Destroy** faces |
| Forward model | Predicting whether an objective is achievable before committing |
| Evaluator | Ranking objectives against each other |
| Search | Choosing among them |
| Opponent posterior | Predicting which objectives *they* will pursue |
| Regret matching | Mixing objective types so the choice cannot be punished |
| Build-order search | Producing the force an objective requires |

Read the other way, this is the test the first plan lacked: **if a component cannot be connected to
the spine, it is not ready to be built.**

---

## 4. The rule that follows from all of it

> **No component ships unwired, and no gate is a proxy.**

Every phase below ends with a number measured on the *game*, not on the component. A phase that
cannot move a game number is not finished, however green its tests. And a phase whose code is not
reachable from the spine is not started, however complete it looks.

The corollary, learned three times over this session: **any quantity that compares something we know
exactly against something the fog hides is not a comparison.** Own cash against enemy cash is a
constant. Own base against their base is a constant. Own army against *seen* enemy army is a
measurement of where the scouts are. Under fog, an unknown must be modelled as an unknown — with a
prior — or it silently becomes a bias term.

---

## 5. The phases, re-ordered along the chain

### A — Spend *(the link upstream of everything else)*

This phase was not in the first draft of this document, because the measurement that produced it
came afterwards — and it belongs first. The commander **earns 289,500 credits and banks 213,413 of
them**, out-earning its opponent 1.6 to 1 while spending less than half as much and finishing with
14 structures against 55. Every later link is moot while that holds: finding the enemy, choosing a
target and destroying it are all downstream of having something to do it with.

Restoring `BuildingFractions` from 1% to the upstream 30/35% and removing `BuildingLimits` recovered
part of it. The rest is unit production, which the coalition drives from its own strategic brain
rather than the stock unit builder.

**Gate:** banked cash falls below a third of earnings, and the commander's structure count is within
reach of the opponent's rather than a quarter of it.

### B — Objective accounting *(the spine exists and tells the truth)*

One `Objective` type flowing decision → execution → verification. Completion tested against effect.
Telemetry reports objectives issued, accomplished, failed, abandoned, and **credits destroyed**.

**Gate:** reported success rate is no longer 100%, and destroyed credits appear in the record.
*A success rate of 100% is not excellence; it is a broken test.*

### C — Find *(what the assaults need)*

**Observe** objectives, directed by the belief state's most-uncertain region rather than by geometry.
Completion: the region was actually seen.

**Gate:** the enemy base is located in the majority of matches, counted directly.

### D — Destroy *(the link that ends games)*

A force that arrives at a **Destroy** objective reduces what is there, and stays until it does or
until a declared abort fires. Target selection prefers structures once the ground is taken.

**Gate:** enemy economic damage is greater than zero in most matches. Currently zero in all of them.

### E — Choose *(the decision layer, now measurable)*

Search ranks objectives using the forward model and evaluator. This is where the existing phase 2–4
work finally attaches — and it could not have been evaluated before D, because every choice produced
the same non-effect.

**Gate:** mirror decisiveness beats the 9-of-24 baseline the old commander already achieves.

### F — Adapt *(the parts currently shelved)*

The posterior predicts which objectives the opponent will pursue; regret matching mixes our own so
the choice cannot be punished; build-order search produces the force an objective needs. Each one
wires to the spine or is deleted.

**Gate:** each beats its absence on a game number. Anything that cannot is removed, not kept "for
later" — that is how the shelfware happened.

---

## 6. Measured status of each phase

Recorded honestly, because a plan whose status is asserted rather than measured is how the first
one went wrong. Every number below is from the same 24 mirror matches or the same three
seeds against Rush on shattered-mountain.

| Phase | Gate | Status |
|---|---|---|
| A — Spend | Banked cash below a third of earnings | **Not met.** 74.3% → 67.6%. Real progress, gate missed. |
| B — Objective accounting | Success rate not 100%; destroyed credits recorded | **Met.** Assaults now require holding the ground; ground-truth structure/cash reporting added to the harness. |
| C — Find | Enemy base located in most matches | **Instrumented, not yet passing.** Scouting is belief-directed and probes the interior rather than map edges, and the located-base rate is now counted directly instead of inferred from a telemetry line that never existed. |
| D — Destroy | Enemy structures destroyed, measured outside the fog | **Met.** 41 destroyed against 37 lost over six seeds - the building trade is now favourable, where it was 41 against 58. The bot's own economic-damage metric read zero throughout, because it only counts enemy economy it has *observed*. |
| E — Choose | Mirror decisiveness beats 9 of 24 | **Not met.** 7 of 24 with the searched planner, 8 of 24 without it. The planner ships disabled. |
| F — Adapt | Each component wired or deleted | **Done.** `StrategyPosterior` wired to counter-production; `RegretMatching` and `BuildOrderSearch` deleted at 6bcef9f. |

What did move, on three seeds against Rush: the exchange ratio from 1.29 to 1.72, structures from
71 to 74, and banked cash from 74.3% to 67.6% — by taking investment out of an economy the
production queues could never spend and putting it into production.

What did not move, through every change tried: **mirror decisiveness, which has sat between 5 and
9 of 24 for the whole rebuild.** That is the number the commander is ultimately judged on, and
nothing built so far has improved it. It is stated here rather than buried because the first plan's
failure was precisely that its phases could all report success while this number stood still.

### What target selection taught us

Two attempts to make assaults finish an opponent were measured and rejected, and both are worth
recording because the reasoning sounded right in each case.

The construction yard was scoring *below a refinery* - economy is weighted x3 and production x2, so
`proc` came to 30 against `fact`'s 20 - and over 90,000 ticks on shattered-mountain/808 the
commander destroyed **87 enemy buildings for the loss of 2 and still could not end the match**,
because the opponent rebuilt behind the yard indefinitely. Raising `fact` to the top of the list
made things distinctly worse: 41 buildings destroyed fell to 15, losses rose from 37 to 46, and
three long games that had been a dominant stalemate and two losses became three losses.

The lesson is that the construction yard is the best target only *if it can actually be taken*.
Ranking it first sends the army into the most heavily defended point of the base, where it dies -
which is what `SiegeTargeting.RequiredLocalSuperiority` exists to prevent and what a target list
alone cannot express. The right fix is conditional on local superiority, not a constant.

### The scorecard, against every scripted opponent

Everything above was tuned against Rush, and measuring against the other three showed how badly
that misled. Two seeds each on shattered-mountain, with the shipped configuration:

| Opponent | Record | Exchange | Enemy base located |
|---|---|---|---|
| rush | 0W 0L | 2.12 | 1 of 3 |
| normal | 0W 0L | 1.23 | 3 of 3 |
| turtle | 0W 0L | 0.85 | 3 of 3 |
| **naval** | **0W 2L** | **0.17** | 2 of 3 |

**Naval is a catastrophe and it is not new.** The same matchup lost 3 of 3 before any of this
phase's work, so it is a longstanding weakness rather than a regression - but the Rush-tuned
configuration made the exchange ratio worse, 0.47 to 0.17.

The cause is specific and it is an intelligence failure, not a combat one. The naval bot builds
helipads and attacks with aircraft; our base is destroyed by 16,000 ticks and the telemetry reads
`Enemy composition: armor=False air=False` throughout. **We are killed by an enemy we never
observe**, so anti-air is never promoted, the counter is never built, and the fight is decided
before it is understood. It loses on every map tried, so it is not terrain.

Two configurations were compared across all four opponents rather than against Rush alone, and they
came out equal - exchange 1.24 against 1.23, same record. The shipped one is kept because it fields
larger armies for the same ratio and honours the power-headroom requirement.

### Two more measured rejections

Both sounded right and both were wrong, which is why they are recorded rather than quietly dropped.

**Kennel spam turned out to be reconnaissance.** With `BuildingLimits` gone the commander built six
dog kennels, which looked like obvious waste - a kennel makes scout dogs, not army. Removing `kenn`
from `ProductionTypes` cleaned the base up handsomely (six kennels to one, three war factories to
four) and improved the exchange ratio on the seed it was inspected on, 1.81 to 2.49. Over six seeds
it was clearly worse: enemy buildings destroyed fell from 41 to 20 and the exchange ratio to 1.54.
The spare kennels were funding the dog scouting that finds bases to attack, and the "waste" was
buying the thing the commander is worst at.

**Single-seed inspection is how both of these nearly shipped.** Every configuration change in this
phase moved at least one seed in the flattering direction. The six-seed measurement disagreed with
the inspected seed three times out of four.

### The constraint behind Phase A

Red Alert's `ClassicProductionQueue` is one queue per player per type. Throughput scales with
`BuildTimeSpeedReduction` as factories are added, not with factory count directly, so a base earning
roughly 250 credits a second cannot convert it all into force however many factories it builds.
Three separate attempts to close the gap were measured and rejected:

- Raising production fractions alone made the base *smaller* (71 → 37 structures), because expensive
  factories crowd out the cheap power and refineries a base needs to grow at all.
- Letting queues duplicate unit types above a cash threshold produced byte-identical results, because
  there is rarely more than one idle queue of a given type to duplicate across.
- Keeping a production backlog so queues never idle between orders cost exchange ratio (1.72 → 1.50)
  and structures (74 → 55), because cash committed to a backlog is cash not available for buildings.

The honest reading is that the remaining third of the income has nowhere to go under this queue
model, and that the next real gain is in *what* is built rather than *how much*.

## 7. What carries over unchanged

The mathematics was not the problem, and most of it stands: the region decomposition and its
chokepoints, the counter-matrix reduction that makes Lanchester legitimate, the measured-and-anchored
economy model, the belief state with negative evidence, the plan-commitment contract, and the
calibration harness that caught four of this session's defects including one of my own making.

What changes is the order they are connected in, and the standard for calling any of it done.
