extends SceneTree

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var packed := load("res://default_3d.tscn") as PackedScene
	if packed == null:
		_fail("Unable to load default_3d.tscn")
		return
	var scene_root: Node = packed.instantiate()
	get_root().add_child(scene_root)
	current_scene = scene_root
	await process_frame
	await process_frame

	var shell := scene_root.get_node_or_null("RetroNeonCabShell")
	var kart := scene_root.get_node_or_null("Kart")
	var mode := get_root().get_node_or_null("EndlessRoadMode")
	if shell == null or kart == null:
		_fail("Missing shell or kart")
		return
	if mode == null:
		_fail("Missing EndlessRoadMode")
		return

	print("PLAYPATH before start z=%.1f freeze=%s sleeping=%s paused=%s" % [
		kart.global_position.z, str(kart.freeze), str(kart.sleeping), str(paused)])
	shell.call("StartEndlessRoad")
	await process_frame
	await process_frame
	var chunk_length := float(mode.get("Settings").get("ChunkLength"))
	if kart.global_position.z < 0.0 or kart.global_position.z >= chunk_length:
		_fail("Endless spawn is outside chunk 0: z=%.1f chunk=0..%.1f" % [kart.global_position.z, chunk_length])
		return

	var start := Time.get_ticks_msec()
	while Time.get_ticks_msec() - start < 6000:
		await process_frame

	var director := get_root().find_child("EndlessRoadDirector", true, false)
	var streamer = director.get_child(0) if director != null and director.get_child_count() > 0 else null
	print("PLAYPATH after start state=%s z=%.1f x=%.1f velocity=%s freeze=%s sleeping=%s paused=%s chunks=%s" % [
		str(mode.get("State") if mode != null else -1),
		kart.global_position.z,
		kart.global_position.x,
		str(kart.linear_velocity),
		str(kart.freeze),
		str(kart.sleeping),
		str(paused),
		str(streamer.get("ActiveChunkCount") if streamer != null else -1)])
	if int(mode.get("State")) != 2:
		_fail("Endless run did not reach Running")
		return
	if kart.global_position.z <= 0.0 or kart.linear_velocity.z <= 0.5:
		_fail("Kart did not drive down the endless road: z=%.1f velocity=%s" % [kart.global_position.z, str(kart.linear_velocity)])
		return
	if streamer == null or int(streamer.get("ActiveChunkCount")) < 5:
		_fail("Endless road streamer did not activate enough chunks")
		return

	await _cleanup(scene_root, shell, mode)
	quit(0)

func _cleanup(scene_root: Node, shell: Node, mode: Node) -> void:
	# Use the production owner paths first so the root-owned streamer, score system,
	# rivals, and kart bindings are released before the scene is queued for deletion.
	if shell != null and shell.has_method("ExitToMainMenu"):
		shell.call("ExitToMainMenu")
	var director := get_root().find_child("EndlessRoadDirector", true, false) as Node
	if director != null and director.has_method("Deactivate"):
		director.call("Deactivate")
	if mode != null and mode.has_method("ResetRun"):
		mode.call("ResetRun")
	var audio_manager := get_root().get_node_or_null("AudioManager") as Node
	if audio_manager != null:
		audio_manager.queue_free()
	if current_scene == scene_root:
		current_scene = null
	var scene_to_free := scene_root
	scene_to_free.queue_free()
	# Audio playbacks are released by the audio server over several frames after the streams are
	# stopped and nulled. A short settle lets the runner see them as leaked resources, so wait
	# long enough for teardown to finish before quitting.
	await _wait_frames(20)

func _wait_frames(count: int) -> void:
	for _index in count:
		await process_frame

func _fail(message: String) -> void:
	push_error("FAIL: %s" % message)
	quit(1)
