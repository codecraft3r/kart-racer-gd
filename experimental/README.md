# experimental/

This folder holds agent-generated work that is **not** part of the shipping PAIN TAXI
build. Files here are excluded from `kart_racer.csproj` compilation (see the `<Compile Remove>`
rule) so they cannot break the verified baseline.

## EndlessRoad/

A parallel "Endless Road" forward-run mode (`EndlessRoadMode.cs`, `EndlessRoadSettings.cs`,
`world/EndlessRoadChunk.cs`, `world/EndlessRoadStreamer.cs`) plus
`tests/endless_road_mode_smoke_test.gd`. This was produced by an implementation agent but is
**out of scope** for the PAIN TAXI Endless Run (the resolved design lives in
`.scratch/single-player-endless-run/issues/` and is built on `TaxiMode`). It is parked here
for later review, not wired into any product scene, and does not affect the multiplayer-free
roadmap.
