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

# Project Map

## Top-level map

| Path | Role | Evidence |
|---|---|---|
| `OpenRA.sln` | .NET solution with engine, mods, launchers, server, utility, platform, and tests. | `OpenRA.sln` |
| `Directory.Build.props` | Shared build settings: .NET 8, C# 12, output to `bin`, analyzers, nullable disabled. | `Directory.Build.props` |
| `Makefile` | Unix build/check/test/install entrypoint. | `Makefile` |
| `make.ps1` | Windows build/check/test entrypoint. | `make.ps1` |
| `OpenRA.Game/` | Core runtime and reusable engine APIs. | `OpenRA.Game/Game.cs` |
| `OpenRA.Launcher/` | Game executable. | `OpenRA.Launcher/Program.cs` |
| `OpenRA.Server/` | Dedicated server executable wrapper. | `OpenRA.Server/Program.cs` |
| `OpenRA.Utility/` | Utility executable and command dispatcher. | `OpenRA.Utility/Program.cs` |
| `OpenRA.Mods.Common/` | Shared gameplay/mod logic. | `OpenRA.Mods.Common/OpenRA.Mods.Common.csproj` |
| `OpenRA.Mods.Cnc/`, `OpenRA.Mods.D2k/` | Official mod-specific assemblies. | project files |
| `OpenRA.Platforms.Default/` | Default platform adapter and native wrapper dependencies. | project file |
| `OpenRA.Test/` | NUnit test project. | `OpenRA.Test/OpenRA.Test.csproj` |
| `mods/` | Mod manifests, rules, maps, scripts, chrome, localization, assets. | `mods/ra/mod.yaml` |
| `packaging/` | Install and release helpers/assets. | `packaging/functions.sh` |
| `.github/workflows/` | CI, docs deployment, release packaging. | workflow files |

## Main entrypoints

| Runtime | Entrypoint | Notes |
|---|---|---|
| Game | `OpenRA.Launcher/Program.cs` | Calls `Game.InitializeAndRun(args)`. |
| Dedicated server | `OpenRA.Server/Program.cs` | Requires `Game.Mod`; reads `MOD_SEARCH_PATHS`. |
| Utility | `OpenRA.Utility/Program.cs` | First argument selects mod; later argument selects utility command. |
| Unix wrapper | `launch-game.sh` | Runs `dotnet bin/OpenRA.dll`. |
| Windows wrapper | `launch-game.cmd` | Runs `bin\OpenRA.exe`. |

## Important external dependencies

- .NET 8 SDK.
- NuGet packages for engine/runtime/tests.
- SDL2, OpenAL, FreeType wrappers; Lua 5.1 for script validation.
- GeoIP data download and player profile/badge HTTP reads.

## Not detected in checked root paths

- `global.json`
- `Directory.Packages.props`
- `Directory.Build.targets`
- `Dockerfile`
- `docker-compose.yml`
- database migration config
