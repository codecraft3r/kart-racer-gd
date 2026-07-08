# Multiplayer Contract

## Target

The installable multiplayer vertical slice is `Neon Checkpoint Rush`, a server-authoritative ENet mode for 2-8 desktop players.

## Scenes

- `res://default_3d.tscn` is the product entry scene. It owns the city, local shell UI, `GameManager`, and mode nodes.
- `res://ui/RetroNeonCabShell.tscn` is the main menu, host/join UI, gameplay HUD, pause menu, settings, and results surface.
- `res://multiplayer_lobby.tscn` is a legacy/debug lobby only.

## States

- `Offline`: no active peer.
- `Hosting`: local ENet server is starting or running.
- `Connecting`: client is connecting to a server.
- `InMatch`: match scene is loaded and the player can spawn/play.
- `Disconnected`: local peer was reset or server disconnected.
- `Failed`: connection failed.

## Rules

- Clients send input intent only.
- Server simulates karts, owns score/timer/checkpoint state, and broadcasts snapshots.
- Late joiners receive current players, score, timer, and active checkpoint.
- The UI must react to manager/mode events instead of owning multiplayer state.

## Command-Line Args

- `--server`: start a dedicated ENet server on UDP 7000.
- `--host`: start a listen server.
- `--join=127.0.0.1`: join a server.
- `--player-name=NAME`: reserved for lobby/display work.
- `--mode=checkpoint-rush`: reserved; current default mode.
