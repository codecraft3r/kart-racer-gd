extends SceneTree
func _initialize():
    call_deferred("capture")
func capture():
    var world=Node3D.new()
    root.add_child(world)
    var env=WorldEnvironment.new()
    var environment=Environment.new()
    environment.background_mode=Environment.BG_COLOR
    environment.background_color=Color(0.10,0.13,0.18)
    environment.ambient_light_source=Environment.AMBIENT_SOURCE_COLOR
    environment.ambient_light_color=Color(0.75,0.82,0.95)
    environment.ambient_light_energy=0.65
    env.environment=environment
    world.add_child(env)
    var sun=DirectionalLight3D.new()
    sun.rotation_degrees=Vector3(-45,-40,0)
    sun.light_energy=1.7
    world.add_child(sun)
    var floor=MeshInstance3D.new()
    var plane=PlaneMesh.new()
    plane.size=Vector2(24,24)
    floor.mesh=plane
    floor.position.y=-0.015
    var groundmat=StandardMaterial3D.new()
    groundmat.albedo_color=Color(0.12,0.16,0.21)
    groundmat.roughness=0.9
    floor.material_override=groundmat
    world.add_child(floor)
    var kart=load("res://kart.tscn").instantiate()
    kart.freeze=true
    world.add_child(kart)
    kart.call("SetVehicleOption",5)
    var camera=Camera3D.new()
    camera.position=Vector3(2.45,1.65,3.35)
    world.add_child(camera)
    camera.look_at(Vector3(0,0.48,0))
    camera.current=true
    camera.fov=42
    DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path("res://timelapse/Crown_Victoria"))
    var names=["yellow","midnight","ivory"]
    for paint in range(3):
        kart.call("SetCrownPaint",paint)
        for i in range(8):
            await process_frame
        await RenderingServer.frame_post_draw
        var path="res://timelapse/Crown_Victoria/crown_game_%s.png" % names[paint]
        var err=root.get_texture().get_image().save_png(ProjectSettings.globalize_path(path))
        print("CROWN_CAPTURE ",err," ",ProjectSettings.globalize_path(path))
        if err != OK:
            quit(int(err))
            return
    quit(0)
func find_part(node,target):
    if node.name==target:return node
    for child in node.get_children():
        var found=find_part(child,target)
        if found!=null:return found
    return null
