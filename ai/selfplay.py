#!/usr/bin/env python3
"""Self-play batch evaluation for the OpenRA AI mod.

Runs headless skirmishes via the --simulate utility command across seeds
(and optionally parameter configurations) and aggregates the outcomes:
winners, game-overs, and the AI event counts from the match telemetry.

Usage (run from the repo root):
  ai/selfplay.py --map mods/ra/maps/shattered-mountain --bots 4 --teams 2 --ticks 6000 --runs 4
  ai/selfplay.py --map <uid> --runs 6 --seed-base 100   # seeds 100..105
  ai/selfplay.py --sweep-reserve 4,6,8 --runs 3          # reserve fraction grid
"""

from __future__ import annotations

import argparse
import os
import re
import statistics
import subprocess
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AI_YAML = os.path.join(REPO, "mods", "ra", "rules", "ai.yaml")


def run_sim(map_arg: str, bots: int, teams: int, ticks: int, seed: int) -> dict:
    map_arg = os.path.abspath(map_arg)
    cmd = [
        "bash", "-lc",
        f'cd "{REPO}/mods/ra" && PATH="$HOME/.dotnet:$PATH" '
        f'../../utility.sh ra --simulate MAP="{map_arg}" BOTS={bots} TEAMS={teams} TICKS={ticks} SEED={seed}',
    ]
    out = subprocess.run(cmd, capture_output=True, text=True, timeout=1200).stdout

    result = {"seed": seed, "game_over": False, "winners": [], "events": {}}
    m = re.search(r"Finished: (\d+) ticks, (game over|time limit reached), (\d+) actors", out)
    if m:
        result["ticks"] = int(m.group(1))
        result["game_over"] = m.group(2) == "game over"
        result["actors"] = int(m.group(3))
    w = re.search(r"Winners: (.+)", out)
    if w:
        result["winners"] = [x.strip() for x in w.group(1).split(",")]

    in_events = False
    for line in out.splitlines():
        if "Match telemetry:" in line:
            in_events = True
            continue
        if in_events and re.match(r"\s+[a-z_]+\s+\d+", line):
            key, value = line.split()
            result["events"][key] = int(value)

    return result


def set_reserve(fraction: int) -> None:
    """Patches ReserveFraction in ai.yaml (restored on exit)."""
    with open(AI_YAML, encoding="utf-8") as f:
        content = f.read()
    content = re.sub(r"(ReserveFraction:\s*)\d+", rf"\g<1>{fraction}", content)
    with open(AI_YAML, "w", encoding="utf-8") as f:
        f.write(content)


def summarize(label: str, results: list) -> None:
    wins = sum(1 for r in results if r["winners"])
    over = sum(1 for r in results if r["game_over"])
    events = {}
    for r in results:
        for k, v in r["events"].items():
            events[k] = events.get(k, 0) + v

    print(f"\n=== {label}: {len(results)} runs ===")
    print(f"wins: {wins}, game overs: {over}, avg actors: {statistics.mean(r.get('actors', 0) for r in results):.0f}")
    if events:
        print("total events: " + ", ".join(f"{k}={v}" for k, v in sorted(events.items(), key=lambda x: -x[1])))


def main() -> None:
    parser = argparse.ArgumentParser(description="Headless self-play evaluation")
    parser.add_argument("--map", default="mods/ra/maps/shattered-mountain")
    parser.add_argument("--bots", type=int, default=4)
    parser.add_argument("--teams", type=int, default=2)
    parser.add_argument("--ticks", type=int, default=6000)
    parser.add_argument("--runs", type=int, default=4)
    parser.add_argument("--seed-base", type=int, default=1000)
    parser.add_argument("--sweep-reserve", help="comma-separated reserve fractions to compare, e.g. 4,6,8")
    args = parser.parse_args()

    if args.sweep_reserve:
        original = open(AI_YAML, encoding="utf-8").read()
        try:
            for fraction in [int(x) for x in args.sweep_reserve.split(",")]:
                set_reserve(fraction)
                results = [run_sim(args.map, args.bots, args.teams, args.ticks, args.seed_base + i)
                           for i in range(args.runs)]
                summarize(f"reserve 1/{fraction}", results)
        finally:
            with open(AI_YAML, "w", encoding="utf-8") as f:
                f.write(original)
            print("\n(ai.yaml restored)")
        return

    results = [run_sim(args.map, args.bots, args.teams, args.ticks, args.seed_base + i)
               for i in range(args.runs)]
    summarize("self-play", results)


if __name__ == "__main__":
    main()
