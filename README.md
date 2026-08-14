<img width="1672" height="941" alt="image" src="https://github.com/user-attachments/assets/54b40334-e283-4b1f-b2dd-0f406476b6b5" />

# OpenRA — AI Mod - Extreme LLM Edition

A fork of the [OpenRA](https://github.com/OpenRA/OpenRA) real-time strategy engine, tracking upstream `bleed`. Everything not described here is unchanged upstream code — see the [upstream repository](https://github.com/OpenRA/OpenRA) for the project overview, build instructions, and gameplay information.

![Continuous Integration](https://github.com/Pummelchen/OpenRA/actions/workflows/ci.yml/badge.svg)

## The concept

This fork introduces a new AI game style — **Supreme Allied Command** — set as the default skirmish mode. You can field a team of up to four allied AI bots that fight as **one coordinated army**, commanded by a single strategic brain.

The AI is a hybrid:

- **Strategic commander (LLM)**: a local vision-capable language model (Gemma 4 E4B, 4-bit MLX quantized) reads a full-map radar snapshot and a precise team report every 15 seconds, then decides strategy, corps roles, attack targets, production counters, and missions.
- **Tactical executor (deterministic engine code)**: C# controllers carry out the plan — build orders, attack waves, retreats, base defense, feints, and stealth transport insertions.
- **Coalition**: every allied bot maintains the identical shared world model (deterministic, no message passing), so the whole team always acts on one plan.

The AI respects fog of war — it only sees territory the team has explored — but it uses that radar ruthlessly: combined-arms multi-pronged attacks, deception, special operations behind enemy lines, and opponent modeling that adapts to how you play. The design target is an AI that is **nearly impossible to beat by skilled human players**.

## Feature overview

- **Coalition command** — one strategic brain, up to four allied bots, one shared deterministic world model.
- **Hybrid intelligence** — a local vision-capable LLM sets strategy every ~15 s; deterministic engine controllers execute it at tick speed. The AI plays fully without the LLM.
- **Fair fog of war** — the AI only ever sees explored territory by default; omniscience is an opt-in intelligence axis, never the default, and there is no hidden income.
- **Independent difficulty axes** — command quality, reaction speed, micro precision, coordination strength, economic bonus (0% = strictly fair), and intelligence/fog advantage (fair fog → reveal structures → omniscient) are configurable separately.
- **Deception with measurement** — feints and baits are launched, their effect on enemy behavior is measured, and the results feed back into planning.
- **Engine-validated LLM tool API** — the commander queries combat estimates, routes, and target scores through read-only engine endpoints; it cannot fabricate mechanics or bypass game rules.
- **Headless harness & self-play** — `--simulate` runs full skirmishes without a renderer; `ai/selfplay.py` batches seeds, sweeps parameters, checks cross-map overfitting, and correlates the predicted win ratio with outcomes. Mixed self-play (the coalition versus scripted rush/turtle/naval bots) is supported.

## Project layout

- `ai/` — the model stack: `run.sh` launcher, `model_server.py` (brain server, port 8765), `COMMAND_API.md` (the C#↔LLM contract), `brain.log` (prompt/reply monitor), `selfplay.py` (batch evaluation).
- `OpenRA.Mods.Common/Traits/BotModules/` — the AI bot modules: coalition command center, strategic brain, external brain, radar capture, engine tool API.
- `OpenRA.Game/HeadlessSkirmish.cs` + `OpenRA.Mods.Common/UtilityCommands/SimulateCommand.cs` — headless simulation harness (`--simulate`) for self-play, batch evaluation, and scenario testing.

## Documentation

The project's design goals, architecture, and what the skilled human player can expect are described in the [project wiki](https://github.com/Pummelchen/OpenRA/wiki). The C#↔LLM interface contract lives in [`ai/COMMAND_API.md`](ai/COMMAND_API.md).

## License

Same as upstream: GPL-3.0-or-later — see [COPYING](https://github.com/Pummelchen/OpenRA/blob/main/COPYING).
