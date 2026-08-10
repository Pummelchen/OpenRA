# OpenRA — Supreme Allied Command AI fork

A fork of the [OpenRA](https://github.com/OpenRA/OpenRA) real-time strategy engine, tracking upstream `bleed`. Everything not described here is unchanged upstream code — see the [upstream repository](https://github.com/OpenRA/OpenRA) for the project overview, build instructions, and gameplay information.

![Continuous Integration](https://github.com/Pummelchen/OpenRA/actions/workflows/ci.yml/badge.svg)

## The concept

This fork introduces a new AI game style — **Supreme Allied Command** — set as the default skirmish mode. You can field a team of up to four allied AI bots that fight as **one coordinated army**, commanded by a single strategic brain.

The AI is a hybrid:

- **Strategic commander (LLM)**: a local vision-capable language model (Gemma 4 E4B, 4-bit MLX quantized) reads a full-map radar snapshot and a precise team report every 15 seconds, then decides strategy, corps roles, attack targets, production counters, and missions.
- **Tactical executor (deterministic engine code)**: C# controllers carry out the plan — build orders, attack waves, retreats, base defense, feints, and stealth transport insertions.
- **Coalition**: every allied bot maintains the identical shared world model (deterministic, no message passing), so the whole team always acts on one plan.

The AI respects fog of war — it only sees territory the team has explored — but it uses that radar ruthlessly: combined-arms multi-pronged attacks, deception, special operations behind enemy lines, and opponent modeling that adapts to how you play. The design target is an AI that is **nearly impossible to beat by skilled human players**.

## Project layout

- `ai/` — the model stack: `run.sh` launcher, `model_server.py` (brain server, port 8765), `COMMAND_API.md` (the C#↔LLM contract), `brain.log` (prompt/reply monitor).
- `OpenRA.Mods.Common/Traits/BotModules/` — the AI bot modules: coalition command center, strategic brain, external brain, radar capture.
- `OpenRA.Game/HeadlessSkirmish.cs` + `OpenRA.Mods.Common/UtilityCommands/SimulateCommand.cs` — headless simulation harness (`--simulate`) for self-play, batch evaluation, and scenario testing.

## Documentation

All design details — the P0–P10 development plan, the AI model stack, and what the skilled human player can expect — live in the [project wiki](https://github.com/Pummelchen/OpenRA/wiki).

## License

Same as upstream: GPL-3.0-or-later — see [COPYING](https://github.com/Pummelchen/OpenRA/blob/main/COPYING).
