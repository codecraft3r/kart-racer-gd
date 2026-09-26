extends SceneTree

# Endless Road is an ode to Burnout Paradise, where the signature move is slamming a rival
# into a wreck. The player must be genuinely faster than the rival to earn it, so boost is what
# closes the gap. This test slams a rival at speed and requires the takedown payout.

const MAIN_SCENE := "res://default_3d.tscn"
const SLAM_SPEED := 40.0
const TAKEDOWN_SCORE := 450

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var packed := load(MAIN_SCENE) as PackedScene
	if packed == null:
		_fail("Unable to load default_3d.tscn")
		return
	var scene_root: Node = packed.instantiate()
	get_root().add_child(scene_root)
	current_scene = scene_root
	await process_frame
	await process_frame

	var mode: Node = get_root().get_node_or_null("EndlessRoadMode")
	if mode == null:
		_fail("EndlessRoadMode not found (autoload)")
		return
	var shell: Node = scene_root.get_node_or_null("RetroNeonCabShell")
	if shell == null:
		_fail("RetroNeonCabShell not found")
		return

	paused = false
	shell.call("StartEndlessRoadRun")
	if not await _wait_for_state(mode, 2, 8000): # Running
		_fail("Never reached Running (state=%s)" % str(int(mode.get("State"))))
		return

	var kart: Node = _find_by_script(get_root(), "Kart.cs")
	if kart == null or not (kart is RigidBody3D):
		_fail("Local kart not found or not a RigidBody3D")
		return

	# Spawn one rival directly instead of waiting ~20s of running for the director's first spawn.
	var rival_script: Script = load("res://world/EndlessRoadRival.cs")
	var rival := rival_script.new() as Node if rival_script != null else null
	if rival == null:
		_fail("Could not create an EndlessRoadRival")
		return
	scene_root.add_child(rival)
	rival.call("SetTarget", kart)
	rival.set("BaseSpeed", 20.0)
	rival.global_position = kart.global_position + Vector3(0.0, 0.0, 6.0)
	await process_frame
	await process_frame

	var score_before := int(mode.get("Score"))
	# Empty the bar first: the payout clamps at a full bar, so a starting-full run would show
	# a takedown scoring with no measurable boost.
	mode.set("Boost", 0.0)
	var boost_before := 0.0
	var earned := false
	for _index in 600:
		if int(mode.get("State")) == 4 or int(mode.get("State")) == 5: # GameOver / Results
			_fail("Run ended before the takedown landed")
			return
		if not is_instance_valid(rival):
			break
		# Slam it: contact plus a big speed advantage is what the takedown rule requires.
		rival.global_position = kart.global_position + Vector3(0.0, 0.0, -1.2)
		kart.linear_velocity = Vector3(0.0, 0.0, -SLAM_SPEED)
		await process_frame
		if int(mode.get("Score")) >= score_before + TAKEDOWN_SCORE:
			earned = true
			break

	var score_gain := int(mode.get("Score")) - score_before
	var boost_gain := float(mode.get("Boost")) - boost_before
	if not earned:
		_fail("Slamming a slower rival did not earn a takedown: score +%d, boost +%.3f" % [score_gain, boost_gain])
		return
	if boost_gain < 0.4:
		_fail("Takedown scored but paid no boost: score +%d, boost +%.3f" % [score_gain, boost_gain])
		return

	print("OK EndlessRoad takedown passed (score +%d, boost +%.3f)" % [score_gain, boost_gain])
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
		if int(mode.get("State")) == 4 or int(mode.get("State")) == 5:
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
	# before freeing the manager, then drain the managed finalizer waves.
	var probe_script: Script = load("res://tests/harness/HarnessProbe.cs")
	var probe := probe_script.new() as Node if probe_script != null else null
	if probe != null:
		get_root().add_child(probe)
		probe.call("ReleaseAudioManagerResources")
	var audio_manager := get_root().get_node_or_null("AudioManager") as Node
	if audio_manager != null:
		audio_manager.free()
	if current_scene == scene_root:
		current_scene = null
	scene_root.queue_free()
	# Audio playbacks are released by the audio server over several frames after the streams are
	# stopped and nulled, so wait long enough for teardown to finish before quitting.
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
