# Crown Victoria game asset

This Godot wrapper instances `taxi_lod0_imported.scn`, a compressed Godot PackedScene generated directly from the supplied LOD0 GLB, at `0.52` uniform scale and rotates it 180 degrees around Y so the GLB's -Z front points along the kart's +Z driving direction. The original GLB remains alongside it as the editable/import source. At the source scale (5.38 m long), this gives an approximately 2.8 m vehicle. The authored wheel centers are about 0.32 m above the GLB origin, so they sit about 0.17 m above the kart origin at this scale; the tires reach the ground near y=0.

`TaxiLOD0` retains the imported GLB node hierarchy. The root has `BodyCore` and 26 attached mesh-part nodes; individual parts keep their `LOD0_...` names, mesh resources, and shape-key channels. The source also embeds per-node `extras.part_id`, `parent_part_id`, breakability, estimated mass, and damage-key metadata for runtime damage mapping. The five dent channels are `Dent_Front`, `Dent_Rear`, `Dent_Left`, `Dent_Right`, and `Dent_Roof`.

Because the 180-degree yaw also swaps left/right in vehicle space, resolve side-specific runtime impacts against the opposite source suffix: physical kart left maps to source `_R`, and physical kart right maps to source `_L`. The GLB hierarchy itself is left unchanged.

## Paint atlases

The imported material uses the embedded base-color atlas. These standalone atlases are for per-instance material customization by the vehicle controller:

- `taxi_basecolor.png` — original yellow-orange taxi paint atlas.
- `taxi_basecolor_midnight.png` and `taxi_basecolor_ivory.png` — repaint atlases.
- `taxi_basecolor_midnight.res` and `taxi_basecolor_ivory.res` — Godot `Texture2D` resources for runtime loading before PNG import metadata is generated.

The repaint atlases replace only exact source background paint pixels (`#e79800`). The remaining 2048×2048 atlas pixels, including black, white, chrome, glass, and decals, are preserved. Use the `.res` texture resources when loading from code in this checkout; each is a compressed, 2048×2048 Godot texture generated from its matching PNG. Apply the selected texture to a duplicated body material per kart instance; keep the glass material unchanged.

## Included source assets

- `taxi_lod0.glb` — original full 27-part player model with morph damage channels.
- `taxi_lod0_imported.scn` — Godot-native compressed scene used by the wrapper at runtime.
- `taxi_lod1.glb`, `taxi_lod2.glb` — lower-detail versions with matching source part IDs and damage channels.
- `taxi_collision.glb` — independent chassis and breakaway collision hulls.
- `taxi_glass_debris.glb` — reusable window fragments.
- `vehicle_physics.json` — source runtime contract, part IDs, attachment points, masses, and thresholds.
- `SOURCE_LICENSE.html`, `SOURCE_README.md` — attribution and upstream documentation.

Original vehicle by MAC2001, “Crown Victoria Taxi 2.0” (BlendSwap #91765), CC BY 3.0. Keep attribution when distributing the asset or a derivative game.

## In the game

The garage lists **Crown Victoria** as an available car. It has three paint choices (taxi yellow, midnight blue, ivory) and an on/off roof sign. The selection and choices save immediately in the existing garage settings. A replacement local kart restores them, and the appearance is sent to other multiplayer peers.

`CrownVictoriaVisual.cs` applies per-instance atlas overrides, dent blend shapes, hidden broken glass, and physics debris for detached panels. Kart collisions and rockets drive those effects; a new run or respawn restores the body. The existing arcade kart rigid body remains the driving chassis. The supplied LOD and authored collision GLBs are retained here as source assets; the current game uses LOD0 and generates simple convex debris hulls from detached render parts.

`DrivingVfx.cs` hides its neon cab lamp meshes for this car. The Crown instead receives smaller fitted front and rear lenses in `CrownVictoriaVisual.cs`, and front or rear impacts can break them.
