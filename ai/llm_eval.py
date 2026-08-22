#!/usr/bin/env python3
"""LLM strategic evaluation harness for the OpenRA coalition AI.

Replays a game state through multiple commander decisions for comparison: runs the
same seed twice -- once with the deterministic brain only (ENABLE_LLM not set) and
once with the LLM (ENABLE_LLM=1) -- then scores the LLM's plans across ten strategic
criteria and emits a structured JSON report.

Usage (run from the repo root):
  ai/llm_eval.py --map mods/ra/maps/shattered-mountain --ticks 6000 --seed 42
  ai/llm_eval.py --map <uid> --ticks 12000 --seed 100

The script runs the simulation via:
  utility.sh ra --simulate MAP=... BOTS=4 TEAMS=2 TICKS=... SEED=... [ENABLE_LLM=1]

Telemetry is parsed from ai-telemetry.log in the platform support directory
(~/.config/openra/ on Linux, ~/Library/Application Support/OpenRA/ on macOS).
"""

import argparse
import hashlib
import json
import os
import re
import subprocess
import sys

if sys.version_info < (3, 11):
    sys.exit("Python 3.11 or newer is required (found %d.%d)." % (sys.version_info[0], sys.version_info[1]))

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPORT_PATH = os.path.join(REPO, "ai", "llm_eval_report.json")


def replay_same_state(state: dict, count: int, decide) -> dict:
    """Runs an identical immutable snapshot through several commander decisions.

    `decide` is injected so the contract is testable without a model server. Each call receives a fresh
    JSON round-trip copy; commander-side mutation can therefore never contaminate the next replay.
    """
    if count < 2:
        raise ValueError("decision replay count must be at least 2")
    canonical = json.dumps(state, sort_keys=True, separators=(",", ":"))
    decisions = [decide(json.loads(canonical)) for _ in range(count)]
    fingerprints = [hashlib.sha256(
        json.dumps(plan, sort_keys=True, separators=(",", ":")).encode("utf-8")
    ).hexdigest() for plan in decisions]
    return {
        "snapshot_sha256": hashlib.sha256(canonical.encode("utf-8")).hexdigest(),
        "decision_count": count,
        "unique_decisions": len(set(fingerprints)),
        "decision_sha256": fingerprints,
        "decisions": decisions,
    }


def telemetry_log_path() -> str:
    """Returns the platform-specific ai-telemetry.log path."""
    home = os.path.expanduser("~")
    if sys.platform == "darwin":
        return os.path.join(home, "Library", "Application Support", "OpenRA", "ai-telemetry.log")
    return os.path.join(home, ".config", "openra", "ai-telemetry.log")


def read_telemetry(path: str | None = None) -> list[str]:
    """Reads the telemetry log, returning lines (empty list if the file is absent)."""
    path = path or telemetry_log_path()
    if not os.path.exists(path):
        return []
    with open(path, encoding="utf-8", errors="replace") as f:
        return f.read().splitlines()


def run_sim(map_arg: str, ticks: int, seed: int, enable_llm: bool) -> dict:
    """Runs one headless simulation and returns parsed outcome fields."""
    map_arg = os.path.abspath(map_arg)
    llm_flag = "ENABLE_LLM=1" if enable_llm else ""
    cmd = [
        "bash", "-lc",
        f'cd "{REPO}/mods/ra" && PATH="$HOME/.dotnet:$PATH" '
        f'../../utility.sh ra --simulate MAP="{map_arg}" BOTS=4 TEAMS=2 '
        f'TICKS={ticks} SEED={seed} {llm_flag}'.rstrip(),
    ]
    out = subprocess.run(cmd, capture_output=True, text=True, timeout=1800).stdout

    result = {"seed": seed, "enable_llm": enable_llm, "game_over": False,
              "winners": [], "exchange": None, "predicted_win_ratio": None,
              "enemy_destroyed": 0, "friendly_lost": 0}

    m = re.search(r"Finished: (\d+) ticks, (game over|time limit reached), (\d+) actors", out)
    if m:
        result["ticks"] = int(m.group(1))
        result["game_over"] = m.group(2) == "game over"
        result["actors"] = int(m.group(3))

    w = re.search(r"Winners: (.+)", out)
    if w:
        result["winners"] = [x.strip() for x in w.group(1).split(",")]

    exchanges = re.findall(r"exchange [\d.]+ \(enemy (\d+) / friendly (\d+) lost\)", out)
    if exchanges:
        enemy_destroyed, friendly_lost = int(exchanges[-1][0]), int(exchanges[-1][1])
        result["enemy_destroyed"] = enemy_destroyed
        result["friendly_lost"] = friendly_lost
        result["exchange"] = enemy_destroyed / max(1, friendly_lost)

    ratios = re.findall(r"predicted win ratio (\d+\.\d+)", out)
    if ratios:
        result["predicted_win_ratio"] = float(ratios[-1])

    return result


# ---------------------------------------------------------------------------
# Telemetry parsing functions (pure: take lines, return scores)
# ---------------------------------------------------------------------------

def _strip_timestamp(line: str) -> str:
    """Strips the leading '[seconds]' timestamp from a telemetry line."""
    m = re.match(r"\[\d+\.\d+\]\s*(.*)", line)
    return m.group(1) if m else line


def _timestamp_seconds(line: str) -> float | None:
    """Extracts the game-time seconds from the leading '[seconds]' timestamp."""
    m = re.match(r"\[(\d+\.\d+)\]", line)
    return float(m.group(1)) if m else None


def score_legality(lines: list[str]) -> dict:
    """Legality (req 729): fraction of commands not rejected.

    Parses REJECTED_* lines and LLM intent lines. Score = 1 - (rejections / total_commands).
    """
    rejections = 0
    total_commands = 0
    for raw in lines:
        msg = _strip_timestamp(raw)
        if "REJECTED_" in msg:
            rejections += 1
        if msg.startswith("LLM intent applied:"):
            m = re.search(r"missions=(\d+).*produce=(\d+)", msg)
            if m:
                total_commands += int(m.group(1)) + int(m.group(2))
            else:
                total_commands += 1

    # Guard against division by zero: no commands means no rejections, so score is 1.
    score = 1.0 if total_commands == 0 else max(0.0, 1.0 - rejections / max(1, total_commands))
    return {"score": round(score, 4), "rejections": rejections, "total_commands": total_commands}


def score_force_availability(lines: list[str]) -> dict:
    """Force availability (req 730): fraction of missions that got forces assigned.

    Parses 'Order arbiter: REJECTED_CONFLICT' (force already committed) and counts
    total LLM-requested missions. Score = 1 - (conflicts / missions).
    """
    conflicts = 0
    total_missions = 0
    for raw in lines:
        msg = _strip_timestamp(raw)
        if "REJECTED_CONFLICT" in msg and "Order arbiter" in msg:
            conflicts += 1
        if msg.startswith("LLM intent applied:"):
            m = re.search(r"missions=(\d+)", msg)
            if m:
                total_missions += int(m.group(1))

    score = 1.0 if total_missions == 0 else max(0.0, 1.0 - conflicts / max(1, total_missions))
    return {"score": round(score, 4), "force_conflicts": conflicts, "total_missions": total_missions}


def score_mission_completeness(lines: list[str]) -> dict:
    """Mission completeness (req 731): fraction of missions that reached a terminal state.

    Counts unique mission IDs created vs the 'Missions: N concluded' summary line.
    """
    created_ids: set[str] = set()
    concluded = 0
    succeeded = 0
    aborted = 0
    for raw in lines:
        msg = _strip_timestamp(raw)
        # Unique mission IDs from any line mentioning "Mission OP-NNN".
        for m in re.finditer(r"Mission (OP-\d+)", msg):
            created_ids.add(m.group(1))
        # Final summary: "Missions: N concluded (S succeeded, A aborted/failed; ...)"
        m = re.match(r"Missions:\s+(\d+)\s+concluded\s+\((\d+)\s+succeeded,\s+(\d+)\s+aborted/failed", msg)
        if m:
            concluded = int(m.group(1))
            succeeded = int(m.group(2))
            aborted = int(m.group(3))

    created = len(created_ids)
    score = 1.0 if created == 0 else min(1.0, concluded / max(1, created))
    return {"score": round(score, 4), "created": created, "concluded": concluded,
            "succeeded": succeeded, "aborted": aborted}


def score_unnecessary_risk(lines: list[str]) -> dict:
    """Unnecessary risk (req 732): fraction of missions not aborted/outmatched.

    Score = 1 - (aborts / missions). Parses 'abort' and 'outmatched' lines.
    """
    aborts = 0
    total_missions = 0
    for raw in lines:
        msg = _strip_timestamp(raw)
        if "aborted" in msg.lower():
            aborts += 1
        m = re.match(r"Missions:\s+(\d+)\s+concluded", msg)
        if m:
            total_missions = int(m.group(1))

    score = 1.0 if total_missions == 0 else max(0.0, 1.0 - aborts / max(1, total_missions))
    return {"score": round(score, 4), "aborts": aborts, "total_missions": total_missions}


def score_baseline_comparison(det_result: dict, llm_result: dict) -> dict:
    """Comparison with deterministic baseline (req 733).

    Compares win rates and exchange ratios between LLM and deterministic runs.
    """
    det_won = 1.0 if det_result.get("winners") else 0.0
    llm_won = 1.0 if llm_result.get("winners") else 0.0
    det_exchange = det_result.get("exchange") or 0.0
    llm_exchange = llm_result.get("exchange") or 0.0

    # Win-rate comparison: LLM should win at least as often as the baseline.
    win_score = 1.0 if det_won == 0 else min(1.0, llm_won / det_won) if llm_won < det_won else 1.0
    # Exchange comparison: LLM should trade at least as well as the baseline.
    exchange_score = 1.0 if det_exchange == 0 else min(1.0, llm_exchange / det_exchange) if llm_exchange < det_exchange else 1.0

    overall = (win_score + exchange_score) / 2.0
    return {"score": round(overall, 4),
            "det_win_rate": det_won, "llm_win_rate": llm_won,
            "det_exchange": round(det_exchange, 4), "llm_exchange": round(llm_exchange, 4),
            "win_score": round(win_score, 4), "exchange_score": round(exchange_score, 4)}


def score_strategic_oscillation(lines: list[str]) -> dict:
    """Strategic oscillation (req 734): posture changes per minute.

    Counts 'Posture' and 'Strategic posture' changes. More than 3/min = oscillation flag.
    Score = max(0, 1 - (changes_per_minute - 3) / 10) when above threshold, else 1.
    """
    posture_changes: list[float] = []
    last_posture: str | None = None
    last_strategic: str | None = None
    for raw in lines:
        ts = _timestamp_seconds(raw)
        msg = _strip_timestamp(raw)
        # "Posture X; coalition ..." — tactical posture changes.
        m = re.match(r"Posture\s+(\w+);", msg)
        if m:
            posture = m.group(1)
            if posture != last_posture:
                last_posture = posture
                if ts is not None:
                    posture_changes.append(ts)
            continue
        # "Strategic posture: X" — strategic posture changes.
        m = re.match(r"Strategic posture:\s+(\w+)", msg)
        if m:
            posture = m.group(1)
            if posture != last_strategic:
                last_strategic = posture
                if ts is not None:
                    posture_changes.append(ts)

    if len(posture_changes) < 2:
        return {"score": 1.0, "changes": len(posture_changes), "changes_per_minute": 0.0,
                "oscillating": False}

    duration_seconds = posture_changes[-1] - posture_changes[0]
    duration_minutes = max(0.001, duration_seconds / 60.0)
    changes_per_minute = len(posture_changes) / duration_minutes
    oscillating = changes_per_minute > 3.0

    # Degrade score proportional to how far above the 3/min threshold we are.
    if changes_per_minute <= 3.0:
        score = 1.0
    else:
        score = max(0.0, 1.0 - (changes_per_minute - 3.0) / 10.0)

    return {"score": round(score, 4), "changes": len(posture_changes),
            "changes_per_minute": round(changes_per_minute, 4), "oscillating": oscillating}


def score_repeated_impossible(lines: list[str]) -> dict:
    """Repeated impossible commands (req 735): duplicate REJECTED_* for the same reason.

    Counts duplicate rejection reasons. Score = 1 - (duplicates / total_rejections).
    """
    reasons: list[str] = []
    for raw in lines:
        msg = _strip_timestamp(raw)
        m = re.search(r"(REJECTED_\w+)", msg)
        if m:
            reasons.append(m.group(1))

    total = len(reasons)
    if total == 0:
        return {"score": 1.0, "total_rejections": 0, "duplicates": 0}

    reason_counts: dict[str, int] = {}
    for r in reasons:
        reason_counts[r] = reason_counts.get(r, 0) + 1
    duplicates = sum(c - 1 for c in reason_counts.values() if c > 1)
    score = max(0.0, 1.0 - duplicates / max(1, total))
    return {"score": round(score, 4), "total_rejections": total,
            "duplicates": duplicates, "reason_counts": reason_counts}


def score_uncertain_intelligence(lines: list[str]) -> dict:
    """Misuse of uncertain intelligence (req 736): missions created against SUSPECTED intel.

    Parses 'SUSPECTED' in mission target context. Score = 1 - (suspect_missions / total_missions).
    """
    suspect_missions = 0
    total_missions = 0
    for raw in lines:
        msg = _strip_timestamp(raw)
        if msg.startswith("LLM intent applied:"):
            m = re.search(r"missions=(\d+)", msg)
            if m:
                total_missions += int(m.group(1))
        # A mission line that references SUSPECTED intel in its target context.
        if "SUSPECTED" in msg.upper() and ("mission" in msg.lower() or "target" in msg.lower()
                                           or "OP-" in msg):
            suspect_missions += 1

    score = 1.0 if total_missions == 0 else max(0.0, 1.0 - suspect_missions / max(1, total_missions))
    return {"score": round(score, 4), "suspect_missions": suspect_missions,
            "total_missions": total_missions}


def score_reserves(lines: list[str]) -> dict:
    """Failing to maintain reserves (req 737): reserve was ever 0 when not committed.

    Parses 'reserve' lines. A reserve fraction of 1/1 (100%) or explicit 0 is a failure.
    """
    reserve_zero_count = 0
    reserve_lines = 0
    for raw in lines:
        msg = _strip_timestamp(raw)
        if "reserve" not in msg.lower():
            continue
        reserve_lines += 1
        # "Reserve fraction overridden by LLM: 1/N" — N=1 means 100% committed (no reserve).
        m = re.search(r"Reserve fraction overridden by LLM:\s+1/(\d+)", msg)
        if m and int(m.group(1)) <= 1:
            reserve_zero_count += 1
        # Any explicit "reserve 0" or "no reserve" mention.
        if re.search(r"reserve.*\b0\b|no reserve|reserve.*depleted", msg, re.IGNORECASE):
            reserve_zero_count += 1

    score = 1.0 if reserve_lines == 0 else max(0.0, 1.0 - reserve_zero_count / max(1, reserve_lines))
    return {"score": round(score, 4), "reserve_zero_count": reserve_zero_count,
            "reserve_lines": reserve_lines}


def score_idle_forces(lines: list[str]) -> dict:
    """Excessive idle forces (req 738): average idle fraction from match metrics.

    Parses 'avg idle N%' from match metrics lines. Flag if > 50% average idle.
    Score = max(0, 1 - (idle_fraction - 0.5) / 0.5) when above 50%, else 1.
    """
    idle_fractions: list[float] = []
    for raw in lines:
        msg = _strip_timestamp(raw)
        m = re.search(r"avg (?:army )?idle (\d+)%", msg)
        if m:
            idle_fractions.append(int(m.group(1)) / 100.0)

    if not idle_fractions:
        return {"score": 1.0, "avg_idle": None, "flagged": False}

    avg_idle = sum(idle_fractions) / len(idle_fractions)
    flagged = avg_idle > 0.5
    if avg_idle <= 0.5:
        score = 1.0
    else:
        score = max(0.0, 1.0 - (avg_idle - 0.5) / 0.5)

    return {"score": round(score, 4), "avg_idle": round(avg_idle, 4), "flagged": flagged}


# ---------------------------------------------------------------------------
# Top-level evaluation
# ---------------------------------------------------------------------------

def evaluate(lines: list[str], det_result: dict | None = None,
             llm_result: dict | None = None) -> dict:
    """Runs all ten scoring functions and returns the combined report."""
    report: dict = {}

    report["legality"] = score_legality(lines)
    report["force_availability"] = score_force_availability(lines)
    report["mission_completeness"] = score_mission_completeness(lines)
    report["unnecessary_risk"] = score_unnecessary_risk(lines)

    if det_result is not None and llm_result is not None:
        report["baseline_comparison"] = score_baseline_comparison(det_result, llm_result)
    else:
        report["baseline_comparison"] = {"score": 1.0, "note": "baseline run not available"}

    report["strategic_oscillation"] = score_strategic_oscillation(lines)
    report["repeated_impossible"] = score_repeated_impossible(lines)
    report["uncertain_intelligence"] = score_uncertain_intelligence(lines)
    report["reserves"] = score_reserves(lines)
    report["idle_forces"] = score_idle_forces(lines)

    # Overall score: mean of all sub-scores.
    scores = [v["score"] for v in report.values() if isinstance(v, dict) and "score" in v]
    report["overall"] = round(sum(scores) / len(scores), 4) if scores else 0.0

    return report


def main() -> None:
    parser = argparse.ArgumentParser(description="LLM strategic evaluation harness")
    parser.add_argument("--map", default="mods/ra/maps/shattered-mountain",
                        help="map path or uid (default: mods/ra/maps/shattered-mountain)")
    parser.add_argument("--ticks", type=int, default=6000, help="simulation tick budget")
    parser.add_argument("--seed", type=int, default=42, help="deterministic seed")
    parser.add_argument("--no-sim", action="store_true",
                        help="skip simulation; parse the existing ai-telemetry.log only")
    parser.add_argument("--snapshot", help="JSON world snapshot to replay through repeated live LLM decisions")
    parser.add_argument("--decision-replays", type=int, default=3,
                        help="number of decisions for --snapshot (minimum 2, default 3)")
    args = parser.parse_args()

    det_result: dict | None = None
    llm_result: dict | None = None

    repeat_state = None
    if args.snapshot:
        from model_server import MODEL_API_KEY, MODEL_ENDPOINT, MODEL_NAME, TOOL_ENDPOINT, llm_plan

        with open(args.snapshot, encoding="utf-8") as snapshot_file:
            snapshot = json.load(snapshot_file)
        repeat_state = replay_same_state(snapshot, args.decision_replays,
            lambda state: llm_plan(state, MODEL_ENDPOINT, MODEL_NAME, MODEL_API_KEY,
                                   vision=False, tools=bool(TOOL_ENDPOINT)))
        lines = read_telemetry()
    elif not args.no_sim:
        # Clear the telemetry log so only this run's lines are captured.
        log_path = telemetry_log_path()
        if os.path.exists(log_path):
            os.remove(log_path)

        # Run deterministic baseline (ENABLE_LLM not set).
        print(f"Running deterministic baseline (seed {args.seed})...", file=sys.stderr)
        det_result = run_sim(args.map, args.ticks, args.seed, enable_llm=False)

        # Capture deterministic telemetry before the LLM run appends to the same file.
        det_lines = read_telemetry()

        # Clear again so the LLM run's telemetry is isolated.
        if os.path.exists(log_path):
            os.remove(log_path)

        # Run with LLM enabled.
        print(f"Running LLM commander (seed {args.seed})...", file=sys.stderr)
        llm_result = run_sim(args.map, args.ticks, args.seed, enable_llm=True)

        # Parse the LLM run's telemetry.
        lines = read_telemetry()
    else:
        lines = read_telemetry()

    report = evaluate(lines, det_result, llm_result)
    if repeat_state is not None:
        report["repeat_state"] = repeat_state

    # Attach run metadata.
    report["metadata"] = {
        "map": args.map,
        "ticks": args.ticks,
        "seed": args.seed,
        "deterministic_result": det_result,
        "llm_result": llm_result,
        "telemetry_lines": len(lines),
        "snapshot": args.snapshot,
        "decision_replays": args.decision_replays if args.snapshot else None,
    }

    # Output JSON to stdout and save to file.
    output = json.dumps(report, indent=2, sort_keys=True)
    print(output)
    with open(REPORT_PATH, "w", encoding="utf-8") as f:
        f.write(output)
    print(f"\nReport saved to {REPORT_PATH}", file=sys.stderr)


if __name__ == "__main__":
    main()
