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

### A — Objective accounting *(the spine exists and tells the truth)*

One `Objective` type flowing decision → execution → verification. Completion tested against effect.
Telemetry reports objectives issued, accomplished, failed, abandoned, and **credits destroyed**.

**Gate:** reported success rate is no longer 100%, and destroyed credits appear in the record.
*A success rate of 100% is not excellence; it is a broken test.*

### B — Find *(the chain's first link)*

**Observe** objectives, directed by the belief state's most-uncertain region rather than by geometry.
Completion: the region was actually seen.

**Gate:** the enemy base is located in the majority of matches, counted directly.

### C — Destroy *(the link that ends games)*

A force that arrives at a **Destroy** objective reduces what is there, and stays until it does or
until a declared abort fires. Target selection prefers structures once the ground is taken.

**Gate:** enemy economic damage is greater than zero in most matches. Currently zero in all of them.

### D — Choose *(the decision layer, now measurable)*

Search ranks objectives using the forward model and evaluator. This is where the existing phase 2–4
work finally attaches — and it could not have been evaluated before C, because every choice produced
the same non-effect.

**Gate:** mirror decisiveness beats the 9-of-24 baseline the old commander already achieves.

### E — Adapt *(the parts currently shelved)*

The posterior predicts which objectives the opponent will pursue; regret matching mixes our own so
the choice cannot be punished; build-order search produces the force an objective needs. Each one
wires to the spine or is deleted.

**Gate:** each beats its absence on a game number. Anything that cannot is removed, not kept "for
later" — that is how the shelfware happened.

---

## 6. What carries over unchanged

The mathematics was not the problem, and most of it stands: the region decomposition and its
chokepoints, the counter-matrix reduction that makes Lanchester legitimate, the measured-and-anchored
economy model, the belief state with negative evidence, the plan-commitment contract, and the
calibration harness that caught four of this session's defects including one of my own making.

What changes is the order they are connected in, and the standard for calling any of it done.
