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


def main():
    compile_all()
    rotation_regression()
    print("selfcheck OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())