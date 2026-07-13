extends SceneTree

const KART_SCENE := "res://kart.tscn"


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var packed := load(KART_SCENE) as PackedScene
	if packed == null:
		_fail("Unable to load %s." % KART_SCENE)
		return

	var kart := packed.instantiate() as Node3D
	get_root().add_child(kart)
	await process_frame

	var visual_container := kart.get_node_or_null("VisualContainer") as Node3D
	var neon_cab := kart.get_node_or_null(
		"VisualContainer/NeonCabVisual/NeonCabSource"
	) as Node3D
	if visual_container == null or neon_cab == null:
		_fail("Kart is missing the Neon Cab visual hierarchy.")
		return

	if kart.get_node_or_null("VisualContainer/ambulance") != null:
		_fail("Legacy ambulance visual is still present.")
		return

	var mesh_nodes := neon_cab.find_children("*", "MeshInstance3D", true, false)
	if mesh_nodes.size() < 150:
		_fail("Expected the modular Neon Cab mesh hierarchy; found %d meshes." % mesh_nodes.size())
		return

	var bounds := AABB()
	var has_bounds := false
	for node in mesh_nodes:
		var mesh_instance := node as MeshInstance3D
		if mesh_instance == null or mesh_instance.mesh == null:
			continue
		var relative_transform := (
			visual_container.global_transform.affine_inverse()
			* mesh_instance.global_transform
		)
		var mesh_bounds: AABB = relative_transform * mesh_instance.mesh.get_aabb()
		bounds = bounds.merge(mesh_bounds) if has_bounds else mesh_bounds
		has_bounds = true

	if not has_bounds:
		_fail("Neon Cab contains no renderable mesh bounds.")
		return

	if bounds.size.x < 1.35 or bounds.size.x > 1.65:
		_fail("Unexpected Neon Cab width: %.3f m." % bounds.size.x)
		return
	if bounds.size.y < 1.45 or bounds.size.y > 1.75:
		_fail("Unexpected Neon Cab height: %.3f m." % bounds.size.y)
		return
	if bounds.size.z < 2.80 or bounds.size.z > 3.15:
		_fail("Unexpected Neon Cab length: %.3f m." % bounds.size.z)
		return
	if bounds.position.y < -0.5 or bounds.position.y > -0.3:
		_fail("Neon Cab ground alignment is out of range: %.3f m." % bounds.position.y)
		return

	var front_bumper := neon_cab.find_child("NCAB_MOD_FrontBumper_A", true, false) as Node3D
	var rear_bumper := neon_cab.find_child("NCAB_MOD_RearBumper_A", true, false) as Node3D
	if front_bumper == null or rear_bumper == null:
		_fail("Neon Cab modular bumper nodes were not preserved by GLB import.")
		return

	var front_z := visual_container.to_local(front_bumper.global_position).z
	var rear_z := visual_container.to_local(rear_bumper.global_position).z
	if front_z <= rear_z:
		_fail("Neon Cab is not facing the kart's +Z driving direction.")
		return

	print(
		"PASS: Neon Cab integrated | meshes=%d bounds=%s front_z=%.3f rear_z=%.3f"
		% [mesh_nodes.size(), bounds, front_z, rear_z]
	)
	mesh_nodes.clear()
	front_bumper = null
	rear_bumper = null
	neon_cab = null
	visual_container = null
	kart.free()
	kart = null
	packed = null
	await process_frame
	quit(0)

func _fail(message: String) -> void:
	push_error("FAIL: %s" % message)
	quit(1)
