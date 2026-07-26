import bpy
import math
import os
from mathutils import Vector


ROOT_DIR = r"C:\Users\Windows\Documents\kart-racer-gd\deliverables\neon-cab"
RENDER_DIR = os.path.join(ROOT_DIR, "renders")
TURN_DIR = os.path.join(ROOT_DIR, "turnaround")
BLEND_PATH = os.path.join(ROOT_DIR, "neon-cab-final.blend")

ASSET_COLLECTIONS = []
MATS = {}
SLOTS = {}
ROOT = None


def ensure_dirs():
    os.makedirs(RENDER_DIR, exist_ok=True)
    os.makedirs(TURN_DIR, exist_ok=True)


def clean_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.curves, bpy.data.cameras, bpy.data.lights):
        for block in list(datablocks):
            if block.users == 0:
                datablocks.remove(block)
    for coll in list(bpy.data.collections):
        if coll.name != "Collection":
            bpy.data.collections.remove(coll)
    base = bpy.data.collections.get("Collection")
    if base:
        base.name = "NCAB_ASSET"
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.length_unit = 'METERS'
    scene.unit_settings.scale_length = 1.0


def get_collection(name, parent=None):
    coll = bpy.data.collections.get(name)
    if coll is None:
        coll = bpy.data.collections.new(name)
        if parent is None:
            bpy.context.scene.collection.children.link(coll)
        else:
            parent.children.link(coll)
    return coll


def move_to_collection(obj, coll):
    for old in list(obj.users_collection):
        old.objects.unlink(obj)
    coll.objects.link(obj)


def tag(obj, role, slot="CORE", variant="A"):
    obj["asset_role"] = role
    obj["module_slot"] = slot
    obj["variant_family"] = variant
    obj["game_asset"] = True


def parent_to(obj, parent):
    obj.parent = parent
    obj.matrix_parent_inverse = parent.matrix_world.inverted()


def material_principled(name, base, metallic=0.0, roughness=0.4,
                        emission=None, emission_strength=0.0,
                        transmission=0.0, alpha=1.0):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    mat.diffuse_color = (*base, alpha)
    mat.metallic = metallic
    mat.roughness = roughness
    nodes = mat.node_tree.nodes
    bsdf = nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*base, 1.0)
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = roughness
        if "Coat Weight" in bsdf.inputs:
            bsdf.inputs["Coat Weight"].default_value = 0.32 if metallic < 0.5 else 0.12
        if emission is not None:
            key = "Emission Color" if "Emission Color" in bsdf.inputs else "Emission"
            bsdf.inputs[key].default_value = (*emission, 1.0)
            if "Emission Strength" in bsdf.inputs:
                bsdf.inputs["Emission Strength"].default_value = emission_strength
        if "Transmission Weight" in bsdf.inputs:
            bsdf.inputs["Transmission Weight"].default_value = transmission
        bsdf.inputs["Alpha"].default_value = alpha
    if alpha < 1.0:
        mat.surface_render_method = 'DITHERED'
        mat.use_transparency_overlap = False
    return mat


def build_materials():
    MATS.clear()
    MATS.update({
        "paint": material_principled("NCAB_MAT_Paint_Solar", (1.0, 0.34, 0.018), 0.18, 0.24),
        "paint_alt": material_principled("NCAB_MAT_Paint_Cyan", (0.015, 0.55, 0.68), 0.2, 0.25),
        "dark": material_principled("NCAB_MAT_Cladding", (0.012, 0.018, 0.028), 0.38, 0.24),
        "rubber": material_principled("NCAB_MAT_Rubber", (0.009, 0.011, 0.014), 0.0, 0.58),
        "metal": material_principled("NCAB_MAT_Metal", (0.14, 0.18, 0.22), 0.86, 0.19),
        "chrome": material_principled("NCAB_MAT_Chrome", (0.6, 0.7, 0.78), 1.0, 0.1),
        "glass": material_principled("NCAB_MAT_Glass", (0.015, 0.045, 0.075), 0.2, 0.12, transmission=0.24, alpha=0.72),
        "cyan": material_principled("NCAB_MAT_Neon_Cyan", (0.01, 0.48, 0.66), 0.05, 0.22, (0.0, 0.85, 1.0), 6.0),
        "magenta": material_principled("NCAB_MAT_Neon_Magenta", (0.72, 0.02, 0.26), 0.05, 0.25, (1.0, 0.01, 0.22), 4.5),
        "decal_cyan": material_principled("NCAB_MAT_Decal_Cyan", (0.015, 0.52, 0.64), 0.08, 0.34),
        "decal_magenta": material_principled("NCAB_MAT_Decal_Magenta", (0.82, 0.018, 0.22), 0.05, 0.36),
        "white": material_principled("NCAB_MAT_Headlamp", (0.72, 0.86, 1.0), 0.0, 0.16, (0.7, 0.9, 1.0), 8.0),
        "red": material_principled("NCAB_MAT_Taillamp", (0.55, 0.005, 0.01), 0.0, 0.2, (1.0, 0.0, 0.015), 7.0),
        "cream": material_principled("NCAB_MAT_Interior", (0.25, 0.16, 0.09), 0.0, 0.62),
        "clay": material_principled("NCAB_MAT_Clay", (0.52, 0.54, 0.56), 0.0, 0.62),
        "wire": material_principled("NCAB_MAT_Wire", (0.0, 0.65, 0.85), 0.0, 0.25, (0.0, 0.75, 1.0), 5.0),
        "floor": material_principled("NCAB_MAT_Floor", (0.012, 0.016, 0.026), 0.15, 0.28),
    })

    paint = MATS["paint"]
    nodes = paint.node_tree.nodes
    links = paint.node_tree.links
    bsdf = nodes.get("Principled BSDF")
    noise = nodes.get("NCAB_SubtleFlake") or nodes.new("ShaderNodeTexNoise")
    noise.name = "NCAB_SubtleFlake"
    noise.inputs["Scale"].default_value = 85.0
    noise.inputs["Detail"].default_value = 2.0
    noise.inputs["Roughness"].default_value = 0.35
    ramp = nodes.get("NCAB_FlakeRamp") or nodes.new("ShaderNodeValToRGB")
    ramp.name = "NCAB_FlakeRamp"
    ramp.color_ramp.elements[0].position = 0.34
    ramp.color_ramp.elements[0].color = (0.12, 0.12, 0.12, 1)
    ramp.color_ramp.elements[1].position = 0.76
    ramp.color_ramp.elements[1].color = (0.32, 0.32, 0.32, 1)
    links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
    if bsdf:
        links.new(ramp.outputs["Color"], bsdf.inputs["Roughness"])


def assign(obj, mat):
    if hasattr(obj.data, "materials"):
        obj.data.materials.clear()
        obj.data.materials.append(mat)


def smooth(obj):
    if obj.type == 'MESH':
        for poly in obj.data.polygons:
            poly.use_smooth = True
        obj.data.set_sharp_from_angle(angle=math.radians(55))


def bevel(obj, width=0.06, segments=3):
    mod = obj.modifiers.new("NCAB_Bevel", 'BEVEL')
    mod.width = width
    mod.segments = segments
    mod.limit_method = 'ANGLE'
    return mod


def box(name, loc, dims, mat, coll, bevel_width=0.05, rotation=(0, 0, 0), parent=None, role="detail", slot="CORE"):
    bpy.ops.mesh.primitive_cube_add(location=loc, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = dims
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel_width:
        bevel(obj, bevel_width)
    assign(obj, mat)
    smooth(obj)
    move_to_collection(obj, coll)
    if parent:
        parent_to(obj, parent)
    tag(obj, role, slot)
    return obj


def cylinder(name, loc, radius, depth, mat, coll, rotation=(math.pi / 2, 0, 0), vertices=40, bevel_width=0.025, parent=None, role="detail", slot="CORE"):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=loc, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    if bevel_width:
        bevel(obj, bevel_width, 2)
    assign(obj, mat)
    smooth(obj)
    move_to_collection(obj, coll)
    if parent:
        parent_to(obj, parent)
    tag(obj, role, slot)
    return obj


def torus(name, loc, major, minor, mat, coll, rotation=(math.pi / 2, 0, 0), parent=None, role="detail", slot="CORE"):
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, major_segments=40, minor_segments=10, location=loc, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    assign(obj, mat)
    smooth(obj)
    move_to_collection(obj, coll)
    if parent:
        parent_to(obj, parent)
    tag(obj, role, slot)
    return obj


def tapered_prism(name, x0, x1, y0, y1, z0, z1, mat, coll, front_shift=0.0, rear_shift=0.0, bevel_width=0.05, parent=None, role="body", slot="CORE"):
    verts = [
        (x0, -y0, z0), (x1, -y0, z0), (x1, y0, z0), (x0, y0, z0),
        (x0 + rear_shift, -y1, z1), (x1 + front_shift, -y1, z1),
        (x1 + front_shift, y1, z1), (x0 + rear_shift, y1, z1),
    ]
    faces = [(0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (4, 0, 3, 7)]
    mesh = bpy.data.meshes.new(name + "_MESH")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    coll.objects.link(obj)
    if bevel_width:
        bevel(obj, bevel_width, 3)
    assign(obj, mat)
    smooth(obj)
    if parent:
        parent_to(obj, parent)
    tag(obj, role, slot)
    return obj


def side_polygon(name, points, y, thickness, mat, coll, parent=None, slot="DECALS"):
    y0 = y - thickness / 2
    y1 = y + thickness / 2
    verts = [(x, y0, z) for x, z in points] + [(x, y1, z) for x, z in points]
    n = len(points)
    faces = [tuple(range(n)), tuple(range(2 * n - 1, n - 1, -1))]
    for i in range(n):
        j = (i + 1) % n
        faces.append((i, j, n + j, n + i))
    mesh = bpy.data.meshes.new(name + "_MESH")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    coll.objects.link(obj)
    bevel(obj, 0.012, 2)
    assign(obj, mat)
    if parent:
        parent_to(obj, parent)
    tag(obj, "decal_geometry", slot)
    return obj


def text_obj(name, body, loc, size, mat, coll, rotation=(0, 0, 0), extrude=0.008, parent=None, slot="SIGNAGE"):
    bpy.ops.object.text_add(location=loc, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    obj.data.body = body
    obj.data.align_x = 'CENTER'
    obj.data.align_y = 'CENTER'
    obj.data.size = size
    obj.data.extrude = extrude
    obj.data.bevel_depth = min(0.004, extrude * 0.45)
    obj.data.bevel_resolution = 2
    assign(obj, mat)
    move_to_collection(obj, coll)
    if parent:
        parent_to(obj, parent)
    tag(obj, "signage", slot)
    return obj


def empty(name, parent=None, coll=None, slot="SLOT"):
    obj = bpy.data.objects.new(name, None)
    obj.empty_display_type = 'PLAIN_AXES'
    obj.empty_display_size = 0.35
    (coll or bpy.context.scene.collection).objects.link(obj)
    if parent:
        parent_to(obj, parent)
    tag(obj, "module_socket", slot)
    return obj


def build_hierarchy():
    global ROOT
    base = bpy.data.collections.get("NCAB_ASSET") or get_collection("NCAB_ASSET")
    body = get_collection("NCAB_01_BODY_CORE", base)
    front = get_collection("NCAB_02_MODULE_FRONT", base)
    rear = get_collection("NCAB_03_MODULE_REAR", base)
    wheels = get_collection("NCAB_04_MODULE_WHEELS", base)
    roof = get_collection("NCAB_05_MODULE_ROOF", base)
    sides = get_collection("NCAB_06_MODULE_SIDES", base)
    interior = get_collection("NCAB_07_INTERIOR", base)
    decals = get_collection("NCAB_08_DECALS_SIGNAGE", base)
    presentation = get_collection("NCAB_90_PRESENTATION")
    ASSET_COLLECTIONS[:] = [body, front, rear, wheels, roof, sides, interior, decals]
    ROOT = empty("NCAB_ROOT", coll=base, slot="ROOT")
    ROOT["asset_name"] = "Neon Cab"
    ROOT["design_intent"] = "Original arcade taxi; modular production asset"
    ROOT["forward_axis"] = "+X"
    ROOT["units"] = "meters"
    for name in ["Front", "Rear", "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR", "Roof", "Side_L", "Side_R", "Decals", "Interior"]:
        slot = empty("NCAB_SLOT_" + name, parent=ROOT, coll=base, slot=name.upper())
        SLOTS[name] = slot
    return {"base": base, "body": body, "front": front, "rear": rear, "wheels": wheels, "roof": roof,
            "sides": sides, "interior": interior, "decals": decals, "presentation": presentation}


def hydrate_scene():
    """Reconnect module globals after the MCP executes this file in a fresh namespace."""
    global ROOT
    ROOT = bpy.data.objects.get("NCAB_ROOT")
    if ROOT is None:
        raise RuntimeError("NCAB_ROOT not found; run phase_1_blockout first")
    build_materials()
    SLOTS.clear()
    for name in ["Front", "Rear", "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR", "Roof", "Side_L", "Side_R", "Decals", "Interior"]:
        obj = bpy.data.objects.get("NCAB_SLOT_" + name)
        if obj is None:
            raise RuntimeError(f"Missing module socket NCAB_SLOT_{name}")
        SLOTS[name] = obj
    ASSET_COLLECTIONS[:] = [bpy.data.collections[name] for name in [
        "NCAB_01_BODY_CORE", "NCAB_02_MODULE_FRONT", "NCAB_03_MODULE_REAR", "NCAB_04_MODULE_WHEELS",
        "NCAB_05_MODULE_ROOF", "NCAB_06_MODULE_SIDES", "NCAB_07_INTERIOR", "NCAB_08_DECALS_SIGNAGE"
    ]]
    return ROOT


def phase_1_blockout():
    ensure_dirs()
    clean_scene()
    build_materials()
    c = build_hierarchy()

    box("NCAB_Body_Undertray", (0, 0, 0.55), (4.25, 1.78, 0.28), MATS["dark"], c["body"], 0.11, parent=ROOT, role="underbody")
    box("NCAB_Body_Lower", (0, 0, 0.9), (4.48, 1.9, 0.62), MATS["paint"], c["body"], 0.21, parent=ROOT, role="body_shell")
    tapered_prism("NCAB_Body_Belt", -1.95, 1.95, 0.90, 0.82, 1.03, 1.48, MATS["paint"], c["body"], front_shift=-0.14, rear_shift=0.08, bevel_width=0.10, parent=ROOT, role="body_shell")
    tapered_prism("NCAB_Hood_A", 0.52, 2.15, 0.83, 0.75, 1.39, 1.55, MATS["paint"], c["body"], front_shift=0.02, rear_shift=0.03, bevel_width=0.07, parent=ROOT, role="hood", slot="HOOD")
    tapered_prism("NCAB_Cabin_Glass", -1.35, 0.72, 0.79, 0.64, 1.46, 2.14, MATS["glass"], c["body"], front_shift=-0.34, rear_shift=0.24, bevel_width=0.065, parent=ROOT, role="glazing")
    box("NCAB_Roof_Panel_A", (-0.27, 0, 2.15), (1.52, 1.34, 0.15), MATS["paint_alt"], c["body"], 0.07, parent=ROOT, role="roof_panel", slot="ROOF")
    box("NCAB_SideSkirt_L", (-0.05, -0.98, 0.63), (3.4, 0.13, 0.24), MATS["dark"], c["body"], 0.05, parent=ROOT, role="side_skirt")
    box("NCAB_SideSkirt_R", (-0.05, 0.98, 0.63), (3.4, 0.13, 0.24), MATS["dark"], c["body"], 0.05, parent=ROOT, role="side_skirt")
    box("NCAB_FrontCrashBar", (2.15, 0, 0.78), (0.25, 1.84, 0.28), MATS["dark"], c["front"], 0.08, parent=SLOTS["Front"], role="front_structure", slot="FRONT")
    box("NCAB_RearCrashBar", (-2.15, 0, 0.78), (0.25, 1.84, 0.28), MATS["dark"], c["rear"], 0.08, parent=SLOTS["Rear"], role="rear_structure", slot="REAR")

    # Stylized pillars separate the dark glass volume and remain easy to swap.
    for side in (-1, 1):
        y = side * 0.785
        box(f"NCAB_A_Pillar_{'L' if side < 0 else 'R'}", (0.55, y, 1.82), (0.12, 0.12, 0.78), MATS["paint"], c["body"], 0.035,
            rotation=(0, math.radians(-24), 0), parent=ROOT, role="pillar")
        box(f"NCAB_B_Pillar_{'L' if side < 0 else 'R'}", (-0.38, y, 1.82), (0.13, 0.12, 0.72), MATS["dark"], c["body"], 0.025,
            parent=ROOT, role="pillar")
        box(f"NCAB_C_Pillar_{'L' if side < 0 else 'R'}", (-1.15, y, 1.80), (0.15, 0.12, 0.72), MATS["paint"], c["body"], 0.035,
            rotation=(0, math.radians(20), 0), parent=ROOT, role="pillar")

    bpy.context.scene["NCAB_BUILD_PHASE"] = "01_BLOCKOUT"
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    return "Neon Cab phase 1 blockout complete"


def build_wheel(name, x, y, coll, slot):
    parent = SLOTS[slot]
    sign = -1 if y < 0 else 1
    tire = cylinder(name + "_Tire_A", (x, y, 0.61), 0.5, 0.36, MATS["rubber"], coll, parent=parent, role="wheel_tire", slot=slot.upper())
    # Tread blocks are low-cost instances around the silhouette.
    tread_proto = box(name + "_Tread_00", (x + 0.48, y, 0.61), (0.13, 0.39, 0.055), MATS["rubber"], coll, 0.012,
                      rotation=(0, 0, 0), parent=parent, role="wheel_tread", slot=slot.upper())
    for i in range(1, 14):
        angle = math.tau * i / 14
        obj = tread_proto.copy()
        obj.data = tread_proto.data
        obj.name = f"{name}_Tread_{i:02d}"
        coll.objects.link(obj)
        obj.location = (x + math.cos(angle) * 0.48, y, 0.61 + math.sin(angle) * 0.48)
        obj.rotation_euler[1] = -angle
        parent_to(obj, parent)
        tag(obj, "wheel_tread", slot.upper())
    outer_y = y + sign * 0.19
    cylinder(name + "_Rim", (x, outer_y, 0.61), 0.34, 0.08, MATS["metal"], coll, parent=parent, role="wheel_rim", slot=slot.upper())
    cylinder(name + "_Hub", (x, outer_y + sign * 0.045, 0.61), 0.12, 0.09, MATS["cyan"], coll, parent=parent, role="wheel_hub", slot=slot.upper())
    for i in range(6):
        angle = math.tau * i / 6
        spoke = box(f"{name}_Spoke_{i:02d}", (x + math.cos(angle) * 0.17, outer_y + sign * 0.05, 0.61 + math.sin(angle) * 0.17),
                    (0.29, 0.055, 0.07), MATS["chrome"], coll, 0.018,
                    rotation=(0, angle, 0), parent=parent, role="wheel_spoke", slot=slot.upper())
    torus(name + "_Fender", (x, y - sign * 0.015, 0.63), 0.55, 0.09, MATS["paint"], coll, parent=parent, role="fender_flare", slot=slot.upper())
    return tire


def phase_2_modules():
    c = {
        "front": bpy.data.collections["NCAB_02_MODULE_FRONT"],
        "rear": bpy.data.collections["NCAB_03_MODULE_REAR"],
        "wheels": bpy.data.collections["NCAB_04_MODULE_WHEELS"],
        "roof": bpy.data.collections["NCAB_05_MODULE_ROOF"],
        "sides": bpy.data.collections["NCAB_06_MODULE_SIDES"],
        "decals": bpy.data.collections["NCAB_08_DECALS_SIGNAGE"],
    }
    for name, x, y, slot in [
        ("NCAB_Wheel_FL", 1.42, -1.00, "Wheel_FL"), ("NCAB_Wheel_FR", 1.42, 1.00, "Wheel_FR"),
        ("NCAB_Wheel_RL", -1.42, -1.00, "Wheel_RL"), ("NCAB_Wheel_RR", -1.42, 1.00, "Wheel_RR")]:
        build_wheel(name, x, y, c["wheels"], slot)

    # Front module: broad readable graphic with stacked lamps and replaceable splitter.
    front = SLOTS["Front"]
    box("NCAB_MOD_FrontBumper_A", (2.30, 0, 0.67), (0.25, 2.02, 0.32), MATS["dark"], c["front"], 0.09, parent=front, role="bumper", slot="FRONT")
    box("NCAB_MOD_FrontSplitter_A", (2.39, 0, 0.43), (0.34, 2.14, 0.10), MATS["magenta"], c["front"], 0.035, parent=front, role="splitter", slot="FRONT")
    box("NCAB_MOD_Grille_A", (2.31, 0, 1.03), (0.09, 1.25, 0.42), MATS["dark"], c["front"], 0.04, parent=front, role="grille", slot="FRONT")
    for y in (-0.48, -0.24, 0, 0.24, 0.48):
        box(f"NCAB_GrilleBar_{y:+.2f}", (2.365, y, 1.03), (0.035, 0.045, 0.34), MATS["metal"], c["front"], 0.008, parent=front, role="grille_insert", slot="FRONT")
    for side in (-1, 1):
        label = "L" if side < 0 else "R"
        y = side * 0.66
        box(f"NCAB_MOD_Headlamp_{label}_A", (2.32, y, 1.31), (0.09, 0.46, 0.17), MATS["white"], c["front"], 0.045, parent=front, role="headlight", slot="FRONT")
        box(f"NCAB_MOD_DRL_{label}", (2.375, y, 1.16), (0.045, 0.50, 0.045), MATS["cyan"], c["front"], 0.018, parent=front, role="daytime_light", slot="FRONT")
        cylinder(f"NCAB_MOD_Fog_{label}", (2.43, side * 0.77, 0.72), 0.12, 0.07, MATS["white"], c["front"], rotation=(0, math.pi / 2, 0), parent=front, role="fog_light", slot="FRONT")
    for i in range(3):
        box(f"NCAB_HoodVent_{i+1}", (1.25 - i * 0.16, -0.18 + i * 0.18, 1.575), (0.48, 0.10, 0.035), MATS["dark"], c["front"], 0.012,
            rotation=(0, math.radians(-2), math.radians(8)), parent=front, role="hood_vent", slot="HOOD")

    # Rear module and arcade aero.
    rear = SLOTS["Rear"]
    box("NCAB_MOD_RearBumper_A", (-2.30, 0, 0.67), (0.25, 2.02, 0.34), MATS["dark"], c["rear"], 0.09, parent=rear, role="bumper", slot="REAR")
    box("NCAB_MOD_RearDiffuser_A", (-2.40, 0, 0.43), (0.30, 1.90, 0.12), MATS["magenta"], c["rear"], 0.035, parent=rear, role="diffuser", slot="REAR")
    for side in (-1, 1):
        label = "L" if side < 0 else "R"
        box(f"NCAB_MOD_Taillamp_{label}_A", (-2.28, side * 0.64, 1.25), (0.09, 0.48, 0.17), MATS["red"], c["rear"], 0.045, parent=rear, role="taillight", slot="REAR")
        box(f"NCAB_MOD_RearSlash_{label}", (-2.34, side * 0.64, 1.12), (0.045, 0.52, 0.045), MATS["cyan"], c["rear"], 0.018, parent=rear, role="rear_marker", slot="REAR")
        cylinder(f"NCAB_Exhaust_{label}", (-2.42, side * 0.62, 0.53), 0.10, 0.18, MATS["chrome"], c["rear"], rotation=(0, math.pi / 2, 0), parent=rear, role="exhaust", slot="REAR")
    for side in (-1, 1):
        box(f"NCAB_SpoilerStand_{side:+d}", (-1.92, side * 0.57, 1.63), (0.15, 0.10, 0.42), MATS["dark"], c["rear"], 0.03, parent=rear, role="spoiler_mount", slot="REAR")
    box("NCAB_MOD_Spoiler_A", (-2.00, 0, 1.86), (0.52, 1.72, 0.12), MATS["paint_alt"], c["rear"], 0.055, rotation=(0, math.radians(-5), 0), parent=rear, role="spoiler", slot="REAR")

    # Side modules: mirrors and protective door blades.
    for side in (-1, 1):
        label = "L" if side < 0 else "R"
        slot = SLOTS["Side_L" if side < 0 else "Side_R"]
        box(f"NCAB_MOD_DoorBlade_{label}_A", (-0.05, side * 0.985, 1.02), (2.34, 0.10, 0.25), MATS["dark"], c["sides"], 0.045, parent=slot, role="door_cladding", slot="SIDE")
        box(f"NCAB_MOD_Mirror_{label}_A", (0.55, side * 1.08, 1.73), (0.32, 0.30, 0.18), MATS["paint_alt"], c["sides"], 0.065, parent=slot, role="mirror", slot="SIDE")
        box(f"NCAB_MirrorSignal_{label}", (0.58, side * 1.235, 1.73), (0.22, 0.035, 0.045), MATS["cyan"], c["sides"], 0.014, parent=slot, role="mirror_signal", slot="SIDE")
        for x in (-0.78, 0.14):
            box(f"NCAB_DoorHandle_{label}_{x:+.2f}", (x, side * 0.968, 1.46), (0.30, 0.045, 0.06), MATS["metal"], c["sides"], 0.018, parent=slot, role="door_handle", slot="SIDE")

    # Oversized roof module is the main long-distance taxi read.
    roof = SLOTS["Roof"]
    box("NCAB_MOD_RoofSign_Base_A", (-0.20, 0, 2.29), (0.95, 0.72, 0.10), MATS["dark"], c["roof"], 0.04, parent=roof, role="roof_sign_base", slot="ROOF")
    tapered_prism("NCAB_MOD_RoofSign_A", -0.68, 0.28, 0.32, 0.25, 2.31, 2.68, MATS["white"], c["roof"], front_shift=-0.08, rear_shift=0.08,
                  bevel_width=0.045, parent=roof, role="roof_sign", slot="ROOF")
    box("NCAB_RoofSign_CyanRail", (-0.20, -0.335, 2.50), (0.82, 0.035, 0.07), MATS["cyan"], c["roof"], 0.014, parent=roof, role="roof_sign_light", slot="ROOF")
    box("NCAB_RoofSign_MagentaRail", (-0.20, 0.335, 2.50), (0.82, 0.035, 0.07), MATS["magenta"], c["roof"], 0.014, parent=roof, role="roof_sign_light", slot="ROOF")

    bpy.context.scene["NCAB_BUILD_PHASE"] = "02_MODULES"
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    return "Neon Cab phase 2 modules complete"


def build_interior(coll):
    slot = SLOTS["Interior"]
    box("NCAB_Interior_Floor", (-0.25, 0, 0.97), (2.65, 1.48, 0.10), MATS["dark"], coll, 0.04, parent=slot, role="interior_floor", slot="INTERIOR")
    for x, prefix in [(0.40, "Front"), (-0.72, "Rear")]:
        for side in (-1, 1):
            y = side * 0.43
            box(f"NCAB_Seat_{prefix}_{'L' if side < 0 else 'R'}", (x, y, 1.27), (0.55, 0.46, 0.66), MATS["cream"], coll, 0.12,
                rotation=(0, math.radians(-7), 0), parent=slot, role="seat", slot="INTERIOR")
            box(f"NCAB_SeatStripe_{prefix}_{'L' if side < 0 else 'R'}", (x - 0.02, y - side * 0.235, 1.30), (0.34, 0.025, 0.42), MATS["magenta"], coll, 0.025,
                parent=slot, role="seat_accent", slot="INTERIOR")
    box("NCAB_Dashboard", (0.62, 0, 1.43), (0.38, 1.42, 0.25), MATS["dark"], coll, 0.08, rotation=(0, math.radians(-12), 0), parent=slot, role="dashboard", slot="INTERIOR")
    cylinder("NCAB_SteeringWheel", (0.48, -0.42, 1.55), 0.17, 0.045, MATS["metal"], coll, rotation=(math.radians(80), 0, 0), parent=slot, role="steering_wheel", slot="INTERIOR")
    box("NCAB_DashScreen", (0.68, -0.05, 1.55), (0.05, 0.45, 0.16), MATS["cyan"], coll, 0.025, rotation=(0, math.radians(-12), 0), parent=slot, role="infotainment", slot="INTERIOR")


def smart_uv_all():
    mesh_objs = [o for coll in ASSET_COLLECTIONS for o in coll.objects if o.type == 'MESH']
    for obj in mesh_objs:
        if len(obj.data.polygons) == 0:
            continue
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        for other in bpy.context.selected_objects:
            if other != obj:
                other.select_set(False)
        try:
            bpy.ops.object.mode_set(mode='EDIT')
            bpy.ops.mesh.select_all(action='SELECT')
            bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.025)
            bpy.ops.object.mode_set(mode='OBJECT')
        except Exception:
            if obj.mode != 'OBJECT':
                bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.select_all(action='DESELECT')


def phase_3_surface_and_decals():
    decals = bpy.data.collections["NCAB_08_DECALS_SIGNAGE"]
    interior = bpy.data.collections["NCAB_07_INTERIOR"]
    sides = bpy.data.collections["NCAB_06_MODULE_SIDES"]
    build_interior(interior)

    # Original identity: a split-color speed glyph, not any series logo.
    bolt = [(-0.95, 1.37), (-0.30, 1.37), (-0.52, 1.23), (0.63, 1.23), (-0.48, 0.92), (-0.15, 1.10), (-0.92, 1.10)]
    side_polygon("NCAB_Decal_SpeedGlyph_L", bolt, -1.045, 0.026, MATS["decal_cyan"], decals, parent=SLOTS["Decals"], slot="DECALS")
    side_polygon("NCAB_Decal_SpeedGlyph_R", bolt, 1.045, 0.026, MATS["decal_cyan"], decals, parent=SLOTS["Decals"], slot="DECALS")
    slash = [(0.42, 1.38), (0.73, 1.38), (0.45, 0.95), (0.14, 0.95)]
    side_polygon("NCAB_Decal_MagentaSlash_L", slash, -1.061, 0.022, MATS["decal_magenta"], decals, parent=SLOTS["Decals"], slot="DECALS")
    side_polygon("NCAB_Decal_MagentaSlash_R", slash, 1.061, 0.022, MATS["decal_magenta"], decals, parent=SLOTS["Decals"], slot="DECALS")

    for side in (-1, 1):
        label = "L" if side < 0 else "R"
        rot = (math.pi / 2, 0, 0) if side < 0 else (-math.pi / 2, 0, 0)
        y = side * 1.075
        brand = text_obj(f"NCAB_Brand_{label}", "NEON//CAB", (-0.22, y, 1.41), 0.21, MATS["dark"], decals, rotation=rot, extrude=0.005, parent=SLOTS["Decals"], slot="SIGNAGE")
        if side > 0:
            brand.rotation_euler = (math.pi / 2, 0, math.pi)
        text_obj(f"NCAB_Number_{label}", "88", (-1.57, y, 1.42), 0.32, MATS["white"], decals, rotation=rot, extrude=0.007, parent=SLOTS["Decals"], slot="SIGNAGE")
        for i, x in enumerate([-1.65, -1.42, -1.19, -0.96, 0.88, 1.11, 1.34, 1.57]):
            mat = MATS["dark"] if i % 2 == 0 else MATS["cream"]
            box(f"NCAB_Check_{label}_{i:02d}", (x, side * 1.056, 1.08), (0.21, 0.028, 0.13), mat, decals, 0.008,
                parent=SLOTS["Decals"], role="checker_decal", slot="DECALS")

    # Sign typography is duplicated on both faces for modular readability.
    text_obj("NCAB_RoofText_L", "NEON CAB", (-0.20, -0.365, 2.50), 0.16, MATS["dark"], decals,
             rotation=(math.pi / 2, 0, 0), extrude=0.008, parent=SLOTS["Roof"], slot="ROOF")
    roof_right = text_obj("NCAB_RoofText_R", "NEON CAB", (-0.20, 0.365, 2.50), 0.16, MATS["dark"], decals,
                          rotation=(-math.pi / 2, 0, 0), extrude=0.008, parent=SLOTS["Roof"], slot="ROOF")
    roof_right.rotation_euler = (math.pi / 2, 0, math.pi)

    # Underglow and small service details.
    box("NCAB_Underglow_L", (-0.05, -0.86, 0.39), (2.8, 0.055, 0.04), MATS["cyan"], sides, 0.012, parent=ROOT, role="underglow")
    box("NCAB_Underglow_R", (-0.05, 0.86, 0.39), (2.8, 0.055, 0.04), MATS["cyan"], sides, 0.012, parent=ROOT, role="underglow")
    cylinder("NCAB_TowHook", (2.48, -0.58, 0.52), 0.07, 0.10, MATS["magenta"], sides, rotation=(0, math.pi / 2, 0), parent=SLOTS["Front"], role="tow_hook", slot="FRONT")
    box("NCAB_RoofAntenna", (-0.90, 0, 2.34), (0.24, 0.04, 0.04), MATS["dark"], sides, 0.012, rotation=(0, math.radians(-35), 0), parent=SLOTS["Roof"], role="antenna", slot="ROOF")

    smart_uv_all()
    bpy.context.scene["NCAB_BUILD_PHASE"] = "03_TEXTURED"
    bpy.context.scene["NCAB_UV_METHOD"] = "Smart Project, 0.025 island margin; shared procedural materials"
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    return "Neon Cab phase 3 materials, decals, interior, and UVs complete"


def look_at(obj, target=(0, 0, 1.1)):
    direction = Vector(target) - obj.location
    obj.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()


def add_area(name, loc, energy, color, size, coll):
    data = bpy.data.lights.new(name + "_DATA", type='AREA')
    data.energy = energy
    data.color = color
    data.shape = 'DISK'
    data.size = size
    obj = bpy.data.objects.new(name, data)
    coll.objects.link(obj)
    obj.location = loc
    look_at(obj, (0, 0, 0.9))
    return obj


def setup_presentation():
    coll = bpy.data.collections.get("NCAB_90_PRESENTATION") or get_collection("NCAB_90_PRESENTATION")
    # Clear only previous presentation objects.
    for obj in list(coll.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    floor = box("NCAB_StudioFloor", (0, 0, 0.03), (18, 18, 0.12), MATS["floor"], coll, 0.10, role="presentation")
    floor["exclude_from_export"] = True
    # Accent strips give the beauty renders a game-select-screen feel.
    box("NCAB_StudioStrip_Cyan", (0, -3.7, 0.105), (12, 0.12, 0.025), MATS["cyan"], coll, 0.02, role="presentation")
    box("NCAB_StudioStrip_Magenta", (0, 3.7, 0.105), (12, 0.12, 0.025), MATS["magenta"], coll, 0.02, role="presentation")
    add_area("NCAB_Light_Key", (4.5, -5.2, 7.2), 1450, (1.0, 0.72, 0.46), 5.2, coll)
    add_area("NCAB_Light_Fill", (-3.0, -3.5, 4.2), 850, (0.28, 0.58, 1.0), 4.0, coll)
    add_area("NCAB_Light_Rim", (-3.5, 4.8, 5.8), 1650, (0.05, 0.85, 1.0), 3.5, coll)
    add_area("NCAB_Light_Top", (0, 0, 8.5), 1100, (1.0, 0.18, 0.34), 4.5, coll)
    camera_data = bpy.data.cameras.new("NCAB_RenderCamera_DATA")
    camera_data.lens = 58
    camera_data.sensor_width = 36
    camera = bpy.data.objects.new("NCAB_RenderCamera", camera_data)
    coll.objects.link(camera)
    bpy.context.scene.camera = camera
    return coll, camera


def setup_render():
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE_NEXT'
    scene.render.resolution_x = 720
    scene.render.resolution_y = 540
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGBA'
    scene.render.film_transparent = False
    scene.render.use_file_extension = True
    scene.render.image_settings.color_depth = '8'
    scene.world.color = (0.004, 0.006, 0.013)
    world = scene.world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs["Color"].default_value = (0.004, 0.006, 0.016, 1)
    bg.inputs["Strength"].default_value = 0.18
    try:
        scene.view_settings.look = 'AgX - Medium High Contrast'
    except Exception:
        pass
    scene.render.filepath = os.path.join(RENDER_DIR, "preview.png")


def render_view(camera, filename, loc, target=(0, 0, 1.1), lens=58, res=(720, 540)):
    scene = bpy.context.scene
    camera.location = loc
    camera.data.lens = lens
    look_at(camera, target)
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.filepath = os.path.join(RENDER_DIR, filename)
    bpy.ops.render.render(write_still=True)


def render_clay(camera):
    objects = [o for coll in ASSET_COLLECTIONS for o in coll.objects if hasattr(o.data, "materials")]
    previous = {o.name: list(o.data.materials) for o in objects}
    for obj in objects:
        obj.data.materials.clear()
        obj.data.materials.append(MATS["clay"])
    render_view(camera, "clay_three_quarter_front.png", (7.4, -7.4, 4.1), (0, 0, 1.12), 62)
    for obj in objects:
        obj.data.materials.clear()
        for mat in previous[obj.name]:
            obj.data.materials.append(mat)


def render_wire(camera):
    coll = bpy.data.collections.get("NCAB_91_WIREFRAME") or get_collection("NCAB_91_WIREFRAME")
    for obj in list(coll.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    originals = [o for c in ASSET_COLLECTIONS for o in c.objects if o.type == 'MESH' and not o.hide_render]
    old_states = {o.name: o.hide_render for o in originals}
    for obj in originals:
        dup = obj.copy()
        dup.data = obj.data.copy()
        dup.name = "WIRE_" + obj.name
        coll.objects.link(dup)
        dup.parent = None
        dup.matrix_world = obj.matrix_world.copy()
        dup.data.materials.clear()
        dup.data.materials.append(MATS["wire"])
        mod = dup.modifiers.new("NCAB_Wireframe", 'WIREFRAME')
        mod.thickness = 0.012
        mod.use_replace = True
        obj.hide_render = True
    render_view(camera, "wireframe_three_quarter_front.png", (7.4, -7.4, 4.1), (0, 0, 1.12), 62)
    for obj in originals:
        obj.hide_render = old_states[obj.name]
    for obj in list(coll.objects):
        bpy.data.objects.remove(obj, do_unlink=True)


def render_exploded(camera):
    offsets = {
        "Front": Vector((1.0, 0, 0.15)), "Rear": Vector((-1.0, 0, 0.15)),
        "Wheel_FL": Vector((0, -0.65, 0)), "Wheel_FR": Vector((0, 0.65, 0)),
        "Wheel_RL": Vector((0, -0.65, 0)), "Wheel_RR": Vector((0, 0.65, 0)),
        "Roof": Vector((0, 0, 0.9)), "Side_L": Vector((0, -0.45, 0)), "Side_R": Vector((0, 0.45, 0)),
        "Decals": Vector((0, 0, 0.2)), "Interior": Vector((0, 0, 0.35)),
    }
    old = {name: slot.location.copy() for name, slot in SLOTS.items()}
    for name, offset in offsets.items():
        SLOTS[name].location += offset
    bpy.context.view_layer.update()
    render_view(camera, "exploded_modular_view.png", (9.5, -10.0, 6.7), (0, 0, 1.35), 66, (800, 600))
    for name, loc in old.items():
        SLOTS[name].location = loc
    bpy.context.view_layer.update()


def render_stills(camera):
    views = [
        ("front.png", (8.8, 0, 2.45), (0, 0, 1.08), 66),
        ("rear.png", (-8.8, 0, 2.45), (0, 0, 1.08), 66),
        ("side.png", (0, -9.2, 2.6), (0, 0, 1.12), 66),
        ("three_quarter_front.png", (7.4, -7.4, 4.1), (0, 0, 1.12), 62),
        ("three_quarter_rear.png", (-7.4, 7.4, 3.9), (0, 0, 1.10), 62),
        ("top.png", (0, -0.01, 11.5), (0, 0, 0.8), 58),
        ("beauty_low_front.png", (7.0, -6.2, 2.15), (0.05, 0, 1.0), 52),
        ("beauty_hero.png", (6.4, 6.8, 3.15), (0, 0, 1.12), 56),
    ]
    for filename, loc, target, lens in views:
        render_view(camera, filename, loc, target, lens)


def create_turnaround(camera):
    scene = bpy.context.scene
    turn = bpy.data.objects.get("NCAB_Turntable")
    if turn is None:
        turn = empty("NCAB_Turntable", coll=bpy.data.collections["NCAB_ASSET"], slot="RIG")
    ROOT.parent = turn
    ROOT.matrix_parent_inverse = turn.matrix_world.inverted()
    turn.rotation_euler = (0, 0, 0)
    turn.keyframe_insert(data_path="rotation_euler", frame=1, index=2)
    turn.rotation_euler.z = math.tau
    turn.keyframe_insert(data_path="rotation_euler", frame=72, index=2)
    if turn.animation_data and turn.animation_data.action:
        for fcurve in turn.animation_data.action.fcurves:
            for point in fcurve.keyframe_points:
                point.interpolation = 'LINEAR'
    scene.frame_start = 1
    scene.frame_end = 72
    scene.render.resolution_x = 640
    scene.render.resolution_y = 360
    scene.render.image_settings.file_format = 'FFMPEG'
    scene.render.ffmpeg.format = 'MPEG4'
    scene.render.ffmpeg.codec = 'H264'
    scene.render.ffmpeg.constant_rate_factor = 'MEDIUM'
    scene.render.fps = 24
    scene.render.filepath = os.path.join(TURN_DIR, "neon-cab-turnaround.mp4")
    camera.location = (7.7, -7.7, 3.6)
    camera.data.lens = 60
    look_at(camera, (0, 0, 1.10))
    bpy.ops.render.render(animation=True)
    scene.render.image_settings.file_format = 'PNG'
    scene.render.resolution_x = 720
    scene.render.resolution_y = 540
    turn.rotation_euler.z = 0
    scene.frame_set(1)


def phase_4_render_all(render_animation=True):
    ensure_dirs()
    setup_render()
    _, camera = setup_presentation()
    render_stills(camera)
    render_clay(camera)
    render_wire(camera)
    render_exploded(camera)
    if render_animation:
        create_turnaround(camera)
    bpy.context.scene["NCAB_BUILD_PHASE"] = "04_FINAL"
    bpy.context.scene["NCAB_RENDER_SET"] = "front, rear, side, 3Q front/rear, top, beauty, clay, wire, exploded, turnaround"
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    return "Neon Cab final render set and Blender file complete"


def validate_asset():
    meshes = [o for c in ASSET_COLLECTIONS for o in c.objects if o.type == 'MESH']
    verts = sum(len(o.data.vertices) for o in meshes)
    polys = sum(len(o.data.polygons) for o in meshes)
    missing_uv = [o.name for o in meshes if len(o.data.uv_layers) == 0]
    required = ["NCAB_ROOT", "NCAB_MOD_FrontBumper_A", "NCAB_MOD_RearBumper_A", "NCAB_MOD_RoofSign_A", "NCAB_Wheel_FL_Tire_A"]
    missing_required = [name for name in required if bpy.data.objects.get(name) is None]
    result = {
        "mesh_objects": len(meshes), "vertices": verts, "polygons": polys,
        "missing_uv_count": len(missing_uv), "missing_uv": missing_uv[:20],
        "missing_required": missing_required, "blend_path": BLEND_PATH,
    }
    bpy.context.scene["NCAB_VALIDATION"] = str(result)
    return result


def build_all(render_animation=True):
    phase_1_blockout()
    phase_2_modules()
    phase_3_surface_and_decals()
    phase_4_render_all(render_animation=render_animation)
    return validate_asset()
