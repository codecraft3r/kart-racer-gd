extends SceneTree

const MAIN_SCENE := "res://default_3d.tscn"

var _state := "menu"
var _output := "res://artifacts/visual/menu.png"

func _initialize() -> void:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--state="):
			_state = argument.trim_prefix("--state=")
		elif argument.begins_with("--output="):
			_output = argument.trim_prefix("--output=")

	call_deferred("_run")

func _run() -> void:
	var packed_scene := load(MAIN_SCENE) as PackedScene
	if packed_scene == null:
		push_error("Unable to load %s" % MAIN_SCENE)
		quit(1)
		return

	var scene := packed_scene.instantiate()
	get_root().add_child(scene)
	await _wait_frames(8)

	var shell := scene.get_node_or_null("RetroNeonCabShell")
	if shell == null:
		push_error("RetroNeonCabShell was not found")
		quit(1)
		return

	if _state == "gameplay":
		shell.call("StartRun")
		await create_timer(3.4).timeout
		Input.action_press("move_forward")
		Input.action_press("move_right")
		await create_timer(0.75).timeout
		Input.action_release("move_right")
		await create_timer(0.45).timeout
		Input.action_release("move_forward")
		await _wait_frames(4)
	elif _state == "vehicle":
		var showcase_kart := scene.get_node_or_null("Kart")
		if showcase_kart == null:
			push_error("Unable to stage vehicle capture")
			quit(1)
			return
		showcase_kart.call("SetVehicleOption", 3)
		shell.call("StartRun")
		await create_timer(3.4).timeout
		Input.action_press("move_forward")
		await create_timer(0.9).timeout
		Input.action_release("move_forward")
		await _wait_frames(4)
	elif _state == "boarding":
		shell.call("StartRun")
		await create_timer(3.4).timeout
		var kart := scene.get_node_or_null("Kart") as RigidBody3D
		var mode := scene.get_node_or_null("Modes/TaxiMode")
		var pickup: Area3D = null
		for child in mode.get_children():
			if child is Area3D and child.name.begins_with("PickupZone"):
				pickup = child
				break
		if kart == null or pickup == null:
			push_error("Unable to stage boarding capture")
			quit(1)
			return
		pickup.set("LoadTime", 0.9)
		kart.global_position = pickup.global_position + Vector3.UP * 0.65
		kart.linear_velocity = Vector3.ZERO
		await create_timer(0.48).timeout
		await _wait_frames(3)
	elif _state == "dropoff":
		shell.call("StartRun")
		await create_timer(3.4).timeout
		var dropoff_kart := scene.get_node_or_null("Kart") as RigidBody3D
		var dropoff_mode := scene.get_node_or_null("Modes/TaxiMode")
		var dropoff_pickup: Area3D = null
		for child in dropoff_mode.get_children():
			if child is Area3D and child.name.begins_with("PickupZone"):
				dropoff_pickup = child
				break
		if dropoff_kart == null or dropoff_pickup == null:
			push_error("Unable to stage drop-off capture")
			quit(1)
			return
		dropoff_pickup.set("LoadTime", 0.1)
		dropoff_kart.global_position = dropoff_pickup.global_position + Vector3.UP * 0.65
		dropoff_kart.linear_velocity = Vector3.ZERO
		await create_timer(0.3).timeout
		var destination: Vector3 = dropoff_mode.call("GetPlayerDestination", 1)
		if destination == Vector3.ZERO:
			push_error("Drop-off destination was not assigned")
			quit(1)
			return
		dropoff_kart.global_position = destination + Vector3.UP * 0.65
		dropoff_kart.linear_velocity = Vector3.ZERO
		var dropoff_camera := scene.get_node_or_null("Camera3D") as Camera3D
		if dropoff_camera != null:
			dropoff_camera.process_mode = Node.PROCESS_MODE_DISABLED
			dropoff_camera.global_position = destination + Vector3(0.0, 3.1, 8.2)
			dropoff_camera.look_at(destination + Vector3.UP * 0.65, Vector3.UP)
		await create_timer(0.22).timeout
		await _wait_frames(3)
	elif _state != "menu":
		push_error("Unknown capture state: %s" % _state)
		quit(1)
		return

	await RenderingServer.frame_post_draw
	var image := get_root().get_texture().get_image()
	var absolute_output := ProjectSettings.globalize_path(_output)
	DirAccess.make_dir_recursive_absolute(absolute_output.get_base_dir())
	var error := image.save_png(absolute_output)
	if error != OK:
		push_error("Unable to save visual capture %s: %s" % [absolute_output, error_string(error)])
		quit(1)
		return

	print("VISUAL_CAPTURE: %s" % absolute_output)
	paused = false
	quit(0)

func _wait_frames(count: int) -> void:
	for _frame in count:
		await process_frame
