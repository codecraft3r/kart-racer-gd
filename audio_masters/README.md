# Audio masters

Godot ignores this folder via `.gdignore`. It contains untouched source renders and normalized 48 kHz/24-bit PCM working masters. Runtime-ready compressed files live under `assets/audio/music/game`.

Each batch is normalized to -16 LUFS integrated with a -1.5 dBTP ceiling, then encoded as stereo Vorbis quality 6 for the game. Because Suno supplied MP3 files, the WAV masters preserve the decoded source but cannot restore information already removed by MP3 compression.

Batch 02 (`suno_batch_02/`) maps the two user-supplied `suno-batch [usesuno.com]` zips onto the remaining slots:

| Suno file | Game stem | Rationale |
| --- | --- | --- |
| `neon-taxi-sprint-2.mp3` / `neon-taxi-sprint.mp3` | `PTX_05_FoundryFreeway_A/B` | Only uptempo pair left; the `-2` render measures ~147 BPM against the 146 BPM industrial target. |
| `neon-taxi-loop-2.mp3` / `neon-taxi-loop.mp3` | `PTX_06_WetAsphalt_A/B` | Loop-named pair kept by elimination for the rain rotation; the brief anticipates Suno missing exact BPM targets. |
| `pre-shift-boogie.mp3` / `pre-shift-boogie-2.mp3` | `PTX_07_FareEvasion_A/B` | Remaining high-energy pair by elimination for the combat rotation. |
| `glassy-hook-rubber-bass.mp3` / `final-meter-tick.mp3` | `PTX_08_RedlineReceipt_A/B` | Keep both singletons: glassy hook for the ticking-meter texture, final tick as the literal accelerating-meter B side. |
| `impossible-fare-2.mp3` / `impossible-fare.mp3` | `PTX_09_LastFareOut_A/B` | Name match on the brief's "one impossible fare can still win this" comeback line. |
| `neon-repair-shop.mp3` / `neon-repair-shop-2.mp3` | `PTX_10_ChromeAndCoffee_A/B` | Direct name match for the depot/repair-shop track. |
| `neon-cab-victory-2.mp3` / `neon-cab-victory.mp3` | `PTX_11_PaidInFull_A/B` | Direct name match for the victory/results track. |
| `shift-expired.mp3` / `shift-expired-2.mp3` | `PTX_12_MeterExpired_A/B` | Direct name match for the defeat/results track. |
| `exact-change-loose-plans.mp3` / `exact-change-loose-plans-2.mp3` | `PTX_RADIO_15_ExactChange_A/B` | Radio song pair; NOT covered by the instrumental-only shared-DNA rule in the brief. |
| `dont-close-yet.mp3` / `dont-close-yet-2.mp3` | `PTX_RADIO_16_DontCloseYet_A/B` | Radio song pair; NOT covered by the instrumental-only shared-DNA rule in the brief. |
