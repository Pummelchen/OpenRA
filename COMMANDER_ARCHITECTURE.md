# The Commander, Rebuilt

A design for a deterministic C# commander strong enough that the LLM is an optional
accelerator rather than the source of competence.

This document answers one question: **what mathematical model gives a commander a real-time
understanding of the whole game, and how is that model encoded in code?** It is the design
input for the rebuild. [`COMMANDER_HANDBOOK.md`](COMMANDER_HANDBOOK.md) remains the doctrine —
what a good commander *does*. This is the machinery that lets it do any of it.

---

## 1. Why the current commander cannot win, and why tuning will not fix it

The current commander is not losing. It is failing to *win*, which is a different defect with a
different cause.

Measured on two mid-size maps, two opponents, fixed seeds:

| Configuration | vs scripted | Mirror games decided |
|---|---|---|
| 5,000 start, fair play | 0W / 1L | 3 of 4 |
| 20,000 start, fair play | 0W / 2L | 1 of 4 |
| 20,000 start, full cheats | 0W / 0L | 0 of 4 |

Its combat exchange ratio is 2.43 — it wins the fights it takes. It converts none of them.

The decisive observation is the direction of the trend. **Every resource injection made it worse
at closing games.** Instant construction, an unlimited tech tree, four times the starting capital:
each one reduced the number of games it could finish. A commander starved of units gets better when
you hand it units. This one gets worse, which means units were never the binding constraint.

The mechanism is a single line of control flow. `RunCommand()` recomputes strategic posture on
every review from the instantaneous army ratio:

```csharp
var ratio = coalitionArmy <= 0 ? 0 : enemyArmy / coalitionArmy;
var newPosture = PostureSelection.Select(ratio, enemyStaticDefense, ...);
```

`PostureSelection.Select` is a pure function of the current snapshot. There is no plan state, no
memory of what was decided ten seconds ago, and no estimate of what the state will look like ten
seconds from now.

Now consider what an attack *is*. You move a force to the enemy base, you lose units to the
defences, and only after that do you kill the production that wins you the game. The army ratio
falls before it rises. It falls **every single time**, in every successful attack ever played.

A commander that re-derives its posture from the instantaneous ratio flips to Defensive at exactly
that moment and recalls the assault. It is not badly tuned. It is structurally incapable of
executing any plan whose intermediate states look worse than its starting state — which is the
definition of an attack. And the more cheaply it can replace losses, the faster the ratio
recovers, the more often it flip-flops, and the less it ever accomplishes. That is precisely the
inverted trend in the table.

No threshold change repairs this. A commander needs three things it does not currently have:

1. A way to say what the state will look like **later** (a forward model).
2. A way to say which of two futures is **better** (an evaluation function).
3. A way to **commit** to a sequence of actions across states that look temporarily worse.

Those three are the whole architecture. Everything below is how to build them.

---

## 2. What kind of problem this actually is

Naming the formalism matters, because it tells you which algorithms are admissible.

An RTS match is a **two-player zero-sum partially observable stochastic game**. Concretely:

- **Zero-sum**: one winner. Your gain is the opponent's loss. This is what licenses the game-theory
  machinery in §8 — Nash equilibria in zero-sum games are computable, interchangeable, and
  unexploitable, none of which holds in general-sum games.
- **Partially observable**: fog. You act on a *belief* over states, not a state. This makes it a
  POMDP, and it is why §5 is a probability distribution rather than a list of last-known positions.
- **Stochastic**: damage rolls, pathing, timing.
- **Simultaneous-move, real-time**: both sides act continuously. There are no turns to alternate.
- **Enormous**: ~1000 actors on continuous positions with per-tick orders.

Solving this exactly is hopeless, and every practical strong RTS agent makes the same three
concessions. They are not shortcuts; they are the design:

- **Abstract the state** until it is small enough to search (§4).
- **Abstract the actions** into a handful of macro-choices, so branching is ~10 and not ~10^100 (§7).
- **Decompose by time scale** — plan in minutes, manoeuvre in seconds, micro in ticks — and let each
  layer treat the layer below as a primitive it can call (§3).

The concession we *refuse* to make is on the transition function. Most RTS agents guess at their
forward model because they are bolted onto a game they cannot inspect. We are inside the engine.
Every damage table, build time, prerequisite, terrain cost and resource yield is available exactly,
at zero cost, from mod data. That is the single largest advantage this project has, and §4 exists
to exploit it.

---

## 3. Architecture: five layers, each with one job

```
┌──────────────────────────────────────────────────────────────────┐
│ L4  EXECUTION          plan → orders, with commitment            │  ticks
│     Existing controllers. They are fine. They were never the bug.│
├──────────────────────────────────────────────────────────────────┤
│ L3  SEARCH             what should I do for the next 2 minutes?  │  ~15 s
│     Puppet MCTS over macro-choices + Nash over strategy portfolio│
├──────────────────────────────────────────────────────────────────┤
│ L2  EVALUATION         which of these two futures is better?     │  called
│     Logistic win-probability model, fit on self-play outcomes    │  by L3
├──────────────────────────────────────────────────────────────────┤
│ L1  FORWARD MODEL      if I do X, what does the world look like? │  called
│     Abstract simulator, calibrated from mod data                 │  by L3
├──────────────────────────────────────────────────────────────────┤
│ L0  WORLD MODEL        what is true right now?                   │  ~1 s
│     Static knowledge · region graph · belief state               │
└──────────────────────────────────────────────────────────────────┘
```

The rule that keeps this honest: **each layer is a pure function of the layer below plus its own
state, and no layer reaches past its neighbour.** The current design's 2,700-line command module is
what happens without that rule. Layers 0–2 are pure and therefore directly unit-testable without a
running game — which is also how they get measured rather than asserted.

---

## 4. Layer 0 — encoding the whole game as data

This is the "full representation" the rebuild is named for. It splits into what is permanently
true, what is true of this map, and what is true right now.

### 4.1 Static knowledge — compiled once at load, from the mod rules

Everything here is *exact*, extracted from `ActorInfo`, `WeaponInfo`, `TechTree` and the terrain
definitions. Nothing is estimated and nothing is hand-tuned.

| Table | Extracted from | Answers |
|---|---|---|
| Unit table — cost, HP, armour class, speed, build time, sight, domain | `ActorInfo`, `Valued`, `Health`, `Armor`, `Mobile` | "What is this thing?" |
| Damage matrix `D[weapon][armour]` | `WeaponInfo.Warheads[].Versus` | "What does it do to that?" |
| Counter matrix `C[attacker][defender]` → DPS, cost-efficiency | derived from the above | "What beats what, per credit?" |
| Tech DAG | `TechTree` prerequisites | "What must exist before this can?" |
| Production graph — queue, throughput, parallelism | `ProductionQueue` | "How fast can I get it?" |
| Terrain cost per locomotor | `TerrainInfo`, `Locomotor` | "Can this go there, and how fast?" |
| Resource yields | `ResourceLayer`, `ResourceType` | "What is that ore worth?" |

`UnitCombatProfile` already builds the counter matrix correctly, including `InvalidTargets` and
ground/air domain gating. It is one of the pieces worth keeping intact.

The load-bearing property is *closure*: once these tables exist, the forward model in §6 needs no
access to the live game at all. It becomes a pure function over numbers, which is what makes it
fast enough to search and cheap enough to test.

### 4.2 The region graph — computing terrain understanding once per map

Tile-level reasoning is too fine to search over. The standard decomposition, and the right one:

1. **Distance transform** — for every passable cell, distance to the nearest impassable cell.
   This is "how open is it here".
2. **Watershed segmentation** on that field → **regions**: open areas separated by narrow ones.
3. **Chokepoints** — local minima of width along region boundaries. Each gets a *capacity*: how many
   units can physically pass abreast.
4. **Region graph** `G = (V, E)`, `|V| ≈ 20–40` on a 128×128 map, edges weighted by choke capacity.

This graph is the commander's mental map, and it unlocks two models that tile grids cannot express:

- **Max-flow / min-cut.** With choke capacity as edge capacity, the *min-cut* between my base and
  the enemy's is the cheapest set of chokes that seals me off — the defensive line, computed rather
  than guessed. The *max-flow* is the maximum rate at which the enemy can physically deliver units
  to me, which bounds how fast any attack can arrive and therefore how much defence is enough.
  Edmonds–Karp on 40 nodes is microseconds.
- **Articulation points and k-connectivity.** A region graph with one path between bases is a map
  where feints are worthless and siege is everything. One with four is a map where the main force
  should never be the only force. *The doctrine follows from the topology*, instead of being a
  constant someone chose.

This also answers the terrain question from the handbook directly: rivers and water are not special
cases to detect, they are edges the ground locomotor cannot traverse. Naval viability is
`∃ path in the water-locomotor graph between my region and a region adjacent to theirs` — a
reachability query, not a heuristic.

### 4.3 Belief state — the honest answer to fog

Fog is a POMDP, so the enemy's state is a **distribution**, not a snapshot with a decay timer.

**Enemy units — particle filter per contact.** Each lost contact becomes a cloud of position
hypotheses:

- *Predict*: each tick, diffuse particles outward at the unit's known top speed, along
  terrain-legal paths only. A tank that vanished 30 seconds ago is somewhere in a reachable
  set, not a fading dot where it was.
- *Update on positive evidence*: a sighting collapses the cloud.
- **Update on negative evidence**: this is the piece almost every bot omits and it is worth more
  than the rest. *If I can currently see region R and the unit is not in it, every particle in R is
  eliminated.* Looking somewhere and finding nothing is real information. It is what makes a scout
  sweep valuable even when it sees nothing — and it is what turns the 360° dog sweep from a map
  reveal into an actual inference engine.

The result: "60% chance their armour is massing in the north-east" — a claim with a probability
attached, which the search in §7 can take an expectation over. Compare with the current
last-known-snapshot-with-decay, which can only say "I saw something there once".

**Enemy economy — Kalman filter.** Harvester count, refinery count and expansion count are observed
noisily and intermittently. A 1-D Kalman filter over enemy income rate gives a smoothed estimate
*and* a variance. The variance matters: it is the difference between "they are on two bases" and
"I have not looked in ninety seconds and they could be on four".

**Enemy strategy — Bayesian posterior.** Maintain `P(strategy | observations)` over a small class
{rush, expand, tech, turtle, air, naval}. Each observation (a barracks at 2:00, no refinery at
4:00, an airfield seen) has a likelihood under each strategy, taken from self-play statistics.
Bayes does the rest. This posterior is the input to §8, and it is the thing that makes adaptation
*fast* — a single airfield sighting can swing the posterior hard toward "air", which reprices the
entire counter-composition before the first aircraft arrives.

---

## 5. The state vector

The abstract state the search operates on. A few hundred numbers, sufficient for decisions,
cheap enough to copy thousands of times per second.

```
AbstractState
├── time                     tick
├── self
│   ├── economy              cash, income/s, harvesters, refineries, queue throughput
│   ├── tech                 bitmask over the tech DAG
│   ├── production           per-queue: capacity, in-progress, ETA
│   └── forces[region]       vector over unit classes → count, HP fraction
├── enemy                    same shape, but expected values under the belief state
│   └── strategyPosterior    P over strategy classes
└── map
    ├── control[region]      ∈ [-1, 1]   (mine ↔ theirs)
    ├── visibility[region]   age in ticks since last seen
    └── value[region]        ore remaining, expansion sites, structures
```

Two properties this shape is chosen for. It is **differentiable-ish** — every field is a scalar or
a small vector, so the evaluation function in §7 can be a linear model over it rather than a
decision tree over special cases. And it is **complete for decisions**: anything the commander needs
to decide *what to do*, as opposed to *how to do it*, is in here. The "how" belongs to L4.

---

## 6. Layer 1 — the forward model

The piece that does not exist today, and the one that makes everything else possible.

```csharp
AbstractState Step(AbstractState s, MacroAction a, Duration dt)
```

Deterministic, allocation-free, target **under 10 µs**. At that cost, a 2-minute lookahead with
thousands of rollouts fits comfortably in the budget between two 15-second reviews.

Four sub-models, each calibrated from §4.1 rather than tuned:

**Economy.** Income is a queueing system, capped by refinery throughput — `HarvesterEconomics`
already models this correctly and carries over. Cash integrates forward; production draws it down.

**Production.** Given a build request, the tech DAG and queue capacities give a completion time
exactly. Note the trap this session already measured the hard way: cost is drawn down *over* build
time, so collapsing build time to zero makes the whole cost fall due immediately. The forward model
must reproduce that coupling or it will confidently plan build orders that stall.

**Movement.** Travel time between regions from the region graph, with the slowest unit in the force
setting the pace. Nothing finer is needed at this abstraction.

**Combat.** Reduce both mixed armies through the counter matrix to effective damage rates:

```
D_A = Σ_i n_i · dps(i → enemy composition)      H_A = Σ_i n_i · hp_i
D_B = Σ_j n_j · dps(j → my composition)         H_B = Σ_j n_j · hp_j
```

Then integrate Lanchester's square law to get survivors on both sides. `LanchesterModel` carries
over, but note the correction: the square law assumes homogeneous forces, so the reduction through
the counter matrix *must* happen first. Applying it to raw unit counts — comparing eight tanks to
eight riflemen as though the numbers were commensurable — is where naive implementations go wrong.
Static defences enter as immobile units with their own armour class, which is what makes the model
able to price attacking into a defended base rather than treating defences as a scary constant.

**Calibration, not faith.** Every prediction the forward model makes is logged against what
actually happened. `EngagementOutcomeLog` and the Brier scoring already in the codebase are exactly
the right instruments. A forward model whose Brier score is not improving is a forward model that
is lying to the search, and it must be visible when that happens.

---

## 7. Layer 2 & 3 — evaluation, then search

### 7.1 Evaluation: learn it, do not hand-tune it

Search needs to rank leaf states. Hand-weighting that ranking is guesswork, and this project has
already paid for guesswork twice.

Instead: **logistic regression on self-play outcomes.**

1. Play self-play games. At every review, log the state vector.
2. Label every logged state with the eventual result of that game (won / lost).
3. Fit `P(win) = σ(w · features)`.

Features: army value ratio, economy ratio, tech lead, region control sum, base integrity, income
derivative, map control momentum.

This is worth being concrete about, because it is where "unbeatable without an LLM" actually comes
from. The model is a **few dozen weights** — deterministic, microseconds to evaluate, no neural
network, no inference server, no nondeterminism. It is fully explainable: you can print the weights
and read what the commander believes wins games. It **improves automatically** as more self-play
accumulates. And it emits a *calibrated probability*, so the same Brier scoring already in the repo
grades it directly. The self-play harness and telemetry needed to fit it already exist.

`StrategyPortfolio`'s UCB1 is superseded here — see §8 for why it must be.

### 7.2 Search: Puppet MCTS over macro-choices

Searching raw orders is impossible; the branching factor is astronomical. **Puppet search** solves
this by searching over *choices a script exposes*, not over primitive actions:

```
MacroAction = (Verb, Region)
Verb   ∈ { Expand, Tech, Produce, Attack, Feint, Defend, Harass, Consolidate }
Region ∈ regions relevant to that verb
⇒ branching ≈ 10–15
```

Each macro-action is carried out by the existing L4 controllers. They already work; they were never
the bug.

MCTS with UCT over these, `dt = 15 s`, depth 8 → a **2-minute horizon**. Selection by UCT,
expansion by the forward model, evaluation at the leaf by §7.1 — no random rollout needed, which is
what keeps it fast. Both sides are modelled: the opponent's move is sampled from the strategy
posterior in §4.3, so the search plans against a distribution of opponents rather than a
conveniently passive one.

### 7.3 Commitment — the fix for the actual measured defect

This is the part that repairs §1, and it must be explicit or the rebuild inherits the same bug in a
more sophisticated wrapper.

When the search selects a plan, it emits a **contract**:

```csharp
sealed record Plan(
    MacroAction Objective,
    int CommittedUntilTick,      // not reconsidered before this
    AbortCondition[] Aborts,     // the ONLY things that may cancel it
    float ExpectedValue);        // what the search believed
```

Between reviews the plan is not re-derived. It is checked against its abort conditions, and those
conditions are declared **at launch**, when the commander is thinking clearly, rather than
mid-assault when the ratio is transiently ugly:

- force strength below 40% of launch strength
- home base integrity below a threshold
- the objective is confirmed gone
- a materially better opportunity, requiring a margin (not a tie-break)

A falling army ratio during an assault is **not** an abort condition. It is the expected cost of
the plan, it was priced in when the plan was selected, and treating it as a reason to stop is the
single defect that produced 38 draws.

Every completed plan is scored: expected value versus realised value. That residual is the training
signal for both the forward model and the evaluator.

---

## 8. Layer 3b — the mathematics of not being exploitable

"Unbeatable" needs a precise definition before it can be engineered toward, and the honest one is
narrower than the word suggests.

Against a *fixed* opponent, the best possible play is a **best response** — maximally exploit their
pattern. Against an *adapting* opponent, a best response is itself exploitable, because whatever
you do consistently can be countered.

The achievable target is **unexploitability**: in a zero-sum game, a Nash equilibrium strategy
guarantees at least the game value *against every possible opponent, including one that knows your
strategy exactly*. That is the strongest guarantee available, and it is computable here.

**Where UCB1 fails.** `StrategyPortfolio` uses UCB1 over strategy arms. UCB1 converges to the single
best arm against a **stationary** environment. An opponent who notices that you always siege will
build against siege, at which point UCB1 — having converged — keeps playing the now-countered arm
until enough losses accumulate to shift the average. Convergence to a pure strategy is precisely
what makes it exploitable.

**The fix — regret matching over the strategy portfolio.** Build the payoff matrix
`M[i][j]` = value of my strategy `i` against enemy strategy `j`, estimated from the forward model
and refined by logged outcomes. Then run **regret matching**: track cumulative regret for each
strategy, and play proportional to positive regret. In a zero-sum game the average strategy
converges to Nash. It is roughly twenty lines of code, needs no LP solver, and yields a *mixed*
strategy — the commander is deliberately unpredictable, and unpredictable in the specific
proportions that cannot be punished.

**Exploit only on confidence.** Nash is a floor, not a ceiling — it guarantees you cannot be beaten
badly, not that you beat weak opponents fast. So: play the Nash mixture by default; deviate to a
best response *only* when the §4.3 posterior is confident, and fall back the moment it is not. This
is the structure that beats scripted bots decisively while remaining safe against a good human.

---

## 9. Model inventory

Every model, what it answers, and where it lives.

| Model | Question it answers | Layer | Status |
|---|---|---|---|
| Counter matrix from damage tables | What beats what, per credit? | L0 | **exists** (`UnitCombatProfile`) |
| Tech DAG | What must exist before this can? | L0 | engine (`TechTree`) |
| Distance transform + watershed | Where are the regions and chokes? | L0 | **new** |
| Max-flow / min-cut | Where is the real defensive line? | L0 | **new** |
| Particle filter (+ negative evidence) | Where are the units I cannot see? | L0 | **new** |
| Kalman filter | What is their income, and how sure am I? | L0 | **new** |
| Bayesian posterior | Which strategy are they playing? | L0 | **new** |
| Queueing / Little's law | What is a harvester worth right now? | L1 | **exists** (`HarvesterEconomics`) |
| Heterogeneous Lanchester | Who wins this fight, and with what left? | L1 | **exists**, needs counter-matrix reduction |
| Build-order branch & bound | Fastest route to a target composition? | L1 | **new** |
| Logistic regression on self-play | Which future is better? | L2 | **new** |
| Puppet MCTS / UCT | What should I do for two minutes? | L3 | **new** |
| Regret matching → Nash | How do I stay unexploitable? | L3b | replaces UCB1 |
| Brier scoring | Are my predictions actually calibrated? | all | **exists** |

Roughly a third of the machinery already exists and is correct in isolation. The reason it
accomplishes so little is that nothing *searches* over it — the models are consulted as advisors by
a rule cascade that was always free to ignore them. The rebuild is largely about putting a search
where the cascade is.

---

## 10. Build order, with measurement gates

Each phase must move a benchmark number or it does not land. This follows `PLAN.md` rule 5:
progress is reported by measurement, never by assertion.

| Phase | Delivers | Gate |
|---|---|---|
| 1 | Region graph, chokes, min-cut | Regions/chokes correct on all 4 mid-size maps; unit-tested on synthetic terrain |
| 2 | `AbstractState` + forward model | Predicted vs actual state at +30 s within 15% on logged games |
| 3 | Self-play feature logging + logistic evaluator | Brier score beats a fixed 0.5 baseline by a wide margin |
| 4 | Puppet MCTS + **plan commitment** | **Mirror decisiveness > 75%** — the direct §1 fix |
| 5 | Particle filter + Bayesian posterior | Strategy identified before the first attack in >80% of games |
| 6 | Regret matching replaces UCB1 | No strategy exceeds 40% frequency; no losing streak vs any single bot |
| 7 | Build-order search | Time-to-first-attack cut measurably vs phase 4 |

**Phase 4 is the one that matters.** Phases 1–3 build the instruments; phase 4 is where the defect
in §1 is actually repaired, and it is the first phase that should move the win column. If mirror
decisiveness does not rise sharply there, the diagnosis in §1 is wrong and the plan should be
reconsidered rather than continued.

### What carries over

Keep: `UnitCombatProfile`, `LanchesterModel` (with the reduction fix), `HarvesterEconomics`,
`InfluenceMap`, `ScoutSelection`, `RadialScoutPattern`, `SiegeTargeting`, the whole L4 execution
layer, telemetry, and the self-play harness.

Replace: `RunCommand()`'s rule cascade, `PostureSelection`'s instantaneous ratio test,
`StrategyPortfolio`'s UCB1, and the last-known-position intel model.

The execution layer is not the problem and should not be rewritten. A 2.43 exchange ratio is
evidence that when this commander is told what to do, it does it well.

---

## 11. What "unbeatable" can honestly mean

It cannot mean *never loses*. No such agent exists for a game of this size, and a design that
claims it is a design that has stopped measuring.

What is achievable, and what this architecture targets:

1. **Unexploitable** — a mixed strategy with a Nash guarantee, so no opponent can find a repeatable
   pattern that beats it. (§8)
2. **Decisive** — converts a winning position into a win, instead of a draw. This is the current
   failure and phase 4 addresses it directly. (§7.3)
3. **Adaptive within one game** — a posterior that moves on evidence, so a counter-composition is
   priced before the counter arrives. (§4.3)
4. **Self-improving across games** — an evaluator and a payoff matrix that are fit from accumulated
   self-play, so the commander is stronger next month without anyone editing a threshold. (§7.1)

Those four are measurable, and each has a gate in §10. "Unbeatable" is not a target; it is what it
looks like from the outside when all four hold at once.
