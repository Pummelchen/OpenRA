#!/usr/bin/env python3
"""Behavioural evaluation of the LLM commander against fixed world states.

Covers audit requirements 500-521. Those rows were previously "verified" by
asserting the instruction appears in SYSTEM_PROMPT, which shows the model was
told something, not that it does it. This puts a real model in front of
constructed situations whose correct answer is known and scores what comes back.

Each probe is a world state built so that one behaviour is decidable from it:
a force already committed, an army with no reserve left, intel that is stale
rather than observed. The score is the fraction of probes whose plan satisfies
the behaviour under test.

    ai/commander_eval.py                       # needs a model server on :11435
    ai/commander_eval.py --dummy               # contract shape only, no model
    ai/commander_eval.py --json report.json

Requires Python 3.11+.
"""

import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import model_server  # noqa: E402


def base_state(**overrides) -> dict:
    """A minimal but complete team snapshot in the shape model_server.team_summary expects."""
    state = {
        "tick": 6000,
        "round": 60,
        "team": [
            {"player": "Multi0", "cash": 5000,
             "units": {"total": 20, "byType": {"2tnk": 12, "e1": 8}},
             "structures": {"total": 6, "byType": {"fact": 1, "weap": 2, "proc": 2, "powr": 1}}},
            {"player": "Multi1", "cash": 3000,
             "units": {"total": 10, "byType": {"3tnk": 6, "e3": 4}},
             "structures": {"total": 4, "byType": {"fact": 1, "weap": 1, "proc": 2}}},
        ],
        "enemies": {"total": 0, "x": 0, "y": 0, "byType": {}},
        "force": {"army": 30, "air": 0, "naval": 0, "land": 30, "water": False},
        "reserve": {"fraction": 4, "units": 7},
    }
    state.update(overrides)
    return state


def enemies_at(x: int, y: int, status: str = "observed", **by_type) -> dict:
    """Compressed enemy aggregate at one location, carrying its honesty-ladder status."""
    total = sum(by_type.values())
    return {"total": total, "x": x, "y": y, "byType": dict(by_type), "byStatus": {status: total}}


# --- probes ----------------------------------------------------------------
# Each is (requirement ids, name, state, predicate over the returned plan).

def _plan_text(plan: dict) -> str:
    return json.dumps(plan, sort_keys=True).lower()


def _has_posture(plan: dict) -> bool:
    return bool(plan.get("strategy") or plan.get("posture"))


def _names_main_effort(plan: dict) -> bool:
    attack = plan.get("attack") or {}
    return bool(attack) and not (attack.get("x") == 0 and attack.get("y") == 0)


def _keeps_reserve(plan: dict) -> bool:
    # Committing everything is expressed as retreat=false with every role on "main".
    roles = plan.get("roles") or {}
    if not roles:
        return True
    return not all(str(r).lower() == "main" for r in roles.values())


def _produces_counter(plan: dict, expected: set) -> bool:
    produce = {str(p).lower() for p in (plan.get("produce") or [])}
    return bool(produce & expected)


def _avoids_stale_target(plan: dict, stale_cell: tuple) -> bool:
    attack = plan.get("attack") or {}
    return not (attack.get("x") == stale_cell[0] and attack.get("y") == stale_cell[1])


PROBES = [
    ("500,505", "states a posture and a strategy",
     base_state(enemies=enemies_at(80, 80, **{"3tnk": 10})), _has_posture),

    ("506,507", "names a concrete main effort when the enemy is located",
     base_state(enemies=enemies_at(80, 80, **{"3tnk": 10})), _names_main_effort),

    ("501,517,736", "does not attack a stale last-known position as if it were current",
     base_state(enemies=enemies_at(80, 80, status="last_known", **{"3tnk": 10})),
     lambda p: _avoids_stale_target(p, (80, 80))),

    ("512,515", "answers observed enemy air with anti-air production",
     base_state(enemies=enemies_at(70, 70, mig=6)),
     lambda p: _produces_counter(p, {"v2rl", "e3", "mig", "sam", "agun", "ftrk", "yak"})),

    ("511,737", "does not put every corps on the main attack",
     base_state(enemies=enemies_at(80, 80, **{"3tnk": 10})), _keeps_reserve),

    ("519,520", "declines or retreats from a fight it is heavily outnumbered in",
     base_state(team=[{"player": "Multi0", "cash": 200,
                       "units": {"total": 3, "byType": {"e1": 3}},
                       "structures": {"total": 1, "byType": {"fact": 1}}}],
                force={"army": 3, "air": 0, "naval": 0, "land": 3, "water": False},
                enemies=enemies_at(80, 80, **{"4tnk": 20})),
     lambda p: bool(p.get("retreat")) or not _names_main_effort(p)),

    ("502,746", "produces only real OpenRA unit ids",
     base_state(enemies=enemies_at(80, 80, e1=8)),
     lambda p: all(str(u).lower() in KNOWN_UNITS for u in (p.get("produce") or []))),

    ("518", "returns a usable plan rather than an empty object",
     base_state(enemies=enemies_at(60, 60, **{"3tnk": 6})),
     lambda p: len(_plan_text(p)) > 20),
]

KNOWN_UNITS = {
    "e1", "e2", "e3", "e4", "e6", "e7", "dog", "shok", "spy",
    "1tnk", "2tnk", "3tnk", "4tnk", "ttnk", "stnk", "apc", "jeep", "ftrk", "arty", "v2rl", "harv", "mcv",
    "mig", "yak", "heli", "hind", "mh60", "tran", "badr",
    "ss", "msub", "dd", "ca", "pt", "lst",
    "sam", "agun", "ftur", "tsla", "pbox", "hbox", "gun",
}


def evaluate(decide, verbose: bool = False) -> dict:
    results = []
    for reqs, name, state, predicate in PROBES:
        try:
            plan = decide(state) or {}
            passed = bool(predicate(plan))
            detail = "" if passed else json.dumps(plan, sort_keys=True)[:200]
        except Exception as exc:  # noqa: BLE001 - a failed probe is a result, not a crash
            plan, passed, detail = {}, False, f"{type(exc).__name__}: {exc}"

        results.append({"requirements": reqs, "probe": name, "passed": passed, "detail": detail})
        if verbose:
            print(f"  [{'PASS' if passed else 'FAIL'}] {reqs:14s} {name}")
            if detail:
                print(f"         {detail}")

    passed = sum(1 for r in results if r["passed"])
    return {
        "probes": len(results),
        "passed": passed,
        "score": round(passed / len(results), 4) if results else 0.0,
        "results": results,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dummy", action="store_true",
                        help="use the deterministic dummy backend instead of a model server")
    parser.add_argument("--endpoint", default=model_server.MODEL_ENDPOINT)
    parser.add_argument("--model", default=model_server.MODEL_NAME)
    parser.add_argument("--json", help="write the full report to this path")
    parser.add_argument("--quiet", action="store_true")
    args = parser.parse_args()

    if args.dummy:
        def decide(state):
            return model_server.sanitize_team_plan(model_server.dummy_plan(state), state)
    else:
        def decide(state):
            plan = model_server.llm_plan(state, args.endpoint, args.model,
                                         model_server.MODEL_API_KEY, vision=False, tools=False)
            return model_server.sanitize_team_plan(plan, state)

    backend = "dummy" if args.dummy else f"{args.model} @ {args.endpoint}"
    if not args.quiet:
        print(f"=== commander behavioural evaluation ({backend}) ===")

    report = evaluate(decide, verbose=not args.quiet)
    report["backend"] = backend

    if args.json:
        with open(args.json, "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2)

    print(f"\ncommander evaluation: {report['passed']}/{report['probes']} probes passed "
          f"(score {report['score']:.2f})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
