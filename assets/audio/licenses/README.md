# Audio license texts

Maps the `license` column in `../audio_asset_manifest.csv` to its text.
`tests/audio_manifest_smoke_test.gd` fails if a manifest license value has no
entry here.

| `license` value | File | Applies to |
| --- | --- | --- |
| `CC0` | `CC0-1.0.txt` | Kenney.nl packs and the OpenGameArt CC0 uploads (rubberduck, domasx2, looneybits, qubodup, IgnasD, TinyWorlds, "Kresiek The Furry"). No attribution required. |
| `CC BY 3.0` | `CC-BY-3.0.txt` | `assets/audio/vehicles/tire_skid.wav` only. Attribution **required** — see the `required_credit` column and the in-game credits screen. |
| `Suno Paid-Plan Commercial` | `Suno_Commercial_Terms.md` | The eight `assets/audio/music/game/PTX_*.ogg` soundtrack files. No attribution required. |

`Kenney_CC0.txt` is the license note shipped inside the Kenney packs and is kept
for provenance alongside the full CC0 1.0 text.
