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
import time
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

DEFAULT_PORT = 8765
BRAIN_LOG = os.path.join(os.path.dirname(os.path.abspath(__file__)), "brain.log")

# One team plan per consultation round: every allied bot posts the same round key and receives
# the identical plan, so all friendly bots act as one coordinated force.
PLAN_CACHE: dict = {}

MODEL_ENDPOINT = os.getenv("AI_MODEL_ENDPOINT", "http://localhost:11434/v1/chat/completions")
MODEL_NAME = os.getenv("AI_MODEL_NAME", "qwen3")
MODEL_API_KEY = os.getenv("AI_MODEL_API_KEY", "")

# Units the dummy backend prefers to produce, in priority order.
DUMMY_ARMY_PRIORITY = ["e1", "e3", "2tnk", "3tnk", "4tnk", "ttnk", "v2rl", "heli", "mig"]

SYSTEM_PROMPT = """You are the tactical command center of a team of OpenRA bots. The team members are listed under
"team"; they fight as ONE force. Decide the team's strategy, roles, production, and maneuvers from the game state.

The image in the user message (if any) is the team's strategic radar: the whole map with terrain, water, mountains,
the explored territory (unexplored areas are darkened), and unit dots (green = own team, red = enemy).

Rules:
- Combine the forces of all team members for coordinated attacks. Assign roles so one bot can build naval units
  ("naval"), another escorts or defends ("escort"/"defend"), and the strongest pushes the main attack ("main").
- Attack when the team's combined army is at least as large as the enemy force. A feint (a decoy position) can
  distract the enemy before the real attack.
- Use "counter" to defend an allied base or intercept a threat position.
- Retreat when the team's units are heavily damaged or heavily outnumbered.
- Production ("produce") should be a small list of units that counters the scouted enemy (anti-air for air, etc.).
  Use EXACT OpenRA unit ids (e.g. e1, e3, 2tnk, 3tnk, 4tnk, ttnk, v2rl, mig, ss, dd) - never generic names.
- A "transport" mission can stealth-insert infantry behind enemy lines ("kind": "naval" or "air").
- Coordinates are OpenRA map cells. Roles keys must exactly match the player ids in "team".

Reply with ONLY a JSON object of the form:
{"strategy": "attack|defend|build|turtle", "attack": {"x": 0, "y": 0}, "feint": {"x": 0, "y": 0},
 "counter": {"x": 0, "y": 0}, "roles": {"playerid": "main|escort|naval|defend"},
 "produce": ["unit1", "unit2"], "retreat": false,
 "transport": {"kind": "naval", "to": {"x": 0, "y": 0}}}
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
            # One plan per consultation round: all allied bots post the same round key and receive
            # the identical team plan, so the whole team acts on the same orders.
            round_key = state.get("round")
            if round_key is not None and round_key in PLAN_CACHE:
                plan = PLAN_CACHE[round_key]
            else:
                plan = self.server.decide(state)
                if round_key is not None:
                    PLAN_CACHE[round_key] = plan
            self._respond(200, plan)
        except Exception as exc:  # noqa: BLE001 - keep the game running on any backend error
            self._respond(200, empty_team_plan())

    def _respond(self, status: int, payload: dict) -> None:
        body = json.dumps(payload).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


def dummy_plan(state: dict) -> dict:
    """Deterministic heuristic plan used for testing and as the no-model fallback."""
    team = state.get("team", []) or []
    own = [u for member in team for u in member.get("units", [])]
    enemies = state.get("enemies", []) or []
    cash = sum(member.get("cash", 0) for member in team)
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
        attack = {"x": int(statistics.mean(e["x"] for e in enemies)), "y": int(statistics.mean(e["y"]) for e in enemies)}

    team_ids = [m.get("player") for m in team]
    roles = {team_ids[0]: "main"} if team_ids else {}
    return {
        "strategy": "attack" if attack else "build",
        "attack": attack,
        "feint": None,
        "counter": None,
        "roles": roles,
        "produce": produce,
        "retreat": retreat,
        "transport": None,
    }


def llm_plan(state: dict, endpoint: str, model: str, api_key: str, vision: bool) -> dict:
    """Asks an OpenAI-compatible model for a team plan and parses its JSON response."""
    prompt = team_summary(state)

    content = [{"type": "text", "text": prompt}]
    image_note = ""
    if vision:
        screenshot = state.get("screenshotPath") or state.get("ScreenshotPath")
        if screenshot and os.path.exists(screenshot):
            with open(screenshot, "rb") as image_file:
                encoded = base64.b64encode(image_file.read()).decode("utf-8")
            image_note = f" + radar image {os.path.basename(screenshot)} ({os.path.getsize(screenshot) // 1024} KB)"
            content.append({
                "type": "image_url",
                "image_url": {"url": f"data:image/png;base64,{encoded}"},
            })

    log_brain(f"PROMPT -> {model}: {prompt}{image_note}")

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
    log_brain(f"REPLY <- {model}: {content[:500]}")
    plan = json.loads(content)
    plan = sanitize_team_plan(plan, state)
    log_brain(f"PLAN  -> {json.dumps(plan)}")
    return plan


def summarize(units: list) -> str:
    counts = {}
    for unit in units:
        key = unit.get("type", "?")
        counts[key] = counts.get(key, 0) + 1
    return ", ".join(f"{count}x {name}" for name, count in sorted(counts.items()))


def team_summary(state: dict) -> str:
    parts = []
    for member in state.get("team", []) or []:
        units = member.get("units", []) or []
        structures = member.get("structures", []) or []
        parts.append(
            f"{member.get('player')}: cash {member.get('cash', 0)}, {len(units)} units ({summarize(units)}), "
            f"{len(structures)} structures"
        )
    enemies = state.get("enemies", []) or []
    return f"Team: {' | '.join(parts)}. Enemy sightings ({len(enemies)}): {summarize(enemies)}."


def sanitize_team_plan(plan: dict, state: dict) -> dict:
    """Hardens the model's team plan: degenerate targets are replaced by the enemy centroid, roles are
    restricted to real team members, and unknown fields are dropped."""
    if not isinstance(plan, dict):
        return empty_team_plan()

    team_ids = {m.get("player") for m in state.get("team", []) or []}
    enemies = state.get("enemies", []) or []
    strategy = plan.get("strategy") if plan.get("strategy") in ("attack", "defend", "build", "turtle") else "build"

    def target(value):
        if isinstance(value, dict) and not (value.get("x", 0) <= 0 and value.get("y", 0) <= 0):
            return {"x": int(value["x"]), "y": int(value["y"])}
        if enemies:
            return {
                "x": int(statistics.mean(e["x"] for e in enemies)),
                "y": int(statistics.mean(e["y"] for e in enemies)),
            }
        return None

    roles = {
        k: v for k, v in (plan.get("roles") or {}).items()
        if k in team_ids and v in ("main", "escort", "naval", "defend")
    }
    produce = [u for u in plan.get("produce", []) if isinstance(u, str) and u]

    transport = plan.get("transport")
    if isinstance(transport, dict) and isinstance(transport.get("to"), dict):
        kind = transport.get("kind") if transport.get("kind") in ("naval", "air") else "naval"
        transport = {"kind": kind, "to": target(transport.get("to"))}
        if transport["to"] is None:
            transport = None
    else:
        transport = None

    return {
        "strategy": strategy,
        "attack": target(plan.get("attack")),
        "feint": target(plan.get("feint")),
        "counter": target(plan.get("counter")),
        "roles": roles,
        "produce": produce,
        "retreat": bool(plan.get("retreat")),
        "transport": transport,
    }


def empty_team_plan() -> dict:
    return {
        "strategy": "build",
        "attack": None,
        "feint": None,
        "counter": None,
        "roles": {},
        "produce": [],
        "retreat": False,
        "transport": None,
    }


def log_brain(message: str) -> None:
    """Terminal monitor: append prompt/reply traffic to ai/brain.log and mirror it to stdout."""
    line = f"[{time.strftime('%H:%M:%S')}] {message}"
    print(line, flush=True)
    try:
        with open(BRAIN_LOG, "a", encoding="utf-8") as log_file:
            log_file.write(line + "\n")
    except OSError:
        pass


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
