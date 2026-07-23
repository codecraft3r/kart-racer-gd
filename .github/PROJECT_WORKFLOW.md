# PAIN TAXI Collaboration Workflow

GitHub Issues and the repository's GitHub Project are authoritative for project work. Humans and local agents should follow `.hermes/skills/pain-taxi-project-manager/SKILL.md`.

## Statuses

| Status | Meaning |
|---|---|
| Backlog | Unassigned and not required for MVP |
| To Do | Unassigned and required for MVP |
| Queued | Assigned, not actively being worked on |
| In Progress | Assigned and actively being worked on |
| Ready | Branch owner approved the work for human review |
| Done | Manager merged the work into `master` |

Ready may return to In Progress, Queued, or To Do. Never reopen Done; create and link a follow-up issue.

## Accountability

- Humans own commits made under their Git/Hub identity, including agent-assisted work.
- The assigned developer owns a scoped branch until merge.
- The branch owner approves the transition to Ready.
- The manager merges into `master` and marks Done.
- Dedicated bots should primarily scan, triage, review, and advise.

## Branch safety

Agents need explicit human confirmation before switching branches, changing another branch, merging, rebasing, resetting, force-pushing, or modifying branch history. Major scopes use scoped branches; only small, known-safe changes may go directly to `master`.

## Initial GitHub setup

This repository is linked to the **PAIN TAXI** user project at <https://github.com/users/codecraft3r/projects/7>. Its Status field is configured in this order: Backlog, To Do, Queued, In Progress, Ready, Done.

A repository/project manager should:

1. Authenticate the GitHub CLI with project access: `gh auth refresh -s project`.
2. Verify access to PAIN TAXI project 7 and its linked repository.
3. Maintain the repository labels `work-item`, `blocked`, `needs-human`, and `agent-advisory`.
4. Keep `master` protected: pull requests are required, force pushes and deletion are disabled, and conversations must be resolved.
5. Give each collaborator the in-repository skill through their agent's supported skill/instruction loading mechanism.

Opaque GitHub Project IDs must be discovered with `gh project list` and `gh project field-list`; do not hard-code field or option IDs in repository files.
