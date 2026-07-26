# Neon Cab — Original Modular Arcade Taxi

## Design intent

Neon Cab is an original compact stunt taxi designed to carry the energy of late-1990s and early-2000s arcade driving games without reproducing any existing Crazy Taxi vehicle. The source study is recorded in [`docs/taxi-design-research.md`](../../docs/taxi-design-research.md).

The transferable ideas were instant taxi readability, saturated color, a planted jump-ready stance, oversized wheels, strong silhouette accessories, and a vehicle that feels personally customized by its driver. The design uses those principles through a compact targa-fastback body, large modular fender rings, a wide impact-bar face, exaggerated splitter and spoiler, and an illuminated roof bridge.

## Intentional differences

- Compact targa-fastback proportions replace the long classic-cruiser proportions associated with the original series.
- The orange, petrol-cyan, magenta, cream, and black palette replaces familiar yellow/checker/flame combinations.
- The original `NEON//CAB` identity uses a split speed-arrow and route-number `88`; no series logo, character plate, official typography, or licensed mark is reproduced.
- The front uses a vertical impact grille and stacked light bars; the rear uses a broad arcade wing and cyan/red marker stack.
- The vehicle has an enclosed passenger cabin and visible four-seat interior rather than copying an open convertible layout.

## Modular system

The `.blend` file uses a stable root and named socket empties. Every swappable assembly is parented to a socket and stored in a dedicated collection.

| Collection | Primary swaps |
|---|---|
| `NCAB_01_BODY_CORE` | Core shell, glazing, pillars, undertray, base hood/roof |
| `NCAB_02_MODULE_FRONT` | Bumper, splitter, grille, lamps, fog lights, hood vents |
| `NCAB_03_MODULE_REAR` | Bumper, diffuser, lamps, exhaust, spoiler |
| `NCAB_04_MODULE_WHEELS` | Tires, linked tread pieces, rims, hubs, spokes, fender rings |
| `NCAB_05_MODULE_ROOF` | Roof sign, sign base, illuminated rails |
| `NCAB_06_MODULE_SIDES` | Mirrors, door blades, handles, underglow, accessories |
| `NCAB_07_INTERIOR` | Seats, dashboard, steering wheel, display |
| `NCAB_08_DECALS_SIGNAGE` | Branding, route number, speed glyph, checker micro-strips |

Socket names begin with `NCAB_SLOT_`. Production objects use `NCAB_` and module objects use `NCAB_MOD_`. Objects also carry `asset_role`, `module_slot`, `variant_family`, and `game_asset` custom properties.

## Game-asset construction

- 190 mesh objects
- 4,396 source vertices and 3,370 source polygons before modifier evaluation
- Bevel modifiers retained for a non-destructive rounded arcade look
- Mirrored/repeated parts use linked geometry where practical, including wheel tread pieces
- All mesh objects have UV maps; Smart Project uses a 0.025 island margin
- Shared procedural materials keep the asset compact and recolorable
- Model scale is metric, with forward along `+X`

## Material and wear strategy

The body uses a shared solar-orange coated paint with subtle procedural flake/roughness variation. Petrol-cyan and magenta divide performance modules from the body core. Emissive materials are reserved for lamps, rails, and underglow; large body decals use non-emissive materials to preserve graphic edges. Weathering is intentionally light and expressed through roughness variation rather than heavy dirt.

## Future variants

1. **Beach Sprint pack:** cream/teal paint, surf-rack roof module, balloon tires, tubular bumpers.
2. **Night Courier pack:** violet/acid-green paint, enclosed digital roof display, aero-disc wheels, camera mirrors.
3. **Heavy Fare pack:** lifted suspension, steel wheel/fender package, reinforced front clip, luggage cage.
4. **Retro Electro pack:** warm yellow/blue palette, chrome wheel set, analog roof meter, smaller wing.
5. **Track Marshal pack:** white/orange blocking, beacon bridge, push bar, safety-number decal sheet.

Each pack should change at least one silhouette module, one major contrast boundary, and one implied handling trait. New packs can share the body core, sockets, UV conventions, material slots, cameras, and presentation rig.

## Deliverables

- `neon-cab-final.blend` — final organized source asset
- `renders/` — front, rear, side, top, three-quarter, beauty, clay, wireframe, and exploded renders
- `turnaround/neon-cab-turnaround.mp4` — 72-frame H.264 turnaround
- `timelapse/neon-cab-making-of-timelapse.mp4` — 100-second, 1280×720, 30 FPS making-of
- `timelapse/neon-cab-process-2fps-recovered.mkv` — complete 25-minute, 2 FPS source capture

