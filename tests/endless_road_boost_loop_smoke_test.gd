extends SceneTree

# Endless Road is an ode to Burnout Paradise, so boost must be EARNED by driving
# dangerously rather than waited for. This test drives the kart past traffic and
# requires the near-miss payout to show up as boost, well above the passive trickle.

const MAIN_SCENE := "res://default_3d.tscn"
const DRIVE_SPEED := 30.0
const PASS_LATERAL := 1.6

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var packed := load(MAIN_SCENE) as PackedScene
	if packed == null:
		_fail("Unable to load default_3d.tscn")
		return
	var scene_root: Node = packed.instantiate()
	get_root().add_child(scene_root)
	await process_frame
	await process_frame

	var mode: Node = get_root().get_node_or_null("EndlessRoadMode")
	if mode == null:
		_fail("EndlessRoadMode not found (autoload)")
		return

	# The shell pauses the tree on the main menu; the mode's tick needs it unpaused.
	paused = false
	# Start through the shell: the bare RestartRun path resets the mode without the
	# streamer, so no chunks or traffic ever spawn.
	var shell: Node = scene_root.get_node_or_null("RetroNeonCabShell")
	if shell == null:
		_fail("RetroNeonCabShell not found")
		return
	shell.call("StartEndlessRoadRun")
	if not await _wait_for_state(mode, 2, 8000): # Running
		_fail("Never reached Running (state=%s)" % str(int(mode.get("State"))))
		return

	var kart: Node = _find_by_script(get_root(), "Kart.cs")
	# Chunks stream in over the first moments of a run, so wait for traffic to exist.
	var traffic: Node = null
	var traffic_wait := Time.get_ticks_msec()
	while Time.get_ticks_msec() - traffic_wait < 6000:
		traffic = _find_by_script(get_root(), "EndlessRoadTraffic.cs")
		if traffic != null:
			break
		await process_frame
	if kart == null or not (kart is RigidBody3D):
		_fail("Local kart not found or not a RigidBody3D")
		return
	if traffic == null or not (traffic is Node3D):
		_fail("No EndlessRoadTraffic vehicle spawned")
		return

	var settings = mode.get("Settings")
	var passive_per_second := float(settings.get("BoostRechargePerSecond"))
	var near_miss_award := float(settings.get("BoostAwardNearMiss"))
	var score_before := int(mode.get("Score"))

	# Empty the bar so anything above the passive trickle must have been earned.
	mode.set("Boost", 0.0)
	var started := Time.get_ticks_msec()
	var earned := false
	for _index in 600:
		if int(mode.get("State")) == 4 or int(mode.get("State")) == 5: # GameOver / Results
			_fail("Run ended before a near miss was scored")
			return
		# The traffic script repositions itself every frame, so move the kart to the
		# traffic instead of trying to move the traffic to the kart.
		var right_axis: Vector3 = kart.global_transform.basis.x
		kart.global_position = traffic.global_position + right_axis * PASS_LATERAL
		kart.linear_velocity = Vector3(0.0, 0.0, -DRIVE_SPEED)
		await process_frame

		var elapsed := float(Time.get_ticks_msec() - started) / 1000.0
		if float(mode.get("Boost")) > passive_per_second * elapsed + near_miss_award * 0.5:
			earned = true
			break

	var elapsed_total := float(Time.get_ticks_msec() - started) / 1000.0
	var boost := float(mode.get("Boost"))
	var passive_only := passive_per_second * elapsed_total
	if not earned:
		_fail("Dangerous driving never earned boost: boost=%.3f after %.1fs, passive trickle alone would be %.3f (near-miss award=%.2f)" % [
			boost, elapsed_total, passive_only, near_miss_award])
		return
	if int(mode.get("Score")) <= score_before:
		_fail("Near miss earned boost but scored nothing: %d -> %d" % [score_before, int(mode.get("Score"))])
		return

	print("OK EndlessRoad boost loop passed (boost=%.3f vs passive %.3f in %.1fs, score=%d)" % [
		boost, passive_only, elapsed_total, int(mode.get("Score"))])
	await _cleanup(scene_root, mode)
	_finish()

func _find_by_script(node: Node, suffix: String) -> Node:
	if node.get_script() != null and str(node.get_script().resource_path).ends_with(suffix):
		return node
	for child in node.get_children():
		var found := _find_by_script(child, suffix)
		if found != null:
			return found
	return null

func _wait_for_state(mode: Node, wanted: int, timeout_ms: int) -> bool:
	var start := Time.get_ticks_msec()
	while Time.get_ticks_msec() - start < timeout_ms:
		await process_frame
		if int(mode.get("State")) == wanted:
			return true
		if int(mode.get("State")) == 4 or int(mode.get("State")) == 5: # GameOver / Results
			return false
	return false

func _cleanup(scene_root: Node, mode: Node) -> void:
	var shell := scene_root.get_node_or_null("RetroNeonCabShell") as Node
	if shell != null and shell.has_method("ExitToMainMenu"):
		shell.call("ExitToMainMenu")
	var director := get_root().find_child("EndlessRoadDirector", true, false) as Node
	if director != null and director.has_method("Deactivate"):
		director.call("Deactivate")
	if mode != null and mode.has_method("ResetRun"):
		mode.call("ResetRun")
	# Hardened audio teardown, matching the other smoke tests: stop and release every player
	# before freeing the manager, then drain the managed finalizer waves. Without this, audio
	# playbacks can still be alive at exit and the runner reports leaked resources.
	var probe_script: Script = load("res://tests/harness/HarnessProbe.cs")
	var probe := probe_script.new() as Node if probe_script != null else null
	if probe != null:
		get_root().add_child(probe)
		probe.call("ReleaseAudioManagerResources")
	var audio_manager := get_root().get_node_or_null("AudioManager") as Node
	if audio_manager != null:
		audio_manager.free()
	var scene_to_free := scene_root
	if current_scene == scene_to_free:
		current_scene = null
	scene_to_free.queue_free()
	# Audio playbacks are released by the audio server over several frames after the streams are
	# stopped and nulled. A short settle lets the runner see them as leaked resources, so wait
	# long enough for teardown to finish before quitting.
	for _index in 20:
		await process_frame
	if probe != null:
		probe.call("CollectManagedResources")
		probe.free()

func _fail(message: String) -> void:
	push_error("FAIL: %s" % message)
	quit(1)

func _finish() -> void:
	if get_meta("failed", false):
		quit(1)
	else:
		quit(0)
