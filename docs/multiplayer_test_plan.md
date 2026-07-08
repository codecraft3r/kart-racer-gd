# Multiplayer Test Plan

## Local Smoke

1. Build C# with `dotnet build kart_racer.sln`.
2. Launch one instance with `--host`.
3. Launch a second instance with `--join=127.0.0.1`.
4. Confirm both players spawn in `default_3d.tscn`.
5. Drive both karts and confirm the remote kart moves on the other client.
6. Drive through the active cyan checkpoint.
7. Confirm the local score, rank, timer, and checkpoint label update.
8. Disconnect one client and confirm its kart is removed.
9. Rejoin and confirm the late joiner receives current checkpoint/timer/score.

## Dedicated Server Smoke

1. Launch server with `--server`.
2. Join from two clients with the server IP.
3. Confirm UDP 7000 is open on the host firewall.
4. Leave the server running for one full 3-minute match.
5. Confirm match end state reports a winner.

## Pass Criteria

- No duplicate karts after reconnect.
- Server remains authoritative for checkpoint scoring.
- Clients cannot increment score without entering the server checkpoint.
- Pause/disconnect returns the local client to the shell without crashing.
- Packaged build behaves the same as editor/debug launch.
