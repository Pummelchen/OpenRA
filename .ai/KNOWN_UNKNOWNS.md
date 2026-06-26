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

# Known Unknowns

## Bootstrap scan limitations

- The requested base branch `main` was not found; repository metadata reports default branch `bleed`, so these files were indexed from `bleed`.
- Connector directory listing was limited, so this bootstrap used targeted source/config/doc inspection plus repository search.
- Full inventory of all utility commands, traits, maps, and scripts was not enumerated.
- Full issue and PR template inventory could not be listed through directory fetching.

## Missing or not detected

- No `global.json` was found. .NET 8 is verified from `Directory.Build.props`, `INSTALL.md`, and CI.
- No `Dockerfile` or `docker-compose.yml` was found in checked root paths.
- No database migration tooling was detected.
- No formal threat model document was found.
- No dependency vulnerability scanning workflow was detected.
- No prior AI-onboarding manifest/index files were found on `bleed`.

## Areas needing human review before risky edits

- network protocol compatibility or handshake semantics
- deterministic simulation, `ISync` state, shared RNG, or `World.SyncHash()` behavior
- replay recording/playback format
- dedicated server authentication and profile verification
- docs deployment or release packaging workflows
- packaging/install destinations and release publishing behavior
- mod package load order and content installer behavior
- cross-platform build target/runtime behavior

## Potential documentation conflicts

- `README.md` points to upstream project links for website, wiki, issue reporting, and repository. This repository is `Pummelchen/OpenRA`; do not assume fork-specific docs exist unless verified.
- The README says the wiki Hacking page is outdated. Treat current source/config as higher priority than older wiki material.

## Refresh notes

On a future refresh, compare `.ai/MANIFEST.json.indexed_commit` to the new head commit. If the old commit is unreachable, do a full rescan and record that in the manifest and changelog.
