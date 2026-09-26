extends SceneTree

# Endless Road is an ode to Burnout Paradise, where the pack tears itself apart as much as the
# player tears it apart. A rival that commits to a ram runs far above the pack's shadowing speed,
# so the fast one ploughs through the slow one. This test forces that speed gap and requires the
# slower rival to be wrecked and the player to be paid.

const MAIN_SCENE := "res://default_3d.tscn"
const SLOW_BASE_SPEED := 4.0
const FAST_BASE_SPEED := 60.0
const RIVAL_TAKEDOWN_SCORE := 200
const STATE_DISABLED := 5

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

	var kart := _find_by_script(get_root(), "Kart.cs") as Node3D
	if kart == null:
		_fail("Local kart not found")
		return

	var rival_script: Script = load("res://world/EndlessRoadRival.cs")
	var slow := rival_script.new() as Node if rival_script != null else null
	var fast := rival_script.new() as Node if rival_script != null else null
	if slow == null or fast == null:
		_fail("Could not create EndlessRoadRival instances")
		return
	scene_root.add_child(slow)
	scene_root.add_child(fast)
	slow.call("SetTarget", kart)
	fast.call("SetTarget", kart)
	# The gap is forced through BaseSpeed because DriveToward drives velocity from it.
	slow.set("BaseSpeed", SLOW_BASE_SPEED)
	fast.set("BaseSpeed", FAST_BASE_SPEED)
	slow.global_position = kart.global_position + Vector3(0.0, 0.0, 30.0)
	fast.global_position = slow.global_position + Vector3(0.0, 0.0, 2.0)
	await process_frame
	await process_frame

	var score_before := int(mode.get("Score"))
	var wrecked := false
	for _index in 600:
		if int(mode.get("State")) == 4 or int(mode.get("State")) == 5: # GameOver / Results
			_fail("Run ended before the pack collided")
			return
		if not is_instance_valid(slow) or not is_instance_valid(fast):
			break
		# Hold the pair in contact; physics separates them between frames otherwise.
		slow.global_position = fast.global_position + Vector3(0.0, 0.0, -2.0)
		await process_frame
		if int(slow.get("State")) == STATE_DISABLED:
			wrecked = true
			break

	var score_gain := int(mode.get("Score")) - score_before
	if not wrecked:
		_fail("A much faster rival did not wreck its slower packmate: slow state=%s, score +%d" % [
			str(int(slow.get("State"))), score_gain])
		return
	if score_gain < RIVAL_TAKEDOWN_SCORE:
		_fail("Pack wreck paid too little: score +%d" % score_gain)
		return

	print("OK EndlessRoad rival wreck passed (slow wrecked, score +%d)" % score_gain)
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
