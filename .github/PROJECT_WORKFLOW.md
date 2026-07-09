# OpenRA Project Workflow

This repository uses GitHub Issues, Milestones, and Projects together to manage work.

## Source of truth

GitHub Issues are the source of truth for tasks, features, bugs, maintenance work, release preparation, and follow-up actions. Pull requests should link back to the issue they resolve or inform.

## Issue classification

Every issue should have one type label:

- `type: task` — general implementation, planning, cleanup, or maintenance work.
- `type: feature` — new functionality or enhancement.
- `type: bug` — defect, regression, crash, or incorrect behavior.

Every issue should also have:

- A priority label: `priority: critical`, `priority: high`, `priority: medium`, or `priority: low`.
- A status label: `status: needs triage`, `status: ready`, or `status: blocked`.
- Relevant area labels when known, such as `area: gameplay`, `area: engine`, `area: ui`, `area: modding`, `area: maps`, `area: docs`, `area: ci`, or `area: build`.
- A milestone assignment for the target phase/release, or `Backlog` when valid but not yet scheduled.

GitHub Issue Types may also be used when available, but labels remain required for filtering, compatibility, and automation.

## Milestones

Milestones represent phases or release buckets:

- `Phase 0 — Triage & Planning` — classify existing work, define priorities, identify blockers.
- `Phase 1 — Stabilization` — fix critical bugs, reduce regressions, improve build/test reliability.
- `Phase 2 — Core Improvements` — engine/gameplay improvements, refactors, and high-priority features.
- `Phase 3 — Content & UX Polish` — maps, UI, docs, modding polish, usability improvements.
- `Release Candidate` — final bug fixing, release notes, compatibility checks, packaging.
- `Backlog` — valid work that is not yet assigned to a release phase.

If a versioned release milestone exists, use the most relevant release milestone instead of creating a duplicate planning bucket.

## GitHub Project

The `OpenRA Roadmap` project is the central board for repository work, roadmap planning, prioritization, release tracking, and status reporting.

Recommended project fields:

| Field | Purpose |
| --- | --- |
| Status | Inbox, Needs Triage, Ready, In Progress, In Review, Blocked, Done. |
| Priority | Critical, High, Medium, Low. |
| Type | Task, Feature, Bug. |
| Phase | Planning/release phase. |
| Area | Gameplay, Engine, UI, Modding, Maps, Docs, CI, Build, Unknown. |
| Target Release | Release name, milestone, or version target. |
| Estimate | Rough effort points, not hours. |
| Risk | High, Medium, Low. |
| Blocked By | Dependency issue/PR links or explanation. |
| Target Date | Date target for planning and roadmap views. |

## Standard views

The project should maintain these views:

- `Triage Board` — board grouped by Status; filters Inbox and Needs Triage.
- `Active Work` — board grouped by Status; filters Ready, In Progress, In Review, and Blocked.
- `Roadmap` — roadmap or table grouped/sorted by Phase and Target Date.
- `Backlog` — table sorted by Priority then Risk; filters out Done.
- `Bugs` — table or board for `type: bug` work, sorted by Priority.
- `Release View` — table grouped by Milestone or Phase for release readiness.

## Definition of Ready

An issue is ready when it has:

- A clear objective.
- Acceptance criteria.
- A relevant area label when the area is known.
- No unknown blocking dependency.

## Definition of Done

An issue is done when:

- Code and/or documentation is complete.
- Tests or validation are noted.
- The linked pull request is merged, or the decision is documented.
- The issue is closed.

## Triage checklist

For new issues:

1. Confirm the issue has a clear type.
2. Apply priority and status labels.
3. Add relevant area labels.
4. Assign the issue to a milestone or `Backlog`.
5. Add it to `OpenRA Roadmap`.
6. Set project fields for Status, Priority, Type, Phase, Area, Risk, and Target Release when known.

## Status guidance

- `Inbox` — newly added to the project but not yet reviewed.
- `Needs Triage` — needs classification, reproduction, prioritization, or scoping.
- `Ready` — actionable and meets the Definition of Ready.
- `In Progress` — active implementation or investigation.
- `In Review` — pull request or maintainer review is active.
- `Blocked` — cannot proceed because of a dependency, missing decision, external issue, or failed prerequisite.
- `Done` — completed, merged/resolved, and closed.
