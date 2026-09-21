extends SceneTree

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	# Smoke test needs the main scene so autoloads (EndlessRoadMode, AudioManager, etc.) exist.
	var packed := load("res://default_3d.tscn") as PackedScene
	if packed == null:
		_fail("Unable to load default_3d.tscn")
		return
	var scene_root: Node = packed.instantiate()
	get_root().add_child(scene_root)
	await process_frame
	await process_frame
	var mode: Node = get_root().get_node_or_null("EndlessRoadMode")
	if mode == null:
		# Also try scene-local lookup
		mode = scene_root.get_node_or_null("EndlessRoadMode")
	if mode == null:
		_fail("EndlessRoadMode not found (autoload or scene)")
		return
	var settings = mode.get("Settings")
	if settings == null:
		_fail("EndlessRoadSettings is null")
		return
	# The RetroNeonCabShell pauses the tree on the main menu; unpause so the
	# mode's _Process tick (countdown, running) advances.
	paused = false
	# StartRun/ResetRun take an optional int? and are not exposed to GDScript
	# by the 4.6 C# source generator — drive the run through RestartRun instead.
	mode.set("RunSeed", 42)
	mode.call("RestartRun")
	var state: int = int(mode.get("State"))
	if state != 1: # Countdown
		_fail("Expected Countdown after RestartRun, got %s" % str(state))
		return
	# Wait wall-clock (headless frames run faster than 60fps) for countdown -> Running.
	if not await _wait_for_state(mode, 2, 8000): # Running
		_fail("Never reached Running (state=%s)" % str(int(mode.get("State"))))
		return
	# Let the run accumulate distance, then sanity-check the runtime state.
	await _wait_ms(2000)
	var score: int = int(mode.get("Score"))
	var health: float = float(mode.get("Health"))
	var boost: float = float(mode.get("Boost"))
	var distance: float = float(mode.get("DistanceMeters"))
	if distance <= 0.0:
		_fail("Distance never grew: %.2f" % distance)
		return
	if health < 0 or health > 110:
		_fail("Health is out of bounds: %s" % str(health))
		return
	if boost < 0 or boost > 1.1:
		_fail("Boost is out of bounds: %s" % str(boost))
		return
	if score < 0:
		_fail("Score is negative")
		return
	# Impact -> recovery -> running loop: a Crash (2) must dent health, enter
	# ImpactRecovery, then return to Running with the lower health.
	mode.call("ApplyImpact", 2) # Kart.ImpactSeverity.Crash
	if int(mode.get("State")) != 3: # ImpactRecovery
		_fail("Expected ImpactRecovery after ApplyImpact, got %s" % str(int(mode.get("State"))))
		return
	if float(mode.get("Health")) >= health:
		_fail("Health did not drop after Crash impact")
		return
	if not await _wait_for_state(mode, 2, 4000): # back to Running
		_fail("Never recovered to Running (state=%s)" % str(int(mode.get("State"))))
		return
	print("OK EndlessRoad smoke passed (state=%s score=%s health=%.1f boost=%.2f dist=%.1f)" % [
		str(int(mode.get("State"))), str(score), float(mode.get("Health")), boost, distance])
	await _cleanup(scene_root, mode)
	_finish()

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
	for _index in 20:
		await process_frame
	if probe != null:
		probe.call("CollectManagedResources")
		probe.free()

func _wait_ms(ms: int) -> void:
	var start := Time.get_ticks_msec()
	while Time.get_ticks_msec() - start < ms:
		await process_frame

func _wait_for_state(mode: Node, wanted: int, timeout_ms: int) -> bool:
	var start := Time.get_ticks_msec()
	while Time.get_ticks_msec() - start < timeout_ms:
		await process_frame
		if int(mode.get("State")) == wanted:
			return true
		if int(mode.get("State")) == 4 or int(mode.get("State")) == 5: # GameOver / Results
			return false
	return false

func _fail(message: String) -> void:
	push_error("FAIL: %s" % message)
	quit(1)

func _finish() -> void:
	if get_meta("failed", false):
		quit(1)
	else:
		quit(0)
