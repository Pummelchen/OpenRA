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

# Architecture

## High-level architecture

OpenRA is a .NET 8 RTS engine with data-driven mods. C# assemblies provide runtime services, simulation, networking, rendering/sound integration, utility commands, and official mod code. YAML, Lua, Fluent, and asset metadata define much of the game behavior under `mods/`.

Evidence:
- `OpenRA.sln`
- `Directory.Build.props`
- `OpenRA.Game/Game.cs`
- `mods/ra/mod.yaml`

## Runtime startup flow

1. Platform wrapper starts the game: `launch-game.sh` or `launch-game.cmd`.
2. `OpenRA.Launcher.Program.Main` calls `Game.InitializeAndRun(args)`.
3. `Game.Initialize` initializes directories, settings, logs, NAT, mod search paths, platform, renderer, and sound.
4. `Game.InitializeMod` clears old runtime state, resets UI, loads `ModData`, initializes loaders/fonts/maps/cursor, then starts the mod load screen.

Evidence:
- `launch-game.sh`
- `launch-game.cmd`
- `OpenRA.Launcher/Program.cs`
- `OpenRA.Game/Game.cs`

## World and simulation flow

- `Game.StartGame` prepares the map, creates a `World`, creates a `WorldRenderer`, calls `World.LoadComplete`, starts the `OrderManager`, then calls `World.PostLoadComplete`.
- `World` owns actors, effects, players, actor maps, screen maps, order validators, local/shared RNG, and world ticks.
- `World.SyncHash()` protects deterministic state by hashing actors, synced trait/effect state, shared RNG state, and selected player state.

Evidence:
- `OpenRA.Game/Game.cs`
- `OpenRA.Game/World.cs`
- `OpenRA.Game/Network/OrderManager.cs`

## Networking and order flow

- `IConnection` abstracts local echo, network, and replay-style order sources.
- `OrderManager` accepts local/immediate orders, sends orders/sync packets, receives remote orders, and advances frames only when expected order packets are available.
- `NetworkConnection` uses TCP and parses order/sync/ack/tick-scale/disconnect packets.

Evidence:
- `OpenRA.Game/Network/Connection.cs`
- `OpenRA.Game/Network/OrderManager.cs`

## Dedicated server flow

`OpenRA.Server.Program.Run` requires `Game.Mod`, initializes settings/NAT/mod search paths, loads `InstalledMods`, creates `ModData`, and starts `Server` on IPv4/IPv6 endpoints for `settings.ListenPort`. `Server` validates handshakes, mod/version/protocol, passwords, bans, profile authentication, whitelist/blacklist rules, and server traits.

Evidence:
- `OpenRA.Server/Program.cs`
- `OpenRA.Game/Server/Server.cs`
- `OpenRA.Game/Settings.cs`

## Utility flow

`utility.sh`/`utility.cmd` run `OpenRA.Utility`. `OpenRA.Utility.Program` loads installed mods, resolves the selected mod, creates `ModData`, discovers `IUtilityCommand` implementations, validates args, and runs commands.

Evidence:
- `utility.sh`
- `utility.cmd`
- `OpenRA.Utility/Program.cs`

## CI/CD flows

- Continuous integration builds and checks Linux/Windows on .NET 8 with checkout v7 and setup-dotnet v5.
- Documentation deployment generates wiki/docs output from utility commands, gated to the upstream `openra/openra` repository.
- Release packaging creates draft releases and packages source, Linux AppImages, macOS disk images, and Windows installers on release/playtest/devtest tags.

Evidence:
- `.github/workflows/ci.yml`
- `.github/workflows/documentation.yml`
- `.github/workflows/packaging.yml`

## Trust boundaries

| Boundary | Risk |
|---|---|
| Network clients to server | untrusted handshake data, passwords, IPs, profile signatures, order packets. |
| Mod YAML/Lua/assets to runtime | malformed data can break load, lint, UI, or gameplay. |
| Synced vs unsynced code | nondeterministic changes can desync multiplayer/replays. |
| External downloads | GeoIP database, player profile/badge URLs, NuGet packages, game asset packages. |
| Workflows and packaging | release/docs jobs use privileged automation and deployment credentials. |
