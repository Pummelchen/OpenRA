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

# Generic Agent Instructions for OpenRA

Start by reading `AI_INDEX.md`, this file, and `.ai/START_HERE.md`. Treat generated docs as guidance and inspect current source, build scripts, workflows, and tests before editing.

## Working rules

- Keep changes small and source-grounded.
- Separate verified facts, assumptions, inferences, and unknowns.
- Do not edit product code for documentation-only tasks.
- Do not add vendor-specific AI instruction files.
- Follow `.editorconfig` and existing C# conventions.
- Preserve deterministic simulation and be careful with networking, server, mod loading, packaging, and workflow changes.
- Do not push to `bleed` directly.

## Validation

Use task-specific checks: `make check`, `make tests`, `make test`, `make check-scripts`, or Windows equivalents in `make.ps1`. Report only commands actually run.

## Refresh policy

Update `AI_INDEX.md`, `AGENTS.md`, and relevant `.ai/` files when architecture, commands, tests, security-sensitive paths, workflows, packaging, or mod data change.
