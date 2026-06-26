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

# Security Notes

## Security-relevant surfaces

| Surface | Why it matters | Evidence |
|---|---|---|
| Dedicated server settings | Passwords, bans, authentication policy, profile allow/block lists, IP-sharing, flood limits, vote kick. | `OpenRA.Game/Settings.cs` |
| Server handshake/auth | Mod/version/protocol checks, password validation, IP bans, profile signature validation. | `OpenRA.Game/Server/Server.cs` |
| Network transport | TCP connect/read/write, order packets, sync packets, replay recording. | `OpenRA.Game/Network/Connection.cs` |
| Deterministic simulation | Desyncs can break multiplayer/replay correctness. | `OpenRA.Game/Network/OrderManager.cs`, `OpenRA.Game/World.cs` |
| External downloads | GeoIP database, NuGet packages, forum profile/badge data, original game content packages. | `Makefile`, `make.ps1`, `OpenRA.Game/PlayerDatabase.cs`, `mods/ra/mod.yaml` |
| Workflows and packaging | Release/docs jobs publish artifacts and use repository or deployment credentials. | `.github/workflows/documentation.yml`, `.github/workflows/packaging.yml` |

## Auth and admission model

Verified from source: server settings include a server password, IP ban set, authentication requirement, profile ID allow/block lists, IP anonymization sharing, GeoIP country sharing, flood limits, and vote-kick settings.

Verified from source: server validation rejects clients for game-started state, missing/incorrect password, incompatible mod/version/order protocol, banned IPs, missing required authentication, blocked profile IDs, and allow-list absence.

Verified from source: authenticated profile validation fetches player profile data, decodes a public key, checks revocation, and verifies a signature over a per-connection random value.

## Sensitive runtime data

Potentially sensitive runtime data includes server settings, client IP addresses, profile IDs, auth signatures, replay files, logs, downloaded content, and user maps in support directories. Do not commit local support-directory data, logs, private server settings, credentials, or downloaded assets.

## AI-agent rules

- Do not weaken authentication, version/protocol checks, flood limits, ban/allow-list checks, or signature validation without explicit task scope and review.
- Treat network packet parsing and order processing as adversarial input surfaces.
- Preserve deterministic simulation. Avoid time, randomness, thread ordering, file I/O, or network I/O in synced game logic unless source shows it is safe.
- Be cautious with external URL construction and downloaded content parsing.
- Do not run install, publish, release, or deployment commands unless explicitly requested and safe.

## Review checklist

For server/network changes, inspect:

- `OpenRA.Game/Settings.cs`
- `OpenRA.Game/Server/Server.cs`
- `OpenRA.Game/Network/Connection.cs`
- `OpenRA.Game/Network/OrderManager.cs`
- `OpenRA.Game/World.cs`

For workflow or packaging changes, inspect:

- `.github/workflows/documentation.yml`
- `.github/workflows/packaging.yml`
- `packaging/functions.sh`
- `Makefile`
- `make.ps1`

## Unknowns

- No formal threat model document was found in checked files.
- No dependency vulnerability scanning workflow was detected.
- No container hardening or deployment configuration was found in checked root paths.
