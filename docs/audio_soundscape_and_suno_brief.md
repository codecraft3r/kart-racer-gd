# PAIN TAXI — Soundscape and Suno Soundtrack Brief

## Creative north star

The game should sound like a dangerous late-night taxi radio broadcast bleeding into an arcade cabinet: neon electro, industrial breakbeats, rubbery analog bass, FM bell tones, crunchy drum-machine transients, and a little low-bit sparkle. It should feel fast, cheeky, and competitive rather than grim or cinematic.

Shared musical DNA across all 12 tracks:

- Instrumental only; no vocals, speech, chants, or vocal chops.
- A recurring four-note taxi-meter motif based on F–Ab–C–Eb. It may be transposed to fit each track.
- A recurring sound palette: dry 808/909-style drums, gated snare, rubbery mono synth bass, bright FM bell/pluck, wide analog pads, short metallic industrial hits, restrained chiptune accents.
- Music must leave room for engines, weapons, UI, and passenger reactions: controlled upper mids, clean bass, sparse lead writing during the main groove.
- The groove should arrive within 8 seconds. Avoid long atmospheric introductions.
- Loop-friendly arrangement: stable tempo, clean 8-bar opening and closing sections, no ritardando, no giant final crash, and no hard-stop ending.
- Retro-futurist and 1980s-inspired, but original and not an imitation of any named artist, soundtrack, or existing song.

## External soundscape resources

### Start here: free and production-friendly

1. **Kenney Interface Sounds** — <https://kenney.nl/assets/interface-sounds>
   - 100 UI sounds under CC0.
   - Use for menu move, confirm, back, toggle, countdown ticks, pickup-zone pulse, and cash-register feedback.
   - Process with light bit reduction, short gated reverb, and pitch variants so the set matches the CRT UI.

2. **Sonniss #GameAudioGDC archive** — <https://sonniss.com/gameaudiogdc/>
   - Large professional game-audio giveaways. The bundle license permits commercial game use, modification, and no-attribution use.
   - Search downloaded metadata for: `car engine onboard`, `engine steady rpm`, `tire skid`, `wet road`, `vehicle impact`, `industrial ambience`, `city night`, `underpass`, `ventilation`, `metal debris`, `rocket`, `explosion`, and `weapon pickup`.
   - Do not redistribute raw source files as a standalone asset pack.

3. **Soundly Free Library** — <https://getsoundly.com/>
   - The Soundly Free and Pro libraries are cleared for commercial projects including games.
   - Excellent workflow for auditioning, tagging, and exporting only selected files.
   - If connecting Freesound through Soundly, set it to **CC0-only**.

4. **Freesound** — <https://freesound.org/>
   - Use the license filter and prefer **CC0**. CC BY is usable only if the creator and sound are tracked for attribution. Avoid CC BY-NC for a potentially commercial game.
   - Useful searches: `night industrial city loop`, `electrical transformer hum`, `parking garage ambience`, `traffic distant loop`, `taxi meter beep`, `car door slam`, `radio squelch`, `rain metal roof`, `tunnel whoosh`, and `neon buzz`.

5. **ZapSplat** — <https://www.zapsplat.com/>
   - Broad library for gaps. Standard-license sounds can be used in commercial games, but Basic users must provide the required ZapSplat credit; Gold downloads retain attribution-removal privileges.
   - Best used for one-off props, human reactions, doors, coins/cash, and UI sweeteners after the CC0 sources are exhausted.

### Paid upgrade path

6. **A Sound Effect — Avenues, Highways, Streets, and Traffic Ambience Kit** — <https://www.asoundeffect.com/sound-library/avenues-highways-streets-and-traffic-ambience-kit/>
   - A strong middle-ground purchase: ready-made urban scenes plus isolated elements, useful for layered zone ambience and smoother loops.

7. **BOOM Library — Urban USA** — <https://www.boomlibrary.com/sound-effects/urban-usa/>
   - Premium urban beds with downtown, traffic, tunnel, industrial, ventilation, refinery, and quiet base layers.
   - For this third-person desktop game, the stereo edition is the practical choice; the large 3D-surround edition is unnecessary unless the spatial-audio scope changes.

### Soundscape layer plan

Build each district from a quiet continuous bed plus sparse randomized emitters:

- **Global night bed:** distant traffic wash, transformer hum, rooftop ventilation, faint wind, occasional far siren. Keep it subtle enough to hear vehicle handling.
- **Depot:** fluorescent/electrical hum, garage ventilation, air-tool bursts, metal clinks, PA/radio squelch, idling vehicles.
- **Downtown:** traffic wash, crosswalk chirps, distant crowd walla, storefront HVAC, rare horns, wet-road pass-bys.
- **Industrial:** refinery rumble, steam vents, chain rattle, metal impacts, freight train or factory pulses.
- **Underpass/tunnel:** filtered traffic, long concrete reflections, water drips, Doppler pass-bys.
- **Rain variant:** rain on asphalt and metal, tire spray, subdued high frequencies, occasional thunder far away.

Recommended Godot bus structure for the later integration pass:

`Master / Music / Ambience / Vehicles / Weapons / Impacts / UI / Voice`

Keep music non-positional, ambience mostly stereo with localized 3D details, and vehicle/combat sounds positional. Use a light compressor/ducking path so weapon impacts, objective chimes, and passenger warnings briefly clear space in the music.

Maintain `audio_asset_manifest.csv` with: local filename, source URL, creator, original filename, license, download date, required credit, and edits performed.

## Suno generation setup

- Use **Custom Mode**.
- Turn **Instrumental** on.
- Paste the track's Style prompt into **Styles**.
- Paste its Exclude prompt into **Advanced Options → Exclude**.
- Generate at least two versions of each track. Keep the version with the clearest groove and least crowded midrange.
- Once track 03 or 04 establishes the ideal sonic identity, create a Persona from it and use that Persona for the remaining gameplay tracks if it improves consistency. Keep menu, shop, and result tracks looser.
- Generate these while subscribed to a paid Suno plan if the tracks may ship in a commercial game. Songs generated on the free plan do not receive retroactive commercial rights merely because the account is upgraded later.

Suno may not obey exact BPM, key, duration, or seamless-loop requests. Those are musical targets; the accepted files will be tempo-mapped, trimmed, looped, leveled, and encoded during the packaging pass.

## Track list and detailed prompts

### 01 — Meter Glow

**Use:** Main menu and title screen
**Target:** 102 BPM, F minor, 2:15–3:00

**Style prompt**

> Instrumental retro-futurist neon electro for the main menu of a competitive arcade taxi-combat game. 102 BPM, F minor, nocturnal but inviting. Open immediately with a memorable four-note FM-bell taxi-meter motif, F–Ab–C–Eb, over a soft analog pulse. Add a warm rubbery mono bass, crisp dry drum-machine kick, gated snare, restrained hi-hats, wide violet analog pads, tiny 8-bit menu sparkles, and occasional muted metallic percussion. Confident late-night downtown attitude, stylish and playful, not ominous. Medium-low intensity with a clear hook but enough empty space for menu UI sounds. Clean modern low end with lightly aged cassette/CRT texture. Stable 4/4, groove established within 6 seconds, subtle development every 8 bars, clean loop-friendly opening and ending, no dramatic finale.

**Exclude**

> vocals, spoken words, rap, choir, vocal chops, saxophone, electric guitar solo, orchestral strings, cinematic trailer drums, siren sounds, police-radio dialogue, lo-fi mud, harsh distortion, huge risers, final cymbal crash, fade-out

### 02 — Dispatch After Dark

**Use:** Multiplayer lobby and matchmaking
**Target:** 116 BPM, C minor, 2:30–3:30

**Style prompt**

> Instrumental late-night electro-funk lobby music for a neon arcade taxi PvP game. 116 BPM, C minor. A playful syncopated synth-bass line, tight drum-machine groove, clipped funk guitar used only as a quiet rhythmic texture, FM dispatch chimes, short analog brass stabs, glassy arpeggiator, and small metallic taxi-meter clicks. Introduce a transposed version of the four-note F-minor taxi motif as a call-and-response figure. The mood is competitive anticipation: drivers checking loadouts under fluorescent depot lights, joking before a dangerous shift. Upbeat and social without sounding like a party song. Background-friendly mix, restrained lead, clear rhythmic pocket, groove within 5 seconds, modest 16-bar rises and drops, clean 8-bar loopable outro with the beat continuing.

**Exclude**

> vocals, spoken dispatch, crowd chants, vocal chops, slap-bass solo, dominant guitar, saxophone, disco strings, cinematic orchestra, trap hi-hat rolls, dubstep drops, sirens, explosion effects, long intro, hard-stop ending, fade-out

### 03 — Flagfall Fever

**Use:** Countdown, match start, first gameplay rotation
**Target:** 150 BPM, F minor, 3:00–4:00

**Style prompt**

> High-energy instrumental arcade racing electro, 150 BPM, F minor, designed for a neon taxi match countdown and opening sprint. Begin with four dry metronomic taxi-meter ticks, then launch into a propulsive four-on-the-floor kick layered with sharp breakbeat ghost notes. Use a rubbery sequenced analog bass, gated snare, bright FM-bell statement of the F–Ab–C–Eb signature motif, pulsing minor-key arpeggios, short industrial metal hits, and restrained chiptune sparks. Urgent, cheeky, colorful, and kinetic rather than aggressive. The main groove must leave midrange room for engines and weapon SFX; use short melodic phrases with pauses. Add a half-time 8-bar breathing section near the middle, then return with denser percussion. Immediate start, stable tempo, clean loop-friendly 8-bar intro and outro, no grand ending.

**Exclude**

> vocals, spoken countdown, vocal samples, choir, rock guitar, orchestral score, cinematic trailer style, hardstyle kick, dubstep wobble, police sirens, car sounds, gunshots, explosion samples, overcompressed wall of sound, long breakdown, fade-out, final crash

### 04 — Rush Hour Riot

**Use:** Core downtown driving rotation; potential Persona anchor
**Target:** 154 BPM, Ab minor, 3:30–4:30

**Style prompt**

> Instrumental neon electro-breakbeat for the core driving loop of a fast competitive taxi-combat game. 154 BPM, Ab minor. Combine punchy syncopated breakbeats with a steady dance-floor kick, rubbery acid-tinged bass that stays controlled, bright FM plucks, wide analog pads, gated snare, and small crunchy 12-bit percussion. State the recurring four-note taxi-meter motif in Ab minor with a short bright synth, then fragment it into rhythmic two-note answers so the music never crowds gameplay. Feels like weaving through downtown traffic at midnight: mischievous, agile, high pressure, and fun. Strong forward motion, clean sub bass, sparse center-channel lead, short breakdowns rather than long ambient sections. Start on the groove, evolve in 8- and 16-bar blocks, include a clean beat-only loop exit, no dramatic song ending.

**Exclude**

> vocals, speech, vocal chops, choir, lead guitar, saxophone, orchestral strings, cinematic brass, trap beat, hard techno, dubstep drop, sirens, horns, engine samples, gunshots, muddy bass, piercing lead, long intro, fade-out

### 05 — Foundry Freeway

**Use:** Industrial district gameplay
**Target:** 146 BPM, D minor, 3:30–4:30

**Style prompt**

> Instrumental industrial electro for an arcade taxi chase through a neon refinery district. 146 BPM, D minor. Heavy but nimble: tight electronic kick, gated snare, rolling toms, sampled metal clangs tuned as percussion, hydraulic hiss used rhythmically, dark mono synth bass, cold FM mallets, and a narrow distorted arpeggio. Recast the four-note taxi-meter motif as metallic D-minor notes, recognizable but tougher. The factory machinery should feel musical and stylized, not like literal sound effects. Maintain an energetic racing groove with clear gaps for combat and engine audio. Dark magenta and cyan mood, dangerous but playful, no horror. Add one sparse 8-bar machinery breakdown and a satisfying return. Stable tempo, immediate rhythm, loop-friendly beat-led intro and outro, no final impact.

**Exclude**

> vocals, whispers, choir, horror drones, literal factory ambience, literal steam blasts, car engines, sirens, gunshots, metal guitar, industrial noise wall, cinematic trailer percussion, orchestral instruments, dubstep bass, excessive distortion, long ambient intro, hard ending, fade-out

### 06 — Wet Asphalt

**Use:** Rain/night ambience gameplay rotation
**Target:** 138 BPM, G minor, 3:30–4:30

**Style prompt**

> Instrumental atmospheric electro-breakbeat for racing through a neon city in rain. 138 BPM, G minor. Crisp shuffled breakbeat, deep rounded synth bass, soft analog chords, glassy FM droplets, filtered arpeggios, brushed electronic noise, subtle gated snare, and occasional reverse textures that feel like reflections on wet pavement. Transpose the taxi-meter motif into G minor and play it sparingly on a bell-like synth with long dark delay tails. Moody and immersive but still driving; romantic noir color without becoming sad or sleepy. Keep percussion precise and the upper mids gentle so rain, tire spray, and weapon effects remain intelligible. Groove begins within 7 seconds, one low-density rainlike bridge, then a confident return. Seamless-loop-friendly 8-bar opening and ending, no cadence or fade.

**Exclude**

> vocals, spoken word, saxophone, jazz solo, acoustic piano lead, orchestral strings, cinematic score, actual rain recordings, thunder effects, sirens, car sounds, trap drums, dubstep drop, lo-fi vinyl crackle, sleepy ambient drift, huge reverb wash, fade-out, final crash

### 07 — Fare Evasion

**Use:** Combat-heavy rotation and contested pickup zones
**Target:** 160 BPM, E minor, 3:00–4:00

**Style prompt**

> Instrumental high-intensity electro breakbeat for vehicular PvP around a contested taxi pickup zone. 160 BPM, E minor. Fast chopped breakbeats, firm kick, distorted but controlled mono bass, tense sixteenth-note analog sequence, gated snare, clipped metallic hits, and bright FM warning tones. The four-note taxi motif appears as an urgent E-minor syncopated stab, never as a long lead melody. Aggressive arcade energy with a grin: tactical, chaotic, and readable, not military or grim. Use short call-and-response phrases and frequent one-beat gaps so rockets, impacts, and warning UI can cut through. Include an 8-bar half-time tactical reset before the final high-energy section. Stable tempo, action begins immediately, beat-only intro/outro for looping, no giant drop and no resolved ending.

**Exclude**

> vocals, shouting, chants, vocal chops, military drums, orchestral brass, cinematic trailer, metal guitar, neurofunk wall of bass, dubstep wobble, literal alarms, sirens, gunshots, explosions, car horns, clipping, harsh high frequencies, long buildup, fade-out, final boom

### 08 — Redline Receipt

**Use:** Low health, passenger panic, or final-minute intensity layer/rotation
**Target:** 164 BPM, F-sharp minor, 2:45–3:45

**Style prompt**

> Instrumental urgent arcade electro at 164 BPM in F-sharp minor for the final minute of a competitive taxi-combat match. Relentless but clean: pulsing octave synth bass, rapid dry drum-machine groove with breakbeat fills, ticking FM percussion like an accelerating taxi meter, tense analog arpeggio, short gated snare, and narrow dissonant synth stabs. Hide a transposed version of the four-note signature motif inside the ticking rhythm, then reveal it clearly every 16 bars. Convey a damaged cab, nervous passenger, shrinking clock, and one last chance to deliver. High stakes without horror, anger, or cinematic heroics. Preserve headroom and leave deliberate holes for health alarms and impacts. Immediate groove, no long breakdown; one 4-bar false drop, then full return. Loopable beat-led ending with no final chord.

**Exclude**

> vocals, breathing, heartbeat sample, spoken countdown, alarm or siren samples, gunshots, explosions, car sounds, metal guitar, orchestra, trailer braams, hardstyle kick, dubstep, gabber distortion, piercing resonance, long intro, long breakdown, fade-out, final crash

### 09 — Last Fare Out

**Use:** Catch-up mode, late-match chase, comeback rotation
**Target:** 156 BPM, C minor, 3:15–4:15

**Style prompt**

> Instrumental uplifting neon electro racer for a late-match comeback. 156 BPM, C minor moving toward brief Eb-major color without becoming triumphant too early. Driving kick and breakbeat hybrid, elastic synth bass, bright analog chords, FM bell arpeggios, gated snare, tiny chiptune sparks, and tasteful tom fills. Transform the recurring taxi-meter motif into a determined rising answer phrase. The emotion is “one impossible fare can still win this”: urgent, hopeful, scrappy, and fun. Keep the melody concise and rhythmic, with open upper mids for engines and combat. Build energy through added percussion and harmony rather than giant risers. Groove in the first 5 seconds, a short 8-bar suspended bridge, then a strong return. Clean loop-friendly intro/outro, unresolved final bars, no anthem ending.

**Exclude**

> vocals, choir, heroic orchestra, cinematic trailer, power metal guitar, pop chorus, supersaw festival drop, trap drums, sirens, car sounds, sound effects, sentimental piano, overly happy major key, long buildup, key-change finale, fade-out, final cymbal crash

### 10 — Chrome & Coffee

**Use:** Depot, repair shop, loadout, and pause
**Target:** 92 BPM, Bb minor, 2:30–3:30

**Style prompt**

> Instrumental low-intensity electro-funk for a neon taxi depot, repair shop, and loadout screen. 92 BPM, Bb minor. Relaxed head-nod drum-machine groove, warm rounded synth bass, dusty electric piano chords, quiet FM-bell taxi-meter motif, muted funk-guitar scratches used sparingly, soft analog pad, rim clicks, and tiny tool-like metallic percussion. Feels like coffee from a vending machine at 3 a.m. while mechanics patch a battle-damaged cab: cool, slightly weary, still humorous. Background-first arrangement with no dominant solo and plenty of room for menu clicks and repair SFX. Begin with the groove, use subtle 8-bar variations, no dramatic build. Clean dry 8-bar opening and closing loops, continuing beat, no cadence and no fade.

**Exclude**

> vocals, spoken radio, saxophone solo, dominant guitar solo, slap bass, orchestral strings, cinematic score, trap beat, lo-fi vinyl noise, sleepy jazz improvisation, sad piano, literal garage sounds, power tools, sirens, long intro, fade-out, final chord

### 11 — Paid in Full

**Use:** Victory and first-place results
**Target:** 120 BPM, F major with borrowed F-minor color, 1:45–2:45

**Style prompt**

> Instrumental retro arcade victory electro for the results screen of a neon taxi-combat game. 120 BPM, F major with brief borrowed chords from F minor to preserve the soundtrack identity. Punchy drum machine, buoyant synth bass, bright FM bells, analog brass stabs, sparkling chiptune accents, handclap-like electronic snare, and a bold celebratory version of the four-note taxi-meter motif. The feeling is cash counted, cab smoking, driver somehow victorious: earned, cheeky, flashy, and compact rather than sentimental or heroic. Start with a one-second win sting that naturally drops into a relaxed groove for scoreboard viewing. Keep the arrangement clean for UI. Include a short B section, then return to the hook. Loop-friendly results-bed ending after the initial sting, no huge orchestra, no fade.

**Exclude**

> vocals, crowd chant, choir, orchestral fanfare, cinematic trailer, rock guitar solo, saxophone, pop chorus, EDM festival drop, literal coin sounds, cash register samples, applause, sirens, overly childish melody, long intro, fade-out, giant final crash

### 12 — Meter Expired

**Use:** Defeat, disconnect, or low-placement results
**Target:** 86 BPM, F minor, 1:45–2:45

**Style prompt**

> Instrumental bittersweet retro-electro results music for losing a chaotic neon taxi match. 86 BPM, F minor. Slow dry drum-machine beat, warm detuned analog bass, soft electric piano, dim FM-bell statement of the F–Ab–C–Eb taxi-meter motif, worn cassette pad, sparse rim clicks, and one gently comic descending synth figure. The mood is “the meter expired, the bumper fell off, try another shift”: disappointed but amused, never tragic. Keep it concise, stylish, and comfortable under statistics and rematch UI. Begin immediately with the motif and beat, add a small hopeful harmonic lift in the middle, then return to F minor. Clean background mix, loop-friendly 8-bar tail with continuing rhythm, no melodramatic ending.

**Exclude**

> vocals, spoken words, crying, choir, orchestral strings, cinematic sadness, solo violin, dramatic piano ballad, blues guitar, saxophone, funeral mood, horror ambience, literal crash effects, sirens, tape damage, excessive wow and flutter, long intro, fade-out, final gong

## Generation and handoff checklist

For each selected track, return:

1. Highest-quality original WAV export, not MP3-only.
2. Filename using `PTX_01_MeterGlow_v01.wav` through `PTX_12_MeterExpired_v01.wav`.
3. The Suno song link or track ID.
4. The exact prompt and Exclude text actually used if changed from this brief.
5. Which generation is preferred if both A/B versions are supplied.
6. Optional but useful: 12-stem export or at minimum separate drums, bass, melody, and ambience stems.
7. Do not pre-trim, normalize, add fades, or attempt a seamless loop. Preserve the full original render so the packaging pass has handles.

The later packaging pass should produce normalized archival WAV masters plus Godot-ready `.ogg` music loops, loop-point notes, loudness measurements, metadata, and an attribution/license manifest for all external SFX.
