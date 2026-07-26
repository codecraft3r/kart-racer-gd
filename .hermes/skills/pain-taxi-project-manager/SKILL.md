---
name: pain-taxi-project-manager
description: Use when planning, assigning, executing, reviewing, or completing work in PAIN TAXI. Coordinates humans and local agents through GitHub Issues, Projects, pull requests, gh, and git while preserving human accountability and branch safety.
version: 1.0.0
author: PAIN TAXI contributors
license: MIT
platforms: [linux, macos, windows]
metadata:
  hermes:
    tags: [github, project-management, multi-agent, collaboration, game-development]
    related_skills: []
---

# PAIN TAXI Project Manager

## Overview

GitHub Issues and the repository's GitHub Project are the source of truth for work. This skill is the shared coordination protocol for humans and model-agnostic local agents; it does not require a coordinator service.

Humans remain accountable:

- A human owns commits made under that human's identity, including agent-assisted commits.
- A bot with its own GitHub identity may be assigned work, but bots should primarily scan, triage, review, and advise rather than change code.
- The assigned developer owns a scoped branch until merge.
- The manager owns merges into `master` and the transition to Done.

## When to Use

Use this skill to:

- inspect or update project work;
- create, classify, assign, or decompose issues;
- start or pause assigned work;
- coordinate work by humans and agents;
- prepare work for review;
- review and merge a scoped branch;
- report blockers, evidence, or follow-up work.

Do not use it to silently change another branch or rewrite Git history.

## Setup Check

Before any mutation, run:

```sh
gh auth status
gh repo view --json nameWithOwner,defaultBranchRef,url
git status --short --branch
git config user.name
git config user.email
```

Stop and explain the missing prerequisite if:

- `gh` is not authenticated as the intended human or bot;
- the repository cannot be identified;
- commit identity is absent or incorrect;
- unresolved conflicts or unrelated local changes make the requested action unsafe.

Project operations require the `project` scope. If needed, tell the user to run this themselves:

```sh
gh auth refresh -s project
```

Never expose tokens or credential contents.

## Project Workflow

Use exactly these Status values:

| Status | Meaning | Required invariant |
|---|---|---|
| Backlog | Unassigned work not required for MVP | No assignee; not MVP-required |
| To Do | Unassigned work required for MVP | No assignee; MVP-required |
| Queued | Assigned work not actively being worked on | At least one assignee |
| In Progress | Assigned work actively being worked on | At least one assignee |
| Ready | Work needs human review and approval | Branch owner has approved readiness |
| Done | Work was merged into `master` | Manager verified the merge |

Allowed normal transitions:

```text
Backlog ─┐
         ├─> Queued -> In Progress -> Ready -> Done
To Do ───┘                         ↙   ↓    ↘
                           In Progress  Queued  To Do
```

Rules:

1. Assignment moves Backlog or To Do work to Queued.
2. Starting active work moves Queued to In Progress.
3. Only the scoped branch owner declares work Ready.
4. Review rejection returns Ready to In Progress, Queued, or To Do according to whether revisions are active, deferred-and-assigned, or unassigned-and-MVP-required.
5. Only the manager moves work to Done, and only after verifying merge into `master`.
6. Never reopen a Done issue. Create a linked follow-up issue instead.
7. Closing an issue is not proof of Done. A merged pull request or a verified direct commit on `master` is required.

Use labels such as `blocked`, `needs-human`, and `agent-advisory` as orthogonal signals; do not invent extra workflow statuses.

## Read Before Writing

Resolve repository metadata dynamically rather than hard-coding account, project, field, or option IDs.

```sh
REPO="$(gh repo view --json nameWithOwner --jq .nameWithOwner)"
OWNER="${REPO%/*}"
gh issue list --repo "$REPO" --state open --limit 100
gh project list --owner "$OWNER" --format json
```

Before changing one issue, read its body, assignees, labels, comments, linked pull requests, and current project fields:

```sh
gh issue view ISSUE --repo "$REPO" --comments --json number,title,body,state,assignees,labels,projectItems,url
```

Before changing a pull request or declaring work Ready/Done:

```sh
gh pr view PR --repo "$REPO" --json number,title,state,isDraft,baseRefName,headRefName,mergeable,reviewDecision,statusCheckRollup,closingIssuesReferences,url
```

Completion criterion: the agent can state the current issue status, assignee, branch owner, linked branch/PR, and requested transition without guessing.

## Chat-to-GitHub Protocol

For each request:

1. **Interpret:** identify the requested issue, transition, assignment, or decomposition.
2. **Inspect:** read current GitHub and Git state.
3. **Validate:** check the workflow invariant and branch-safety boundary.
4. **Confirm when required:** ask before crossing branch boundaries or changing history.
5. **Execute:** use `gh` and `git`; make the smallest authoritative mutation.
6. **Read back:** query GitHub/Git again and verify the resulting state.
7. **Report:** name what changed, who owns it, its status, and its URL. Never claim success from command intent alone.

A request to discuss, brainstorm, estimate, or explain is read-only unless the human clearly asks for a mutation.

## Branch Safety Boundary

The currently checked-out branch is the agent's execution boundary.

Explicit human confirmation is required before:

- switching to another branch;
- creating commits on another branch;
- merging or rebasing;
- resetting or reverting history;
- force-pushing;
- deleting, renaming, or otherwise altering another branch;
- any operation that modifies the history of the current branch.

Ordinary edits, tests, commits, and non-force pushes on the current scoped branch are allowed when they implement assigned work. The agent must still follow repository-level instructions and must not commit unrelated pre-existing changes.

If confirmation is denied, leave the working tree and branches unchanged and report the blocked operation.

## Issues and Decomposition

Use `.github/ISSUE_TEMPLATE/work-item.yml` for work items. Each issue should make these facts legible:

- outcome;
- MVP requirement;
- acceptance criteria;
- assets or code areas involved;
- dependencies;
- validation evidence;
- branch/PR when scoped work begins.

Use GitHub task lists and native linked sub-issues where available. Otherwise, maintain explicit links in the parent body:

```markdown
## Sub-issues
- [ ] #123 Short outcome
- [ ] #124 Short outcome
```

A sub-issue must have an independently verifiable outcome. Do not create an issue for every agent action. Local agents without bot identities work under the authorizing human; do not fabricate an assignee identity for them.

When assigning an issue:

1. Set the human or authenticated bot assignee.
2. Move the Project Status to Queued.
3. Comment only when ownership, branch, dependency, or handoff context is not already evident.
4. Read the issue back and verify both assignee and status.

## Scoped Branches and Small Changes

Use a scoped branch for major work. Very small, known-safe changes may be committed directly to `master`, but only when `master` remains buildable and the manager's repository policy permits it.

For scoped work:

1. Identify the issue and assigned developer.
2. Confirm before creating or switching branches.
3. Name the branch for its scope, not for an agent session (for example, `fare-payout-balancing`).
4. Record the branch in the issue.
5. Keep one assigned developer as branch owner even when multiple developers contribute.
6. Open a pull request to `master` and link the issue.

Do not create one branch per tiny subtask or one branch per agent.

## Moving Work to Ready

Only the branch owner may authorize this transition. Before moving an issue to Ready:

1. Verify implementation and acceptance criteria.
2. Run relevant tests/builds and capture real results.
3. Ensure the pull request targets `master`, is not merely an unpushed local branch, and links the issue.
4. Summarize validation evidence in the pull request or issue.
5. Obtain the branch owner's explicit readiness approval if the current user is not clearly that owner.
6. Move Project Status to Ready and read it back.

Ready means awaiting manager review/approval; it does not mean merged.

## Moving Work to Done

Only the manager performs this action. Before moving an issue to Done:

1. Confirm the actor is the manager.
2. Confirm the linked pull request's base is `master` and GitHub reports it merged, or verify the allowed direct commit is contained in `master`.
3. Confirm required checks/review have passed according to repository policy.
4. Move Project Status to Done.
5. Close the issue with a final reference to the pull request and merge commit when that context is not already linked.
6. Read back the issue and project item.

If further work is discovered after Done, create a new Backlog or To Do issue and link it as a follow-up. Never reopen the completed issue.

## Project Field Mutations

Prefer `gh project item-edit`; use `gh api graphql` only when a required feature lacks direct CLI support.

Discover IDs every session:

```sh
gh project list --owner "$OWNER" --format json
gh project field-list PROJECT_NUMBER --owner "$OWNER" --format json
```

Then resolve the issue's project item and the exact Status option by name. Never copy opaque IDs from comments, examples, or another repository. After editing, query the item and verify the human-readable Status value.

## Asset-Aware Evidence

Game work is not limited to code. Match evidence to the deliverable:

| Deliverable | Minimum useful evidence |
|---|---|
| C# / GDScript / project files | Build plus focused test or reproducible manual check |
| Godot scene or resource | Import succeeds plus scene/smoke test or screenshot/video |
| 3D model | Source/export provenance, import succeeds, scale/material check, screenshot |
| Audio | Source/license or generation provenance, import/playback check, loudness/loop notes when relevant |
| UI/art | In-engine screenshot at relevant resolutions and interaction check |
| Documentation/design | Human review against the requested decision or acceptance criteria |

Do not mark work Ready based only on files existing. Verify them in the relevant tool or engine whenever possible.

## Advisory Bots

Bots with dedicated identities may be assigned scanning or advisory issues. Their output must be evidence, not authority:

- include inspected revision/branch;
- cite files, lines, assets, or commands;
- distinguish confirmed defects from suggestions;
- label advisory output `agent-advisory`;
- never approve readiness, merge, or mark Done on a human's behalf.

## Common Pitfalls

1. **Treating Closed as Done:** verify merge into `master` before Done.
2. **Reopening history:** create a linked follow-up instead of reopening Done.
3. **Unassigned Queued work:** Queued and In Progress require an assignee.
4. **Agent self-approval:** Ready requires the branch owner's human approval; Done requires the manager.
5. **Hidden agent identity:** local agents act under the authenticated human; dedicated bots use their own accounts.
6. **Blind Project IDs:** discover and read back all IDs and options.
7. **Branch escape:** obtain confirmation before branch switching, cross-branch changes, or history modification.
8. **Code-only validation:** require appropriate evidence for models, audio, scenes, art, and documentation.
9. **Status-only updates:** keep assignee, issue, branch, PR, and Project Status consistent.

## Verification Checklist

- [ ] `gh` and Git identities match the intended actor
- [ ] Current branch and working tree were inspected
- [ ] Issue and Project state were read before mutation
- [ ] Result obeys Backlog/To Do/Queued/In Progress/Ready/Done invariants
- [ ] Branch owner approved Ready
- [ ] Manager alone merged and marked Done
- [ ] Done is backed by a merge into `master`
- [ ] No Done issue was reopened
- [ ] Branch/history confirmation was obtained when required
- [ ] GitHub state was read back after mutation
- [ ] Report includes ownership, status, evidence, and links
