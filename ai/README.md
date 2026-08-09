# OpenRA AI Bot — external model server

The `AI` bot (selectable in the skirmish lobby) combines a deterministic in-game strategic
brain with an optional **external model server** that can refine its decisions.

## How it works

Every 200 game ticks (default), the bot serializes its state — own units, enemy sightings
(only from explored territory), cash, and world time — to JSON and posts it to this server's
`POST /decide` endpoint. The server returns a plan:

```json
{
  "produce": ["2tnk", "3tnk"],
  "attack": {"x": 12, "y": 34},
  "retreat": false
}
```

The game applies the plan on the next tick. Requests are asynchronous with a 2-second
timeout: if the server is down or slow, the bot silently falls back to its built-in
scripted brain, so the game never stalls.

## Running the server

Dependency-free (Python standard library only):

```sh
# Dummy heuristic backend (no model needed - good for testing)
python3 model_server.py

# LLM backend (OpenAI-compatible endpoint, e.g. Ollama / llama.cpp)
AI_MODEL_ENDPOINT=http://localhost:11434/v1/chat/completions \
AI_MODEL_NAME=qwen3 \
python3 model_server.py --llm

# Custom port
python3 model_server.py --port 9000
```

Health check: `curl http://127.0.0.1:8765/health`

## Wiring

The bot is configured in `mods/ra/rules/ai.yaml` (and the other mods' `ai.yaml` after
rollout). The external brain is enabled via the `ExternalBrainBotModule` trait:

```yaml
ExternalBrainBotModule:
    RequiresCondition: enable-ai
    ExternalBrainUrl: http://127.0.0.1:8765
    ExternalBrainInterval: 200
    ExternalBrainTimeout: 2000
```

Set `ExternalBrainUrl` to empty to disable the external brain entirely (the deterministic
brain then runs alone).

## Determinism warning

Orders issued from the model server are **not deterministic**: they depend on wall-clock
time and model output. Games using this bot will **desync in multiplayer and break replay
fidelity**. Use the `AI` bot with the external brain in single-player skirmish; the
built-in scripted brain alone is deterministic and safe everywhere.
