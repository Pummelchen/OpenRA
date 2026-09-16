# OpenRA

<!-- agent-harnesses:begin -->
> **One instruction file.** This is it. Codex, DeepSeek Harness, OpenCode,
> Qwen Code, Qoder and Zed read `AGENTS.md` directly, and Claude Code reads it
> through the committed `CLAUDE.md`, which contains nothing but `@AGENTS.md`.
> **Edit only this file** — do not add a second set of instructions anywhere.
>
> Do **not** add `.rules`, `.cursorrules`, `.windsurfrules`, `.clinerules`,
> `.github/copilot-instructions.md` or `AGENT.md`. Zed takes the *first match*
> from that list, **ahead of `AGENTS.md`**, so any one of them silently
> replaces this file for every Zed user.
<!-- agent-harnesses:end -->

A modified **fork** of the OpenRA RTS engine that adds "Supreme Allied Command": a
hybrid AI commander where a local vision LLM sets strategy and deterministic C#
controllers execute it. This is `Pummelchen/OpenRA`, a fork of `OpenRA/OpenRA`
tracking upstream `bleed`, **not the upstream project**. The fork's own work is a
skirmish AI (default game style) plus the headless harness, evaluation and model
stack around it — roughly 230 files added and 46 modified against upstream. It is an
active, non-upstreamed development tree: **no tags and no GitHub releases exist**.

**The fork's own audit states plainly that requirement 804 — beating the stock bots
— is not met: 0 wins in 36 fixed-seed Fair-Fog matches against the standard Normal
bot's 4.** Treat "the AI is good" as unproven.

## Never upstream

Every change stays in `Pummelchen/OpenRA`. **Do not open a pull request, cherry-pick
or prepare a patch against `OpenRA/OpenRA`.** In a fork `gh` silently defaults to the
**parent** repository, so always pass `-R Pummelchen/OpenRA` or an explicit
`repos/Pummelchen/OpenRA/...` API path. The repository is deliberately left as a
fork.

`main` has **diverged** from upstream — hundreds of commits ahead and tens behind,
and the clone has **no `upstream` remote**, only `origin`. Read the current numbers
with `gh api repos/Pummelchen/OpenRA/compare/OpenRA:bleed...Pummelchen:main` rather
than trusting a figure written here; syncing means adding that remote yourself.

## Layout

- `OpenRA.Game/` — engine core, including `HeadlessSkirmish.cs`.
- `OpenRA.Mods.Common/` — shared traits. **The fork's AI lives in
  `Traits/BotModules/`**: `ExternalBrainBotModule.cs`, `StrategicBrainBotModule.cs`,
  `ToolApiBotModule.cs`, `RadarCaptureBotModule.cs`, `TacticalControllers.cs`, plus
  `BotModules/Coalition/` and `Commander/{Model,Search,Staff,Terrain}/`.
- `OpenRA.Mods.Cnc/`, `OpenRA.Mods.D2k/`, `OpenRA.Platforms.Default/`,
  `OpenRA.Server/`, `OpenRA.Utility/` (with `UtilityCommands/SimulateCommand.cs`,
  the `--simulate` harness), `OpenRA.Test/` (NUnit).
- `mods/` — MiniYAML mod data (`ra`, `cnc`, `d2k`, `ts`).
  `mods/ra/rules/ai.yaml` wires `ExternalBrainBotModule`
  (`ExternalBrainUrl: http://127.0.0.1:8765`, 15 s break, 120 s timeout) and
  `RadarCaptureBotModule`.
- `ai/` — the Python model stack (`run.sh`, `model_server.py` on :8765,
  `COMMAND_API.md`, `selfplay.py`, `llm_eval.py`, `selfcheck.py`, `baselines/`).
  `ml/` — offline training.
- Docs: `README.md`, `TESTING.md` (the authoritative build/test text),
  `MODIFICATIONS.md`, `COMMANDER_ARCHITECTURE.md`, `COMMANDER_HANDBOOK.md`,
  `AUDIT_REPORT.md`, `PLAN.md`.

## Build and test

```bash
make                                   # dotnet build -c Release, then geoip
dotnet build OpenRA.slnx -c Debug -p:TargetPlatform=osx-arm64   # whole solution

dotnet test bin/OpenRA.Test.dll --test-adapter-path:.
#  Passed: 1250, Failed: 0, Skipped: 2, Total: 1252  (~7 min)

cd mods/ra && ../../utility.sh ra --simulate MAP=shattered-mountain BOTS=4 TEAMS=2 TICKS=6000 SEED=100
```

Needs the **.NET 10 SDK**; `TARGETPLATFORM` is derived from `dotnet --info`'s RID
(`osx-arm64` on Apple Silicon). **Build the whole solution before running the test
dll** — see the trap below.

Python layer: `ai/selfcheck.py`, `ai/test_llm_eval.py`, `ai/test_selfplay.py`, which
hard-exit below Python 3.11.

## Run

```bash
./launch-game.sh          # after a build; launch-dedicated.sh for a server
ai/run.sh                 # Qwen3.5 vision endpoint + brain server, then play a skirmish
tail -f ai/brain.log      # prompts and plans; engine telemetry goes to
                          # ~/Library/Application Support/OpenRA/ai-telemetry.log
```

## Identity

`VERSION` contains the literal placeholder `{DEV_VERSION}`, and `mods/*/mod.yaml`
likewise. **The version is stamped, not stored**: `make version` runs
`packaging/functions.sh`'s `set_engine_version`/`set_mod_version` with a value from
`git name-rev --name-only --tags --no-undefined HEAD`, falling back to
`git-<short-sha>`. With no tags in this repository, a checkout has no fixed released
version.

## Gates

`.github/workflows/ci.yml` is **upstream's file, byte-identical to
`OpenRA/OpenRA`'s**: Linux and Windows jobs run `make check`, `make tests`,
`make check-scripts` and `make TREAT_WARNINGS_AS_ERRORS=true test`.

**It is red, and has only ever produced `push` runs on `main`, all failed.** Its
`pull_request` trigger lists only `bleed` and `prep-*`, so a PR based on this fork's
`main` **never triggers it**. `documentation.yml` and `itch.yml` are guarded by
`if: github.repository == 'openra/openra'` and are inert here; `packaging.yml` fires
only on `release-*`/`playtest-*`/`devtest-*` tags, none of which exist. CodeQL is
GitHub's dynamic default setup and does run on PRs.

## Traps

- **`make test` is not the test suite.** It is MiniYAML `--check-yaml` validation
  for ts/d2k/cnc/ra. The suite is `make tests` or `dotnet test` as above.
  `make check` is a Debug build with `-warnaserror` plus explicit-interface and
  conditional-trait-interface-override checks; `make check-scripts` runs `luac -p`
  over mod Lua and needs Lua 5.1.
- **`make check` fails today with 238 analyzer diagnostics promoted to errors**
  (IDE0300, IDE0005, IDE0062, IDE0047, IDE0055, SA1137, SA1203, SA1500, SA1507,
  SA1513, SA1515, SA1629, missing XML-doc `param` tags, …). A plain `dotnet build`
  prints the same set as `238 Warning(s), 0 Error(s)`. **Do not treat `make check`,
  or the red CI badge, as a signal that you broke something.**
- **Building only the test project is not enough for the headless tests.** On a tree
  whose `bin/` lacks the mod assemblies, the test dll reports **22 failures out of
  1252**, and the messages mislead: **one** is the real cause
  (`FileNotFoundException: …/bin/OpenRA.Mods.Cnc.dll`) and the other **21** are
  `InvalidOperationException: Attempted to override engine directory after it has
  already been accessed.` — `HeadlessSkirmishTest.LoadModAndMap` caches a
  once-per-process `Platform.OverrideEngineDir` and, on the exception path, never
  sets its `unavailableReason`, so every later test retries and re-throws a second,
  different error. **Build the whole solution first.**
- The NUnit suite takes about **7 minutes**.
- `utility.sh` execs `dotnet bin/OpenRA.Utility.dll`, so `--simulate`,
  `--analyze-replay` and the `--check-*` commands all fail without a prior build.
- **The LLM layer is Apple-Silicon-only.** `ai/run.sh` defaults to
  `/opt/homebrew/bin/python3.13` and requires `mlx-vlm` + `jinja2`; the Qwen3.5 4B
  8-bit snapshot is ~4.8 GiB on disk and took ~6 GB peak memory. `.venv-ai/` and
  `.hf-cache/` are git-ignored. Without the model server the bot falls back to the
  deterministic brain after the 120 s request timeout.
- **External-brain orders depend on wall-clock time and model output: such games
  desync in multiplayer and break replay fidelity.** Set `ExternalBrainUrl` empty to
  disable. The built-in scripted brain alone is deterministic.
- Two requirements, 645 and 804, are still open.
- `ai/selfplay.py --sweep-*` rewrites `mods/ra/rules/ai.yaml` and restores it on
  exit; **a killed batch can leave the file patched** (`(ai.yaml restored)` is the
  confirmation line).

## Releasing

**Read [`RELEASE.md`](RELEASE.md) before cutting a release.** It is this repository's
own release standard — edited here, not deployed from anywhere — and it carries both
the general rules and this repository's own section. Do not improvise a release.

The non-negotiables:

- **Apple Silicon only** — build native `arm64` (M1–M6). Never `--arch x86_64`,
  never `ARCHS=arm64 x86_64`, and never `lipo -create`, which is how a universal
  binary gets made.
- **Assert it** — `lipo -archs <binary>` must report exactly `arm64`. A build that
  silently produced a fat binary is a release defect, not a build option.
- **Every release carries the artifacts.** A tag alone is not a release.
- **Identity is single-sourced and enforced** — never bump one declaration of the
  version or build number on its own; the build or CI must fail on a mismatch.
- **Dry run first**; publish only on an explicit flag.
- **Never fetch a model, dataset or dependency to make a gate pass.** A check that
  cannot run is reported *not checked*, and the release notes must name it.
