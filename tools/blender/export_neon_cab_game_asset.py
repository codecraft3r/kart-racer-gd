"""Export the authored Neon Cab collection as a Godot-ready GLB.

Run with Blender after opening ``neon-cab-final.blend``.  The exporter only
selects the production asset hierarchy, strips presentation animation in the
temporary Blender process, and leaves the source ``.blend`` untouched.
"""

from __future__ import annotations

import os
import sys

import bpy


ASSET_COLLECTION = "NCAB_ASSET"
ASSET_ROOT = "NCAB_ROOT"


def _output_path() -> str:
    args = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    if args:
        return os.path.abspath(args[0])

    script_dir = os.path.dirname(os.path.abspath(__file__))
    project_dir = os.path.dirname(os.path.dirname(script_dir))
    return os.path.join(project_dir, "assets", "neon-cab", "neon_cab.glb")


def _asset_hierarchy() -> list[bpy.types.Object]:
    collection = bpy.data.collections.get(ASSET_COLLECTION)
    root = bpy.data.objects.get(ASSET_ROOT)
    if collection is None or root is None:
        raise RuntimeError(
            f"Expected collection {ASSET_COLLECTION!r} and root {ASSET_ROOT!r}."
        )

    hierarchy = [root, *root.children_recursive]
    collection_objects = set(collection.all_objects)
    hierarchy = [obj for obj in hierarchy if obj in collection_objects]

    mesh_count = sum(obj.type == "MESH" for obj in hierarchy)
    if mesh_count == 0:
        raise RuntimeError("The Neon Cab export hierarchy contains no meshes.")

    return hierarchy


def export_neon_cab() -> None:
    output_path = _output_path()
    os.makedirs(os.path.dirname(output_path), exist_ok=True)

    scene = bpy.context.scene
    scene.frame_set(1)
    hierarchy = _asset_hierarchy()
    root = bpy.data.objects[ASSET_ROOT]

    # The final presentation file parents the asset to a turntable.  Detach it
    # while retaining its authored world transform so no render rig or spin
    # animation leaks into the game asset.
    root_world = root.matrix_world.copy()
    root.parent = None
    root.matrix_parent_inverse.identity()
    root.matrix_world = root_world

    for obj in hierarchy:
        obj.animation_data_clear()
        obj.hide_set(False)
        obj.hide_viewport = False

    bpy.ops.object.select_all(action="DESELECT")
    for obj in hierarchy:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = root

    mesh_count = sum(obj.type == "MESH" for obj in hierarchy)
    vertex_count = sum(
        len(obj.data.vertices) for obj in hierarchy if obj.type == "MESH"
    )
    polygon_count = sum(
        len(obj.data.polygons) for obj in hierarchy if obj.type == "MESH"
    )

    bpy.ops.export_scene.gltf(
        filepath=output_path,
        export_format="GLB",
        use_selection=True,
        export_apply=True,
        export_animations=False,
        export_cameras=False,
        export_lights=False,
        export_materials="EXPORT",
        export_extras=True,
        export_yup=True,
    )

    print(
        "NEON_CAB_EXPORT_OK "
        f"path={output_path} meshes={mesh_count} "
        f"vertices={vertex_count} polygons={polygon_count} "
        f"bytes={os.path.getsize(output_path)}"
    )


export_neon_cab()
