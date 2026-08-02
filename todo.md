# PAIN TAXI project TODO

Last audited: 2026-08-02

This is the shared triage queue for incomplete work. It is deliberately evidence-backed and owner-labeled so agents can add detail without creating duplicate work. GitHub Issues and the PAIN TAXI project remain the authoritative workflow system; promote durable work from this file into issues using `.github/PROJECT_WORKFLOW.md`.

## Contribution guide for agents

### Ownership

| Owner | GitHub identity | Primary scope |
| --- | --- | --- |
| NM | `codecraft3r` | Networking, multiplayer authority, replication, lifecycle, fault handling, and network-coupled gameplay |
| HHE | `H-H-E` / Hussein | General implementation, gameplay systems, tests, tooling, CI, documentation, assets, cleanup, and release work |
| CS | `kitsirince` | Frontend visual changes, HUD, menus, presentation, camera feel, shaders, visual QA, and player-facing polish |

### How to add an item

1. Search this file and the relevant source before adding a new item. Prefer updating an existing item over creating a duplicate.
2. Use the next ID in the correct owner section (`CS-###`, `NM-###`, or `HHE-###`).
3. Include: priority, status, outcome, evidence (`path:line` or a reproducible command), acceptance criteria, and dependencies.
4. Mark an item `Claimed` only when the owner is actively taking it. Add the branch or issue number when one exists.
5. Keep findings separate from fixes. A suspected risk must say `Needs confirmation`; do not present it as a confirmed defect.
6. Do not mark work `Done` because a file exists. Attach the relevant build, smoke test, screenshot/video, import, or runtime evidence.
7. Do not delete completed items. Change them to `Done` with the validation evidence and link the PR/commit; create a follow-up item for newly discovered work.
8. Before pushing a shared update, pull/rebase as appropriate, inspect `git status`, and stage only the intended TODO/documentation files. Never include unrelated generated artifacts or another contributor's changes.

### Priority and status

| Priority | Meaning |
| --- | --- |
| P0 | Verification is broken or the central playable/network slice cannot be trusted without it |
| P1 | Required for the claimed vertical slice/MVP or blocks several downstream tasks |
| P2 | Important quality, polish, scale, or maintainability work after P0/P1 |
| P3 | Deferred/backlog work; useful but not a current release gate |

Use these statuses: `Open`, `Needs confirmation`, `Claimed`, `Blocked`, `Done`.

### Evidence rules

- Prefer a source path and line number, an existing test, or a command that reproduces the issue.
- Distinguish “not implemented” from “implemented but not validated” and from “documentation is stale.”
- If a task crosses owners, keep one primary owner and list the other owner under dependencies/reviewers.
- Keep this file append-friendly: preserve IDs, do not reorder another owner's section, and add a short dated note when changing priority or scope.

## Verified baseline

- `dotnet build kart_racer.sln --nologo --warnaserror` passes with 0 warnings and 0 errors.
- `tools\dev.ps1 verify` passes with Godot `4.6.3.stable.mono` and .NET SDK `8.0.422`.
- `tools\dev.ps1 test` currently stops at `audio_system_smoke_test.gd` because Godot reports `ObjectDB instances leaked at exit`; this is a confirmed release-gate failure, not a guessed TODO.
- Existing worktree changes were present before this file: `tools/blender-mcp` has submodule state and `artifacts/performance/` plus `artifacts/visual/next_steps_2026-07-25/` are untracked. They are intentionally not part of this TODO commit.

## P0 — unblock the shared baseline

### HHE-001 — Fix the smoke-test teardown leak

- Priority: P0
- Status: Open
- Outcome: Make the complete local/CI verification command finish without Godot leak diagnostics.
- Evidence: `tools/dev.ps1:112-120` rejects `ObjectDB instances leaked at exit`; `tools\dev.ps1 test` currently fails during `tests/audio_system_smoke_test.gd` after the audio scene is instantiated.
- Acceptance: `tools\dev.ps1 test` completes every smoke test, the 180-frame boot, and teardown diagnostics with no `SCRIPT ERROR`, leaked ObjectDB/RID/resource messages, or unexpected exit code.
- Dependencies: none. Review with CS because the audio smoke test starts the shell and gameplay scene.

### NM-001 — Reconcile the canonical multiplayer mode

- Priority: P0
- Status: Open
- Outcome: Decide and wire one authoritative multiplayer mode so the scene, command-line contract, tests, and docs agree.
- Evidence: `default_3d.tscn:9,119` instantiates `modes/TaxiMode.cs`; `modes/CheckpointRushMode.cs:1` exists but is not referenced by the product scene; `docs/multiplayer_contract.md:34-35` calls `checkpoint-rush` the current default/reserved mode.
- Acceptance: `default_3d.tscn`, `MultiplayerManager.MatchScenePath`, `--mode=...`, HUD, smoke tests, and docs describe the same mode; unused mode code is either wired, explicitly archived, or removed in a separate scoped change.
- Dependencies: HHE-006 documentation cleanup; CS review of the player-facing flow.

### NM-002 — Prove and fix client-local input ownership

- Priority: P0
- Status: Open
- Outcome: A connected client must send input for its own kart, and the server must accept only that peer's validated input.
- Evidence: `artifacts/performance/REPORT.md:61` records a connected client emitting zero measured input RPCs because its spawned local kart had `UseLocalInput == false`; `GameManager.cs:348-390` configures dynamic kart ownership; `Kart.cs:198-235` validates and sends input.
- Acceptance: an actively driven client produces input traffic; host logs no false rejection for the scene kart; both peers see movement and convergence; wrong-owner and stale-sequence input remains rejected; the multiplayer smoke script covers this path.
- Dependencies: NM-001. Re-run the active-client performance capture after the fix.

### NM-003 — Replicate pickup and drop-off objective state

- Priority: P0
- Status: Open
- Outcome: Every peer sees the same active fare opportunities, passenger data, claims, drop-off target, and visual markers.
- Evidence: `modes/TaxiMode.cs:337-435` creates pickup zones locally where the server runs; `modes/TaxiMode.cs:496-516` creates a new fare/drop-off on the server; `modes/TaxiMode.cs:816-887` syncs scores, phase, timer, and one destination but not pickup-zone IDs/data/availability or passenger state; `modes/TaxiMode.cs:910-927` searches the local `_activeZones` list.
- Acceptance: clients render the active pickup pool and their own objective; late joiners receive the complete current state; simultaneous claims resolve once on the server; zone removal, passenger boarding, bailout, payout, and drop-off are reflected on all relevant peers.
- Dependencies: NM-001, NM-004, CS-002.

### NM-004 — Make player state genuinely server-authoritative and visible

- Priority: P0
- Status: Open
- Outcome: Score, cash, health, active fare, panic/boarding state, loadout, and match status have one server-owned source and a tested client projection.
- Evidence: `GameManager.cs:545-665` mutates `PlayerState` and calls `SyncPlayerState` directly; `GameManager.cs:657-665` defines an authority RPC but does not broadcast state from those mutation sites; `ui/RetroNeonCabShell.cs:1132-1137` reads local `GameManager` state for HUD values.
- Acceptance: server mutations broadcast a complete versioned state snapshot/delta; clients cannot award cash, repair, damage, or alter loadouts locally; late join and reconnect get current state; HUD and results use the replicated state rather than default values.
- Dependencies: NM-003, NM-005, CS-001.

## P1 — complete the claimed multiplayer slice

### NM-005 — Finish match lifecycle and return flow

- Priority: P1
- Status: Open
- Outcome: Define and implement ready state, countdown, late-join policy, disconnect behavior, match end, results, rematch, and return-to-lobby.
- Evidence: `docs/Multiplayer_TODO.md:62-95` lists the missing lifecycle/results work; `MultiplayerManager.cs:115-177` handles connection and scene readiness but has no ready-room or explicit post-match lobby flow; `ui/RetroNeonCabShell.cs:1567-1617` shows a local results surface without a network rematch/standings contract.
- Acceptance: a 2–8 player test covers host/client join, ready/countdown, late join, disconnect, match end, ignored post-match input, results, rematch/back-to-lobby, and clean reconnect without duplicate karts.
- Dependencies: NM-001, NM-004, CS-003.

### NM-006 — Replace random/placeholder depot and interaction paths

- Priority: P1
- Status: Open
- Outcome: Use authored depot/repair/drop-off markers and explicit server-authoritative requests for respawn, repair, ammo, and delivery interactions.
- Evidence: `GameManager.cs:545-561` labels the work “respawn at depot” but resets through `GetSpawnTransform`; `docs/Multiplayer_TODO.md:81-84` calls out the placeholder depot position; `RepairShop.cs:234-311` performs server-side repair logic without a network request/response contract for remote UI.
- Acceptance: the depot is a scene/world marker, respawn positions are deterministic and collision-safe, fare/repair/ammo interactions validate peer identity and phase on the server, and clients receive progress/result/error feedback.
- Dependencies: NM-003, NM-004, CS-002.

### NM-007 — Build the first networked combat slice

- Priority: P1
- Status: Open
- Outcome: Deliver one readable weapon pickup, inventory limit, fire request, projectile/hit simulation, damage/knockback, ammo update, and impact presentation path.
- Evidence: `GameManager.cs:682-699` contains only weapon enums, a mutable `Weapon` class, a loadout dictionary, and a distance helper; `docs/TacticalTaxis_GDD.md:35-49` requires weapon pickups, loadouts, damage, and combat feedback; no projectile/weapon pickup gameplay node is wired into `default_3d.tscn`.
- Acceptance: server validates ownership, cooldown, ammo, range, and target; clients cannot spoof hits or ammo; pickup/fire/impact/health/audio/VFX state is replicated and covered by a focused multiplayer test.
- Dependencies: HHE-002 (combat design), NM-004, CS-004.

### NM-008 — Make all match-affecting randomness authoritative and testable

- Priority: P1
- Status: Open
- Outcome: Close the remaining gap between seeded track generation and unseeded match gameplay decisions.
- Evidence: `MultiplayerManager.cs:81-128` synchronizes a host seed and `TrackBuilder.cs` consumes it, but `modes/TaxiMode.cs:358-425,520-563` uses `GD.RandRange` for active fare/customer/destination choices and `GameManager.cs:338-346` uses a randomized spawn fallback.
- Acceptance: the server owns or seeds every match-affecting random decision; clients receive stable IDs/data; replaying a known seed reproduces the same initial state; the old unchecked “make track deterministic” item in `docs/Multiplayer_TODO.md` is updated to reflect what is actually covered.
- Dependencies: NM-003, HHE-006.

### NM-009 — Add network fault and security coverage

- Priority: P2
- Status: Open
- Outcome: Test latency, packet loss, reconnect, malformed requests, host migration policy, and dedicated-server behavior.
- Evidence: `docs/Multiplayer_TODO.md:105-110` leaves host migration and packet-loss/latency testing open; `docs/multiplayer_test_plan.md:15-25` has a dedicated-server smoke plan but no automated fault injection.
- Acceptance: documented host-migration decision, bounded input/state behavior under loss and latency, rejected malformed/stale/over-limit RPCs, and a passing dedicated-server run with two clients.
- Dependencies: NM-002, NM-004, NM-005.

## CS — frontend visual and player-facing work

### CS-001 — Replace placeholder HUD values with authoritative product metrics

- Priority: P1
- Status: Open
- Outcome: Every gameplay HUD label communicates a real, named value from the active mode/state contract.
- Evidence: `ui/RetroNeonCabShell.cs:705-709` labels the second pill `BoostPill` while displaying `HP`; `_objectiveLabel` is referenced at `:234,1204,1268` but the gameplay builder notes at `:818` that it was removed; the handoff explicitly says to replace placeholder score/drift/rivals in `docs/handoff/retro-neon-ui-shell-handoff.md:36`.
- Acceptance: quota progress, current cash/bank, health, panic, fare traits, boarding/drop-off progress, timer/countdown, rank/standings, objective direction, rival pressure, and weapon state have clear labels and no dead/null UI paths; UI smoke covers the state transitions.
- Dependencies: NM-001, NM-004, HHE-002.

### CS-002 — Make objective visuals resilient to dynamic/networked content

- Priority: P1
- Status: Open
- Outcome: The local player always has a readable pickup/drop-off target, marker, direction indicator, and arrival feedback in solo and multiplayer.
- Evidence: `ui/RetroNeonCabShell.cs:1314-1342` derives the objective from local `TaxiMode` lists; those lists are not replicated per NM-003; `default_3d.tscn:128` starts with a single baked camera target while dynamic players are spawned later.
- Acceptance: the camera, shell, world markers, and objective HUD retarget after local spawn; missing/late state shows an honest connection/loading state; no “SEARCHING” false negative when the server has active pickups; visual QA covers wide and ultrawide safe areas.
- Dependencies: NM-003, NM-005.

### CS-003 — Finish player-facing multiplayer/results flow

- Priority: P1
- Status: Open
- Outcome: Host/join, connection errors, player name, standings, results, rematch, and back-to-lobby states feel like one coherent front end.
- Evidence: `ui/RetroNeonCabShell.cs:628-684,902-953` builds the surfaces; `_playerNameField` is created at `:650` but is not consumed by the manager; `docs/multiplayer_contract.md:33-35` marks player name and mode as reserved.
- Acceptance: input fields have validation and feedback, the full scoreboard is visible where promised, results distinguish win/loss/shift-clear/run-over, and every button has a tested destination in both success and failure cases.
- Dependencies: NM-001, NM-005.

### CS-004 — Lock the canonical post-process and camera presentation

- Priority: P2
- Status: Open
- Outcome: Decide and document the supported post-process path and lock camera/lighting/shader values for readable racing at the target resolutions.
- Evidence: `docs/handoff/driving-roads-camera-shader-handoff.md:37-51,124-140` still calls for a pixel-size matrix, 3D-overlay vs 2D comparison, camera/lighting balance, and a final shader pass.
- Acceptance: fixed-condition captures compare pixel sizes `2,3,4,5,6`, 3D and 2D paths have a recorded decision, chase camera/road/curb/kart readability passes human review, and the chosen values are reflected in the scene and handoff.
- Dependencies: HHE-001 for repeatable capture; review by NM for network camera retargeting.

### CS-005 — Resolve visible renderer stability and visual QA matrix

- Priority: P2
- Status: Open
- Outcome: Establish a supported renderer/launch matrix and capture the required menu, gameplay, pause, settings, credits, boarding, and drop-off states.
- Evidence: `docs/handoff/driving-roads-camera-shader-handoff.md:101-119` records D3D12 instability and OpenGL3 as the current prototype fallback; `tools/dev.ps1` supports visual capture but the handoff still lists visible QA states as follow-up.
- Acceptance: the supported renderer is explicit; each capture state exists and is reviewed at 1280×720, 1920×1080, and at least one ultrawide size; no clipping, unreadable overlay, broken focus, or renderer-specific crash remains undocumented.
- Dependencies: HHE-001, CS-004.

### CS-006 — Reduce visual render-submit cost without losing readability

- Priority: P2
- Status: Open
- Outcome: Profile and reduce repeated road/building/decoration/light draw overhead in the city presentation.
- Evidence: `artifacts/performance/REPORT.md:65,74` records 4,175 draw calls and 44.37 ms render CPU in the worst visual baseline, and recommends batching/instancing/LOD work.
- Acceptance: a before/after capture uses the same scenario and documents draw calls, frame time, visual regressions, and target-device behavior; no optimization is accepted without a readable-road screenshot review.
- Dependencies: CS-005, HHE-007.

## HHE — general implementation, design, tests, tooling, and cleanup

### HHE-002 — Resolve the open Endless Run design chain before expanding systems

- Priority: P1
- Status: Open
- Outcome: Turn the open design questions into a decision-complete, implementation-ready spec with acceptance tests and dependencies.
- Evidence: `.scratch/single-player-endless-run/issues/02-shape-fare-choice-into-risk-and-reward.md`, `03-define-rival-pressure.md`, `04-set-damage-panic-bailout-and-recovery-stakes.md`, `05-choose-the-first-combat-slice.md`, `06-design-between-shift-pit-stops.md`, `07-design-the-escalation-director.md`, `08-define-score-bonuses-and-run-records.md`, `09-design-the-speed-readable-hud.md`, `10-design-the-first-run-ramp.md`, `11-set-the-replayability-content-budget.md`, and `12-assemble-the-implementation-ready-spec.md` all remain `Status: open`; only issue 01 is resolved.
- Acceptance: each open decision has an answer in the project vocabulary from `CONTEXT.md`, an owner, numeric/tuning boundaries where needed, explicit acceptance tests, and a ticket/dependency path; implementation tickets no longer depend on unresolved questions.
- Dependencies: HHE-003; CS reviews HUD/feedback decisions; NM reviews anything that enters multiplayer scope.

### HHE-003 — Complete non-network game-state foundations after the design lock

- Priority: P1
- Status: Open
- Outcome: Replace scaffolding with the agreed offline Endless Run systems: fare choice/tuning, Rival behavior, damage/panic/bailout, pit-stop choices, score/records, escalation, and run/restart persistence rules.
- Evidence: `GameManager.cs:668-737` labels customer, weapon, and economy sections as scaffolding/stubs; `docs/TacticalTaxis_GDD.md:53-71` lists core systems and future UI/match sections; the smoke suite currently validates the existing thin solo path, not the full combat/replayability promise.
- Acceptance: each implemented system has a deterministic focused test plus an end-to-end run test; cash quota remains the sole Endless Run continuation rule; fresh-run and intermission state reset/carry rules match the resolved spec.
- Dependencies: HHE-002; CS-001; NM-007 if combat is networked later.

### HHE-004 — Make economy and state mutation APIs safe and testable

- Priority: P1
- Status: Open
- Outcome: Centralize validation for payout, repair, ammo, damage, respawn, and state synchronization instead of exposing mutable helpers/stub objects.
- Evidence: `GameManager.cs:579-637,657-737` mutates state through public methods; `TryPurchaseRepair` and `TryPurchaseAmmo` accept caller-provided costs/objects; `SyncPlayerState` is a public authority RPC with no version/recipient policy.
- Acceptance: invalid callers, negative values, wrong phase, wrong peer, passenger-in-car repairs, duplicate payouts, and repeated requests are rejected; unit/smoke tests cover boundary cases; NM can reuse the same validated server path.
- Dependencies: HHE-002, NM-004.

### HHE-005 — Expand automated validation around the real release claims

- Priority: P1
- Status: Open
- Outcome: Make build, import, solo, UI, audio, world-generation, multiplayer, dedicated-server, and visual-capture checks runnable and diagnosable in one documented path.
- Evidence: `tools/dev.ps1` has build/test/capture commands; `docs/multiplayer_test_plan.md` contains manual dedicated-server and late-join steps; `tools/test_multiplayer_local.ps1` rejects logs containing `Rejected kart input`, but the current full suite stops before reaching the multiplayer tests because of HHE-001.
- Acceptance: CI and local commands report the failing test name and retain useful logs/artifacts; multiplayer tests include active client input, late join, disconnect/reconnect, and results; export-template availability is checked explicitly.
- Dependencies: HHE-001, NM-002, NM-005.

### HHE-006 — Refresh stale contracts, handoffs, and TODO checkboxes

- Priority: P1
- Status: Open
- Outcome: Make docs describe the current code, not an earlier branch state, and link every remaining checkbox to an item in this queue or GitHub.
- Evidence: `docs/Multiplayer_TODO.md` marks track determinism incomplete even though `MultiplayerManager.cs:81-128` and `TrackBuilder.cs` already synchronize a seed; the same TODO lists still-open lifecycle/state/pickup/combat/results work; `docs/multiplayer_contract.md` names `CheckpointRushMode` while `default_3d.tscn` wires `TaxiMode`.
- Acceptance: each “done” statement has current code/test evidence; stale items are closed with a note or split into a precise follow-up; mode, command-line, scene, owner, and test terminology is consistent.
- Dependencies: NM-001, NM-008, HHE-005.

### HHE-007 — Finish performance and export-readiness validation

- Priority: P2
- Status: Open
- Outcome: Validate a packaged/exported build and establish performance budgets for the city, AI, physics, audio, and network scenarios.
- Evidence: `artifacts/performance/REPORT.md:7` says export templates were missing, so no true exported release executable was produced; `:72-87` recommends render-submit profiling, physics tracing, active-client input verification, and later network rate tuning.
- Acceptance: export templates/build artifacts are available; target-machine or documented fallback captures include startup, 2/6 AI, pickup/drop-off, and multiplayer; performance budgets and regression thresholds are recorded and enforced where practical.
- Dependencies: HHE-005, NM-002, CS-006.

### HHE-008 — Complete asset provenance and audio packaging

- Priority: P2
- Status: Open
- Outcome: Produce the shippable audio/content package with normalized masters, loop metadata, attribution, and a checked manifest.
- Evidence: `docs/audio_soundscape_and_suno_brief.md:64-70,242-254` describes a later packaging pass; `assets/audio/audio_asset_manifest.csv`, `assets/audio/licenses/`, `audio_masters/README.md`, and `tools/process_suno_batch_01.ps1` are the existing inputs/tooling.
- Acceptance: every shipped asset has source/license/creator metadata, loop/loudness notes where applicable, deterministic processing output, and a smoke test that verifies the manifest matches referenced assets.
- Dependencies: HHE-005; CS reviews in-game mix/readability.

### HHE-009 — Retire or clearly label dead/legacy entry points

- Priority: P2
- Status: Open
- Outcome: Remove ambiguity between the native shell/product scene and legacy/debug UI paths.
- Evidence: `GameUI.tscn`/`GameUI.cs` and `multiplayer_lobby.tscn`/`MultiplayerLobby.cs` exist alongside the production `RetroNeonCabShell`; docs call the lobby legacy/debug-only, but the files remain easy to mistake for active entry points.
- Acceptance: each retained legacy scene has an explicit debug-only README/label and smoke coverage, or it is removed in a separate approved cleanup; no contributor starts work against an inactive UI path by accident.
- Dependencies: NM-001, CS-003, HHE-006.

## Deferred / backlog

- Host migration if the product ever requires it; currently document the unsupported policy (NM-009).
- Client prediction/interpolation beyond the current server snapshots after active input correctness is fixed (NM-002, NM-009).
- Additional cities, permanent progression, ghosts/daily runs, and final storefront/platform packaging remain outside the current Endless Run map unless the product scope changes (`.scratch/single-player-endless-run/map.md:29-34`).

## Change log

- 2026-08-02 — Initial owner-mapped triage queue created from parallel read-only audits, repository docs/source inspection, build verification, and the failing full smoke command. No existing worktree changes were staged.
