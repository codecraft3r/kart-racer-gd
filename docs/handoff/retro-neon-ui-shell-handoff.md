# Handoff: Retro Neon Cab UI Shell

## Scope
- Branch: `HUSSEIN-DRIVING-and-roads`.
- Native Godot shell added from `C:\Users\Windows\Downloads\retro_neon_cab_ui (1).html`.
- The HTML demo canvas and prototype footer toolbar are intentionally not embedded; `default_3d.tscn` remains the real gameplay scene.

## Changed / Added Files
- `ui/RetroNeonCabShell.tscn` provides the lightweight `CanvasLayer` scene.
- `ui/RetroNeonCabShell.cs` builds and controls the native UI screens.
- `default_3d.tscn` instances the shell and wires the local kart plus postprocess material path.
- `tests/ui_shell_smoke_test.gd` validates default state, screen transitions, pause behavior, and shader pixelation wiring.
- `assets/fonts/` contains the bundled Google Fonts used by the reference: VT323, Press Start 2P, Orbitron, and Mr Dafoe.

## Visual Contract
- Palette mirrors the reference: neon pink `#ff007f`, cyan `#00f0ff`, purple `#9d4edd`, green `#7bb374`, grey `#8c89a0`, panel `#252331`, background `#0b0214`.
- Main menu preserves the copy and structure: `80's`, `NEON CAB`, `PRESS START TO DRIFT`, `START RUN`, `SETTINGS`, `CREDITS`, `v1.86 - PVP DRIFT EDITION`.
- Gameplay HUD exposes score, boost, speed, and `PAUSE [ESC]`.
- Pause/settings/credits preserve the reference labels and flow, including pause-context return from settings.
- CRT and scanline effects are native overlay controls so HUD/menu text stays crisp.

## Runtime Controls
- `StartRun()` resets arcade counters and unpauses gameplay.
- `TogglePause()`, `ResumeRun()`, `RestartRun()`, and `ExitToMainMenu()` manage the race shell flow.
- `OpenSettings(string fromScreen)` accepts `main` or `pause`; closing settings returns to the correct prior context.
- `SetPixelation(int factor)` updates the existing `retropostprocessing.gdshader` `pixel_size` uniform.
- `SetScanlinesEnabled(bool enabled)` and `SetCrtEnabled(bool enabled)` control the visible retro overlays.

## Verification
- Build: `dotnet build "C:\Users\Windows\Documents\kart-racer-gd\kart_racer.csproj"`.
- UI smoke: `Godot_v4.6.3-stable_mono_win64_console.exe --headless --path "C:\Users\Windows\Documents\kart-racer-gd" --script "res://tests/ui_shell_smoke_test.gd"`.
- Road smoke: `Godot_v4.6.3-stable_mono_win64.exe --headless --path "C:\Users\Windows\Documents\kart-racer-gd" --script "res://tests/road_generation_smoke_test.gd"`.
- Visible QA states to capture: main menu, gameplay HUD, pause overlay, settings with toggles/pixelation, credits.

## Follow-Up Notes
- Replace placeholder score/drift/rivals with real match state when the taxi objective loop lands.
- The title is a native approximation of the HTML chrome gradient because Godot Label text does not directly support CSS-style gradient fill.
- If future multiplayer work makes `default_3d.tscn` spawn local karts dynamically, update `KartPath` after local-player spawn or expose a retargeting method.
