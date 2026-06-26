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

# AI Onboarding Changelog

## 2026-06-26 — bootstrap

Indexed commit: `972c10ec80f90a30a4fa80abfebc633af3365847`

### Added

- `AI_INDEX.md`
- `AGENTS.md`
- `.ai/START_HERE.md`
- `.ai/PROJECT_MAP.md`
- `.ai/ARCHITECTURE.md`
- `.ai/COMPONENTS.md`
- `.ai/COMMANDS.md`
- `.ai/TESTING.md`
- `.ai/SECURITY.md`
- `.ai/PLAYBOOKS.md`
- `.ai/KNOWN_UNKNOWNS.md`
- `.ai/MANIFEST.json`

### README

Added a vendor-neutral AI-agent onboarding block near the top of `README.md`.

### Source areas used

- `README.md`, `INSTALL.md`, `CONTRIBUTING.md`
- `OpenRA.sln`, `Directory.Build.props`, project files
- `Makefile`, `make.ps1`, `.github/workflows/ci.yml`, `.github/workflows/documentation.yml`, `.github/workflows/packaging.yml`
- launch and utility wrapper scripts
- `OpenRA.Game/Game.cs`, `OpenRA.Game/World.cs`, `OpenRA.Game/Settings.cs`
- `OpenRA.Game/Network/Connection.cs`, `OpenRA.Game/Network/OrderManager.cs`
- `OpenRA.Game/Server/Server.cs`, `OpenRA.Server/Program.cs`, `OpenRA.Utility/Program.cs`
- `mods/ra/mod.yaml`, `packaging/functions.sh`, `.editorconfig`, `.gitignore`

### Known risks

- Requested base branch `main` was not found; `bleed` was used because repository metadata reports it as default.
- Markdown-only changes are ignored by the continuous-integration workflow.
- A previous onboarding branch existed but was behind current `bleed`; this branch was recreated from current `bleed`.
