<!--
AI onboarding file.
Mode: bootstrap
Indexed commit: 972c10ec80f90a30a4fa80abfebc633af3365847
Last generated: 2026-06-26T11:30:00Z
Generator: generic high-end AI coding agent
Purpose: Help future AI sessions understand this repository quickly.
Audience: Any high-capability AI coding agent, regardless of vendor or model family.
Human edits are allowed. Future refreshes should preserve valid human edits.
-->

# Components

## `OpenRA.Game`

- Responsibility: core engine runtime, settings, mod discovery/loading, world simulation, networking, server implementation, rendering/sound integration, utility abstractions.
- Key files: `OpenRA.Game/Game.cs`, `OpenRA.Game/World.cs`, `OpenRA.Game/Settings.cs`, `OpenRA.Game/Network/`, `OpenRA.Game/Server/`.
- Risks: deterministic simulation, networking protocol compatibility, static runtime state, replay/sync behavior.

## `OpenRA.Launcher`

- Responsibility: interactive game executable.
- Key files: `OpenRA.Launcher/Program.cs`, `OpenRA.Launcher/OpenRA.Launcher.csproj`.
- Risk: exception handling, log flushing, mod arguments, platform launch behavior.

## `OpenRA.Server`

- Responsibility: dedicated server process wrapper.
- Key files: `OpenRA.Server/Program.cs`, `OpenRA.Server/OpenRA.Server.csproj`.
- Interface: command-line args including required `Game.Mod`; environment `MOD_SEARCH_PATHS`.
- Risk: network listen endpoints, server lifecycle, support directory settings, log files.

## `OpenRA.Utility`

- Responsibility: mod-aware command-line utility host.
- Key files: `OpenRA.Utility/Program.cs`, `utility.sh`, `utility.cmd`.
- Interface: `OpenRA.Utility.exe [MOD] [COMMAND] ...`.
- Risk: command discovery, argument validation, file-writing utility commands.

## `OpenRA.Mods.Common`

- Responsibility: shared official mod code: traits, commands, widget logic, gameplay systems, utility logic.
- Key paths: `OpenRA.Mods.Common/Traits/`, `OpenRA.Mods.Common/Widgets/Logic/`.
- Risk: shared behavior affects multiple official mods and can affect deterministic simulation.

## Official mod assemblies

- `OpenRA.Mods.Cnc/`: C&C-family mod assembly.
- `OpenRA.Mods.D2k/`: Dune 2000 mod assembly.
- Risk: changes may require matching YAML/rules/localization updates under `mods/`.

## `mods/`

- Responsibility: data-driven manifests, packages, rules, weapons, sequences, chrome, hotkeys, maps, Lua scripts, and localization.
- Key files: `mods/*/mod.yaml`, `mods/*/rules/`, `mods/*/chrome/`, `mods/*/fluent/`, `mods/*/maps/`, `mods/*/scripts/`.
- Tests: `make test`, `make check-scripts`.

## `OpenRA.Platforms.Default`

- Responsibility: default platform integration.
- External dependencies: OpenRA wrapper packages for FreeType, OpenAL, SDL2.
- Risk: native library availability and target platform selection.

## Build, CI, docs, and packaging

- Key files: `Directory.Build.props`, `Makefile`, `make.ps1`, `.github/workflows/*.yml`, `packaging/functions.sh`.
- Risk: platform-specific target selection, analyzer behavior, native dependencies, release artifacts, docs deployment, signing/upload conditions.
