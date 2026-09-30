# Crown Victoria taxi — efficient, destructible asset

Open `crown_victoria_game.blend` in Blender 4.5 or newer. It opens to the intact vehicle. Textures are packed, so the Blender file is self-contained.

The original vehicle evaluated to **625,490 triangles**. The close version uses **16,388 triangles**, a **97.4% reduction**, with the source silhouette retained. Length is normalized to approximately **5.38 metres**; the exported mesh measures 5.375 m after simplification. Sampled source-to-mesh distances for the main body have a 95th percentile of approximately 1.3 mm. Small grille perforations use a texture rather than individual holes. `geometry_quality.json` records sampled deviations for every source part; this is a geometric check rather than a certified Hausdorff bound.

## Files

| File | Triangles | Purpose |
| --- | ---: | --- |
| `assets/taxi_lod0.glb` | 16,388 | Close vehicle, detachable hierarchy and dent morphs |
| `assets/taxi_lod1.glb` | 7,499 | Medium distance, same part IDs and damage channels |
| `assets/taxi_lod2.glb` | 2,915 | Distant damaged vehicles, same detachable hierarchy |
| `assets/taxi_traffic_static.glb` | 2,915 | Merged intact traffic vehicle, two material primitives |
| `assets/taxi_collision.glb` | 1,590 | Four chassis boxes and 26 convex debris hulls |
| `assets/taxi_glass_debris.glb` | 3,200 total | 60 separate reusable window fragments |

The car uses one shared opaque PBR atlas and one glass material. The part hierarchy has approximately 27 material primitives. The merged traffic model uses two. Reuse the materials and texture instances across LODs and vehicles when loading multiple files.

## Inspect it in Blender

- **Taxi_Asset:** intact asset with panel pivots and damage shape keys.
- **Damage_Preview:** an authored animation showing dents, hood opening, a released bumper and lamp, an open door, and broken glass. Play frames 1–110.
- **Physics_Demo:** a live rigid body crash with 26 breakable FIXED constraints. Start at frame 1 and press Space. The red block hits the car near frame 10.
- **Exploded_Parts:** view the actual independent panels and attachment locations.
- **Taxi_LOD1 / Taxi_LOD2:** inspect lower-detail alternatives.
- **Taxi_Colliders:** independent low-cost collision proxies. They are excluded from visual exports.
- **Glass_Debris:** fragments positioned in their original windows; spawn only fragments from the impacted pane.
- **Taxi_Traffic_Static:** merged intact-only distant traffic mesh.

To use dent sliders, open the embedded `Taxi_Damage_Tools.py` in Blender's Text Editor and click **Run Script**. Open the 3D View sidebar with **N**, then **Taxi Damage**. This also provides release and restore buttons. Scripts do not auto-run. The same helper is in `scripts/taxi_damage_tools.py`.

The five independent channels are `Dent_Front`, `Dent_Rear`, `Dent_Left`, `Dent_Right`, and `Dent_Roof`. Each mesh includes the channels that affect its geometry; set a channel on all surviving meshes that contain it. Wheels stay rigid. Panels have inward thickness to provide visible edges when released; interior detail is kept simple.

## Connect it to a car game

This package contains the asset and a working Blender destruction demonstration. A game must bind its collision events and vehicle physics to the part hierarchy, hulls, and damage channels. Blender rigid body constraints do not become engine physics through GLB export.

`vehicle_physics.json` contains part IDs, attachment parents, pivot positions, collision associations, estimated masses, adjustable break thresholds, damage keys, and the runtime contract. GLB node `extras.part_id` identifies a part across all LODs. Custom-property vectors retain their documented coordinate system; use the explicitly named JSON positions for your engine rather than guessing.

1. Keep one main vehicle rigid body with the four chassis collision boxes and your engine's wheel system. Attached visual panels stay kinematic; their debris hulls remain disabled.
2. Map a collision location to a damage region. Increase corresponding morph weights on the current LOD, clamped to 0–1. Use local impacts and incremental damage rather than jumping every hit to maximum deformation.
3. When a part exceeds its threshold, preserve its world transform, remove its attachment, and enable a rigid body with that part's convex hull. Use the estimated centre of mass or recenter the hull; visual hinge pivots are not mass centres. Inherit the vehicle's linear and angular velocity at the release point, then apply the hit impulse.
4. Release child windows and mirrors with their door unless that child is already broken. Identify children through `parent_part_id`.
5. Shatter glass by hiding the pane and spawning its fragments. Pool and cap active debris at approximately 16–24 bodies, enable ordinary debris collision layers after release, and sleep/despawn fragments after a few seconds.
6. Reapply current damage weights and visibility after switching LOD. Starting distances are 12 m and 35 m; use screen size and profile on the target device. Use the merged traffic file for distant intact cars.

Axes: Blender **+Y front, +Z up, −X left**. GLB **−Z front, +Y up, −X left**. All exported scales are applied. Masses, break impulses, and LOD distances are starting estimates that need gameplay tuning. This is panel destruction with inexpensive morph dents; it does not include a continuous soft-body vehicle solver.

## Verification

The exported files each contain one intended scene. All three LODs have 27 mesh parts, the five damage channel names, two materials, valid indices, and finite numeric data. Collision hulls are closed after welding coincident positions. The LOD0 GLB was imported back into Blender and its part hierarchy, damage channels, and dimensions were checked.

The live crash test was stepped from frame 1 to 90. Before impact, maximum attachment drift was 0.66 mm. After impact, the grille, front bumper, left headlight, and hood separated. The Blender demonstration uses separate collision layers for attached part categories to prevent panel overlaps from causing false breaks; all parts collide with the floor and impact block. A game should enable normal debris collisions at release. Full records are in `export_verification.json`, `physics_verification.json`, and `round_trip_check.json`.

## Attribution

Original model: **“Crown Victoria Taxi 2.0” by MAC2001**, [BlendSwap #91765](https://www.blendswap.com/blends/view/91765), released under [Creative Commons Attribution 3.0](https://creativecommons.org/licenses/by/3.0/). The original license is included as `SOURCE_LICENSE.html`.

Modified for polygon reduction, normalized scale, consolidated PBR textures, three LODs, pivots, independent panels, dent morphs, collision hulls, glass fragments and Blender previews. Retain this attribution when distributing the model or a game that uses it.
