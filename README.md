# OpenRA — Pummelchen fork

A fork of the [OpenRA](https://github.com/OpenRA/OpenRA) real-time strategy engine, tracking upstream `bleed`. Everything not listed below is unchanged upstream code — see the [upstream repository](https://github.com/OpenRA/OpenRA) for the project overview, build instructions, and gameplay information.

![Continuous Integration](https://github.com/Pummelchen/OpenRA/actions/workflows/ci.yml/badge.svg)

## What this fork adds or changes

### Project management

- **Issue forms** (`.github/ISSUE_TEMPLATE/`): structured bug report, feature request, and task templates with required priority and area selection.
- **Workflow documentation** (`.github/PROJECT_WORKFLOW.md`): issue classification, milestones, triage checklist, and Definition of Ready/Done.
- **Metadata bootstrap** (`.github/scripts/bootstrap_project_management.py` + `.github/workflows/bootstrap-project-management.yml`): idempotently creates and reconciles the repository's issue labels, milestones, and starter issues, and keeps them in sync on `main`.
- The repository is configured with these labels, milestones, and starter issues (see the issue tracker).

### Maintenance

- Removed AI-agent onboarding documentation (`.ai/`, `AGENTS.md`, `AI_INDEX.md`).
- README and INSTALL links updated to point at this repository.

## License

Same as upstream: GPL-3.0-or-later — see [COPYING](https://github.com/Pummelchen/OpenRA/blob/main/COPYING).
