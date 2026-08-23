# The Commander's Handbook

**Audience:** the deterministic C# tactical commander (`CoalitionCommandCenterBotModule` +
`StrategicBrainBotModule` + `TacticalControllers`), one level below the optional Qwen strategic layer.

**Mandate:** the commander must be able to beat a skilled human *on its own*, with the LLM absent.
The LLM is an advisor that may improve a plan; it is never load-bearing. If the C# layer cannot win
without it, the mod does not work.

**Scope:** OpenRA's Red Alert mod, mid-size maps (122×122 to 130×130): `chernobyl`, `snow-town`,
`shattered-mountain`, `code-19`.

---

## 1. Standing orders

1. **Win the match.** Not the exchange ratio. A 3:1 trade that ends in a time-limit draw is a loss
   dressed up as a statistic. Every metric in this handbook is subordinate to win rate.
2. **Do not turtle.** The OpenRA community's own guidance is blunt about this: static defence is
   useful only in narrow circumstances, and passive play loses to economy. The current
   implementation draws by being passive; that is a failure state, not a safe default.
3. **Never cheat the fog.** Exact positions of hidden enemy units and buildings are off limits, now
   and permanently. Everything else the engine knows is fair game (§3).
4. **Prefer information to guessing.** The commander has cheap access to ground truth about its own
   side and about the rules of the game. Compute; do not hardcode.
5. **Act on a plan with an objective.** Every committed force answers: what am I destroying, what
   opens if I succeed, what do I do if it fails.

---

## 2. What the commander may and may not know

### Forbidden (fog integrity)
- Position, health, or existence of enemy actors not currently visible to the coalition.
- The assigned enemy spawn point, and occupancy of unexplored cells.
- Anything reachable only by reading another player's private state.

### Permitted, and currently under-used
Everything below is public rules data or the coalition's own state. This is the commander's real
advantage over a human, who must remember it or look it up:

| Source | What it gives | Trait / API |
|---|---|---|
| `ValuedInfo.Cost` | exact credit cost of any actor | per-actor |
| `HealthInfo.HP` | exact hit points | per-actor |
| `ArmorInfo.Type` | armour class: `None`, `Light`, `Heavy`, `Wood`, `Concrete`, `Ship`, `Defense` | per-actor |
| `Armament` → `WeaponInfo` | weapon, range, `ReloadDelay`, `Burst` | per-actor |
| `DamageWarhead.Versus` | **damage multiplier per armour class** | per-warhead |
| `MobileInfo.Speed`, `AircraftInfo` | movement speed, locomotor, terrain costs | per-actor |
| `BuildableInfo.Prerequisites` | exact tech tree | per-actor |
| `ProductionQueue` | what is buildable *right now*, queue contents, progress | own player |
| `PlayerResources` | cash, ore, storage capacity | own player |
| `PowerManager` | power supplied/drained, low-power state | own player |
| `RevealsShroudInfo.Range` | exact vision radius of every unit | per-actor |
| `DetectCloakedInfo` / `CloakInfo` | who can see stealth, and who is stealthed | per-actor |
| `Shroud.IsExplored` / `IsVisible` | the coalition's own knowledge, honestly | own player |
| `Map.ActorDefinitions` (`mpspawn`) | public starting locations | map data |
| `SupportPowerManager` | charge state of every power | own player |

**Rule:** if a number can be derived from the ruleset, it must not be a hand-maintained string in
`ai.yaml`. Hardcoded lists like `AntiArmorUnits: 4tnk, ttnk, 3tnk, v2rl` are a bug waiting to
happen — they go stale when the mod changes and they encode one person's guess where the engine has
the answer.

---

## 3. The combat model — derive it, don't guess it

RA damage is `Damage × Versus[targetArmour] / 100`, modified by range and reload. From the tables
above the commander can compute, for any attacker/defender pair:

- **Time to kill** = `targetHP / (damage × versus × burst / reloadDelay)`
- **Cost efficiency** = `targetCost / attackerCost` weighted by time to kill
- **Counter score** = the cost-efficiency of A against B, normalised

That yields a *real* counter matrix instead of four hand-written lists. Ground truth from the
shipped mod:

| Unit | Cost | HP | Armour |
|---|---:|---:|---|
| Rifle infantry `e1` | 100 | 5 000 | None |
| Rocket infantry `e3` | 300 | 4 500 | None |
| Light tank `1tnk` | 700 | 23 000 | Heavy |
| Medium tank `2tnk` | 850 | 46 000 | Heavy |
| Heavy tank `3tnk` | 1 150 | 60 000 | Heavy |
| **Mammoth `4tnk`** | **2 000** | **90 000** | Heavy |
| Artillery `arty` | 850 | 10 000 | Light |
| V2 launcher `v2rl` | 900 | 20 000 | Light |
| Ranger `jeep` | 500 | 15 000 | Light |
| APC | 850 | 35 000 | Heavy |
| Harvester `harv` | 1 100 | 60 000 | Heavy |
| MiG | 2 000 | 8 000 | Light (air) |
| Longbow `heli` | 2 000 | 12 000 | Light (air) |

Two consequences the commander must internalise:

- **Infantry are `None` armour, tanks are `Heavy`, artillery/jeeps/V2s are `Light`.** A weapon good
  against one is usually poor against another; that is the whole counter system.
- **Artillery and V2s are `Light` with 10 000–20 000 HP.** They delete infantry and buildings and
  evaporate to anything that reaches them. They must never lead an advance.

---

## 4. Economy — the thing that actually decides matches

Community guidance and the mod data agree on the shape:

- **Two refineries early is the competitive floor.** One refinery is a loss condition against any
  competent opponent.
- **Place refineries next to ore**, not next to the construction yard. Harvester round-trip time is
  the real income rate.
- **Expand to a second ore patch** as soon as it can be defended. Placing base assets on an ore
  patch also denies it to the enemy.
- **Floating cash is wasted production.** If credits are accumulating, either the queues are idle or
  the build plan is too conservative. Both are errors.
- **The trade-off is permanent:** at every moment, credits go to economy, army, or tech. The
  commander must hold an explicit position on which, and change it deliberately.

Diagnostic: `avg cash` rising while `production idle` is non-zero means the commander is losing to
its own build plan, regardless of what the battlefield looks like.

---

## 5. Match phases

Approximate wall-clock on a mid-size map. The commander should know which phase it is in and what
losing that phase looks like.

| Phase | Time | Objective | Failure looks like |
|---|---|---|---|
| **Opening** | 0–1 min | Deploy, power, barracks, refinery, war factory, second refinery | Single refinery; no scout out |
| **Scouting / expansion** | 1–3 min | Locate enemy base, find ore patches, first harassment | Map still dark; enemy position unknown |
| **Production** | 3–7 min | Army core, service depot, second expansion | Cash floating; one production building |
| **Standard** | 7–13 min | Tech, combined arms, contest the map | No air or naval arm raised |
| **Attacking** | 13–18 min | Kill the enemy economy, then the enemy base | Trading evenly with no ground taken |
| **Endgame** | 18+ min | Finish, or lose to resource depletion | Time-limit draw |

**The current implementation reliably reaches phase 5 and stops.** It trades well and never converts.
Everything in §7 exists to fix that.

---

## 6. Scouting — the precondition for everything

The commander cannot name an objective it has not found. Scouting is not optional overhead; it is
what unlocks the entire offensive doctrine.

- **Scout continuously until the enemy base is located**, not for a fixed number of attempts. A
  scout that dies has bought information about where the defences are — that is a successful probe,
  not a wasted unit.
- **A target is finished when the cell is explored, not when a scout was sent toward it.**
- **Use cheap units.** Rifle infantry (100) and dogs (200) are the correct scouts. Dogs are fast and
  also detect spies.
- **Spread, don't queue.** Send probes to different bearings simultaneously. Sequential probing down
  one lane tells you about one lane.
- **Starting locations are public map data.** Probing them is legitimate and is the fastest way to
  find a base. Reading who occupies them is not.
- **Re-scout after losing contact.** An enemy army that vanished is the single most dangerous state
  in the game (§9.1).

---

## 7. Offensive doctrine — how to actually kill a base

This is where the current implementation fails, so it is the longest section.

### 7.1 Attack the economy first
Harvesters cost 1 100 and are `Heavy` armour with 60 000 HP. Killing them is slow but strategically
decisive: an opponent with no income cannot replace losses. **Refineries and harvesters outrank
military targets** until the enemy economy is materially damaged.

### 7.2 Take ground, do not trade
An attack that kills units and withdraws is a raid, and raids do not win. A real assault must:
1. **Have a named objective** — a specific structure or ore field, not "the enemy".
2. **Arrive concentrated.** Local superiority of ~1.5:1 or better at the point of contact.
3. **Reduce, not skirmish.** Artillery and V2s out-range base defences; use them to remove the
   defence, then move armour in. Sending armour into a pillbox line first is how the assault dies.
4. **Hold what it takes.** Withdrawing from ground taken means the assault bought nothing.

### 7.3 Composition
- **Armour is the core.** Tank spam is genuinely strong in this game and is the baseline.
- **Rocket infantry screen the armour** against enemy armour; rifle infantry screen against enemy
  infantry. Both are cheap enough to be expendable.
- **Artillery/V2 travel behind the screen**, never in front. They out-range static defence — that is
  their entire purpose.
- **Anti-air travels with the column.** Air can be produced in under three minutes; a column with no
  AA is free kills.
- **Aircraft strike, then leave.** Yaks and MiGs are hit-and-run weapons; parking them over a SAM is
  throwing away 1 350–2 000 credits.

### 7.4 The mammoth problem, both directions
Mammoths (2 000, 90 000 HP, Heavy) beat anything one-on-one and are beaten by *cost efficiency*:
three heavy tanks (3 450) beat two mammoths (4 000). Against a mammoth push: mass anti-tank, strip
the infantry screen with artillery first, and let base defence contribute. When fielding mammoths,
remember they are slow and expensive — losing one is losing two heavy tanks.

### 7.5 Sequencing
A serious operation is not one order. In order:
**recon → deception → shaping fires → breach → exploitation → consolidation.**
Components with different speeds must be *launched* at different times so they *arrive* together.
An air strike that lands four minutes before the ground column is a wasted strike.

---

## 8. Defensive doctrine

- **Respond proportionally.** A two-unit raid does not merit the field army. Send the nearest
  adequate force; the main effort continues.
- **Defend forward where possible.** Meeting a raid at the ore field beats meeting it at the refinery.
- **Spread structures.** Packed bases die to one nuke. Accept slightly harder defence for survivability.
- **Wall high-value structures** with concrete: construction yard, tech centre, superweapons.
- **Do not cluster AA.** Spread SAMs/AA guns around the perimeter; a cluster covers one approach.
- **Static defence supports, never substitutes.** Defences buy time for the mobile force to arrive.
- **A repelled attack is an opportunity.** The attacker has just spent its army away from home; that
  is the counterattack window (§9.4).

---

## 9. Threat library — what a good human will actually do

Each entry: the trick, the tell, the answer.

### 9.1 The hidden mass push
*Trick:* bank credits, build a mammoth/heavy-tank fleet out of sight, hit once with overwhelming force.
*Tell:* enemy army disappears from view; contact is lost; enemy income continues but no attacks come.
Silence is the signal.
*Answer:* loss of contact must **raise** alarm, not lower it. Re-scout immediately and broadly.
Assume the worst case consistent with observed income, not the last seen army. Keep the reserve
uncommitted. Prefer anti-tank production and defensive depth until contact is re-established.

### 9.2 The 360° infantry scout spam
*Trick:* a dozen rifle infantry (100 each) sent in all directions to reveal the whole map cheaply.
*Tell:* multiple single cheap units on divergent bearings, not converging on anything.
*Answer:* do not chase them with valuable units — that is the trap. Kill them with static defence
and units already in place. Recognise that the enemy now knows your layout, and expect the attack to
land where you are thinnest.

### 9.3 Early air harassment
*Trick:* Yaks by ~2:20 against an undefended base, killing power plants or harvesters.
*Tell:* enemy radar/airfield early; air units seen scouting.
*Answer:* AA before it is needed only if air is *observed* or the enemy has an airfield; otherwise
spread AA reactively. Anti-air units with the harvesters, not just at the base.

### 9.4 Overcommit and counterattack
*Trick:* the human attacks, loses the fight, and expects you to sit still.
*Answer:* a repelled attack is the counterattack window. Check: is the attacker depleted, is its
origin thin, is its production busy replacing losses. If yes, go immediately — this is the highest
value moment in the match. If the enemy still has an intact defence at home, do not.

### 9.5 Harvester sniping
*Trick:* fast units (jeeps, dogs, aircraft) kill harvesters and leave.
*Answer:* escort harvesters, and treat repeated harvester loss as an economic emergency, not a
nuisance. Each harvester is 1 100 credits plus lost income.

### 9.6 Engineer / spy infiltration
*Trick:* capture a structure or steal tech with a cheap unit while attention is elsewhere.
*Answer:* dogs detect spies and are cheap. Keep one near the base. Treat an unexplained lone enemy
unit heading for the base as an infiltration attempt.

### 9.7 Turtle-and-tech
*Trick:* wall up, tech to superweapons, win late.
*Tell:* heavy static defence, few mobile units, tech buildings appearing.
*Answer:* do not grind into the defence. Take the map, take the ore, deny expansions, and force the
game before the superweapon lands. Turtling loses to economy — but only if you actually take the
economy.

---

## 10. Deception and stealth

- **A feint must be believable and cheap.** Commit enough that the enemy must answer, little enough
  that losing it does not matter.
- **A feint has an intended reaction; measure it.** If enemy forces did not move, the feint failed
  and the doctrine should stop repeating it.
- **Launch the real attack only after the reaction is observed**, and somewhere else.
- **A fake retreat is a success when the enemy follows.** Withdrawal into prepared ground is the plan,
  not a defeat.
- **Stealth insertions** (Tanya/engineers/spies by transport) go around threat, not through it. Plan
  the extraction route before launching; a scarce asset abandoned in enemy territory is a loss that
  has not been counted yet.

---

## 11. Combined arms and coordination

One target, one plan, protected:

- All arms converge on the **same objective**, at the **same time**.
- The ground column carries its own AA and its own artillery screen.
- Air arrives shortly before the ground force, strikes the defence, and leaves.
- Naval, where water permits, shells the coast in the same window.
- A reserve stays **uncommitted** during the main attack, ready to exploit a breach or answer a
  counter-raid.
- Multiple allied players contribute to one operation. Each issues its own orders — OpenRA forbids
  ordering another player's units — but the objective, the timing, and the main effort are shared.

---

## 12. Adaptation

Re-plan when, and only when, something material changed:
- Enemy base discovered, or contact with the enemy army lost.
- Enemy composition shifts (armour → air, ground → naval).
- An allied production building is lost.
- A route or bridge is destroyed.
- An attack fails, or a superweapon becomes ready.

Do not re-plan because a review interval elapsed. Constant re-planning is indistinguishable from
having no plan.

---

## 13. Anti-patterns — errors this implementation has actually made

Recorded because they were measured, not imagined:

1. **Scouting that gives up.** A lifetime budget of four scouts meant the enemy base was never
   located, so no offensive objective could ever be named. The coalition produced 49 counterattacks,
   19 interceptions and **zero offensive missions** in 30 000 ticks.
2. **Retiring a scout target on dispatch instead of on exploration.** A scout dying en route
   permanently excluded the cell it never reached.
3. **Passive play scored as success.** High exchange ratio with zero wins was reported as strength.
   Exchange ratio is not a win condition.
4. **Hardcoded counter lists** in `ai.yaml` where the engine has the real `Versus` table.
5. **Attacking without concentration.** Waves launched at global parity, achieving local superiority
   in 0 of 9 engagements.
6. **Sanitiser overriding a correct decision.** A deliberate "do not attack" was rewritten into an
   attack on the enemy's main force.

---

## 14. How success is judged

In order of authority:

1. **Win rate** against the scripted bots on the four mid-size maps, Fair Fog, 0% economic bonus.
   The standard Normal bot is the bar to clear; today it wins 4 of 9 where the coalition wins 0 of 12.
2. **Win rate against a human playtest.**
3. Exchange ratio, economic damage, mission success — diagnostics that explain the above, and never
   a substitute for it.

A change that improves exchange ratio and does not improve win rate has not improved the AI.

---

## 15. The decision model — the mathematics

The commander must not be a chain of rules ("attack when army ≥ 24"). Rules of that shape are
brittle, readable by an opponent after two matches, and cannot express *where* or *why*. What
follows is the model it should reason with instead. Each entry is a standard result from the RTS-AI
literature, chosen because it answers a question the commander actually has.

### 15.1 Influence maps — "where"

A grid over the map. Each unit and structure deposits influence at its cell, spread outward with
distance decay; own forces positive, observed enemy negative. Summing gives derived layers that
answer the spatial questions directly:

| Layer | Definition | Question it answers |
|---|---|---|
| **Influence** | `Σ own − Σ enemy` | Who controls this ground |
| **Front line** | zero crossings of influence | Where the fighting is |
| **Tension** | `Σ own + Σ enemy` | Where both sides are invested |
| **Vulnerability** | `tension − abs(influence)` | Where the enemy is *thin relative to what it is worth* |
| **Threat** | enemy influence weighted by anti-X capability | Where this force must not go |

The assault objective is the cell maximising *enemy value × vulnerability*. The feint objective is
the cell that maximises **drawn influence per credit risked** — somewhere the enemy must answer but
we can afford to lose. Defensive positioning is the front line, not the base perimeter.

This replaces "attack the enemy base region" with a continuous, terrain-aware answer that updates as
the map is uncovered — which is exactly the adaptive behaviour the fixed-region approach cannot express.

### 15.2 Lanchester's square law — "will this fight be won"

For ranged units, fighting power scales with the **square** of numbers: `α·N² − β·M² = constant`.
The practical consequences the commander must act on:

- **Concentration beats increments.** Doubling a force quadruples its power. Two waves of 10 lose to
  one wave of 20 — this is the mathematical statement of the handbook's "arrive concentrated".
- **Predicted survivors** = `sqrt(N² − (β/α)·M²)`, which is a far better commit signal than a
  strength ratio, because it says how much is *left* to exploit with.
- Splitting a force is only correct when the halves fight *separate* battles, never the same one.

### 15.3 Multi-armed bandits — "learn what works"

Strategy selection as a bandit problem, using UCB1:

```
score(arm) = mean_reward(arm) + c·sqrt(2·ln(total_plays) / plays(arm))
```

Each arm is an opening or a posture (expand-first, early pressure, tech, harass, siege). The reward
is measured progress — economy delta, ground taken, enemy value destroyed per credit committed. The
second term forces exploration of under-tried options and decays as evidence accumulates.

This is the **self-improving** part: within a match it shifts weight toward what is working against
*this* opponent on *this* map, and across matches the priors persist. It is also the correct answer
to the brief's "not static": the commander does not follow one doctrine, it runs a portfolio and
lets the results re-weight it.

### 15.4 Harvester economics — "is ore being converted fast enough"

Income is a queueing problem, and Little's Law applies directly:

```
income_rate = harvesters × load_size / round_trip_time
round_trip_time = 2 × distance/speed + harvest_time + unload_time
```

Consequences the commander must compute rather than guess:
- **A refinery closer to ore is worth more than another harvester**, whenever travel dominates the
  round trip. That is why the community advice is to place refineries next to ore.
- **Adding a harvester pays off only until the refinery's unload queue saturates.** Past that the
  marginal harvester adds zero income and costs 1 100 credits.
- **The value of an expansion** is its ore volume divided by its round-trip time, discounted by the
  risk of holding it.

Ore is not a background activity. It is the rate that sets everything else, and the commander should
treat a drop in income as an emergency on the same level as losing a production building.

### 15.5 Terrain — read it, don't assume it

Region decomposition plus chokepoint detection (already present in `CoalitionMapAnalysis`) gives the
graph the influence map runs over. What matters for planning:

- **Chokepoints are force multipliers**, in both directions: hold one with few units, and never
  assault through one without reducing it first.
- **Water splits the map into components.** Naval matters only where a contiguous water body
  actually connects the two bases — otherwise a shipyard is wasted credits.
- **Articulation points** (regions whose removal disconnects the graph) are the highest-value ground
  on the map and the correct place for a blocking force or an expansion.

### 15.6 Bayesian opponent modelling — "what is he doing"

Maintain a posterior over enemy strategy given observations: `P(strategy | evidence)`. Evidence is
cheap and continuous — structures seen, unit types met, timing of first contact, whether probes were
punished. The posterior drives production and posture, and its *variance* drives reconnaissance: the
commander scouts where the model is least certain, which is the value-of-information rule the
handbook already asks for.

### 15.7 How these compose

```
terrain graph ──► influence maps ──► candidate objectives (attack / feint / defend / expand)
                        ▲                        │
   observations ────────┘                        ▼
        │                            Lanchester: can this be won, with what left over
        ▼                                        │
  Bayesian opponent model ──► production          ▼
        │                                UCB1 portfolio: which plan, given what has worked
        └────────────────────────────────────────┘
                     harvester economics gates all of it
```

The first 10–15 minutes are deliberately **balanced**: economy first, continuous cheap
reconnaissance, terrain learned as it is uncovered, no committed strategy. The portfolio commits only
when the influence map and the opponent model agree on an objective and Lanchester says it can be
taken with a force left over to hold it.
