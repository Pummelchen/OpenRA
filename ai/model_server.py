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
# the identical plan, so all friendly bots act as one coordinated force. Keys are scoped by
# (round, team) so opposing teams never share plans.
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
{"posture": "attack|defend|build|turtle", "strategy": "attack|defend|build",
 "attack": {"x": 0, "y": 0}, "feint": {"x": 0, "y": 0}, "counter": {"x": 0, "y": 0},
 "roles": {"playerid": "main|escort|naval|defend"}, "produce": ["unit1", "unit2"],
 "retreat": false, "transport": {"kind": "naval", "to": {"x": 0, "y": 0}},
 "missions": [{"type": "attack|defend|recon|raid|feint|transport|counterattack|specialops",
               "x": 0, "y": 0, "priority": 70}]}
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
            # One plan per team per consultation round: all allied bots post the same round key and
            # receive the identical team plan, so the whole team acts on the same orders. The key is
            # scoped by (round, team), so opposing teams - which share the round counter - keep their
            # own plans and never receive the other side's decisions.
            cache_key = self._plan_cache_key(state)
            if cache_key is not None and cache_key in PLAN_CACHE:
                plan = PLAN_CACHE[cache_key]
            else:
                plan = self.server.decide(state)
                if cache_key is not None:
                    PLAN_CACHE[cache_key] = plan
            self._respond(200, plan)
        except Exception as exc:  # noqa: BLE001 - keep the game running on any backend error
            self._respond(200, empty_team_plan())

    @staticmethod
    def _plan_cache_key(state: dict):
        """Cache key for a team plan: (round, team). The team is derived from the allied player ids in
        the snapshot, which every allied bot computes identically. Falls back to the round alone for
        snapshots without a team list."""
        round_key = state.get("round")
        if round_key is None:
            return None

        player_ids = sorted(m.get("player") for m in (state.get("team", []) or []) if m.get("player"))
        if not player_ids:
            return round_key

        return (round_key, "|".join(player_ids))

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
    own = []
    for member in team:
        units = member.get("units", {}) or {}
        if isinstance(units, dict):
            for name, n in (units.get("byType") or {}).items():
                own += [{"type": name}] * n
        else:
            own += units
    enemies = enemy_list(state)
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
    missions = [{"type": "attack", "x": attack["x"], "y": attack["y"], "priority": 90}] if attack else []
    return {
        "posture": "attack" if attack else "build",
        "strategy": "attack" if attack else "build",
        "attack": attack,
        "feint": None,
        "counter": None,
        "roles": roles,
        "produce": produce,
        "retreat": retreat,
        "transport": None,
        "missions": missions,
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


def summarize(units) -> str:
    """units is either a legacy list of {type} dicts or the compressed counts object {total, byType}."""
    counts = {}
    if isinstance(units, dict):
        for name, n in (units.get("byType") or {}).items():
            counts[name] = counts.get(name, 0) + n
    else:
        for unit in units or []:
            key = unit.get("type", "?")
            counts[key] = counts.get(key, 0) + 1
    return ", ".join(f"{count}x {name}" for name, count in sorted(counts.items()))


def total_of(units) -> int:
    if isinstance(units, dict):
        return int(units.get("total", 0))
    return len(units or [])


def enemy_list(state: dict) -> list:
    """Enemies are either a legacy list of {x, y, type} dicts or the compressed aggregate {total, x, y, byType}."""
    enemies = state.get("enemies") or {}
    if isinstance(enemies, list):
        return enemies

    result = []
    for name, n in (enemies.get("byType") or {}).items():
        for _ in range(n):
            result.append({"type": name, "x": enemies.get("x", 0), "y": enemies.get("y", 0)})
    return result


def team_summary(state: dict) -> str:
    parts = []
    for member in state.get("team", []) or []:
        units = member.get("units", {}) or {}
        structures = member.get("structures", {}) or {}
        parts.append(
            f"{member.get('player')}: cash {member.get('cash', 0)}, "
            f"{total_of(units)} units ({summarize(units)}), {total_of(structures)} structures"
        )

    enemies = enemy_list(state)
    force = state.get("force") or {}
    force_part = ""
    if force:
        force_part = (f" Coalition force: {force.get('army', 0)} total "
                      f"({force.get('air', 0)} air, {force.get('naval', 0)} naval, {force.get('land', 0)} land).")
    estimate = state.get("estimate") or {}
    estimate_part = ""
    if estimate:
        estimate_part = (f" Engagement estimate: friendly power {estimate.get('friendly', 0):.1f} vs "
                         f"enemy {estimate.get('enemy', 0):.1f} (win ratio {estimate.get('winRatio', 0):.2f}).")
    return (f"Team: {' | '.join(parts)}. Enemy sightings ({len(enemies)}): {summarize(enemies)}."
            f"{force_part}{estimate_part}")


def sanitize_team_plan(plan: dict, state: dict) -> dict:
    """Hardens the model's team plan: degenerate targets are replaced by the enemy centroid, roles are
    restricted to real team members, and unknown fields are dropped."""
    if not isinstance(plan, dict):
        return empty_team_plan()

    team_ids = {m.get("player") for m in state.get("team", []) or []}
    enemies = enemy_list(state)
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

    mission_types = {"attack", "defend", "recon", "raid", "feint", "transport", "counterattack", "specialops"}
    missions = []
    for m in plan.get("missions", []) or []:
        if not isinstance(m, dict) or m.get("type") not in mission_types:
            continue
        t = target(m.get("target") or m)
        if t is None:
            continue
        missions.append({
            "type": m["type"],
            "x": t["x"],
            "y": t["y"],
            "priority": int(m.get("priority", 50)) if isinstance(m.get("priority"), (int, float)) else 50,
        })

    transport = plan.get("transport")
    if isinstance(transport, dict) and isinstance(transport.get("to"), dict):
        kind = transport.get("kind") if transport.get("kind") in ("naval", "air") else "naval"
        transport = {"kind": kind, "to": target(transport.get("to"))}
        if transport["to"] is None:
            transport = None
    else:
        transport = None

    return {
        "posture": strategy,
        "strategy": strategy,
        "attack": target(plan.get("attack")),
        "feint": target(plan.get("feint")),
        "counter": target(plan.get("counter")),
        "roles": roles,
        "produce": produce,
        "retreat": bool(plan.get("retreat")),
        "transport": transport,
        "missions": missions,
    }


def empty_team_plan() -> dict:
    return {
        "posture": "build",
        "strategy": "build",
        "attack": None,
        "feint": None,
        "counter": None,
        "roles": {},
        "produce": [],
        "retreat": False,
        "transport": None,
        "missions": [],
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
