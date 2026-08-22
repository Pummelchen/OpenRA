# OpenRA Coalition AI — C# ↔ LLM API Contract v1

The single interface the engine and the commander agree on. The LLM decides *what must
happen and why*; the tactical runtime decides *how*. Everything below is JSON over
`POST /decide` (state in, intent out) plus the function-calling tool surface.

**Schema discipline:** every message carries `"schema": "<name>.<version>"`. Unknown
fields are ignored; missing required fields are rejected by the validator and the
deterministic fallback takes over. The engine is authoritative — the LLM never receives
raw engine objects, only snapshots and tool results it cannot fabricate.

---

## 1. WorldSnapshot — engine → commander (`world.snapshot.v1`)

One snapshot per consultation round, identical for every allied bot (computed
deterministically from the shared world + allied shroud).

```json
{
  "schema": "world.snapshot.v1",
  "round": 412,
  "tick": 246000,
  "self": "Multi0",
  "map": {
    "id": "mymap",
    "width": 128,
    "height": 128,
    "regions": [
      {
        "id": "REGION_14",
        "label": "western-plateau",
        "bounds": { "x0": 20, "y0": 20, "x1": 55, "y1": 60 },
        "terrain": "land",
        "entrances": ["CHOKE_7", "BRIDGE_2"],
        "naval": false,
        "friendly_control": 0.72,
        "enemy_pressure": 0.4,
        "threat": { "aa": 0.1, "ground": 0.8, "artillery": 0.2 }
      }
    ]
  },
  "forces": [
    {
      "id": "ARMY_GROUP_EAST",
      "owner": "Multi0",
      "role": "armor",
      "composition": { "heavy_armor": 23, "medium_armor": 8, "infantry": 11, "aa": 5, "artillery": 4 },
      "strength": 0.89,
      "cohesion": 0.93,
      "readiness": 1.0,
      "region": "REGION_14",
      "nearest_enemy": "EAST_BASE",
      "opposing_power_ratio": 0.78,
      "commitment": "reserve"
    }
  ],
  "unique_assets": [
    { "id": "TANYA_781", "type": "tanya", "owner": "Multi0", "region": "REGION_14", "status": "available" },
    { "id": "LST_282", "type": "lst", "owner": "Multi0", "region": "REGION_14", "status": "idle" }
  ],
  "enemies": [
    {
      "id": "ENEMY_HTANK_382",
      "type": "3tnk",
      "class": "armor",
      "status": "LAST_KNOWN",
      "last_seen": { "x": 118, "y": 74, "tick": 245500, "region": "EAST_BASE" },
      "age_seconds": 37,
      "confidence": 0.31,
      "count": { "min": 9, "expected": 15, "max": 24 },
      "probable_region": "south-central"
    }
  ],
  "economy": {
    "coalition_cash": 24000,
    "production": [
      { "owner": "Multi0", "queue": "Vehicle", "current": "2tnk", "next": ["3tnk", "3tnk"] },
      { "owner": "Multi1", "queue": "Aircraft", "current": "mig", "next": [] }
    ],
    "idle_time_seconds": { "Multi0": 4.2, "Multi1": 12.8 }
  },
  "threats": {
    "ground_anti_armor":  { "REGION_14": 0.6, "EAST_BASE": 0.9 },
    "anti_air":           { "EAST_BASE": 0.9, "REGION_14": 0.1 },
    "artillery":          { "EAST_BASE": 0.5 },
    "naval":              { "SOUTH_COAST": 0.3 },
    "submarine":          {},
    "vision_exposure":    { "EAST_BASE": 0.8, "REGION_14": 0.2 },
    "detection":          {},
    "static_defense":     { "EAST_BASE": 0.85 },
    "reinforcement":      { "NORTH_ROAD": 0.6 }
  },
  "missions": [
    {
      "mission_id": "OP-142",
      "type": "BREAKTHROUGH",
      "status": "EXECUTING",
      "phase": "shaping",
      "readiness": 0.8,
      "abort_reasons": []
    }
  ],
  "posture": "PRESSURE",
  "opponent": {
    "armor_bias": 0.72,
    "air_bias": 0.13,
    "static_defense_bias": 0.81,
    "preferred_attack_lane": "north",
    "average_response_time": 8.4,
    "responds_strongly_to_harvester_raids": true,
    "usually_moves_entire_army_to_defend": true,
    "anti_air_density": "low",
    "expansion_behavior": "late"
  },
  "uncertainties": [
    { "question": "enemy_main_army_position", "value": 0.9 },
    { "question": "enemy_aa_coverage_east", "value": 0.6 }
  ]
}
```

### Field rules
- `forces` are **aggregates** — never raw actor lists (unique assets excepted: Tanya, Spy,
  transports, MCV). Cohesion/readiness are engine-computed.
- `enemies` use the honesty ladder `KNOWN | OBSERVED | LAST_KNOWN | INFERRED | SUSPECTED |
  UNKNOWN`, each with `age_seconds` + `confidence` that the engine decays over time.
- `threats` are per-region 0..1 fields, one per capability (see §4); the LLM never sees raw
  positions of hidden actors.
- `map.regions` are static (computed once per map); `friendly_control`/`enemy_pressure`
  update per round.

---

## 2. Mission — the central abstraction (`mission.v1`)

The commander creates, modifies, and cancels missions; the engine tracks them.

```json
{
  "mission_id": "OP-142",
  "type": "BREAKTHROUGH",
  "objective": "Destroy eastern production complex",
  "priority": 90,
  "target_region": "EAST_BASE",
  "desired_effects": ["destroy_vehicle_production", "force_enemy_army_east", "open_southern_expansion"],
  "forces": { "assault": "ARMY_GROUP_3", "artillery": "BATTERY_2", "air_support": "AIR_GROUP_5", "reserve": "QRF_1" },
  "staging_region": "RIDGE_7",
  "phases": ["recon", "staging", "air_shaping", "ground_breach", "exploitation", "withdraw_or_hold"],
  "timing": [
    { "t_seconds": 0,  "action": "northern_feint_enters_visual_range" },
    { "t_seconds": 12, "action": "bombers_hit_aa" },
    { "t_seconds": 16, "action": "main_army_crosses_choke" },
    { "t_seconds": 22, "action": "tanya_transport_enters_southern_rear" }
  ],
  "launch_conditions": ["assault_readiness >= 0.90", "enemy_strength_estimate <= threshold", "air_group_available"],
  "abort_conditions": ["friendly_combat_power < 0.55", "enemy_reinforcement_estimate > 1.8x", "staging_compromised"],
  "contingencies": ["shift_attack_north", "convert_assault_to_feint", "withdraw_behind_ridge"]
}
```

### Mission types (enum, engine-validated)
`breakthrough | frontal_assault | flank | pincer | exploitation | base_assault | siege |
harassment | economy_raid | harvester_interdiction | production_raid | chokepoint_seizure |
naval_blockade | coastal_bombardment | air_strike | mass_air_attack | support_power_strike |
feint | demonstration | fake_buildup | diversion | probing_attack | fake_retreat |
bait_and_ambush | transport_decoy | reconnaissance | air_reconnaissance | naval_scouting |
route_reconnaissance | special_ops_insertion | special_ops_extraction | rescue_escort |
local_defense | mobile_defense | emergency_reinforcement | counterattack | interception |
anti_air_umbrella | anti_naval_screen | retreat | evacuation | delaying_action |
reserve_deployment`

### Lifecycle
`DRAFT → READY → EXECUTING → SUCCEEDED | ABORTED | FAILED` (engine owns transitions;
commander may cancel or supersede). Missions are **stable**: the engine keeps executing
until objective achieved, assumptions invalidated, abort condition reached, or a superior
mission is issued.

---

## 3. Commander reply — commander → engine (`command.intent.v1`)

The LLM submits intent; the engine validates and executes.

```json
{
  "schema": "command.intent.v1",
  "round": 412,
  "posture": "BREAKTHROUGH",
  "missions": [ /* mission.v1, only NEW/CHANGED */ ],
  "cancel_missions": ["OP-130"],
  "production": [
    { "owner": "Multi0", "capabilities": { "anti_armor": 1.0, "anti_air": 0.65, "artillery": 0.8 } },
    { "owner": "Multi1", "capabilities": { "air": 1.0, "anti_air": 1.6 } }
  ],
  "reserve": 0.15,
  "local_postures": { "NORTH": "HOLD", "EAST": "BREAKTHROUGH", "SOUTH": "RAID" },
  "fallback": "Continue current missions; posture PRESSURE."
}
```

### Validation rules (engine-side, `CommandValidator`)
- Every referenced force/region/asset must exist and be available.
- `REJECTED_CONFLICT` if a force is committed elsewhere with equal/higher priority
  (`OrderArbiter` levels: survival > special_mission > active_combat > defense > reserve >
  recon > staging > idle).
- Coordinates/regions are clamped; missions with empty `forces` or missing `objective`
  are rejected.
- The reply must match the `round`; stale replies are discarded.

---

## 4. Threat fields (`threat_field.v1`)

Independent per-region 0..1 maps (engine-computed, LLM-consumed):

```
ground_anti_armor | ground_anti_infantry | artillery | anti_air | air_to_air |
naval | submarine | vision_exposure | detection | static_defense |
likely_reinforcement | support_power_risk
```

Route requests may weight them per force class:

```json
{
  "route": {
    "from_region": "RIDGE_7",
    "to_region": "EAST_REAR",
    "for": "special_ops_transport",
    "weights": { "anti_air": 8, "vision_exposure": 10, "detection": 12, "active_combat_zone": 20, "known_enemy_route": 5, "chokepoint_risk": 4 },
    "constraints": { "avoid_regions": ["EAST_BASE"], "max_length_seconds": 90 }
  }
}
```

The engine returns the best route + cost + alternatives (never the LLM pathfinding).

---

## 5. Tool interface (function calling)

The commander is offered tools; it must use them instead of estimating mechanics.

| Tool | Input | Output | Engine-side |
|---|---|---|---|
| `get_global_summary` | — | posture, force/economic summary | snapshot |
| `inspect_region` | region_id | region details, control, threats | map analyzer |
| `inspect_force` | force_id | composition, strength, readiness, commitment | force registry |
| `inspect_enemy_intelligence` | region? | enemy intel with confidence/age | intel tracker |
| `get_recent_events` | since_tick | event log (see §8) | telemetry |
| `get_opponent_model` | — | opponent model | opponent model |
| `get_uncertainties` | — | high-value open questions | intel tracker |
| `estimate_engagement` | forceA, forceB, terrain, window | success prob, losses, risks | combat evaluator |
| `compare_force_packages` | packages[] | ranked options | combat evaluator |
| `plan_routes` | route request | routes + costs | route evaluator |
| `score_targets` | region?, weights | ranked targets | target evaluator |
| `estimate_enemy_response` | action | likely reactions | opponent model |
| `find_special_ops_routes` | asset, target | insertion/extraction corridors | exposure map |
| `find_attack_windows` | — | enemy regions ranked by lowest threat | combat evaluator |
| `get_route_status` | from_region, to_region | route found + cost | route evaluator |
| `get_economy_state` / `get_production_state` | — | cash, queues, idle time | economy extractor |
| `set_production_directive` | owner, capabilities | accepted | production director |
| `set_expansion_priority` | region, priority | accepted | production director |
| `create_mission` / `modify_mission` / `cancel_mission` | mission.v1 | mission_id / REJECTED_* | mission manager |
| `assign_force` / `release_force` | force_id, mission_id | accepted / REJECTED_CONFLICT | order arbiter |
| `set_reserve` | fraction, justification? | accepted / REJECTED_UNJUSTIFIED_RESERVE_COMMITMENT | reserve manager |
| `request_recon` | region, priority | recon mission created | mission manager |
| `set_strategic_posture` | posture | accepted | strategic state |
| `get_mission_status` / `get_force_readiness` / `get_transport_status` | id | status | mission/force/transport |

The **read-only** tools (`estimate_engagement`, `plan_routes`, `score_targets`, `compare_force_packages`,
`estimate_enemy_response`, `find_attack_windows`, `find_special_ops_routes`, `get_*`, `inspect_*`) are
implemented and served by `ToolApiBotModule` on `http://127.0.0.1:8766/tools`. The **mutation** tools
(`set_production_directive`, `set_expansion_priority`, `request_capability`, `create_mission`,
`modify_mission`, `cancel_mission`, `assign_force`, `release_force`, `set_reserve`, `request_recon`,
`set_strategic_posture`) are also callable, but remain side-effect free: each returns an engine-validated
`plan_patch`. The model merges accepted patches into its final `command.intent.v1`; the game thread then
validates the complete intent again before executing it.

`fraction` is the denominator of the held reserve (`4` = 25%, `5` = 20%). Values `7`-`10`
reduce the reserve below roughly 15% and therefore require a concrete `justification` of at least
20 characters. The validated patch preserves that rationale as `reserve_justification` for the
game-thread validation pass.

The deterministic commander derives one posture policy per review. The policy jointly controls
production capabilities, acceptable loss, reserve size/commitment, expansion timing, and the budget
for secondary operations. Each map region may independently override the global posture from its own
friendly control, enemy pressure, and expansion value.

Operation-driven production requirements are reported in `get_global_summary.production_requirements`.
The tracked vocabulary is `recon`, `mobility`, `fast_raiding`, `air_superiority`, `transport`,
`special_operations`, and `naval`. Requirements are coalition-wide: production is not duplicated when
any ally already supplies the capability. Production roles are non-overlapping (`main`, `naval`,
`expansion`, `escort`), and the richest otherwise-unassigned ally receives expansion specialization.

`get_opponent_model` reports sample-backed raid and feint response rates plus observed expansion timing.
Historical tendencies affect bait/feint planning only after model confidence reaches 0.6; lower-confidence
history remains an uncertainty rather than a guaranteed prediction. Immediate counterattacks likewise
require observed attacker depletion or an exposed production origin and a verified local advantage.

Executable team-plan directives carry an `assignments` map keyed by `attack`, `strike`, `pincer`,
`supportPower`, `feint`, `recon`, `bait`, `counter`, and `transport`. Once this map is present, a bot
executes only keys that explicitly list its player id; an empty list is deliberately inert. Tactical
controllers expose an inability reason and request a debounced strategic replan when their assigned
domain or asset is unavailable.

RA support powers are synchronized through `supportPowerTick`. Spy planes are reconnaissance,
paratroopers are reinforcement, and parabombs/nukes are strikes; chronoshift powers are left to their
specialized multi-target controller. Strike powers require sufficient observed target value and are
withheld when friendly units crowd the blast area.

**Rule:** the commander may not move or attack with individual units. Emergency survival remains in the
deterministic tactical controllers, so there is no LLM direct-control bypass.

### Tool endpoint (`tool.call.v1`)

The engine serves the tools over HTTP from the running game (`ToolApiBotModule`,
`http://127.0.0.1:8766/tools`). Every request is validated against the live blackboard
(regions, forces, capabilities, coordinates) and answered from deterministic engine
computations; the model server forwards the commander's function calls here and relays
the results verbatim:

```json
// request
{ "tool": "estimate_engagement", "arguments": { "force_a": "Multi0", "force_b": "Multi1" } }
// response
{ "ok": true, "result": { "force_a_power": 32.0, "force_b_power": 8.0, "win_ratio": 4.0,
  "expected_friendly_loss_fraction": 0.0, "expected_enemy_loss_fraction": 0.75, "model_version": "v1" } }
// rejection
{ "ok": false, "error": "UNKNOWN_REFERENCE", "message": "Unknown region \"99\"." }
```

Error codes: `INVALID_REQUEST`, `INVALID_ARGUMENTS`, `UNKNOWN_TOOL`, `UNKNOWN_REFERENCE`,
`NOT_READY` (engine state not yet built). Read calls are side-effect free. Mutation calls
also never issue actor orders: they return validated `plan_patch` data that is merged into
the command intent and validated again on the game thread, so the endpoint cannot bypass
the deterministic execution boundary.

---

## 6. Combat estimate (`engagement_estimate.v1`)

Engine-computed, deterministic:

```json
{
  "estimated_success": 0.71,
  "expected_friendly_losses": 4400,
  "expected_enemy_losses": 8100,
  "time_to_resolution_seconds": 25,
  "major_risks": ["enemy_artillery", "insufficient_anti_air"],
  "reinforcement_advantage_after_25s": "enemy",
  "model_version": "v1"
}
```

Formula (engine-side): `CombatPower = Σ unit_value × health_factor × readiness ×
target_matchup × weapon_range_factor × mobility_factor × terrain_factor × support_factor`.

---

## 7. Events (`event.v1`) — what wakes strategic reasoning

```json
{ "tick": 246000, "type": "enemy_base_discovered", "region": "EAST_BASE", "payload": {} }
```

Event types: `enemy_base_discovered | enemy_expansion_discovered | enemy_composition_change |
major_attack_begins | major_attack_fails | base_attacked | production_building_destroyed |
special_unit_available | transport_loaded | transport_detected | mission_objective_completed |
bridge_lost | resource_situation_change | support_power_available | enemy_high_value_discovered |
enemy_army_lost_from_observation`

The current detector directly observes major allied attack starts, failed/aborted missions, newly ready
transports, completed missions, bridge-state/route-signature changes, and coalition-cash changes. These
events use the same review debounce as the established discovery, base-loss, support-power, and
loss-of-contact triggers.

Cadence: unit micro = engine tick · mission execution ≈ 0.2–1 s · operational reassessment
≈ 1–3 s · **LLM strategic review = every ~5–15 s or on significant events** (already the
15 s `ExternalBrainBreakSeconds` pacing).

---

## 8. Mapping to the current implementation

| Contract element | Current code | Status |
|---|---|---|
| `world.snapshot.v1` (team, forces, enemies, economy) | `ExternalBrainBotModule.BuildSnapshot` plus coalition blackboard summaries | done |
| round-based caching (one plan per team) | `PLAN_CACHE` in `model_server.py` | done |
| `command.intent.v1` (strategy/roles/produce/retreat) | `TeamPlan` in `StrategicBrainBotModule.ApplyTeamPlan` | done |
| feint / counter / transport / roles | `UpdateTactics`, `ExecuteTransportMission` | done (v0 of these primitives) |
| honesty ladder + confidence | `CoalitionIntelTracker` and bounded snapshot summaries | done |
| tools (estimate_engagement, plan_routes, …) | `CommandToolApi` + `ToolApiBotModule` (HTTP `127.0.0.1:8766/tools`) | done — engine-validated; mutations return plan patches |
| deterministic fallback | scripted brain on timeout/invalid plan | done |

### Context and decision-log bounds

Variable-size snapshot sections are deterministically capped before JSON serialization: 8 notable unique
assets per ally, 16 army groups, 32 newest events, 32 distinct uncertainties, and 64 enemy type buckets.
Enemy actor coordinates enter the external snapshot only while at least one coalition member currently sees
the actor; historical information comes only from actor-free intelligence records.

`ai/brain.log` records every consultation with game tick and round, the full structured tool call (including
call ID and arguments), the full engine result, and the validated final plan. Its 10 MiB rotation cap keeps
that reconstructable record bounded.

The commander policy does not equate force preservation with zero casualties. It may accept an
engine-estimated loss within the active posture's acceptable-loss bound when the mission purchases a higher
strategic value or decisive follow-on, but it must retain an explicit withdrawal threshold. Conversely,
verified short-lived mistakes (depleted attackers, exposed production, missing counters, undefended
expansions, or newly open routes) are exploited in the same review using the response/window/engagement
tools; uncertain intelligence cannot be promoted to a “mistake.”

---

## 9. Recommended build order (per the architecture)

1. **Observability** — upgrade `BuildSnapshot` to `world.snapshot.v1`: force aggregation,
   regions, threat fields, confidence-tagged intel, event log. (No LLM changes.)
2. **Coalition control** — deterministic command: one commander marshals several allied
   bots (missions as orders to `StrategicBrainBotModule` per bot).
3. **Missions** — `MissionManager` + `Mission.v1` lifecycle (attack/defend/recon/raid/
   retreat/air-strike/transport) executed by the tactical runtime.
4. **Tactical intelligence** — threat fields, `estimate_engagement`, routes, readiness.
5. **Deterministic strategic commander** — utility-based posture/mission chooser (proves
   the API without an LLM).
6. **LLM commander** — swap the heuristic strategist for Gemma via `command.intent.v1`
   + tools; keep 5 as fallback.
7. **Deception** — feints/probes/fake retreats with measured enemy reaction.
8. **Special operations** — Tanya/spy/engineer missions with exposure-map routing.
9. **Opponent modeling** — behavioral profile + exploitation.
10. **Optimization** — self-play, replay evaluation, metrics (§37 of the design), tuning.

The interface above is the stable foundation for all ten phases.
