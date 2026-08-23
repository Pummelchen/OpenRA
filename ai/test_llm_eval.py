#!/usr/bin/env python3
"""Tests for ai/llm_eval.py — the shipped LLM strategic-evaluation scorers.

Covers audit requirements 729-738. These exercise the real module rather than a
re-implementation, so a regression in llm_eval.py fails here instead of passing
against a parallel copy of the same algorithm.

Run directly (`python3 ai/test_llm_eval.py`) or via `ai/selfcheck.py`.
Requires Python 3.11+, like the rest of the AI tooling.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import llm_eval  # noqa: E402


FAILURES: list[str] = []


def check(label: str, actual, expected) -> None:
    if actual != expected:
        FAILURES.append(f"{label}: expected {expected!r}, got {actual!r}")


def check_near(label: str, actual, expected, tolerance: float = 1e-4) -> None:
    if actual is None or abs(actual - expected) > tolerance:
        FAILURES.append(f"{label}: expected ~{expected!r}, got {actual!r}")


# --- req 729: legality -----------------------------------------------------

def test_legality():
    clean = ["[1.0] LLM intent applied: missions=2 produce=1"]
    check("legality/clean", llm_eval.score_legality(clean)["score"], 1.0)

    rejected = [
        "[1.0] LLM intent applied: missions=2 produce=2",
        "[1.1] REJECTED_UNKNOWN_TYPE: unknown mission type \"blitz\"",
    ]
    result = llm_eval.score_legality(rejected)
    check("legality/rejections", result["rejections"], 1)
    check("legality/commands", result["total_commands"], 4)
    check_near("legality/score", result["score"], 0.75)

    # No commands at all is not a failure: nothing illegal was requested.
    check("legality/empty", llm_eval.score_legality([])["score"], 1.0)

    # Score is floored at 0 rather than going negative when everything is rejected.
    flood = ["[1.0] LLM intent applied: missions=1 produce=0"] + \
            [f"[1.{i}] REJECTED_CONFLICT: dup" for i in range(5)]
    if llm_eval.score_legality(flood)["score"] < 0.0:
        FAILURES.append("legality/floor: score went negative")


# --- req 730: force availability -------------------------------------------

def test_force_availability():
    lines = [
        "[1.0] LLM intent applied: missions=4 produce=0",
        "[1.1] Order arbiter: REJECTED_CONFLICT force already committed",
    ]
    result = llm_eval.score_force_availability(lines)
    check("force/conflicts", result["force_conflicts"], 1)
    check("force/missions", result["total_missions"], 4)
    check_near("force/score", result["score"], 0.75)

    # A rejection that is not an arbiter conflict must not count as a force clash.
    other = [
        "[1.0] LLM intent applied: missions=4 produce=0",
        "[1.1] REJECTED_OUT_OF_BOUNDS: target (999,999) outside map",
    ]
    check("force/other-rejection", llm_eval.score_force_availability(other)["force_conflicts"], 0)
    check("force/no-missions", llm_eval.score_force_availability([])["score"], 1.0)


# --- req 731: mission completeness -----------------------------------------

def test_mission_completeness():
    lines = [
        "[1.0] Mission OP-001 created",
        "[2.0] Mission OP-002 created",
        "[3.0] Mission OP-001 executing",
        "[9.0] Missions: 2 concluded (1 succeeded, 1 aborted/failed; success 50%)",
    ]
    result = llm_eval.score_mission_completeness(lines)
    check("completeness/created", result["created"], 2)
    check("completeness/concluded", result["concluded"], 2)
    check("completeness/succeeded", result["succeeded"], 1)
    check("completeness/aborted", result["aborted"], 1)
    check("completeness/score", result["score"], 1.0)

    # Repeating a mission id must not inflate the created count.
    repeated = ["[1.0] Mission OP-001 created", "[2.0] Mission OP-001 phase breach",
                "[9.0] Missions: 1 concluded (1 succeeded, 0 aborted/failed"]
    check("completeness/unique-ids", llm_eval.score_mission_completeness(repeated)["created"], 1)

    # Missions created but never concluded score below 1.
    dangling = ["[1.0] Mission OP-001 created", "[2.0] Mission OP-002 created",
                "[9.0] Missions: 1 concluded (1 succeeded, 0 aborted/failed"]
    check_near("completeness/dangling", llm_eval.score_mission_completeness(dangling)["score"], 0.5)

    check("completeness/empty", llm_eval.score_mission_completeness([])["score"], 1.0)


# --- req 732: unnecessary risk ---------------------------------------------

def test_unnecessary_risk():
    lines = [
        "[1.0] Mission OP-001 aborted: outmatched",
        "[9.0] Missions: 4 concluded (3 succeeded, 1 aborted/failed",
    ]
    result = llm_eval.score_unnecessary_risk(lines)
    check("risk/missions", result["total_missions"], 4)
    if result["score"] >= 1.0:
        FAILURES.append("risk/score: an abort must reduce the score")

    check("risk/empty", llm_eval.score_unnecessary_risk([])["score"], 1.0)


# --- req 733: baseline comparison ------------------------------------------

def test_baseline_comparison():
    det = {"winners": ["Bot 1"], "exchange": 1.0}
    better = {"winners": ["Bot 1"], "exchange": 2.0}
    worse = {"winners": [], "exchange": 0.5}

    check("baseline/better", llm_eval.score_baseline_comparison(det, better)["score"], 1.0)

    lost = llm_eval.score_baseline_comparison(det, worse)
    if lost["score"] >= 1.0:
        FAILURES.append("baseline/worse: losing to the baseline must not score perfectly")
    check("baseline/llm-win-rate", lost["llm_win_rate"], 0.0)
    check("baseline/det-win-rate", lost["det_win_rate"], 1.0)
    check_near("baseline/exchange", lost["llm_exchange"], 0.5)

    # A baseline that itself won nothing cannot be undercut on win rate.
    check("baseline/no-baseline-win",
          llm_eval.score_baseline_comparison({"winners": [], "exchange": 0}, worse)["score"], 1.0)


# --- req 734: strategic oscillation ----------------------------------------

def test_oscillation():
    stable = ["[0.0] Posture attack; ratio 1.0", "[60.0] Posture attack; ratio 1.1"]
    result = llm_eval.score_strategic_oscillation(stable)
    check("oscillation/stable", result["score"], 1.0)
    check("oscillation/stable-flag", result["oscillating"], False)

    # Six posture flips inside ten seconds is 36/min, far above the 3/min threshold.
    flapping = []
    for i in range(6):
        flapping.append(f"[{i * 2}.0] Posture {'attack' if i % 2 == 0 else 'defend'}; ratio 1.0")
    flapped = llm_eval.score_strategic_oscillation(flapping)
    check("oscillation/flagged", flapped["oscillating"], True)
    if flapped["score"] >= 1.0:
        FAILURES.append("oscillation/score: flapping must reduce the score")

    # A single change has no rate to measure and is not oscillation.
    check("oscillation/single", llm_eval.score_strategic_oscillation(
        ["[0.0] Posture attack; ratio 1.0"])["score"], 1.0)


# --- req 735: repeated impossible commands ---------------------------------

def test_repeated_impossible():
    check("impossible/none", llm_eval.score_repeated_impossible([])["score"], 1.0)

    # Distinct reasons are learning; the same reason repeatedly is not.
    distinct = ["[1.0] REJECTED_UNKNOWN_TYPE: x", "[2.0] REJECTED_CONFLICT: y"]
    check("impossible/distinct", llm_eval.score_repeated_impossible(distinct)["duplicates"], 0)
    check("impossible/distinct-score", llm_eval.score_repeated_impossible(distinct)["score"], 1.0)

    repeated = [f"[{i}.0] REJECTED_UNKNOWN_TYPE: blitz" for i in range(4)]
    result = llm_eval.score_repeated_impossible(repeated)
    check("impossible/duplicates", result["duplicates"], 3)
    check_near("impossible/score", result["score"], 0.25)


# --- req 736: misuse of uncertain intelligence -----------------------------

def test_uncertain_intelligence():
    lines = [
        "[1.0] LLM intent applied: missions=4 produce=0",
        "[1.1] Mission OP-001 target SUSPECTED enemy position",
    ]
    result = llm_eval.score_uncertain_intelligence(lines)
    check("uncertain/suspect", result["suspect_missions"], 1)
    check_near("uncertain/score", result["score"], 0.75)

    # Acting only on observed intel is the correct behaviour and scores clean.
    observed = ["[1.0] LLM intent applied: missions=4 produce=0",
                "[1.1] Mission OP-001 target OBSERVED enemy position"]
    check("uncertain/observed", llm_eval.score_uncertain_intelligence(observed)["score"], 1.0)

    check("uncertain/empty", llm_eval.score_uncertain_intelligence([])["score"], 1.0)


# --- req 737: failing to maintain reserves ---------------------------------

def test_reserves():
    check("reserves/none", llm_eval.score_reserves([])["score"], 1.0)

    held = ["[1.0] Reserve fraction overridden by LLM: 1/4"]
    check("reserves/held", llm_eval.score_reserves(held)["score"], 1.0)

    spent = ["[1.0] Reserve fraction overridden by LLM: 1/1"]
    result = llm_eval.score_reserves(spent)
    if result["reserve_zero_count"] == 0:
        FAILURES.append("reserves/spent: committing the whole army must be flagged")
    if result["score"] >= 1.0:
        FAILURES.append("reserves/spent-score: no reserve must reduce the score")


# --- req 738: excessive idle forces ----------------------------------------

def test_idle_forces():
    low = ["[1.0] Match metrics: avg army idle 10%, cohesion 0.9"]
    result = llm_eval.score_idle_forces(low)
    check("idle/low-score", result["score"], 1.0)
    check("idle/low-flag", result["flagged"], False)
    check_near("idle/low-avg", result["avg_idle"], 0.10)

    high = ["[1.0] Match metrics: avg army idle 90%, cohesion 0.9"]
    high_result = llm_eval.score_idle_forces(high)
    check("idle/high-flag", high_result["flagged"], True)
    check_near("idle/high-score", high_result["score"], 0.2)

    # Exactly at the threshold is not yet a failure.
    boundary = ["[1.0] Match metrics: avg army idle 50%, cohesion 0.9"]
    check("idle/boundary", llm_eval.score_idle_forces(boundary)["flagged"], False)

    # No metrics is unknown, reported as a clean score rather than a fabricated one.
    check("idle/none", llm_eval.score_idle_forces([])["avg_idle"], None)


# --- req 728: same-state replay --------------------------------------------

def test_replay_same_state():
    state = {"tick": 100, "cash": 5000}
    calls: list[dict] = []

    def decide(snapshot):
        calls.append(snapshot)
        return {"posture": "attack", "n": len(calls)}

    report = llm_eval.replay_same_state(state, 3, decide)
    check("replay/count", len(calls), 3)
    check("replay/identical-input", all(c == state for c in calls), True)
    if "snapshot_sha256" not in report:
        FAILURES.append("replay/hash: the snapshot hash must be preserved")
    check("replay/decision-count", report.get("decision_count"), 3)
    if len(report.get("decision_sha256", [])) != 3:
        FAILURES.append("replay/fingerprints: one hash per replayed decision is required")
    if report.get("unique_decisions", 0) < 1:
        FAILURES.append("replay/unique: distinct plans must be counted")


# --- aggregate --------------------------------------------------------------

def test_evaluate_aggregates_every_scorer():
    lines = [
        "[1.0] LLM intent applied: missions=2 produce=1",
        "[2.0] Posture attack; ratio 1.0",
        "[3.0] Match metrics: avg army idle 20%, cohesion 0.9",
        "[9.0] Missions: 2 concluded (2 succeeded, 0 aborted/failed",
    ]
    report = llm_eval.evaluate(lines)
    if not isinstance(report, dict) or not report:
        FAILURES.append("evaluate: expected a non-empty report")
        return

    # Every requirement 729-738 must be represented in the aggregate report, or a
    # scorer could silently drop out of the evaluation without any test noticing.
    text = repr(report)
    for token in ("legality", "oscillation", "idle"):
        if token not in text:
            FAILURES.append(f"evaluate: report is missing the {token} scorer")


def main() -> int:
    tests = [v for k, v in sorted(globals().items()) if k.startswith("test_") and callable(v)]
    for test in tests:
        try:
            test()
        except Exception as exc:  # noqa: BLE001 - report, do not abort the batch
            FAILURES.append(f"{test.__name__} raised {type(exc).__name__}: {exc}")

    if FAILURES:
        print(f"llm_eval tests FAILED ({len(FAILURES)}):")
        for failure in FAILURES:
            print(f"  - {failure}")
        return 1

    print(f"llm_eval tests OK ({len(tests)} groups, all 11 scorers covered)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
