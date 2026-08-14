# Testing the Coalition AI

This file documents how to build, test, and evaluate the Supreme Allied Command AI. The AI is a
hybrid: deterministic C# engine code plus an optional local LLM. The deterministic layer is fully
automated-testable; the LLM layer is validated by the in-game session described at the end.

## Prerequisites

- A .NET **10** SDK (`~/.dotnet`), or set `DOTNET` in the `Makefile`.
- For the LLM layer (optional): `mlx-lm` + `mlx-vlm` on Apple Silicon (see `ai/README.md`).

## Build

```sh
export PATH="$HOME/.dotnet:$PATH"
dotnet build OpenRA.Test/OpenRA.Test.csproj -c Debug --nologo
```

## Unit / integration tests

The test project (`OpenRA.Test`) uses NUnit. The pure subsystems are tested without a `World`; the
headless skirmish tests drive real games through the `--simulate` harness.

```sh
dotnet test bin/OpenRA.Test.dll --test-adapter-path:.
# or, one fixture:
dotnet test bin/OpenRA.Test.dll --test-adapter-path:. --filter "FullyQualifiedName~CommandValidatorTest"
```

Key fixtures:

| Fixture | What it covers |
|---|---|
| `ThreatModelTest`, `MapAnalysisTest`, `RoutePlannerTest` | threat fields, region graphs, chokepoints, routes, expansion/rally/artillery/insertion value |
| `CombatEstimatorTest`, `TargetEvaluatorTest`, `PostureSelectionTest` | matchups, AA suppression, terrain, target scoring, posture selection |
| `MissionLifecycleTest`, `TransportStateMachineTest`, `OrderArbiterTest`, `CommandValidatorTest` | mission phases, transport states, force arbitration, intent validation |
| `IntelTrackerTest`, `DeceptionTest`, `ProductionContractTest`, `DifficultyTest`, `ForceRegistryTest` | honesty ladder, deception measurement, production contracts, difficulty axes, force capabilities |
| `HeadlessSkirmishTest` | end-to-end headless games: determinism per seed, team caps, mission lifecycle, match metrics |

## Headless simulation & self-play

`--simulate` runs a full skirmish with no renderer; `ai/selfplay.py` batches it for evaluation and
parameter sweeps:

```sh
# one game, deterministic per seed:
cd mods/ra && ../../utility.sh ra --simulate MAP=shattered-mountain BOTS=4 TEAMS=2 TICKS=6000 SEED=100

# batch evaluation:
ai/selfplay.py --map mods/ra/maps/shattered-mountain --runs 4

# parameter sweeps (patches ai.yaml, restores on exit):
ai/selfplay.py --sweep-reserve 4,6,8
ai/selfplay.py --sweep-retreat 25,35,45
ai/selfplay.py --sweep-coordinated 30,50,70

# cross-map evaluation: reports per-map win rates and flags map-specific overfitting
ai/selfplay.py --maps mods/ra/maps/a,mods/ra/maps/b,mods/ra/maps/c --runs 4
```

The telemetry log is written to `~/Library/Application Support/OpenRA/ai-telemetry.log` (and printed
to stdout): waves, missions (with success/abort + special-ops/recon breakdown), feints, reserve
commits, counterattacks, support-power fires, transports, sync error, cohesion, and match metrics
(exchange ratio, predicted win ratio, etc.).

## LLM / tool API (manual)

1. Start the model stack (`ai/run.sh`), then the game in skirmish mode with the AI bot.
2. Watch `tail -f ai/brain.log` (prompts, tool calls, plans) and the telemetry log (decisions).
3. The engine tool API is served on `http://127.0.0.1:8766/tools`; health is `GET /health`, a sample:

   ```sh
   curl -X POST http://127.0.0.1:8766/tools -d '{"tool":"get_global_summary","arguments":{}}'
   ```

## What the automated suite does not cover (manual / in-game)

- The 16 end-to-end acceptance cases (789–804): unified coalition, combined arms, deception,
  special-ops extraction, human-attention multi-threat, counter-composition, reserve, counterattack,
  intelligence honesty, fairness inspection, LLM-failure fallback, invalid-commander rejection,
  adaptation, withdrawal, campaign, and brutal-fair strength. These are asserted by telemetry markers
  during an in-game skirmish; the deterministic ones can be exercised headless with `--simulate`.
- Stress/scale (hundreds of units, many bots) and memory-leak observation.
- LLM plan-quality scoring against a live model.
- The fair-but-brutal reference configuration (extreme command/coordination, fair fog, 0% economic
  bonus) still needs a human playtest to confirm it is "nearly impossible" without omniscience.
