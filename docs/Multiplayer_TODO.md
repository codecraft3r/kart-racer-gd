# Multiplayer TODO

## Current State

- `MultiplayerManager.cs` is an autoload that can create an ENet server or client on port `7000`, load the match scene, and reset to offline mode on disconnect/failure.
- `MultiplayerLobby.cs` exposes Host, Join, Disconnect, and an IP/address field.
- `GameManager.cs` has early server-side player state, match timer, spawn, respawn, customer, weapon, and economy scaffolding.
- `Kart.cs` uses server-authoritative physics with sequenced, unreliable owner input and sequenced server snapshots. `kart.tscn` does not use `MultiplayerSynchronizer`; replication is explicit.
- `default_3d.tscn` hosts the production multiplayer flow. Its baked solo kart is hidden, frozen, and process-disabled before a network peer spawns dynamic peer-owned karts.
- The local spawned kart on every peer sends input while all rigid bodies remain server-authoritative. Snapshots interpolate normally and snap only on first receipt or large corrections.
- Track seed synchronization, late-join state sync, camera/shell retargeting, and server-side disconnect removal are already implemented.

## First Playable Multiplayer Test

- [x] Wire `MultiplayerManager` into the running game.
  - Added it as an autoload.
  - Guarded `MultiplayerManager.Instance` against duplicate or missing instances.
  - Unsubscribes multiplayer signals on exit.

- [x] Decide the boot flow.
  - `default_3d.tscn` is the match scene; `multiplayer_lobby.tscn` is legacy/debug-only.
  - [x] After Host or Join succeeds, transition into the shared match scene.
  - [x] Add an IP/address field instead of hardcoding `127.0.0.1`.
  - [x] Disable buttons while connecting and show connection failure/server disconnect status.

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
  - `Kart._PhysicsProcess()` sends sequenced client input before the frozen-replica early return.
  - `IsLocalPlayer` and `UseLocalInput` are set only on each peer's matching dynamic kart.

- [x] Validate server-authoritative input.
  - The server accepts input only from the peer that owns that kart.
  - Stale sequences and wrong owners are rejected; unreceived remote input clears after 250 ms.
  - Latest input is stored on the owning kart after sender validation.
  - Input values are clamped on the server.
  - Keep transfer mode unreliable for frequent input, but send important events reliably.

- [x] Synchronize visible kart state.
  - Server snapshots position, rotation, and linear velocity at 20 Hz.
  - Clients discard stale snapshots, smoothly interpolate normal corrections, and snap first/large corrections.

- [x] Rework the camera for multiplayer.
  - The camera and shell retarget to the local dynamic kart during network play and restore the baked kart on disconnect.

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

- [x] Run two local instances: one host and one client on `127.0.0.1`.
- [x] Verify host sees both karts.
- [x] Verify client sees both karts.
- [x] Verify each instance controls only its own kart.
- [x] Verify movement is visible on the other instance.
- [x] Verify disconnect removes the correct kart on all peers.
- [x] Run `tools/test_multiplayer_local.ps1` with an isolated `--network-port`; it checks movement, ownership, convergence, disconnect/reconnect, duplicate prevention, and rejected-input logs.
- [ ] Verify host migration is intentionally unsupported or explicitly handled.
- [ ] Test packet loss/latency once basic local play works.

## Deferred Risks

- Host migration, client prediction, and networked fare/economy/combat remain intentionally out of scope.
- Test packet loss and latency after localhost reliability is continuously green.
