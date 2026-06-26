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

# Commands

## Prerequisites

| Platform | Verified prerequisites | Evidence |
|---|---|---|
| Windows | Windows PowerShell >= 4.0 and .NET 8 SDK. | `INSTALL.md` |
| Linux | .NET 8 SDK; system libraries if using `DEPENDENCIES=system`. | `INSTALL.md`, `Makefile` |
| macOS | .NET 8 SDK and `make`. | `INSTALL.md` |
| Lua checks | Lua 5.1 / `luac`. | `Makefile`, `.github/workflows/ci.yml` |

## Build

```sh
make
```

```powershell
./make.ps1 all
```

Unix `make` builds with `dotnet build -c ${CONFIGURATION} -p:TargetPlatform=$(TARGETPLATFORM)`, configures system libraries when `DEPENDENCIES != bundled`, then runs `fetch-geoip.sh`.

## Build using system native libraries

```sh
make DEPENDENCIES=system
```

The current Makefile names this switch `DEPENDENCIES=system`; older onboarding that mentions `TARGETPLATFORM=unix-generic` is stale.

## Run the game

```sh
./launch-game.sh Game.Mod=ra
./launch-game.sh Game.Mod=cnc
./launch-game.sh Game.Mod=d2k
./launch-game.sh Game.Mod=ts
```

```cmd
launch-game.cmd Game.Mod=ra
```

## Dedicated server

Verified entrypoint shape:

```sh
dotnet bin/OpenRA.Server.dll Engine.EngineDir=".." Game.Mod=ra
```

`OpenRA.Server.Program` requires `Game.Mod`; it also reads `Engine.EngineDir`, `Engine.SupportDir`, and `MOD_SEARCH_PATHS`.

## Utility commands

```sh
./utility.sh ra --help
./utility.sh ra --check-yaml
```

```cmd
utility.cmd ra --check-yaml
```

The first utility argument is the mod id or mod path. Later arguments select a command discovered from `IUtilityCommand` implementations.

## Validation commands

| Purpose | Unix-like | Windows |
|---|---|---|
| Code/style/engine checks | `make check` | `./make.ps1 check` |
| NUnit tests | `make tests` | `./make.ps1 tests` |
| Mod YAML checks | `make test` | `./make.ps1 test` |
| Lua syntax checks | `make check-scripts` | `./make.ps1 check-scripts` |
| Broad CI-like local check | `make check && make tests && make check-scripts && make TREAT_WARNINGS_AS_ERRORS=true test` | see `.github/workflows/ci.yml` |

## Unit-test command expansion

```sh
dotnet build OpenRA.Test/OpenRA.Test.csproj -c Debug --nologo -p:TargetPlatform=$(TARGETPLATFORM)
dotnet test bin/OpenRA.Test.dll --test-adapter-path:.
```

For focused tests, inspect actual test names first and use supported `dotnet test` filters.

## Lint, typecheck, and format

- `make check` is the closest verified lint/typecheck command.
- `.editorconfig`, StyleCop analyzers, and Roslynator analyzers enforce formatting/style during Debug builds.
- No separate formatter command was found.

## Database and Docker

No database migration tooling, `Dockerfile`, or `docker-compose.yml` was detected in checked root paths. Do not invent commands for them.

## Release, documentation, and install

```sh
make install
make install-linux-shortcuts
make install-linux-appdata
make install-man
```

Release packaging and docs deployment are GitHub Actions workflows. Do not run release or deployment workflows manually unless explicitly requested.

Evidence:
- `Makefile`
- `make.ps1`
- `.github/workflows/ci.yml`
- `.github/workflows/documentation.yml`
- `.github/workflows/packaging.yml`
