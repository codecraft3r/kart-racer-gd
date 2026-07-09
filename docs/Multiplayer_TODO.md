# Multiplayer TODO

## Current State

- `MultiplayerManager.cs` is an autoload that can create an ENet server or client on port `7000`, load the match scene, and reset to offline mode on disconnect/failure.
- `MultiplayerLobby.cs` exposes Host, Join, Disconnect, and an IP/address field.
- `GameManager.cs` has early server-side player state, match timer, spawn, respawn, customer, weapon, and economy scaffolding.
- `Kart.cs` uses a server-authoritative input model, validates input by owning peer id, and a `MultiplayerSynchronizer` exists in `kart.tscn`.
- The main project scene is still `default_3d.tscn`, which contains one local kart and no multiplayer flow.

## First Playable Multiplayer Test

- [x] Wire `MultiplayerManager` into the running game.
  - Added it as an autoload.
  - Guarded `MultiplayerManager.Instance` against duplicate or missing instances.
  - Unsubscribes multiplayer signals on exit.

- [ ] Decide the boot flow.
  - Make `multiplayer_lobby.tscn` the main scene, or add the lobby UI to a boot scene.
  - [x] After Host or Join succeeds, transition into the shared match scene.
  - [x] Add an IP/address field instead of hardcoding `127.0.0.1`.
  - Disable buttons while connecting and show connection failure/server disconnect status.

- [x] Put `GameManager` in the match scene or make it an autoload.
  - `default_3d.tscn` now instantiates `GameManager`.
  - Confirm only the server owns match start, match timer, spawning, scoring, pickups, and purchases.

- [x] Replace ad hoc server spawning with Godot multiplayer spawning.
  - Explicit authority-only RPC spawn/despawn is used for clients.
  - Every peer sees the same player kart nodes with stable peer-id names.
  - The host player and scene-ready clients are spawned by the server.
  - The single baked-in `Kart` from `default_3d.tscn` is gated during network matches.

- [x] Fix local player ownership and input.
  - Kart authority now stays server-owned.
  - `OwnerPeerId` identifies the peer allowed to send input.
  - `Kart._Process()` sends client input with `RpcId(1, nameof(SendInputRpc), forward, steer)`.
  - `IsLocalPlayer` is set on each client for that client's owned kart after the kart exists locally.

- [x] Validate server-authoritative input.
  - The server accepts input only from the peer that owns that kart.
  - Latest input is stored on the owning kart after sender validation.
  - Input values are clamped on the server.
  - Keep transfer mode unreliable for frequent input, but send important events reliably.

- [ ] Synchronize visible kart state.
  - Position, rotation, and linear velocity are listed in `kart.tscn`.
  - Add any missing visual state required for remote clients, especially yaw or visual container rotation if it is not derived cleanly from synced physics.
  - Test whether `RigidBody3D` replication is stable enough, or whether server snapshots/interpolation are needed.

- [ ] Rework the camera for multiplayer.
  - The current camera targets a single scene kart by NodePath.
  - In network matches, attach or retarget the local camera to the local player's spawned kart.
  - Keep remote karts visible without giving them active cameras.

## Match and Gameplay Networking

- [ ] Make track generation deterministic or server-driven.
  - `TrackBuilder` currently randomizes locally.
  - Use a server-chosen seed replicated to clients, or build the track only on the server and replicate spawned obstacles.

- [ ] Finish player lifecycle.
  - Add ready state in lobby.
  - Add match countdown.
  - Handle late join policy.
  - Handle player disconnects during a match.
  - Return everyone to lobby after match end.

- [ ] Finish replicated player state.
  - Make score, money, health, active fare, held weapons, and current status server-owned.
  - Change `SyncPlayerState` to server-only broadcast instead of `AnyPeer`.
  - Call state sync after payout, repair, ammo purchase, damage, respawn, pickup, and delivery.
  - Add HUD binding for local and scoreboard state.

- [ ] Build pickup and customer replication.
  - Server chooses active pickup zones and customer data.
  - Replicate pickup availability and lock/claim state to clients.
  - Prevent two players from claiming the same customer at the same time.
  - Add load timers and cancel conditions.

- [ ] Build delivery/depot flow.
  - Replace the placeholder depot position in `RespawnAtDepot`.
  - Add actual depot markers to the scene.
  - Network repair, ammo, respawn, and delivery interactions as server-authoritative requests.

- [ ] Build combat replication.
  - Define weapon pickup, inventory limits, fire requests, projectile/hit simulation, damage, and knockback on the server.
  - Replicate weapon pickups, fired projectiles, impact effects, ammo counts, and health changes.
  - Add anti-spam cooldowns and validate fire requests.

- [ ] Add match results.
  - Broadcast final scores when the server timer ends.
  - Freeze or ignore gameplay input after match end.
  - Show standings and allow rematch/back-to-lobby.

## Testing Checklist

- [ ] Run two local instances: one host and one client on `127.0.0.1`.
- [ ] Verify host sees both karts.
- [ ] Verify client sees both karts.
- [ ] Verify each instance controls only its own kart.
- [ ] Verify movement is visible on the other instance.
- [ ] Verify disconnect removes the correct kart on all peers.
- [ ] Verify host migration is intentionally unsupported or explicitly handled.
- [ ] Test packet loss/latency once basic local play works.

## Known Risks

- The current lobby scene can call a null `MultiplayerManager.Instance` unless the manager is created elsewhere.
- The current match scene is still single-player oriented.
- Direct `AddChild` spawning on the server is not enough for clients unless it is paired with Godot multiplayer spawning or explicit RPC replication.
- The input RPC path currently does not appear to send anything over the network.
- Authority is currently contradictory between `GameManager.SpawnPlayer` and `Kart._Ready()`.
- Local camera and local input selection are still tied to single-player assumptions.
