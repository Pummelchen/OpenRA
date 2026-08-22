#!/usr/bin/env python3
"""Self-check for the ai/ scripts.

Compiles every Python module and regression-tests brain.log rotation so the
"fixed but unregressed" hardenings stay covered without a model endpoint or the
game engine. Dependency-free (Python standard library only).

Run:
  python3 ai/selfcheck.py
Exit status is 0 on success and non-zero on any failure.
"""

import os
import py_compile
import sys
import tempfile

AI_DIR = os.path.dirname(os.path.abspath(__file__))
SCRIPTS = ("model_server.py", "selfplay.py", "llm_eval.py")


def compile_all():
    for name in SCRIPTS:
        py_compile.compile(os.path.join(AI_DIR, name), doraise=True)
    print("py_compile OK: %s" % ", ".join(SCRIPTS))


def rotation_regression():
    sys.path.insert(0, AI_DIR)
    from model_server import BRAIN_LOG_MAX_BYTES, rotate_brain_log

    with tempfile.TemporaryDirectory() as tmp:
        log = os.path.join(tmp, "brain.log")

        # Under the cap: rotation is a no-op.
        under = "keep me\n" * 3
        with open(log, "w", encoding="utf-8") as f:
            f.write(under)
        rotate_brain_log(log, BRAIN_LOG_MAX_BYTES)
        with open(log, "r", encoding="utf-8") as f:
            assert f.read() == under, "under-cap rotation must be a no-op"

        # Over the cap: keep only the most recent half.
        cap = 100
        lines = ["line %02d\n" % i for i in range(40)]
        with open(log, "w", encoding="utf-8") as f:
            f.writelines(lines)
        rotate_brain_log(log, cap)
        with open(log, "r", encoding="utf-8") as f:
            kept = f.readlines()
        assert kept == lines[len(lines) // 2:], "over-cap rotation must keep the most recent half"

    print("rotation regression OK")


def commander_contract_regression():
    import json
    from model_server import SYSTEM_PROMPT, format_tool_trace

    required = (
        "Coalition victory is your primary",
        "inspect its readiness and current mission",
        "Never double-commit",
        "concrete objective, launch and abort conditions",
        "withdrawal or extraction path",
        "likely enemy response",
        "reconnaissance gaps",
        "deception windows",
        "combined-arms capabilities",
        "Preserve a valid plan",
        "strategically pointless attrition",
        "Calculated losses are acceptable",
        "higher strategic value or decisive follow-on",
        "Exploit a major verified enemy mistake in the current review",
        "short-lived window",
        "Never call an uncertain guess a mistake",
    )
    missing = [rule for rule in required if rule not in SYSTEM_PROMPT]
    assert not missing, "commander prompt contract missing: %s" % ", ".join(missing)

    call = {"id": "call-7", "function": {"name": "plan_routes", "arguments": "{\"from_region\":1}"}}
    result = {"ok": True, "result": {"route": [1, 2, 3], "cost": 7.25}}
    trace = format_tool_trace("tick=123 round=4", call, result)
    payload = json.loads(trace.split(" <- ", 1)[1])
    assert "tick=123 round=4" in trace
    assert payload == {"call": call, "result": result}, "tool trace must be lossless and reconstructable"
    print("commander prompt contract OK")


def repeat_state_regression():
    from llm_eval import replay_same_state

    snapshot = {"tick": 123, "team": [{"player": "Multi0", "cash": 5000}]}
    calls = []

    def decide(state):
        calls.append(state)
        state["tick"] = 999
        return {"posture": "build", "strategy": "build"}

    report = replay_same_state(snapshot, 3, decide)
    assert snapshot["tick"] == 123, "replay must not mutate the source snapshot"
    assert [call["tick"] for call in calls] == [999, 999, 999]
    assert report["decision_count"] == 3
    assert report["unique_decisions"] == 1
    assert len(set(report["decision_sha256"])) == 1
    print("repeat-state evaluation OK")


def main():
    compile_all()
    rotation_regression()
    commander_contract_regression()
    repeat_state_regression()
    print("selfcheck OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
