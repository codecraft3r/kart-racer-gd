extends SceneTree

const MAIN_SCENE := "res://default_3d.tscn"
const SEED := 1337

var _failures: Array[String] = []
var _scene: Node
var _probe: Node
var _profile_directory := ""

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var settings_path := ProjectSettings.globalize_path("user://pain_taxi_settings.cfg")
	var records_path := ProjectSettings.globalize_path("user://pain_taxi_records.json")
	var settings_before := _snapshot(settings_path)
	var records_before := _snapshot(records_path)

	_profile_directory = ProjectSettings.globalize_path(
		"res://artifacts/harness/profile-smoke-%s" % Time.get_ticks_usec())
	var make_error := DirAccess.make_dir_recursive_absolute(_profile_directory)
	_check(make_error == OK, "could create fresh profile directory: %s" % make_error)

	var packed := load(MAIN_SCENE) as PackedScene
	_check(packed != null, "could load %s" % MAIN_SCENE)
	if packed == null:
		await _finish()
		return

	_scene = packed.instantiate()
	var probe_script = load("res://tests/harness/HarnessProbe.cs")
	_check(probe_script != null, "could load HarnessProbe.cs")
	if probe_script == null:
		await _finish()
		return
	_probe = probe_script.new() as Node
	# This ordering is the contract: TrackBuilder and shell persistence are configured
	# before the scene enters the tree and before any _Ready method can run.
	_probe.call("ConfigureScene", _scene, SEED, _profile_directory)
	_probe.call("SetSetupMode", "fixture")
	root.add_child(_probe)
	root.add_child(_scene)
	await _wait_frames(3)

	var track := _scene.find_child("TrackBuilder", true, false) as Node
	_check(track != null, "scene exposes TrackBuilder")
	if track != null:
		_check(track.get("Seed") == SEED, "TrackBuilder.Seed was configured before _Ready")

	var menu_observation: Dictionary = _probe.call("Observe", _scene)
	_check(menu_observation.get("harness_observed", false), "probe observed the shell")
	_check(menu_observation.get("screen", "") == "main", "initial shell screen is main")
	_check(menu_observation.get("actual_screen", "") == "main", "actual shell screen is main")
	var visible_screens = menu_observation.get("visible_screens", [])
	_check(visible_screens is Array and visible_screens.has("main"), "main shell screen is visible")
	for key in [
		"mode", "phase", "score", "elapsed_seconds", "time_remaining_seconds",
		"distance_meters", "health", "boost", "has_passenger", "passenger_state",
		"boost_active", "repair_active", "boarding_progress", "dropoff_settle_progress", "panic_meter", "drift_phase",
		"drift_amount", "drift_meters", "vehicle_option", "vehicle_name", "seed"
	]:
		_check(menu_observation.has(key), "typed observation includes %s" % key)
	_check(menu_observation.get("configured_seed", -1) == SEED, "probe records configured seed")
	_check(menu_observation.get("profile_directory", "") == _profile_directory, "probe records isolated profile")

	var shell := _scene.find_child("RetroNeonCabShell", true, false) as Node
	_check(shell != null, "scene exposes RetroNeonCabShell")
	if shell != null:
		# Save through the normal UI persistence path; the configured resolver should redirect
		# this write into the fresh profile directory.
		shell.call("SaveAndApplySettings")
	await _wait_frames(1)

	var isolated_settings := _profile_directory.path_join("pain_taxi_settings.cfg")
	_check(FileAccess.file_exists(isolated_settings), "settings were written to isolated profile")
	_check(_same_snapshot(settings_before, _snapshot(settings_path)), "player settings were unchanged")
	_check(_same_snapshot(records_before, _snapshot(records_path)), "player records were unchanged")

	# Exercise the public seeded entry point and verify the typed mode/seed state. This is
	# semantic proof only; the smoke test does not capture or inspect rendered pixels.
	_check(bool(_probe.call("StartSeededEndless", _scene, SEED)), "seeded Endless Road entry point exists")
	await _wait_frames(2)
	var endless_observation: Dictionary = _probe.call("Observe", _scene)
	_check(endless_observation.get("mode", "") == "endless", "seeded run reports endless mode")
	_check(endless_observation.get("seed", -1) == SEED, "seeded run reports requested seed")
	_check(endless_observation.get("screen", "") == "gameplay", "seeded run enters gameplay screen")
	_check(endless_observation.has("phase"), "seeded run reports typed phase")

	await _finish()

func _snapshot(path: String) -> Dictionary:
	if not FileAccess.file_exists(path):
		return {"exists": false, "hash": 0}
	var file := FileAccess.open(path, FileAccess.READ)
	if file == null:
		return {"exists": true, "hash": -1}
	var bytes := file.get_buffer(file.get_length())
	file.close()
	return {"exists": true, "hash": hash(bytes)}

func _same_snapshot(left: Dictionary, right: Dictionary) -> bool:
	return left.get("exists", false) == right.get("exists", false) and left.get("hash", 0) == right.get("hash", 0)

func _check(condition: bool, message: String) -> void:
	if condition:
		return
	_failures.append(message)
	push_error("HARNESS_PROFILE_SMOKE_FAIL: %s" % message)

func _wait_frames(count: int) -> void:
	for _index in count:
		await process_frame

func _finish() -> void:
	# Let the production owner tear down Endless Road children and reset the local
	# session before freeing the scene. This keeps a semantic smoke run from leaving
	# streamer/rival nodes alive until process shutdown.
	if is_instance_valid(_scene):
		var shell := _scene.find_child("RetroNeonCabShell", true, false) as Node
		if shell != null:
			shell.call("ExitToMainMenu")
		var director := root.find_child("EndlessRoadDirector", true, false) as Node
		if director != null and director.has_method("Deactivate"):
			director.call("Deactivate")
		var endless_mode := root.get_node_or_null("EndlessRoadMode") as Node
		if endless_mode != null and endless_mode.has_method("ResetRun"):
			endless_mode.call("ResetRun")
	var audio_manager := root.get_node_or_null("AudioManager") as Node
	if is_instance_valid(_probe):
		_probe.call("ReleaseAudioManagerResources")
	if audio_manager != null:
		audio_manager.queue_free()
	audio_manager = null

	var scene_to_free := _scene
	var probe_to_free := _probe
	_scene = null
	_probe = null
	if current_scene == scene_to_free:
		current_scene = null
	if is_instance_valid(scene_to_free):
		scene_to_free.queue_free()
	if is_instance_valid(probe_to_free):
		probe_to_free.queue_free()
	scene_to_free = null
	probe_to_free = null
	# Give queued children, streamer chunks, and audio players several idle turns to
	# release before the supervisor sees the success line and closes the process.
	await _wait_frames(8)
	var collector_script: Script = load("res://tests/harness/HarnessProbe.cs")
	var collector := collector_script.new() as Node if collector_script != null else null
	if collector != null:
		root.add_child(collector)
		collector.call("CollectManagedResources")
		collector.free()
	if _failures.is_empty():
		print("HARNESS_PROFILE_SMOKE_SUCCESS: profile=%s seed=%d" % [_profile_directory, SEED])
		quit(0)
	else:
		push_error("HARNESS_PROFILE_SMOKE_FAILED: %d assertion(s)" % _failures.size())
		quit(1)
