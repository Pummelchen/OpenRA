#!/usr/bin/env python3
"""External brain server for the OpenRA "AI" bot.

The game posts a JSON snapshot of the bot's state to POST /decide and expects a
plan back:

    {"produce": ["2tnk", "3tnk"], "attack": {"x": 12, "y": 34}, "retreat": false}

Two backends are supported:
  * dummy   - a deterministic heuristic (default, no model required)
  * llm     - an OpenAI-compatible chat completion endpoint (Ollama, llama.cpp, ...)

Usage:
  python3 model_server.py                      # dummy backend on 127.0.0.1:8765
  python3 model_server.py --port 9000          # custom port
  AI_MODEL_ENDPOINT=http://localhost:11434/v1/chat/completions \
  AI_MODEL_NAME=qwen3 \
  python3 model_server.py --llm                # LLM backend

The server is deliberately dependency-free (Python standard library only).
"""

from __future__ import annotations

import argparse
import base64
import json
import os
import statistics
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

DEFAULT_PORT = 8765
MODEL_ENDPOINT = os.getenv("AI_MODEL_ENDPOINT", "http://localhost:11434/v1/chat/completions")
MODEL_NAME = os.getenv("AI_MODEL_NAME", "qwen3")
MODEL_API_KEY = os.getenv("AI_MODEL_API_KEY", "")

# Units the dummy backend prefers to produce, in priority order.
DUMMY_ARMY_PRIORITY = ["e1", "e3", "2tnk", "3tnk", "4tnk", "ttnk", "v2rl", "heli", "mig"]

SYSTEM_PROMPT = """You are the strategic brain of an OpenRA bot. Decide production and tactics from the game state.

The image in the user message is the bot's strategic radar: the whole map with terrain, water, mountains,
the explored territory (unexplored areas are darkened), and unit dots (green = own, red = enemy).

Rules:
- Produce a small, focused army. Prioritize anti-air when enemy air units are present, anti-armor for tanks, anti-infantry otherwise.
- Attack when your army is at least as large as the enemy force. Prefer attacking through the uncovered terrain shown on the radar.
- Retreat when your units are heavily damaged (many below 30% health) or heavily outnumbered.
- Coordinates are OpenRA map cells. Use the average of the enemy positions as the attack target when attacking.

Reply with ONLY a JSON object of the form:
{"produce": ["unit1", "unit2"], "attack": {"x": 0, "y": 0}, "retreat": false}
Do not include markdown, comments, or any other text."""


class PlanServer(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        # Keep the console quiet unless something interesting happens.
        message = fmt % args
        if "POST" in message or "error" in message.lower():
            print(message, flush=True)

    def do_GET(self):
        if self.path == "/health":
            self._respond(200, {"status": "ok", "backend": self.server.backend_name})
            return
        self._respond(404, {"error": "not found"})

    def do_POST(self):
        if self.path != "/decide":
            self._respond(404, {"error": "not found"})
            return

        try:
            length = int(self.headers.get("Content-Length", 0))
            state = json.loads(self.rfile.read(length))
        except (ValueError, json.JSONDecodeError) as exc:
            self._respond(400, {"error": f"invalid request: {exc}"})
            return

        try:
            plan = self.server.decide(state)
            self._respond(200, plan)
        except Exception as exc:  # noqa: BLE001 - keep the game running on any backend error
            self._respond(200, {"produce": [], "attack": None, "retreat": False, "error": str(exc)})

    def _respond(self, status: int, payload: dict) -> None:
        body = json.dumps(payload).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


def dummy_plan(state: dict) -> dict:
    """Deterministic heuristic plan used for testing and as the no-model fallback."""
    own = state.get("own", [])
    enemies = state.get("enemies", [])
    cash = state.get("cash", 0)
    army = [u for u in own if u.get("type") not in ("harv", "mcv")]
    damaged = [u for u in army if u.get("healthPercent", 100) < 30]
    retreat = len(damaged) > max(1, len(army) // 2) or len(enemies) > len(army) * 3

    produce = []
    if cash > 400:
        produce = [DUMMY_ARMY_PRIORITY[0]] if not army else [next(
            (t for t in DUMMY_ARMY_PRIORITY[1:] if t not in [u["type"] for u in army][:3]),
            DUMMY_ARMY_PRIORITY[1],
        )]

    attack = None
    if enemies and not retreat and len(army) >= max(4, len(enemies)):
        xs = [e["x"] for e in enemies]
        ys = [e["y"] for e in enemies]
        attack = {"x": int(statistics.mean(xs)), "y": int(statistics.mean(ys))}

    return {"produce": produce, "attack": attack, "retreat": retreat}


def llm_plan(state: dict, endpoint: str, model: str, api_key: str, vision: bool) -> dict:
    """Asks an OpenAI-compatible model for a plan and parses its JSON response."""
    prompt = (
        f"Tick {state.get('tick', 0)}. Cash {state.get('cash', 0)}. "
        f"Own units ({state.get('armyCount', 0)}): {summarize(state.get('own', []))}. "
        f"Enemy sightings: {summarize(state.get('enemies', []))}."
    )

    content = [{"type": "text", "text": prompt}]
    if vision:
        screenshot = state.get("screenshotPath") or state.get("ScreenshotPath")
        if screenshot and os.path.exists(screenshot):
            with open(screenshot, "rb") as image_file:
                encoded = base64.b64encode(image_file.read()).decode("utf-8")
            content.append({
                "type": "image_url",
                "image_url": {"url": f"data:image/png;base64,{encoded}"},
            })

    payload = {
        "model": model,
        "messages": [
            {"role": "system", "content": SYSTEM_PROMPT},
            {"role": "user", "content": content},
        ],
        "temperature": 0.1,
        "max_tokens": 200,
    }
    headers = {"Content-Type": "application/json"}
    if api_key:
        headers["Authorization"] = f"Bearer {api_key}"

    req = urllib.request.Request(endpoint, data=json.dumps(payload).encode("utf-8"), headers=headers)
    with urllib.request.urlopen(req, timeout=15) as response:
        data = json.loads(response.read().decode("utf-8"))

    content = data["choices"][0]["message"]["content"]
    content = content.strip()
    if content.startswith("```"):
        content = content.split("\n", 1)[1].rsplit("```", 1)[0]
    return json.loads(content)


def summarize(units: list) -> str:
    counts = {}
    for unit in units:
        key = unit.get("type", "?")
        counts[key] = counts.get(key, 0) + 1
    return ", ".join(f"{count}x {name}" for name, count in sorted(counts.items()))


def main() -> None:
    parser = argparse.ArgumentParser(description="External brain server for the OpenRA AI bot")
    parser.add_argument("--port", type=int, default=DEFAULT_PORT)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--llm", action="store_true", help="use the OpenAI-compatible LLM backend")
    parser.add_argument("--vision", action="store_true", help="attach the bot's radar screenshot to LLM requests (requires a vision-capable model)")
    args = parser.parse_args()

    if args.llm:
        def decide(state):
            return llm_plan(state, MODEL_ENDPOINT, MODEL_NAME, MODEL_API_KEY, args.vision)

        backend_name = f"llm ({MODEL_NAME} @ {MODEL_ENDPOINT})" + (" + vision" if args.vision else "")
    else:
        decide = dummy_plan
        backend_name = "dummy"

    server = ThreadingHTTPServer((args.host, args.port), PlanServer)
    server.decide = decide
    server.backend_name = backend_name
    print(f"OpenRA AI model server listening on http://{args.host}:{args.port} (backend: {backend_name})", flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
