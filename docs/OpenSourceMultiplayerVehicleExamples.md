# Open-source multiplayer vehicle examples

Research date: 2026-07-05

## What was pulled locally

Temporary research clones were made at:

`C:\Users\Windows\AppData\Local\Temp\kart-racer-oss-research-20260705`

Pulled repos:

- `DAShoe1/Godot-Easy-Vehicle-Physics` - MIT, Godot 4.2+ GDScript raycast vehicle addon.
- `Dechode/Godot-Advanced-Vehicle` - MIT, Godot GDScript advanced raycast vehicle demo.
- `remram44/godot-multiplayer-example` - MIT, Godot 3 local plus network multiplayer sample.
- `Jibby-Games/Flappy-Race` - MIT code, Godot 3 online multiplayer racing-adjacent game.
- `kuba--/f1` - Unlicense, Godot 3.5 racing game.
- `godotengine/godot-demo-projects` sparse checkout for `networking/multiplayer_bomber` and `networking/multiplayer_pong` - MIT official samples.

## Best direct-use candidates

| Project | Fit | License | Decision |
| --- | --- | --- | --- |
| [Godot Easy Vehicle Physics](https://github.com/DAShoe1/Godot-Easy-Vehicle-Physics) | Strong vehicle physics fit, Godot 4, arcade/simcade setups | MIT | Best candidate to vendor later under `addons/gevp` if we replace the current sphere-kart controller. Not imported yet because it is GDScript and would require scene/controller migration. |
| [Godot Advanced Vehicle](https://github.com/Dechode/Godot-Advanced-Vehicle) | Strong sim vehicle reference, more complex than needed | MIT | Reference or later import for drivetrain/tire model ideas. Too heavy for the current first multiplayer pass. |
| [Godot demo projects](https://github.com/godotengine/godot-demo-projects) | Strong Godot 4 multiplayer pattern match | MIT | Pattern adapted now: stable peer-id player node names, server spawn authority, clients wait until match scene is loaded before requesting spawn. |
| [Flappy Race](https://github.com/Jibby-Games/Flappy-Race) | Mature online multiplayer race flow, server-owned state | MIT for code | Pattern reference: client intent RPCs, server validation, world-state broadcasts, ready/late-join flow. No direct code copied due Godot 3/GDScript architecture. |
| [remram44/godot-multiplayer-example](https://github.com/remram44/godot-multiplayer-example) | Small and readable network input sample | MIT | Pattern reference: client sends input to server; server validates sender against controlled object. Adapted into `Kart.OwnerPeerId` plus sender validation. |
| [kuba--/f1](https://github.com/kuba--/f1) | Useful 3D-ish racing feel and circuit/UI structure | Unlicense | Safe reference for race-game mechanics, but Godot 3.5 and not online multiplayer. |

## Reference-only / avoid direct copy

| Project | Why not direct-copy |
| --- | --- |
| [KevinVG207/GodotUmaKart](https://github.com/KevinVG207/GodotUmaKart) | Closest online kart game found, but GPL-3.0 and fan-content constraints. Use for high-level architecture ideas only unless this project intentionally adopts GPL-compatible distribution. |
| [KevinVG207/GodotUmaKartServer](https://github.com/KevinVG207/GodotUmaKartServer) | Same GPL-3.0 concern. |
| [electronstudio/godot_racing](https://github.com/electronstudio/godot_racing) | AGPL-3.0; avoid direct integration unless the whole project accepts AGPL obligations. |

## What was adapted into this repo

- `MultiplayerManager.cs` now behaves like a persistent networking singleton, creates ENet peers, resets cleanly, and loads the shared match scene.
- `GameManager.cs` now follows the official Godot sample pattern: clients announce match-scene readiness, the server spawns karts using stable peer-id node names, and spawn/remove RPCs are authority-only.
- `Kart.cs` now tracks `OwnerPeerId` and rejects input RPCs from the wrong sender.
- `default_3d.tscn` now includes `GameManager`, so every peer has the same RPC node path in the match scene.
- `multiplayer_lobby.tscn` now has an address field instead of localhost-only join.

## Next reuse step

If vehicle feel becomes the priority, vendor `DAShoe1/Godot-Easy-Vehicle-Physics/addons/gevp` with its MIT license and build a new `gevp_kart.tscn` prototype beside the current `kart.tscn`. Do not replace the current C# kart until the GDScript addon is tested with `MultiplayerSynchronizer` on position, rotation, velocity, and any visual wheel state.
