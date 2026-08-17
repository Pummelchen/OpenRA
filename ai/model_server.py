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

Tool API (optional): the commander may call engine-validated tools (estimate_engagement,
plan_routes, score_targets, ...) served by the game's ToolApiBotModule. Set AI_TOOL_ENDPOINT
to the game's tool endpoint (default http://127.0.0.1:8766/tools) or empty to disable tools.

The server is deliberately dependency-free (Python standard library only).
"""

import argparse
import base64
import json
import os
import statistics
import sys
import time
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

if sys.version_info < (3, 11):
    sys.exit("Python 3.11 or newer is required (found %d.%d)." % (sys.version_info[0], sys.version_info[1]))

DEFAULT_PORT = 8765
BRAIN_LOG = os.path.join(os.path.dirname(os.path.abspath(__file__)), "brain.log")
BRAIN_LOG_MAX_BYTES = 10 * 1024 * 1024  # 10 MB cap; truncate from the top when exceeded.

# One team plan per consultation round: every allied bot posts the same round key and receives
# the identical plan, so all friendly bots act as one coordinated force. Keys are scoped by
# (round, team) so opposing teams never share plans.
PLAN_CACHE: dict = {}
PLAN_CACHE_MAX = 100

MODEL_ENDPOINT = os.getenv("AI_MODEL_ENDPOINT", "http://localhost:11434/v1/chat/completions")
MODEL_NAME = os.getenv("AI_MODEL_NAME", "qwen3")
MODEL_API_KEY = os.getenv("AI_MODEL_API_KEY", "")

# The engine's tool API (ToolApiBotModule in the game). Tool calls from the commander are forwarded
# here; the engine validates every request against its live blackboard and returns computed results
# the LLM cannot fabricate. Empty disables tools.
TOOL_ENDPOINT = os.getenv("AI_TOOL_ENDPOINT", "http://127.0.0.1:8766/tools")

# How many tool rounds a consultation may use before the model must commit to a plan.
MAX_TOOL_ROUNDS = 4

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

Intelligence honesty (do not cheat or invent):
- Enemy intel carries a status: observed (seen now), last_known (was seen, position may have moved),
  inferred (a structure still assumed present), suspected (a guess about an unexplored region), unknown.
- A last_known position is NOT the enemy's current position. If intel is stale or low-confidence, order
  reconnaissance rather than attacking the old position.
- You never receive hidden enemy positions: do not invent coordinates, force sizes, or outcomes. If you do
  not know something, use a tool (estimate_engagement, plan_routes, score_targets) or say so in the plan.

Plan discipline:
- Identify one main effort: concentrate the coalition on a single primary objective; secondary missions
  (feints, raids, air strikes) support it, they do not spread the army evenly.
- Keep a reserve in mind: do not commit every unit; a held-back reserve stops counterattacks and exploits
  breakthroughs. Consider the coalition's reserve before going all-in.
- Each major operation should have a launch condition (enough force, a route) and a fallback if it fails
  (withdraw, convert to a feint). State the mission list accordingly.

TOOLS (engine-validated: results come from the engine, never fabricated):
- get_global_summary() -> posture, force ratio, cash, army/enemy strength
- inspect_region(region) -> control, pressure, threat fields (region = int or "REGION_n")
- inspect_force(force) -> composition, strength, readiness (force = player id)
- inspect_enemy_intelligence(region?) -> enemy intel with confidence and age
- get_recent_events(since_tick?) -> engine event log
- get_opponent_model() -> behavioral profile of the enemy
- get_uncertainties() -> low-confidence questions worth scouting
- estimate_engagement(force_a, force_b) -> win ratio and expected losses
- score_targets(region?, posture?) -> ranked targets by the engine target model
- plan_routes(from_region, to_region, movement?, profile?, weights?) -> route and cost
- get_economy_state() -> coalition and per-member cash
- compare_force_packages(against) -> ranked forces by matchup power
- estimate_enemy_response() -> likely enemy reactions
- find_attack_windows() -> enemy regions ranked by lowest threat
- find_special_ops_routes() -> rear insertion targets
- get_mission_status() -> active missions and their phases
- get_force_readiness(force) / get_transport_status() / get_route_status(from_region, to_region)

Before estimating mechanics (combat odds, routes, target value, enemy behavior), call the matching
tool. You may issue several tool calls at once; you then receive the engine's verified results.
After your analysis, reply with ONLY the final plan, a JSON object of the form:
{"posture": "attack|defend|build|turtle", "strategy": "attack|defend|build",
 "attack": {"x": 0, "y": 0}, "feint": {"x": 0, "y": 0}, "counter": {"x": 0, "y": 0},
 "roles": {"playerid": "main|escort|naval|defend"}, "produce": ["unit1", "unit2"],
 "retreat": false, "transport": {"kind": "naval", "to": {"x": 0, "y": 0}},
 "missions": [{"type": "attack|defend|recon|raid|feint|transport|counterattack|specialops",
               "x": 0, "y": 0, "priority": 70}],
 "request_capability": "anti_air|anti_armor|anti_infantry|artillery|naval|recon|transport|base_defense",
 "production_directive": ["unit1", "unit2"],
 "expansion_priority": -1|0|1,
 "modify_missions": ["attack", "defend"],
 "cancel_missions": ["attack"],
 "reserve_fraction": 2,
 "assign_force": [{"force_id": "Multi0", "mission_id": "OP-1"}],
 "release_force": ["Multi1"]}
Do not include markdown, comments, or any other text.

Field reference:
- request_capability: request production of a specific capability counter (e.g. "anti_air" when the
  enemy fields aircraft, "naval" when sea power is needed, "base_defense" for a defensive build-up).
- production_directive: directly specify which unit ids to prioritize (same format as "produce");
  use when you want exact unit control rather than a capability hint.
- expansion_priority: 1 = prioritize expansion (claim new resource fields), -1 = suppress expansion
  (focus on military), 0 = no override.
- modify_missions: mission types to cancel and recreate with updated parameters; the deterministic
  commander recreates them on the next tick with fresh target scoring.
- cancel_missions: mission types to cancel outright (releasing their forces) without recreating them.
- reserve_fraction: override the coalition reserve as 1/N of the army held back; 0 = no override.
- assign_force: commit a player force to a mission: [{"force_id": "Multi0", "mission_id": "OP-1"}].
- release_force: release player forces back to the pool: ["Multi1"].
"""


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
                    if len(PLAN_CACHE) >= PLAN_CACHE_MAX:
                        PLAN_CACHE.pop(next(iter(PLAN_CACHE)))
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


def llm_plan(state: dict, endpoint: str, model: str, api_key: str, vision: bool, tools: bool) -> dict:
    """Asks an OpenAI-compatible model for a team plan and parses its JSON response.

    When the engine tool API is reachable, the model may call tools first (either through the native
    tool_calls protocol or by emitting the tool_calls JSON inside its content). Every call is
    forwarded to the engine, which validates it and returns a computed result; the model then commits
    to the final plan. Tool results are relayed verbatim - the model never sees raw engine internals.
    """
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

    system_prompt = SYSTEM_PROMPT + (
        "\n\nThe engine tool API is available: call tools before estimating mechanics."
        if tools else
        "\n\nThe engine tool API is unavailable; estimate from the state alone."
    )
    messages = [
        {"role": "system", "content": system_prompt},
        {"role": "user", "content": content},
    ]

    for _ in range(MAX_TOOL_ROUNDS + 1):
        reply = chat_completion(endpoint, model, api_key, messages)
        message = reply.get("message", {})
        tool_calls = list(message.get("tool_calls") or [])
        reply_text = message.get("content") or ""

        # Some backends surface tool calls as JSON inside content instead of the native field.
        if not tool_calls:
            tool_calls = tool_calls_from_content(reply_text)
            if tool_calls:
                reply_text = None

        if not tool_calls:
            plan = sanitize_team_plan(parse_plan_content(reply_text), state)
            log_brain(f"PLAN  -> {json.dumps(plan)}")
            return plan

        messages.append({"role": "assistant", "content": reply_text, "tool_calls": tool_calls})
        log_brain(f"TOOL CALLS -> {model}: " + ", ".join(
            tc.get("function", {}).get("name", "?") for tc in tool_calls))

        for tool_call in tool_calls:
            result = execute_tool_call(tool_call, TOOL_ENDPOINT)
            log_brain(f"TOOL RESULT <- {json.dumps(result)[:300]}")
            messages.append({
                "role": "tool",
                "tool_call_id": tool_call.get("id", ""),
                "content": json.dumps(result),
            })

    # The model kept calling tools past the round limit: fall back to the deterministic plan.
    log_brain("Tool rounds exhausted; falling back to the deterministic plan.")
    return empty_team_plan()


def chat_completion(endpoint: str, model: str, api_key: str, messages: list) -> dict:
    """One chat-completion round against an OpenAI-compatible endpoint."""
    payload = {
        "model": model,
        "messages": messages,
        "temperature": 0.1,
        "max_tokens": 200,
    }
    headers = {"Content-Type": "application/json"}
    if api_key:
        headers["Authorization"] = f"Bearer {api_key}"

    req = urllib.request.Request(endpoint, data=json.dumps(payload).encode("utf-8"), headers=headers)
    with urllib.request.urlopen(req, timeout=15) as response:
        data = json.loads(response.read().decode("utf-8"))
    return data["choices"][0]


def tool_calls_from_content(reply_text: str) -> list:
    """Recognizes a tool_calls object embedded in a plain-text reply (non-native function calling)."""
    stripped = reply_text.strip()
    if not stripped.startswith("{"):
        return []
    try:
        parsed = json.loads(stripped)
    except json.JSONDecodeError:
        return []
    calls = parsed.get("tool_calls") if isinstance(parsed, dict) else None
    return list(calls) if isinstance(calls, list) else []


def execute_tool_call(tool_call: dict, endpoint: str) -> dict:
    """Forwards one tool call to the engine's tool API and returns its validated result."""
    function = tool_call.get("function", {}) if isinstance(tool_call, dict) else {}
    name = function.get("name")
    arguments = function.get("arguments") or "{}"
    if isinstance(arguments, str):
        try:
            arguments = json.loads(arguments)
        except json.JSONDecodeError:
            arguments = {}
    if not isinstance(arguments, dict):
        arguments = {}

    request = {"tool": name, "arguments": arguments}
    try:
        req = urllib.request.Request(
            endpoint,
            data=json.dumps(request).encode("utf-8"),
            headers={"Content-Type": "application/json"},
        )
        with urllib.request.urlopen(req, timeout=5) as response:
            return json.loads(response.read().decode("utf-8"))
    except Exception as exc:  # noqa: BLE001 - the model sees an honest failure, not a fabricated result
        return {"ok": False, "error": "TOOL_ENDPOINT_UNREACHABLE", "message": str(exc)}


def probe_tool_endpoint(endpoint: str) -> bool:
    """True when the engine's tool API answers (an ok:false NOT_READY is still a live engine)."""
    try:
        req = urllib.request.Request(
            endpoint,
            data=json.dumps({"tool": "get_global_summary", "arguments": {}}).encode("utf-8"),
            headers={"Content-Type": "application/json"},
        )
        with urllib.request.urlopen(req, timeout=3) as response:
            body = json.loads(response.read().decode("utf-8"))
        return isinstance(body, dict) and "ok" in body
    except Exception:  # noqa: BLE001
        return False


def parse_plan_content(content: str) -> dict:
    """Strips code fences from a model reply and parses the plan JSON."""
    content = content.strip()
    if content.startswith("```"):
        content = content.split("\n", 1)[1].rsplit("```", 1)[0]
    log_brain(f"REPLY <- {json.dumps(content[:500])}")
    return json.loads(content)


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

    def first_of(*keys):
        for key in keys:
            value = plan.get(key)
            if value not in (None, ""):
                return value
        return None

    def strs(value):
        if isinstance(value, list):
            return [v for v in value if isinstance(v, str) and v]
        if isinstance(value, str) and value:
            return [value]
        return []

    cancel_missions = strs(first_of("cancel_missions", "cancelMissions"))
    modify_missions = strs(first_of("modify_missions", "modifyMissions"))
    production_directive = strs(first_of("production_directive", "productionDirective"))
    release_force = strs(first_of("release_force", "releaseForce"))

    request_capability = first_of("request_capability", "requestCapability")
    if not isinstance(request_capability, str):
        request_capability = None

    expansion_priority = first_of("expansion_priority", "expansionPriority")
    expansion_priority = expansion_priority if expansion_priority in (-1, 0, 1) else 0

    reserve_fraction_raw = first_of("reserve_fraction", "reserveFraction")
    reserve_fraction = 0
    if isinstance(reserve_fraction_raw, (int, float)) and not isinstance(reserve_fraction_raw, bool):
        reserve_fraction = int(reserve_fraction_raw)
    elif isinstance(reserve_fraction_raw, str):
        try:
            reserve_fraction = int(reserve_fraction_raw)
        except ValueError:
            reserve_fraction = 0

    assignments = []
    for item in first_of("assign_force", "assignForce") or []:
        if not isinstance(item, dict):
            continue
        force_id = item.get("force_id") or item.get("forceId")
        mission_id = item.get("mission_id") or item.get("missionId")
        if isinstance(force_id, str) and force_id and isinstance(mission_id, str) and mission_id:
            assignments.append({"forceId": force_id, "missionId": mission_id})

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
        "cancelMissions": cancel_missions,
        "reserveFraction": reserve_fraction,
        "requestCapability": request_capability,
        "productionDirective": production_directive,
        "expansionPriority": expansion_priority,
        "modifyMissions": modify_missions,
        "assignForce": assignments,
        "releaseForce": release_force,
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
        "cancelMissions": [],
        "reserveFraction": 0,
        "requestCapability": None,
        "productionDirective": [],
        "expansionPriority": 0,
        "modifyMissions": [],
        "assignForce": [],
        "releaseForce": [],
    }


def rotate_brain_log(path: str, max_bytes: int) -> None:
    """If the log at path exceeds max_bytes, truncate it from the top, keeping the most recent half."""
    if os.path.getsize(path) > max_bytes:
        with open(path, "r", encoding="utf-8") as f:
            lines = f.readlines()
        with open(path, "w", encoding="utf-8") as f:
            f.writelines(lines[len(lines) // 2:])


def log_brain(message: str) -> None:
    """Terminal monitor: append prompt/reply traffic to ai/brain.log and mirror it to stdout."""
    line = f"[{time.strftime('%H:%M:%S')}] {message}"
    print(line, flush=True)
    try:
        # Rotate: if the log has grown past the cap, keep only the most recent half.
        try:
            rotate_brain_log(BRAIN_LOG, BRAIN_LOG_MAX_BYTES)
        except OSError:
            pass
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
        tools = bool(TOOL_ENDPOINT) and probe_tool_endpoint(TOOL_ENDPOINT)
        if tools:
            print(f"Engine tool API reachable at {TOOL_ENDPOINT}; tool calls are enabled.", flush=True)
        else:
            print("Engine tool API unreachable; the commander plans without tools.", flush=True)

        def decide(state):
            return llm_plan(state, MODEL_ENDPOINT, MODEL_NAME, MODEL_API_KEY, args.vision, tools)

        backend_name = f"llm ({MODEL_NAME} @ {MODEL_ENDPOINT})" + (" + vision" if args.vision else "") + (" + tools" if tools else "")
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
