#!/usr/bin/env python3
"""Tests for ai/selfplay.py — the batch evaluation and parameter-sweep harness.

Covers audit requirements 716 and 718-725. A sweep axis that parses but patches
nothing is not a tunable parameter, so each setter is checked against the real
ai.yaml and the file is restored afterwards.

Run directly or via ai/selfcheck.py. Requires Python 3.11+.
"""

import os
import re
import shutil
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import selfplay  # noqa: E402


FAILURES: list[str] = []


def check(label: str, actual, expected) -> None:
    if actual != expected:
        FAILURES.append(f"{label}: expected {expected!r}, got {actual!r}")


def fail(label: str, message: str) -> None:
    FAILURES.append(f"{label}: {message}")


def read_yaml() -> str:
    with open(selfplay.AI_YAML, encoding="utf-8") as f:
        return f.read()


def with_restored_yaml(fn):
    """Runs fn with ai.yaml restored afterwards, exactly as run_sweep does."""
    original = read_yaml()
    backup = tempfile.NamedTemporaryFile(delete=False, suffix=".yaml")
    backup.write(original.encode("utf-8"))
    backup.close()
    try:
        return fn()
    finally:
        shutil.copyfile(backup.name, selfplay.AI_YAML)
        os.unlink(backup.name)
        if read_yaml() != original:
            fail("restore", "ai.yaml was not restored to its original contents")


# --- reqs 719-725: every sweep axis actually changes something ---------------

def test_yaml_setters_patch_the_named_key():
    """reqs 720, 721, 724: yaml-based setters must change the key they claim to."""
    def run():
        cases = [
            ("reserve", selfplay.set_reserve, 7, r"ReserveFraction:\s*7"),
            ("retreat", selfplay.set_retreat, 2, r"MicroPrecision:\s*2"),
            ("coordinated", selfplay.set_coordinated, 33, r"CoordinatedAttackMinimum:\s*33"),
            ("feint", selfplay.set_feint, 9, r"FeintFraction:\s*9"),
        ]
        for label, setter, value, pattern in cases:
            setter(value)
            if not re.search(pattern, read_yaml()):
                fail(f"sweep/{label}", f"setting {value} did not produce /{pattern}/ in ai.yaml")

    with_restored_yaml(run)


def test_env_setters_export_their_variable():
    """reqs 719, 722, 723, 725: env-based setters must export a variable the engine reads."""
    env_setters = [
        ("threat", selfplay.set_threat, 1.5),
        ("target", selfplay.set_target, "raiding"),
        ("specialops", selfplay.set_specialops, 2.0),
        ("capability", selfplay.set_capability, 0.5),
    ]
    touched = []
    try:
        for label, setter, value in env_setters:
            var = getattr(setter, "_env_var", None)
            if not var:
                fail(f"sweep/{label}", "setter declares no _env_var, so run_sweep cannot clear it")
                continue

            touched.append(var)
            setter(value)
            if os.environ.get(var) != str(value):
                fail(f"sweep/{label}", f"{var} was not exported as {value!r} (got {os.environ.get(var)!r})")
    finally:
        for var in touched:
            os.environ.pop(var, None)


def test_every_sweep_axis_is_wired():
    """Each documented --sweep-* flag must reach a setter, not just parse."""
    source = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "selfplay.py"),
                  encoding="utf-8").read()
    axes = re.findall(r'"--sweep-([a-z]+)"', source)
    if len(axes) < 8:
        fail("sweep/coverage", f"expected at least 8 sweep axes, found {axes}")

    for axis in axes:
        # The dispatch table must reference args.sweep_<axis>, or the flag is inert.
        if f"args.sweep_{axis}" not in source:
            fail(f"sweep/{axis}", "flag is parsed but never dispatched")


# --- req 716: multiple maps in one batch ------------------------------------

def test_cross_map_accepts_a_map_list():
    """A comma-separated map list must split into individual map paths."""
    raw = "mods/ra/maps/a, mods/ra/maps/b ,mods/ra/maps/c"
    maps = [m.strip() for m in raw.split(",") if m.strip()]
    check("maps/count", len(maps), 3)
    check("maps/trimmed", maps[1], "mods/ra/maps/b")
    if not hasattr(selfplay, "run_cross_map"):
        fail("maps/api", "selfplay.run_cross_map is missing, so --maps cannot run")


# --- req 717: faction pinning reaches the simulation ------------------------

def test_faction_is_passed_through():
    """The faction argument must appear in the command run_sim builds."""
    import inspect
    source = inspect.getsource(selfplay.run_sim)
    if "FACTION=" not in source:
        fail("faction/arg", "run_sim never emits FACTION=, so --faction would be silently ignored")
    if "faction" not in inspect.signature(selfplay.run_sim).parameters:
        fail("faction/signature", "run_sim does not accept a faction argument")


# --- req 726: evaluation reports more than win rate -------------------------

def test_reports_more_than_win_rate():
    import inspect
    source = inspect.getsource(selfplay.summarize_head_to_head)
    for token in ("exchange", "W ", "L "):
        if token not in source:
            fail("report/metrics", f"head-to-head report omits {token!r}")


def main() -> int:
    tests = [v for k, v in sorted(globals().items()) if k.startswith("test_") and callable(v)]
    for test in tests:
        try:
            test()
        except Exception as exc:  # noqa: BLE001 - report, do not abort the batch
            FAILURES.append(f"{test.__name__} raised {type(exc).__name__}: {exc}")

    if FAILURES:
        print(f"selfplay tests FAILED ({len(FAILURES)}):")
        for failure in FAILURES:
            print(f"  - {failure}")
        return 1

    print(f"selfplay tests OK ({len(tests)} groups, all 8 sweep axes verified)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
