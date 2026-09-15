# Modifications

This repository is a **modified fork** of [OpenRA](https://github.com/OpenRA/OpenRA),
tracking upstream `bleed`. It is not the upstream project.

- **Modified by:** André Borchert
- **Modification dates:** 2026-06-26 through 2026-09-16
- **Copyright:** Copyright (c) 2026 André Borchert, for the modifications and
  additions made in this fork.

## Licence

The OpenRA engine in this repository remains free software under the **GNU General
Public License, version 3 or later** — see [COPYING](COPYING). The licence is
unchanged: forking and modifying OpenRA is exactly what it permits. Upstream
copyright remains with the OpenRA developers and contributors and is retained in
every file they wrote; files added by this fork carry the fork author's copyright
instead, which is why their headers differ.

This notice exists because GPL-3.0 section 5(a) requires a modified work to carry
prominent notices stating that it was modified and giving a relevant date.

## Scope of the changes

Measured against upstream `OpenRA:bleed` on 2026-09-16, at fork `main` `95963fcf24` (re-measure with
`gh api repos/Pummelchen/OpenRA/compare/OpenRA:bleed...Pummelchen:main` and, after fetching
upstream, `git diff --name-status $(git merge-base upstream/bleed HEAD) HEAD`):

| | |
|---|---|
| Commits ahead | 224 |
| Commits behind | 53 |
| Files added | 235 |
| Files modified | 46 |
| Files removed | 3 |

The fork adds a hybrid AI commander for a new skirmish game style, **Supreme Allied
Command**: a local vision-capable LLM acts as strategic commander, with a
deterministic tactical runtime executing its intent. The engine's own code is
otherwise unchanged upstream code.

See the [README](README.md) for the concept and [COMMANDER_ARCHITECTURE.md](COMMANDER_ARCHITECTURE.md)
for the design.
