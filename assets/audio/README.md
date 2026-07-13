# PAIN TAXI audio assets

This folder contains the free sound-effects pass and the first mastered Suno soundtrack batch used by the Godot audio system.

## Layout

- `ui/` — menu rollover, confirm, back, and toggle sounds.
- `gameplay/` — countdown, pickup, passenger, fare, warning, respawn, and results cues.
- `vehicles/` — startup, looping engine layers, tire skid, impacts, and destruction.
- `ambience/` — city traffic, neon/cyber bed, industrial loops, and a staged rain loop.
- `weapons/` — staged arcade weapon, rocket, explosion, and pickup cues for the weapon system.
- `music/game/` — normalized, Godot-ready Vorbis soundtrack variants. Lossless masters and untouched source renders live in the Godot-ignored `audio_masters/` folder.
- `licenses/` — source license texts.
- `audio_asset_manifest.csv` — source, creator, license, and local-file tracking.

## Runtime integration

`res://audio/AudioManager.cs` is an autoload. It owns pooled non-positional one-shots, ambient loops, match/countdown cues, audio-bus setup, and positional world one-shots. `res://audio/VehicleAudioController.cs` is attached to every kart and crossfades idle/drive engine loops while driving tire-skid volume from lateral slip.

The audio buses are:

`Master / Music / Ambience / Vehicles / Weapons / Impacts / UI / Voice`

Short effects remain in OGG/WAV as supplied. Loop flags are applied at runtime so the source files can remain untouched.

## Attribution

Most assets are CC0 and do not require attribution. The tire-skid loop is CC BY 3.0 and must retain this credit in distributions:

> “Car tire squeal skid loop” by audible-edge (Tom Haigh), licensed under CC BY 3.0. Source: https://opengameart.org/content/car-tire-squeal-skid-loop

This credit is also displayed in the in-game credits screen.
