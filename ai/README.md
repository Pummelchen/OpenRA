# OpenRA AI Bot — external model server

The `AI` bot (selectable in the skirmish lobby) combines a deterministic in-game strategic
brain with an optional **external model server** that refines its decisions — including a
vision channel: the bot's full-map radar (terrain, water, mountains, explored-vs-unexplored
shroud, and currently visible unit dots) is sent to a vision-capable model. Exact enemy
positions require current visibility; lost contacts become actor-free last-known snapshots.

## Architecture

```
Allied AI bots (ExternalBrainBotModule on each)
  → POST /decide  { identical team snapshot + "screenshotPath" }
  → model_server.py (ai/model_server.py)  — one team plan per round, cached
  → mlx-vlm server (Qwen3.5 4B, MLX 8-bit, vision)
  ← team plan { strategy, attack, feint, counter, roles, produce, retreat, transport }
  → each bot applies its own share (role, production, tactics)
```

- **Team command center**: every allied bot posts an *identical* team snapshot (all members'
  units, buildings, cash, and enemy intel from the shared allied shroud). The server caches
  **one team plan per consultation round**, so all friendly bots receive the same orders and
  act as a single coordinated force. Qwen3.5 assigns roles (`main`/`escort`/`naval`/`defend`),
  picks coordinated attack/feint/counter targets, production boosts, retreats, and optional
  transport missions (stealth infantry insertions).
- The radar PNG is generated on demand by `RadarCaptureBotModule` (HD, 1920 px wide) right
  before each consultation.
- Consultations are paced: the next request (and a fresh radar capture) is only sent **15
  seconds after the previous analysis was received** (`ExternalBrainBreakSeconds`).
- Requests are asynchronous with a 120-second timeout: if the server is down or does not answer
  within that budget, the bots silently fall back to their built-in scripted brains.

## Running the model server

The brain server is dependency-free Python (stdlib only). The vision endpoint requires a
current `mlx-vlm` release with Qwen3.5 support. Keep the runtime and model cache local to
the checkout so `ai/run.sh` can use them automatically:

```sh
# 1. Create the project-local Apple-Silicon vision runtime
/opt/homebrew/bin/python3.13 -m venv .venv-ai
.venv-ai/bin/python -m pip install --upgrade pip mlx-vlm jinja2

# 2. Download Qwen3.5 4B (MLX 8-bit) into the project-local cache
hf download mlx-community/Qwen3.5-4B-MLX-8bit --cache-dir .hf-cache/hub

# 3. Start the vision endpoint and coalition brain server
ai/run.sh

# Dummy backend (no model needed, for testing):
.venv-ai/bin/python ai/model_server.py
```

Health check: `curl http://127.0.0.1:8765/health`

`ai/run.sh` automatically prefers `.venv-ai/bin/python` and sets `HF_HOME` to the ignored
project-local `.hf-cache` directory. Qwen thinking mode is left disabled so the commander's
512-token response budget is spent on the required plan JSON and tool calls. If the explicit
download step is skipped, the first launch downloads the model into that cache. The verified
snapshot occupies approximately 4.8 GiB on disk, and a local completion smoke test used 6.04 GB
peak memory. Override `PYTHON`, `HF_HOME`, `AI_MODEL_TIMEOUT_SECONDS`, or `AI_MODEL_MAX_TOKENS`
when an intentionally shared runtime/cache or a different bounded inference budget is desired.

## Engine tool API

The game also serves an **engine-validated tool API** for the commander
(`ToolApiBotModule`, `http://127.0.0.1:8766/tools`): `estimate_engagement`, `plan_routes`,
`score_targets`, `inspect_region` (control, threats, buildable/expansion data), `inspect_force`
(composition, capabilities, status, assignment, casualties), `inspect_enemy_intelligence`,
`get_opponent_model`, `get_uncertainties`, `get_recent_events`, `get_global_summary`,
`get_economy_state` (cash, power, refineries, harvesters, resources), `get_production_state`
(queues + progress), `compare_force_packages`, `estimate_enemy_response`, `find_attack_windows`,
`find_special_ops_routes`, `get_mission_status`, `get_force_readiness`, `get_transport_status`,
`get_route_status`, `inspect_force_package` (a joint force spanning several allied players), plus the production, mission, force, reserve, reconnaissance, and posture
mutation tools documented in `COMMAND_API.md`. Every call is validated against the live blackboard and answered from
deterministic engine computations — the LLM never receives fabricated mechanics. The model
server forwards the commander's function calls here and relays results back into the conversation:

```sh
curl -X POST http://127.0.0.1:8766/tools -d '{"tool":"get_global_summary","arguments":{}}'
# {"ok":true,"result":{"posture":"attack","force_ratio":0.33,...}}
```

Set `AI_TOOL_ENDPOINT` (default `http://127.0.0.1:8766/tools`, empty disables) to point the
server at a different engine; at startup the server probes the endpoint and only enables
tool calls when the engine answers. Mutation calls return validated `plan_patch` objects and never
issue orders directly; the complete final plan is validated again on the game thread.

## Headless evaluation

Run fixed-seed Fair-Fog matches and compare Supreme with a standard scripted baseline:

```sh
python3 ai/selfplay.py --map mods/ra/maps/shattered-mountain \
  --vs rush,turtle,naval --runs 3 --ticks 30000 --seed-base 805 --intelligence 0 --details
python3 ai/selfplay.py --map mods/ra/maps/shattered-mountain \
  --bot-type normal --vs rush --runs 3 --ticks 30000 --seed-base 805 --intelligence 0

# hold the faction constant so a batch varies only the strategy under test
python3 ai/selfplay.py --map mods/ra/maps/shattered-mountain --faction soviet --runs 4
```

The report separates fog-limited commander exchange from ground-truth player statistics
and reports wins, losses, and time-limit draws. A nonzero simulation exit or missing
`Finished:` marker fails the batch instead of being counted as a match. `--details` adds
each seed's outcome, ground-truth exchange, and duration so a mean regression can be traced
to the exact deterministic match.

The fallback tactical executor owns all actor orders. Ground forces advance as cohesive
screened groups; air and naval forces acquire only currently visible, weapon-valid contacts,
cap focus assignments, preserve active weapon cycles, and refresh stale movement at a bounded
cadence. Aircraft rearm instead of receiving conflicting attack orders, recoverable damaged
units use service facilities, and close raids trigger a nearest-unit response proportional to
the observed attackers. These rules run identically whether the external model is available or
the deterministic strategic brain is in control.

## Replay analysis

Any OpenRA replay - including a game played against a human - can be evaluated with the same
tooling, and AI decisions can be read against the replay's own tick timeline:

```sh
utility.sh ra --analyze-replay REPLAY=~/path/to/game.orarep \
  TELEMETRY="$HOME/Library/Application Support/OpenRA/ai-telemetry.log"
```

The report lists players, factions, teams, outcomes, duration and an explicit human-vs-AI verdict.
Headless matches use an echo connection with no recorder, so batch runs remain covered by fixed-seed
determinism rather than replay files.

## Terminal monitor

Every prompt sent to the model, each complete tool call/result pair, the raw reply, and the parsed plan are written to
**`ai/brain.log`** and mirrored to the server's stdout:

```sh
tail -f ai/brain.log
```

Example:

```
[06:12:31] PROMPT [tick=2000 round=20] -> mlx-community/Qwen3.5-4B-MLX-8bit: Tick 2000. Cash 8000. Own units (16): 2x 2tnk, 1x e1. Enemy sightings: 1x 3tnk, 1x mig. + radar image ai-radar.png (312 KB)
[06:12:41] REPLY <- "{\"produce\": [\"2tnk\"], \"attack\": {\"x\": 0, \"y\": 0}, \"retreat\": false}"
[06:12:41] PLAN [tick=2000 round=20] -> {"produce": ["2tnk"], "attack": {"x": 82, "y": 92}, "retreat": false}
```

Degenerate model output is sanitized server-side (e.g. a `(0,0)` attack target is replaced
by the enemy centroid).

The engine-side `ai-telemetry.log` records posture, mission and production changes plus quantitative
match outcomes: win/loss and duration, combat exchange, economic damage, army/production idle time,
cohesion, reserve availability, expansion timing, wave synchronization error, retreat preservation,
recon efficiency, transport survival, counterattack results, defense response, mission/special-ops
success rates, and deception response. `ai/llm_eval.py` consumes this log for repeatable plan scoring.
It can also replay one immutable world state through several live commander decisions:

```sh
python3 ai/llm_eval.py --snapshot ai/world-snapshot.json --decision-replays 5
```

The report preserves the snapshot hash, every plan and plan hash, and the number of unique decisions.

## Wiring

`mods/ra/rules/ai.yaml` configures the AI bot:

```yaml
ExternalBrainBotModule:
    RequiresCondition: enable-ai
    ExternalBrainUrl: http://127.0.0.1:8765
    ExternalBrainBreakSeconds: 15
    ExternalBrainTimeout: 120000
RadarCaptureBotModule:
    RequiresCondition: enable-ai
    RadarCaptureWidth: 1920
```

Set `ExternalBrainUrl` to empty to disable the external brain (the deterministic brain then
runs alone).

## Determinism warning

Orders issued from the model server are **not deterministic**: they depend on wall-clock
time and model output. Games using this bot with the external brain will **desync in
multiplayer and break replay fidelity**. Use it in single-player skirmish; the built-in
scripted brain alone is deterministic and safe everywhere.
