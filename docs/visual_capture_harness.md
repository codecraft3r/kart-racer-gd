# PAIN TAXI scenario harness

The harness captures game states and checks the observations that make those states meaningful. Each run has its own directory, logs, result records, source/build identity, and a review page. A saved screenshot by itself is not a passing scenario.

## Everyday workflow

Use PowerShell 7 and the pinned Godot 4.6.3 Mono/.NET 8.0.422 toolchain:

```powershell
.\tools\dev.ps1 verify
.\tools\dev.ps1 build
.\tools\dev.ps1 test
.\tools\dev.ps1 capture -State menu
.\tools\dev.ps1 capture -Suite core
```

`tools/visual_harness.ps1` is a compatibility entry point to the same capture runner. It does not implement a separate capture policy.

```powershell
.\tools\visual_harness.ps1 -State gameplay -Seed 1337 -CameraPreset hood
.\tools\visual_harness.ps1 -State drift -BurstCount 5 -BurstInterval 0.15
.\tools\visual_harness.ps1 -Scene res://assets/neon-cab/neon_cab_game.tscn -CameraPreset orbit
.\tools\visual_harness.ps1 -State gameplay -CameraPos "10,5,15" -CameraTarget "0,0,0" -CameraFov 55
.\tools\visual_harness.ps1 -State menu -Crt:$false -Scanlines:$false
```

Pass `-GodotPath` or set `GODOT` if the pinned engine is not discoverable. Capture builds the C# project before execution. A skip-build option must validate the existing build receipt; it is not permission to use an arbitrary old DLL.

## Scenarios and evidence

The registry in `tests/harness/scenarios.json` owns valid state and suite names. Unknown names fail. The core suite covers menu, gameplay, Endless Road, vehicle, boarding, drop-off, repair, and settings. Other suites cover the broader state inventory, camera presets, vehicle variants, and gameplay flow.

Fixture mode arranges a state for visual inspection, including controlled teleporting where appropriate. Journey mode exercises UI/input paths and asserts their resulting state. A fixture screenshot is not proof that a player can complete the corresponding journey. Unsupported journey requests must fail instead of silently using fixture shortcuts.

The drift fixture supplies controlled velocity and driving input, then verifies the game's actual drift phase. UI journeys dispatch viewport input and check the resulting screen and content area. Asset scene viewing checks the loaded target, renderer, and camera; it does not claim a gameplay journey.

The scenario engine applies the seed before the scene enters the tree, waits for bounded state predicates, and records assertions and typed observations. Capture follows the rendering server's completed-frame signal. Motion scenarios use fixed simulation pacing for review; they are not frame-time benchmarks.

Each scenario runs in a fresh process through the public runner. Its profile is separate from normal player settings, bindings, unlocks, and records. Required metadata remains mandatory even when optional companion telemetry is disabled.

## Reading the result

The default artifact root is `artifacts/harness/`. Each invocation creates a unique run directory so earlier or concurrent results cannot satisfy a new run. Generated artifacts are ignored by Git and Godot's importer.

The run contains an aggregate manifest and an HTML review page, with per-scenario result JSON, images, and retained process logs. The engine result contract includes:

```json
{
  "schema_version": 1,
  "run_id": "unique-run-id",
  "state": "gameplay",
  "status": "passed",
  "seed": 1337,
  "setup_mode": "fixture",
  "resolution": {"width": 1920, "height": 1080},
  "errors": [],
  "assertions": [{"name": "example readiness assertion", "passed": true, "observed": "example value"}],
  "artifacts": [{"kind": "image", "path": "absolute-path-to-image.png"}],
  "observations": {"gameplay": {"probe": {"harness_observed": true, "actual_screen": "gameplay", "phase": "active"}}},
  "cleanup_complete": true
}
```

This is a schema illustration, not an actual successful run. `result.json` is the authoritative metadata record; per-image JSON is companion telemetry. The supervisor checks result freshness, process completion, diagnostics, identity, and fresh PNG structure, checksums, and decompressed scanlines. A failed required suite member fails the entire suite. Timeouts retain diagnostics and cannot be reported as success.

Use the manifest's source fingerprint and assembly hash when comparing runs. Git HEAD alone is insufficient for a dirty checkout. Compare visual output within the same engine/renderer/environment profile; changes in GPU, driver, platform, or physical simulation can change pixels even when the seed is identical.

The source fingerprint includes relevant tracked edits and untracked source files. Generated output, caches, UID sidecars, and untracked import sidecars are excluded. Tracked importer settings remain part of the fingerprint. The profile smoke test verifies that saving harness settings leaves normal player settings and records unchanged.

## Rendering and performance

Local visual capture uses a rendered Godot process with background window handling and silent audio. Godot's actual `--headless` mode disables rendering and cannot supply viewport screenshots. `-Visible` is for explicitly viewing the game window. CI uses its existing Xvfb graphical environment for rendered checks.

Keep real-time performance measurement separate from fixed-pacing captures. Do not interpret a CI/software-rendered screenshot, a node-count budget, or an offline capture duration as proof of target Windows GPU frame time.

## Agent use and optional live exploration

An agent should first reproduce an issue with a named scenario, inspect its result/observations and image, make a bounded change, and rerun the affected scenarios. Preserve the result path in the handoff. Promote fixes only after the ordinary command-line checks pass.

The repository also contains a pinned Godot MCP Runtime dependency under `tools/godot-mcp-runtime`. It can support live screenshots, UI discovery, and input exploration when connected to the agent host. That optional session is separate from the authoritative command-line runner. Record useful reproductions as repeatable scenarios, and rebuild/restart after C# changes. Merely having the dependency on disk does not establish a working MCP connection.

For concurrent work, use distinct checkouts, profiles, artifact directories, and runtime ports. Serialize GPU performance measurements. Follow `.github/PROJECT_WORKFLOW.md` for project ownership and review; the harness does not replace it.

## Validation and design rationale

`tests/harness_runner_tests.ps1` checks the supervisor with controlled subprocesses and artifacts, including failure and timeout paths. CI runs those checks alongside the pinned build, smoke tests, and rendered capture.

The original findings and primary-source research are recorded in [the September 20 design review](harness-review-2026-09-20.md). GdUnit4Net remains a possible later testing-framework pilot; the existing GDScript smoke tests remain part of this implementation.
