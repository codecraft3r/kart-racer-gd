# Audio masters

Godot ignores this folder via `.gdignore`. It contains untouched source renders and normalized 48 kHz/24-bit PCM working masters. Runtime-ready compressed files live under `assets/audio/music/game`.

Batch 01 is normalized to -16 LUFS integrated with a -1.5 dBTP ceiling, then encoded as stereo Vorbis quality 6 for the game. Because Suno supplied MP3 files, the WAV masters preserve the decoded source but cannot restore information already removed by MP3 compression.
