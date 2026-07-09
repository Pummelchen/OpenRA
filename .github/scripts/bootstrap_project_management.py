#!/usr/bin/env python3
"""Idempotently bootstrap repository project-management metadata.

This script is designed for GitHub Actions. It intentionally never prints tokens.
Repo-level setup uses GITHUB_TOKEN. User-owned Project v2 setup requires a
project-capable token in PROJECT_TOKEN, OPENRA_PROJECT_TOKEN, or GIT_ACCESS_TOKEN.
"""

from __future__ import annotations

import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
from typing import Any

OWNER = os.getenv("OPENRA_OWNER", "Pummelchen")
REPO = os.getenv("OPENRA_REPO", "OpenRA")
FULL_REPO = f"{OWNER}/{REPO}"
PROJECT_OWNER = os.getenv("OPENRA_PROJECT_OWNER", OWNER)
PROJECT_TITLE = os.getenv("OPENRA_PROJECT_TITLE", "OpenRA Roadmap")
API_VERSION = os.getenv("GITHUB_API_VERSION", "2022-11-28")
DRY_RUN = os.getenv("DRY_RUN", "false").lower() == "true"

REPO_TOKEN = os.getenv("GITHUB_TOKEN") or os.getenv("GH_TOKEN") or os.getenv("GIT_ACCESS_TOKEN")
PROJECT_TOKEN = (
    os.getenv("PROJECT_TOKEN")
    or os.getenv("OPENRA_PROJECT_TOKEN")
    or os.getenv("GH_PROJECT_TOKEN")
    or os.getenv("GIT_ACCESS_TOKEN")
)

LABELS = [
    ("type: task", "5319E7", "General implementation or maintenance work"),
    ("type: feature", "A2EEEF", "New functionality or enhancement"),
    ("type: bug", "D73A4A", "Defect, regression, crash, incorrect behavior"),
    ("priority: critical", "B60205", "Blocks release or causes severe breakage"),
    ("priority: high", "D93F0B", "Important, should be addressed soon"),
    ("priority: medium", "FBCA04", "Normal priority"),
    ("priority: low", "0E8A16", "Nice to have"),
    ("status: needs triage", "D4C5F9", "Needs review and classification"),
    ("status: ready", "0E8A16", "Clear enough to start"),
    ("status: blocked", "B60205", "Cannot proceed due to dependency"),
    ("area: gameplay", "1D76DB", "Gameplay-related work"),
    ("area: engine", "0052CC", "Engine-related work"),
    ("area: ui", "C5DEF5", "User interface work"),
    ("area: modding", "5319E7", "Modding support or compatibility"),
    ("area: maps", "F9D0C4", "Maps or map tooling"),
    ("area: docs", "0075CA", "Documentation"),
    ("area: ci", "0E8A16", "Continuous integration"),
    ("area: build", "FBCA04", "Build system or packaging"),
    ("good first issue", "7057FF", "Good for first-time contributors"),
    ("help wanted", "008672", "External help is welcome"),
]

MILESTONES = [
    ("Phase 0 — Triage & Planning", "Classify existing work, define priorities, identify blockers."),
    ("Phase 1 — Stabilization", "Fix critical bugs, reduce regressions, improve build/test reliability."),
    ("Phase 2 — Core Improvements", "Engine/gameplay improvements, refactors, and high-priority features."),
    ("Phase 3 — Content & UX Polish", "Maps, UI, docs, modding polish, usability improvements."),
    ("Release Candidate", "Final bug fixing, release notes, compatibility checks, packaging."),
    ("Backlog", "Valid work that is not yet assigned to a release phase."),
]

SEED_ISSUES = [
    {
        "title": "Audit current OpenRA fork state",
        "labels": ["type: task", "status: ready", "priority: high"],
        "milestone": "Phase 0 — Triage & Planning",
        "body": """## Objective
Audit the current OpenRA fork state.

## Acceptance criteria
- [ ] Identify divergence from upstream OpenRA.
- [ ] List build/test status.
- [ ] Identify high-risk areas.
- [ ] Produce next recommended issues.
""",
        "project": {"Status": "Ready", "Priority": "High", "Type": "Task", "Phase": "Phase 0 — Triage & Planning", "Area": "Unknown", "Risk": "Medium", "Estimate": 3, "Target Release": "Phase 0 — Triage & Planning"},
    },
    {
        "title": "Set up repository project workflow",
        "labels": ["type: task", "status: ready", "priority: high", "area: docs"],
        "milestone": "Phase 0 — Triage & Planning",
        "body": """## Objective
Set up the repository project workflow.

## Acceptance criteria
- [ ] Labels, milestones, project fields, and views are configured.
- [ ] CONTRIBUTING or project workflow docs explain how to use issues/milestones/projects.
""",
        "project": {"Status": "Ready", "Priority": "High", "Type": "Task", "Phase": "Phase 0 — Triage & Planning", "Area": "Docs", "Risk": "Low", "Estimate": 2, "Target Release": "Phase 0 — Triage & Planning"},
    },
    {
        "title": "Verify current build and test status",
        "labels": ["type: bug", "status: needs triage", "priority: high", "area: build", "area: ci"],
        "milestone": "Phase 1 — Stabilization",
        "body": """## Objective
Verify current build and test status.

## Acceptance criteria
- [ ] Build commands are documented.
- [ ] Failing build/test steps are captured.
- [ ] Follow-up issues are created for failures.
""",
        "project": {"Status": "Needs Triage", "Priority": "High", "Type": "Bug", "Phase": "Phase 1 — Stabilization", "Area": "Build", "Risk": "High", "Estimate": 3, "Target Release": "Phase 1 — Stabilization"},
    },
    {
        "title": "Define roadmap for fork-specific improvements",
        "labels": ["type: feature", "status: needs triage", "priority: medium"],
        "milestone": "Phase 2 — Core Improvements",
        "body": """## Objective
Define roadmap for fork-specific improvements.

## Acceptance criteria
- [ ] Candidate improvements are listed.
- [ ] Each accepted improvement has its own issue.
- [ ] Roadmap view reflects priorities and target phase.
""",
        "project": {"Status": "Needs Triage", "Priority": "Medium", "Type": "Feature", "Phase": "Phase 2 — Core Improvements", "Area": "Unknown", "Risk": "Medium", "Estimate": 5, "Target Release": "Phase 2 — Core Improvements"},
    },
]

PROJECT_SINGLE_SELECT_FIELDS = {
    "Status": ["Inbox", "Needs Triage", "Ready", "In Progress", "In Review", "Blocked", "Done"],
    "Priority": ["Critical", "High", "Medium", "Low"],
    "Type": ["Task", "Feature", "Bug"],
    "Phase": ["Phase 0 — Triage & Planning", "Phase 1 — Stabilization", "Phase 2 — Core Improvements", "Phase 3 — Content & UX Polish", "Release Candidate", "Backlog"],
    "Area": ["Gameplay", "Engine", "UI", "Modding", "Maps", "Docs", "CI", "Build", "Unknown"],
    "Risk": ["High", "Medium", "Low"],
}

PROJECT_OTHER_FIELDS = {
    "Target Release": "TEXT",
    "Estimate": "NUMBER",
    "Blocked By": "TEXT",
    "Target Date": "DATE",
}

PROJECT_VIEWS = [
    ("Triage Board", "board", 'Status:"Inbox","Needs Triage"'),
    ("Active Work", "board", 'Status:"Ready","In Progress","In Review","Blocked"'),
    ("Roadmap", "roadmap", ""),
    ("Backlog", "table", "-Status:Done"),
    ("Bugs", "table", 'Type:Bug label:"type: bug"'),
    ("Release View", "table", '-Phase:Backlog'),
]

SUMMARY: dict[str, list[str]] = {"created": [], "reused": [], "skipped": [], "failed": []}


class ApiError(RuntimeError):
    def __init__(self, status: int | str, message: str, payload: Any = None):
        super().__init__(f"{status}: {message}")
        self.status = status
        self.payload = payload


def log(message: str) -> None:
    print(message, flush=True)


def rest(method: str, path: str, token: str, data: Any = None, ok: tuple[int, ...] = (200, 201, 204, 304)) -> Any:
    url = path if path.startswith("https://") else f"https://api.github.com{path}"
    headers = {
        "Accept": "application/vnd.github+json",
        "Authorization": f"Bearer {token}",
        "X-GitHub-Api-Version": API_VERSION,
        "User-Agent": "openra-project-bootstrap",
    }
    body = None
    if data is not None:
        headers["Content-Type"] = "application/json"
        body = json.dumps(data).encode("utf-8")
    if DRY_RUN and method not in {"GET"}:
        log(f"DRY-RUN {method} {url}")
        return {}
    req = urllib.request.Request(url, data=body, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=30) as response:
            raw = response.read().decode("utf-8")
            if response.status not in ok:
                raise ApiError(response.status, raw)
            return json.loads(raw) if raw else None
    except urllib.error.HTTPError as exc:
        raw = exc.read().decode("utf-8", errors="replace")
        try:
            payload = json.loads(raw) if raw else {}
            message = payload.get("message", raw)
        except json.JSONDecodeError:
            payload = raw
            message = raw
        raise ApiError(exc.code, message, payload) from exc


def graphql(query: str, variables: dict[str, Any], token: str) -> dict[str, Any]:
    payload = rest("POST", "https://api.github.com/graphql", token, {"query": query, "variables": variables}, ok=(200,))
    if payload.get("errors"):
        raise ApiError("GraphQL", json.dumps(payload["errors"]), payload)
    return payload["data"]


def paginate(path: str, token: str) -> list[Any]:
    page = 1
    out: list[Any] = []
    while True:
        sep = "&" if "?" in path else "?"
        chunk = rest("GET", f"{path}{sep}per_page=100&page={page}", token)
        if not isinstance(chunk, list):
            break
        out.extend(chunk)
        if len(chunk) < 100:
            break
        page += 1
    return out


def ensure_labels() -> None:
    existing = {label["name"]: label for label in paginate(f"/repos/{FULL_REPO}/labels", REPO_TOKEN)}
    for name, color, description in LABELS:
        if name in existing:
            SUMMARY["reused"].append(f"label {name}")
            continue
        rest("POST", f"/repos/{FULL_REPO}/labels", REPO_TOKEN, {"name": name, "color": color, "description": description})
        SUMMARY["created"].append(f"label {name}")


def ensure_milestones() -> dict[str, int]:
    existing = {m["title"]: m for m in paginate(f"/repos/{FULL_REPO}/milestones?state=all", REPO_TOKEN)}
    for title, description in MILESTONES:
        if title in existing:
            SUMMARY["reused"].append(f"milestone {title}")
            continue
        created = rest("POST", f"/repos/{FULL_REPO}/milestones", REPO_TOKEN, {"title": title, "description": description, "state": "open"})
        existing[title] = created
        SUMMARY["created"].append(f"milestone {title}")
    return {title: int(data["number"]) for title, data in existing.items()}


def find_issue_by_title(title: str) -> dict[str, Any] | None:
    q = urllib.parse.quote(f'repo:{FULL_REPO} is:issue in:title "{title}"', safe="")
    result = rest("GET", f"/search/issues?q={q}", REPO_TOKEN)
    for item in result.get("items", []):
        if item.get("title", "").strip().casefold() == title.casefold():
            return item
    return None


def ensure_seed_issues(milestone_numbers: dict[str, int]) -> list[dict[str, Any]]:
    issues = []
    for spec in SEED_ISSUES:
        existing = find_issue_by_title(spec["title"])
        if existing:
            SUMMARY["reused"].append(f"issue #{existing['number']} {spec['title']}")
            issues.append(existing)
            continue
        created = rest(
            "POST",
            f"/repos/{FULL_REPO}/issues",
            REPO_TOKEN,
            {
                "title": spec["title"],
                "body": spec["body"],
                "labels": spec["labels"],
                "milestone": milestone_numbers.get(spec["milestone"]),
            },
        )
        SUMMARY["created"].append(f"issue #{created['number']} {spec['title']}")
        issues.append(created)
    return issues


def load_project(project_token: str) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any]]:
    query = """
    query($login: String!, $repoOwner: String!, $repoName: String!) {
      user(login: $login) {
        id
        databaseId
        projectsV2(first: 100) {
          nodes {
            id
            number
            title
            url
            fields(first: 100) {
              nodes {
                __typename
                ... on ProjectV2Field { id name dataType }
                ... on ProjectV2SingleSelectField { id name dataType options { id name } }
              }
            }
            views(first: 100) { nodes { id number name layout } }
          }
        }
      }
      repository(owner: $repoOwner, name: $repoName) { id }
    }
    """
    data = graphql(query, {"login": PROJECT_OWNER, "repoOwner": OWNER, "repoName": REPO}, project_token)
    user = data.get("user")
    if not user:
        raise ApiError("GraphQL", f"Could not resolve project owner {PROJECT_OWNER}")
    repo = data.get("repository")
    if not repo:
        raise ApiError("GraphQL", f"Could not resolve repository {FULL_REPO}")
    project = next((p for p in user["projectsV2"]["nodes"] if p["title"] == PROJECT_TITLE), None)
    return user, repo, project


def ensure_project(project_token: str) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any]]:
    user, repo, project = load_project(project_token)
    if project:
        SUMMARY["reused"].append(f"project {PROJECT_TITLE}")
        return user, repo, project
    mutation = """
    mutation($ownerId: ID!, $title: String!) {
      createProjectV2(input: {ownerId: $ownerId, title: $title}) {
        projectV2 { id number title url fields(first: 100) { nodes { __typename ... on ProjectV2Field { id name dataType } ... on ProjectV2SingleSelectField { id name dataType options { id name } } } } views(first: 100) { nodes { id number name layout } } }
      }
    }
    """
    data = graphql(mutation, {"ownerId": user["id"], "title": PROJECT_TITLE}, project_token)
    project = data["createProjectV2"]["projectV2"]
    SUMMARY["created"].append(f"project {PROJECT_TITLE}")
    return user, repo, project


def field_map(project: dict[str, Any]) -> dict[str, dict[str, Any]]:
    return {field["name"]: field for field in project["fields"]["nodes"] if field.get("name")}


def ensure_project_fields(project_token: str, project: dict[str, Any]) -> dict[str, dict[str, Any]]:
    fields = field_map(project)
    create_mutation = """
    mutation($input: CreateProjectV2FieldInput!) {
      createProjectV2Field(input: $input) {
        projectV2Field {
          __typename
          ... on ProjectV2Field { id name dataType }
          ... on ProjectV2SingleSelectField { id name dataType options { id name } }
        }
      }
    }
    """
    update_mutation = """
    mutation($fieldId: ID!, $options: [ProjectV2SingleSelectFieldOptionInput!]) {
      updateProjectV2Field(input: {fieldId: $fieldId, singleSelectOptions: $options}) {
        projectV2Field {
          __typename
          ... on ProjectV2SingleSelectField { id name dataType options { id name } }
        }
      }
    }
    """
    colors = ["GRAY", "BLUE", "GREEN", "YELLOW", "ORANGE", "RED", "PINK", "PURPLE"]
    for name, options in PROJECT_SINGLE_SELECT_FIELDS.items():
        option_inputs = [{"name": option, "color": colors[i % len(colors)], "description": option} for i, option in enumerate(options)]
        if name in fields:
            existing_options = [option["name"] for option in fields[name].get("options", [])]
            if existing_options != options:
                data = graphql(update_mutation, {"fieldId": fields[name]["id"], "options": option_inputs}, project_token)
                fields[name] = data["updateProjectV2Field"]["projectV2Field"]
                SUMMARY["created"].append(f"updated project field {name}")
            else:
                SUMMARY["reused"].append(f"project field {name}")
            continue
        data = graphql(create_mutation, {"input": {"projectId": project["id"], "name": name, "dataType": "SINGLE_SELECT", "singleSelectOptions": option_inputs}}, project_token)
        fields[name] = data["createProjectV2Field"]["projectV2Field"]
        SUMMARY["created"].append(f"project field {name}")
    for name, data_type in PROJECT_OTHER_FIELDS.items():
        if name in fields:
            SUMMARY["reused"].append(f"project field {name}")
            continue
        data = graphql(create_mutation, {"input": {"projectId": project["id"], "name": name, "dataType": data_type}}, project_token)
        fields[name] = data["createProjectV2Field"]["projectV2Field"]
        SUMMARY["created"].append(f"project field {name}")
    # Reload so single-select option IDs are complete.
    _, _, refreshed = load_project(project_token)
    return field_map(refreshed)


def add_issue_to_project(project_token: str, project_id: str, issue_node_id: str) -> str | None:
    mutation = """
    mutation($projectId: ID!, $contentId: ID!) {
      addProjectV2ItemById(input: {projectId: $projectId, contentId: $contentId}) { item { id } }
    }
    """
    try:
        data = graphql(mutation, {"projectId": project_id, "contentId": issue_node_id}, project_token)
        return data["addProjectV2ItemById"]["item"]["id"]
    except ApiError as exc:
        if "already" not in str(exc).lower():
            raise
    query = """
    query($projectId: ID!) {
      node(id: $projectId) {
        ... on ProjectV2 {
          items(first: 100) { nodes { id content { ... on Issue { id title number } } } }
        }
      }
    }
    """
    data = graphql(query, {"projectId": project_id}, project_token)
    for item in data["node"]["items"]["nodes"]:
        content = item.get("content") or {}
        if content.get("id") == issue_node_id:
            return item["id"]
    return None


def update_project_field(project_token: str, project_id: str, item_id: str, field: dict[str, Any], value: Any) -> None:
    if value in (None, ""):
        return
    if field.get("dataType") == "SINGLE_SELECT":
        options = {option["name"]: option["id"] for option in field.get("options", [])}
        option_id = options.get(str(value))
        if not option_id:
            return
        field_value = {"singleSelectOptionId": option_id}
    elif field.get("dataType") == "NUMBER":
        field_value = {"number": float(value)}
    elif field.get("dataType") == "DATE":
        field_value = {"date": str(value)}
    else:
        field_value = {"text": str(value)}
    mutation = """
    mutation($projectId: ID!, $itemId: ID!, $fieldId: ID!, $value: ProjectV2FieldValue!) {
      updateProjectV2ItemFieldValue(input: {projectId: $projectId, itemId: $itemId, fieldId: $fieldId, value: $value}) { projectV2Item { id } }
    }
    """
    graphql(mutation, {"projectId": project_id, "itemId": item_id, "fieldId": field["id"], "value": field_value}, project_token)


def ensure_project_views(project_token: str, user_database_id: int, project: dict[str, Any]) -> None:
    existing = {view["name"] for view in project.get("views", {}).get("nodes", [])}
    for name, layout, filter_query in PROJECT_VIEWS:
        if name in existing:
            SUMMARY["reused"].append(f"project view {name}")
            continue
        try:
            rest(
                "POST",
                f"/users/{user_database_id}/projectsV2/{project['number']}/views",
                project_token,
                {"name": name, "layout": layout, "filter": filter_query},
            )
            SUMMARY["created"].append(f"project view {name}")
        except ApiError as exc:
            SUMMARY["skipped"].append(f"project view {name}: {exc}")


def bootstrap_project(issues: list[dict[str, Any]]) -> None:
    if not PROJECT_TOKEN:
        SUMMARY["skipped"].append("project setup: no PROJECT_TOKEN/OPENRA_PROJECT_TOKEN/GIT_ACCESS_TOKEN secret provided")
        return
    try:
        user, _, project = ensure_project(PROJECT_TOKEN)
        fields = ensure_project_fields(PROJECT_TOKEN, project)
        issue_by_title = {issue["title"]: issue for issue in issues}
        for spec in SEED_ISSUES:
            issue = issue_by_title.get(spec["title"])
            if not issue:
                continue
            item_id = add_issue_to_project(PROJECT_TOKEN, project["id"], issue["node_id"])
            if not item_id:
                continue
            for field_name, value in spec["project"].items():
                field = fields.get(field_name)
                if field:
                    update_project_field(PROJECT_TOKEN, project["id"], item_id, field, value)
            SUMMARY["created"].append(f"project item for issue #{issue['number']}")
        # Reload project to include views after field creation.
        _, _, refreshed = load_project(PROJECT_TOKEN)
        ensure_project_views(PROJECT_TOKEN, int(user["databaseId"]), refreshed)
    except ApiError as exc:
        SUMMARY["skipped"].append(f"project setup: {exc}")


def main() -> int:
    if not REPO_TOKEN:
        print("GITHUB_TOKEN/GH_TOKEN/GIT_ACCESS_TOKEN is required", file=sys.stderr)
        return 2
    ensure_labels()
    milestone_numbers = ensure_milestones()
    issues = ensure_seed_issues(milestone_numbers)
    bootstrap_project(issues)
    print("\nBootstrap summary:")
    for section in ("created", "reused", "skipped", "failed"):
        if SUMMARY[section]:
            print(f"\n{section.upper()}:")
            for item in SUMMARY[section]:
                print(f"- {item}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
