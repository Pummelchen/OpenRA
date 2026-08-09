# OpenRA AI Bot — external model server

The `AI` bot (selectable in the skirmish lobby) combines a deterministic in-game strategic
brain with an optional **external model server** that refines its decisions — including a
vision channel: the bot's full-map radar (terrain, water, mountains, explored-vs-unexplored
shroud, unit dots) is sent to a vision-capable model.

## Architecture

```
Game (ExternalBrainBotModule)
  → POST /decide  { state JSON + "screenshotPath": "/path/to/ai-radar.png" }
  → model_server.py (ai/model_server.py)
  → mlx-vlm server (Gemma 4 E4B, MLX 4-bit, vision)  [or any OpenAI-compatible endpoint]
  ← {"produce": ["2tnk"], "attack": {"x": 12, "y": 34}, "retreat": false}
```

- The radar PNG is generated on demand by `RadarCaptureBotModule` (HD, 1920 px wide, map
  aspect ratio) right before each model consultation.
- Consultations are paced: the next request (and a fresh radar capture) is only sent **15
  seconds after the previous analysis was received** (`ExternalBrainBreakSeconds`), giving
  the game and the Mac a break.
- Requests are asynchronous with a timeout: if the server is down or slow, the bot silently
  falls back to its built-in scripted brain.

## Running the model server

Dependency-free Python (stdlib only); requires `mlx-lm` + `mlx-vlm` for the vision model:

```sh
# 1. Serve Gemma 4 E4B (MLX 4-bit) with vision support on port 11435
/opt/homebrew/bin/python3 -m mlx_vlm.server \
  --model mlx-community/gemma-4-e4b-it-4bit --port 11435

# 2. Run the brain server
AI_MODEL_ENDPOINT=http://127.0.0.1:11435/v1/chat/completions \
AI_MODEL_NAME=mlx-community/gemma-4-e4b-it-4bit \
python3 ai/model_server.py --llm --vision

# Dummy backend (no model needed, for testing):
python3 ai/model_server.py
```

Health check: `curl http://127.0.0.1:8765/health`

## Terminal monitor

Every prompt sent to the model, the raw reply, and the parsed plan are written to
**`ai/brain.log`** and mirrored to the server's stdout:

```sh
tail -f ai/brain.log
```

Example:

```
[06:12:31] PROMPT -> mlx-community/gemma-4-e4b-it-4bit: Tick 2000. Cash 8000. Own units (16): 2x 2tnk, 1x e1. Enemy sightings: 1x 3tnk, 1x mig. + radar image ai-radar.png (312 KB)
[06:12:41] REPLY <- mlx-community/gemma-4-e4b-it-4bit: {"produce": ["2tnk"], "attack": {"x": 0, "y": 0}, "retreat": false}
[06:12:41] PLAN  -> {"produce": ["2tnk"], "attack": {"x": 82, "y": 92}, "retreat": false}
```

Degenerate model output is sanitized server-side (e.g. a `(0,0)` attack target is replaced
by the enemy centroid).

## Wiring

`mods/ra/rules/ai.yaml` configures the AI bot:

```yaml
ExternalBrainBotModule:
    RequiresCondition: enable-ai
    ExternalBrainUrl: http://127.0.0.1:8765
    ExternalBrainBreakSeconds: 15
    ExternalBrainTimeout: 2000
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
