# Handoff: Driving, Roads, Camera, and Shader

## Scope / branch
- Working branch: `HUSSEIN-DRIVING-and-roads`.
- `TrackBuilder.cs`, `TrackCamera.cs`, `Kart.cs`, `assets/shader/retropostprocessing.gdshader`, `kart.tscn`, and `default_3d.tscn` contain in-progress visuals, camera, and road-generation tuning work.
- `tests/road_generation_smoke_test.gd` exists for deterministic validation.
- `tests/scale_capture_driver.gd` and `tools/record_scale_review.ps1` now provide the deterministic object-scale capture pass.

## Deterministic scale video workflow
- Generate the current audit video with:
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\record_scale_review.ps1`
- Output artifacts are ignored by git under `captures/`:
  - `captures/road_scale_review.avi`
  - `captures/scale_capture_000.png`
  - `captures/scale_capture_075.png`
  - `captures/scale_capture_150.png`
  - `captures/scale_capture_225.png`
  - `captures/scale_capture_300.png`
- The capture script loads `res://default_3d.tscn`, detaches the live chase camera script for deterministic framing, and records:
  1. top-down full track scale,
  2. road-width orbit,
  3. curb cross-section,
  4. roadside building/light scale,
  5. close kart-vs-road pass.
- Current video smoke result: `road_scale_review.avi` recorded 362 frames at 30 FPS, about 12 seconds, with five still frames saved successfully.
- Wide capture revealed the world floor needed more visual buffer, so `default_3d.tscn` ground size is now `Vector3(260, 1, 260)`.

## Interactive control workflow
- Launch visible game for Windows-control testing:
  - `"<Godot4.6.3MonoPath>\Godot_v4.6.3-stable_mono_win64.exe" --path "C:\Users\Windows\Documents\kart-racer-gd" --scene res://default_3d.tscn --resolution 854x480 --position 120,120`
- Target the window titled `kart_racer (DEBUG)`, click the game viewport, then send repeated `w`, `a`, `s`, and `d` key taps.
- `project.godot` now binds both physical and virtual W/A/S/D key events so Windows automation taps are recognized.
- Verified through Computer Use: baseline screenshot showed the kart on the road; after repeated `w` and `d` taps, the camera/kart moved to a different course position.

## Shader work and screenshot tuning goal
- Goal of this continuation pass is to finalize a stable retro look while keeping scene clarity for track visibility and input readability.
- Current 3D post stack in `default_3d.tscn` is:
  - `WorldEnvironment` (`Environment_race`) + lighting nodes + camera + `MeshInstance3D` overlay.
  - `TrackCamera` -> `MeshInstance3D` + `ShaderMaterial_erduv` -> `assets/shader/retropostprocessing.gdshader`.
- Current shader behavior in `assets/shader/retropostprocessing.gdshader`:
  - Spatial shader with `render_mode unshaded, fog_disabled, depth_draw_never, cull_disabled`.
  - Uses screen-space quantization controlled by integer `pixel_size` (1-16).
  - No color quantization/dither in this revision.
- Current tuned value after visible screenshots: `pixel_size=3`. This kept car/curb silhouettes readable while still looking deliberately pixelated.
- Screenshot tuning objective:
  - Build a baseline matrix for `pixel_size` and compare in-editor screenshots (before lighting/camera finalization):
    - Candidate sizes: `2, 3, 4, 5, 6`.
    - Keep capture conditions fixed (resolution, viewport size, speed, FOV baseline).
  - Compare 3D overlay path (`default_3d.tscn`) against 2D path (`default.tscn` + `retropostprocessing_2d.gdshader`) before deciding canonical route.
- Useful baseline scenes:
  - Primary (3D overlay): `res://default_3d.tscn`
  - Alternate fallback (2D post): `res://default.tscn`

## Racing lighting and camera systems (current behavior)
- `default_3d.tscn` now provides race-day lighting scaffolding:
  - `RaceWorldEnvironment` (ambient + fog).
  - `RaceSun` directional key light.
  - `PaddockFillLight` helper omni fill.
  - Dynamic track lights in `TrackBuilder`:
    - `TrackLightPole*`, `TrackLightHead*`, `TrackLightGlow*` generated in ring pattern.
- `kart.tscn` currently adds:
  - `HeadlightLeft` / `HeadlightRight` `SpotLight3D` under `VisualContainer`.
- `TrackCamera.cs` now includes a more race-oriented feel stack:
  - Follow/look smoothing (`FollowSpeed`, `LookAtSpeed`).
  - Speed-reactive offset (`SpeedPullback`, `SpeedLift`, `LookAheadDistance`).
  - Dynamic FOV (`MinFov`, `MaxFov`, `FovLerpSpeed`) using `MaxReferenceSpeed`.
  - Lateral roll (`MaxRollDegrees`, `RollResponse`) derived from body velocity.
  - Targeting still depends on `TargetVehiclePath`, expects `CameraTarget` and `VisualContainer` child nodes.
- `default_3d.tscn` camera tuning today:
  - `BaseDistance=6.0`, `BaseHeight=2.65`, `SpeedPullback=3.4`, `LookAheadDistance=6.2`, `MinFov=62`, `MaxFov=82`.
- Risk to track: camera is still a single-node local target (`../Kart`) and requires multiplayer-aware retargeting later.

## Road-generation smoke test status
- Track geometry + ambience generation happens in `TrackBuilder._Ready()`, with randomization controlled by `Seed`.
- The current layout is now a seeded city grid, not a circular track:
  - City blocks: `CityColumns`, `CityRows`, `CityBlockSize`, and `CityBlockSizeJitter`.
  - Street hierarchy: primary avenues use `MainAvenueWidthMultiplier`; side streets use `SideStreetMinWidthMultiplier`/`SideStreetMaxWidthMultiplier`.
  - Street panels: `RoadSegmentHorizontal*` and `RoadSegmentVertical*`.
  - Intersections/turns: `Intersection*` plus `Crosswalk*`.
  - Dashed lane markers: `LaneMarker*`.
  - Curbs with open intersections: `CurbMarkerHorizontal*` and `CurbMarkerVertical*`.
  - Lighting props: `TrackLightPole*`, `TrackLightHead*`, `TrackLightGlow*`.
  - Buildings/decorations are placed inside block lots from `assets/kenney_industrial`.
- `tests/road_generation_smoke_test.gd` checks:
  - `TrackBuilder` node exists.
  - expected counts for road panels, intersections, curbs, crosswalks, track lights, buildings, and decorations.
  - lane markers are generated across the city street network.
  - street widths include both wider avenues and narrower side streets.
  - generated buildings stay inside block lots and out of road bands.
- Current default values in `default_3d.tscn` for smoke-test alignment:
  - `CityColumns=6`, `CityRows=4`, `CityBlockSize=27`, `TrackWidth=15`, `TrackLightCount=64`, `BuildingCount=88`, `Seed=1337`.
- Follow-up scale pass:
  - Imported buildings are now normalized to a target footprint range (`BuildingFootprintMin=7.5`, `BuildingFootprintMax=13.0`) and grounded after scaling.
  - Buildings now use block-relative slot offsets plus `BuildingSetback=7.0` and `BuildingJitter=5.5` so larger assets do not crowd the road edge.
  - Decorations are normalized to a smaller target footprint range (`DecorationFootprintMin=3.0`, `DecorationFootprintMax=5.5`).
  - Generated buildings/decorations receive stable names (`BuildingBlock*`, `Decoration*`) for debugging and tests.
  - Lane markers are generated per street and skip intersection bands.
  - Curbs are segmented between intersections so turns stay visually open.
- `tests/road_generation_smoke_test.gd` now validates city road panels, intersections, lane markers, curb segments, crosswalks, lights, building/decor counts, building scale, street width hierarchy, and buildings staying out of road bands.

## Visible-launch crash investigation
- D3D12 visible launch was unstable with the current postprocess/lighting scene, while headless checks passed.
- Launching with OpenGL3 survived and produced usable screenshots, so `project.godot` now sets:
  - `renderer/rendering_method="gl_compatibility"`
  - `rendering_device/driver.windows="opengl3"`
- Treat OpenGL3 as the current prototype baseline. Revisit D3D12 later by isolating the postprocess quad, render flags, and dynamic lights one at a time.

## Validation commands
- Build C# project:
  - `dotnet build "C:\Users\Windows\Documents\kart-racer-gd\kart_racer.csproj"`
- Headless road smoke test:
  - `"<Godot4.6.3MonoPath>\\Godot_v4.6.3-stable_mono_win64.exe" --headless --path "C:\Users\Windows\Documents\kart-racer-gd" --script "res://tests/road_generation_smoke_test.gd"`
- Headless scale-capture script validation:
  - `"<Godot4.6.3MonoPath>\\Godot_v4.6.3-stable_mono_win64_console.exe" --headless --fixed-fps 30 --path "C:\Users\Windows\Documents\kart-racer-gd" --script "res://tests/scale_capture_driver.gd"`
- Deterministic scale video:
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\record_scale_review.ps1`
- Visible run (baseline behavior check):
  - `"<Godot4.6.3MonoPath>\\Godot_v4.6.3-stable_mono_win64.exe" --path "C:\Users\Windows\Documents\kart-racer-gd" --verbose`
  - Current branch should launch visibly without extra renderer flags because `project.godot` now uses OpenGL3 compatibility mode.
- 3D overlay vs 2D fallback comparison:
  - `"<Godot4.6.3MonoPath>\\Godot_v4.6.3-stable_mono_win64.exe" --path "C:\Users\Windows\Documents\kart-racer-gd" --scene res://default_3d.tscn --verbose`
  - `"<Godot4.6.3MonoPath>\\Godot_v4.6.3-stable_mono_win64.exe" --path "C:\Users\Windows\Documents\kart-racer-gd" --scene res://default.tscn --verbose`

## Next-agent instructions
- Continue shader tuning pass:
  1. Capture fixed camera screenshots across pixel_size candidates.
  2. Decide canonical post stack (3D overlay vs 2D SubViewport path).
  3. Add any final shader controls (optional color quantization/dither) only after base pixel readability is stable.
- Continue camera/lighting balancing:
  1. Lock tuning values for race readability and chase feel in `TrackCamera`/`default_3d.tscn`.
  2. Revisit headlight/track-light intensity/color against road marker and curb readability.
- Resolve launch stability and remove uncertainty:
  1. Confirm whether the crash is backend-dependent by reproducing with one scene at a time.
  2. If reproducible, isolate by disabling overlay shader and capturing a minimal crash repro.
  3. Log exact steps/output (render driver + launch args) before deeper code changes.
- Finish deterministic track/multiplayer follow-up:
  1. Keep `Road`/seed generation stable and validated by smoke test.
  2. Preserve `TrackBuilder` naming scheme required by smoke test.
  3. Carry forward camera ownership/retargeting work so multiplayer locals follow spawned karts (single local target path is insufficient).
