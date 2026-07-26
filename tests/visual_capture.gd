extends SceneTree

const MAIN_SCENE := "res://default_3d.tscn"

var _state := "menu"
var _output := "res://artifacts/visual/menu.png"
var _scene: Node
var _finished := false

func _initialize() -> void:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--state="):
			_state = argument.trim_prefix("--state=")
		elif argument.begins_with("--output="):
			_output = argument.trim_prefix("--output=")
		elif argument.begins_with("--resolution="):
			var parts := argument.trim_prefix("--resolution=").split("x")
			if parts.size() == 2:
				DisplayServer.window_set_size(Vector2i(int(parts[0]), int(parts[1])))

	call_deferred("_run")

func _run() -> void:
	var packed_scene := load(MAIN_SCENE) as PackedScene
	if packed_scene == null:
		push_error("Unable to load %s" % MAIN_SCENE)
		_finish(1)
		return

	_scene = packed_scene.instantiate()
	get_root().add_child(_scene)
	await _wait_frames(8)

	var shell := _scene.get_node_or_null("RetroNeonCabShell")
	if shell == null:
		push_error("RetroNeonCabShell was not found")
		_finish(1)
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
		var showcase_kart := _scene.get_node_or_null("Kart")
		if showcase_kart == null:
			push_error("Unable to stage vehicle capture")
			_finish(1)
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
		var kart := _scene.get_node_or_null("Kart") as RigidBody3D
		var mode := _scene.get_node_or_null("Modes/TaxiMode")
		var pickup: Area3D = null
		for child in mode.get_children():
			if child is Area3D and child.name.begins_with("PickupZone"):
				pickup = child
				break
		if kart == null or pickup == null:
			push_error("Unable to stage boarding capture")
			_finish(1)
			return
		pickup.set("LoadTime", 0.9)
		kart.global_position = pickup.global_position + Vector3.UP * 0.65
		kart.linear_velocity = Vector3.ZERO
		await create_timer(0.48).timeout
		await _wait_frames(3)
	elif _state == "dropoff":
		shell.call("StartRun")
		await create_timer(3.4).timeout
		var dropoff_kart := _scene.get_node_or_null("Kart") as RigidBody3D
		var dropoff_mode := _scene.get_node_or_null("Modes/TaxiMode")
		var dropoff_pickup: Area3D = null
		for child in dropoff_mode.get_children():
			if child is Area3D and child.name.begins_with("PickupZone"):
				dropoff_pickup = child
				break
		if dropoff_kart == null or dropoff_pickup == null:
			push_error("Unable to stage drop-off capture")
			_finish(1)
			return
		dropoff_pickup.set("LoadTime", 0.1)
		dropoff_kart.global_position = dropoff_pickup.global_position + Vector3.UP * 0.65
		dropoff_kart.linear_velocity = Vector3.ZERO
		await create_timer(0.3).timeout
		var destination: Vector3 = dropoff_mode.call("GetPlayerDestination", 1)
		if destination == Vector3.ZERO:
			push_error("Drop-off destination was not assigned")
			_finish(1)
			return
		dropoff_kart.global_position = destination + Vector3.UP * 0.65
		dropoff_kart.linear_velocity = Vector3.ZERO
		var dropoff_camera := _scene.get_node_or_null("Camera3D") as Camera3D
		if dropoff_camera != null:
			dropoff_camera.process_mode = Node.PROCESS_MODE_DISABLED
			dropoff_camera.global_position = destination + Vector3(0.0, 3.1, 8.2)
			dropoff_camera.look_at(destination + Vector3.UP * 0.65, Vector3.UP)
		await create_timer(0.22).timeout
		await _wait_frames(3)
	elif _state != "menu":
		push_error("Unknown capture state: %s" % _state)
		_finish(1)
		return

	await RenderingServer.frame_post_draw
	var viewport_texture := get_root().get_texture()
	if viewport_texture == null:
		push_error("Visual capture did not receive a viewport texture")
		_finish(1)
		return
	var image := viewport_texture.get_image()
	if image == null or image.is_empty():
		push_error("Visual capture image is empty")
		_finish(1)
		return
	var absolute_output := ProjectSettings.globalize_path(_output)
	DirAccess.make_dir_recursive_absolute(absolute_output.get_base_dir())
	var error := image.save_png(absolute_output)
	if error != OK:
		push_error("Unable to save visual capture %s: %s" % [absolute_output, error_string(error)])
		_finish(1)
		return
	if not FileAccess.file_exists(absolute_output):
		push_error("Visual capture file was not created: %s" % absolute_output)
		_finish(1)
		return
	var output_file := FileAccess.open(absolute_output, FileAccess.READ)
	if output_file == null or output_file.get_length() <= 0:
		push_error("Visual capture file is empty: %s" % absolute_output)
		_finish(1)
		return
	output_file.close()

	print("VISUAL_CAPTURE: %s" % absolute_output)
	_finish(0)

func _wait_frames(count: int) -> void:
	for _frame in count:
		await process_frame

func _finish(exit_code: int) -> void:
	if _finished:
		return
	_finished = true
	call_deferred("_teardown_and_quit", exit_code)

func _teardown_and_quit(exit_code: int) -> void:
	Input.action_release("move_forward")
	Input.action_release("move_backward")
	Input.action_release("move_left")
	Input.action_release("move_right")
	Input.action_release("drift")
	paused = false

	var multiplayer_manager := get_root().get_node_or_null("MultiplayerManager")
	if multiplayer_manager != null and multiplayer_manager.has_method("Disconnect"):
		multiplayer_manager.call("Disconnect")
	else:
		var multiplayer_api := get_multiplayer()
		if multiplayer_api.multiplayer_peer != null:
			multiplayer_api.multiplayer_peer.close()
			multiplayer_api.multiplayer_peer = OfflineMultiplayerPeer.new()

	_stop_audio_players(get_root())
	if is_instance_valid(_scene):
		_scene.queue_free()
		_scene = null

	await process_frame
	await process_frame
	print("VISUAL_CAPTURE_CLEANUP: complete")
	quit(exit_code)

func _stop_audio_players(node: Node) -> void:
	if node is AudioStreamPlayer:
		(node as AudioStreamPlayer).stop()
	elif node is AudioStreamPlayer3D:
		(node as AudioStreamPlayer3D).stop()
	for child in node.get_children():
		_stop_audio_players(child)
