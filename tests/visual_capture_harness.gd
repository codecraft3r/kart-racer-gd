extends SceneTree

## Deterministic, fail-closed visual capture engine.
## The supervisor owns process isolation/timeouts; this script owns scene setup,
## typed observations, frame synchronization, and the terminal result.
const MAIN_SCENE := "res://default_3d.tscn"
const REGISTRY_PATH := "res://tests/harness/scenarios.json"
const DEFAULT_OUTPUT_DIR := "res://artifacts/visual/captures"
const RESULT_SCHEMA_VERSION := 1
const DEFAULT_SEED := 1337
const MAX_PHYSICS_TICKS := 900

var _state := "menu"
var _output := ""
var _result_path := ""
var _profile_dir := ""
var _run_id := ""
var _setup_mode := "fixture"
var _resolution := Vector2i(1920, 1080)
var _suite := ""
var _scene_path := ""
var _camera_preset := "default"
var _camera_pos := Vector3.ZERO
var _camera_target := Vector3.ZERO
var _camera_fov := 0.0
var _has_camera_pos := false
var _has_camera_target := false
var _has_camera_fov := false
var _vehicle_index := 0
var _has_custom_vehicle := false
var _pixel_size := -1
var _crt_enabled := -1
var _scanlines_enabled := -1
var _seed := DEFAULT_SEED
var _burst_count := 1
var _burst_interval := 0.2
var _wait_seconds := -1.0
var _dump_metadata := true
var _contact_sheet := true
var _timeout_seconds := 120.0
var _visible := false
var _parse_errors: Array[String] = []
var _registry: Dictionary = {}
var _scene: Node = null
var _scene_to_check: Node = null
var _probe: Node = null
var _finished := false
var _exit_code := 1
var _captured_images: Array[Dictionary] = []
var _errors: Array[String] = []
var _assertions: Array[Dictionary] = []
var _observations: Dictionary = {}
var _artifacts: Array[Dictionary] = []
var _started_usec := 0

func _initialize() -> void:
	_started_usec = Time.get_ticks_usec()
	_parse_arguments()
	if _run_id.is_empty(): _run_id = "capture-%s" % Time.get_datetime_string_from_system(false, true).replace(":", "").replace("-", "")
	_load_registry()
	if _parse_errors.is_empty(): _validate_request()
	if not _parse_errors.is_empty():
		_errors.append_array(_parse_errors); _finish(1); return
	if not _visible:
		DisplayServer.window_set_flag(DisplayServer.WINDOW_FLAG_NO_FOCUS, true)
		DisplayServer.window_set_flag(DisplayServer.WINDOW_FLAG_MOUSE_PASSTHROUGH, true)
		DisplayServer.window_set_flag(DisplayServer.WINDOW_FLAG_BORDERLESS, true)
		DisplayServer.window_set_position(Vector2i(-32000, -32000))
	DisplayServer.window_set_size(_resolution)
	call_deferred("_run_harness")

func _parse_arguments() -> void:
	var known := ["--state=", "--output=", "--result=", "--profile-dir=", "--run-id=", "--resolution=", "--suite=", "--scene=", "--camera-preset=", "--camera-pos=", "--camera-target=", "--camera-fov=", "--vehicle-index=", "--pixel-size=", "--crt=", "--scanlines=", "--seed=", "--burst-count=", "--burst-interval=", "--wait-seconds=", "--timeout-seconds=", "--setup-mode=", "--dump-metadata=", "--contact-sheet=", "--visible"]
	for argument in OS.get_cmdline_user_args():
		var matched := argument == "--visible"
		for prefix in known:
			if argument.begins_with(prefix): matched = true; break
		if argument == "--visible": _visible = true
		if not matched: _parse_errors.append("unknown argument: %s" % argument); continue
		if argument.begins_with("--state="): _state = argument.trim_prefix("--state=").strip_edges().to_lower()
		elif argument.begins_with("--output="): _output = argument.trim_prefix("--output=").strip_edges()
		elif argument.begins_with("--result="): _result_path = argument.trim_prefix("--result=").strip_edges()
		elif argument.begins_with("--profile-dir="): _profile_dir = argument.trim_prefix("--profile-dir=").strip_edges()
		elif argument.begins_with("--run-id="): _run_id = argument.trim_prefix("--run-id=").strip_edges()
		elif argument.begins_with("--resolution="): _parse_resolution(argument.trim_prefix("--resolution="))
		elif argument.begins_with("--suite="): _suite = argument.trim_prefix("--suite=").strip_edges().to_lower()
		elif argument.begins_with("--scene="): _scene_path = argument.trim_prefix("--scene=").strip_edges()
		elif argument.begins_with("--camera-preset="): _camera_preset = argument.trim_prefix("--camera-preset=").strip_edges().to_lower()
		elif argument.begins_with("--camera-pos="): _camera_pos = _parse_vector3(argument.trim_prefix("--camera-pos="), "camera-pos"); _has_camera_pos = _parse_errors.is_empty()
		elif argument.begins_with("--camera-target="): _camera_target = _parse_vector3(argument.trim_prefix("--camera-target="), "camera-target"); _has_camera_target = _parse_errors.is_empty()
		elif argument.begins_with("--camera-fov="): _camera_fov = _parse_float(argument.trim_prefix("--camera-fov="), "camera-fov"); _has_camera_fov = _parse_errors.is_empty()
		elif argument.begins_with("--vehicle-index="): _vehicle_index = _parse_int(argument.trim_prefix("--vehicle-index="), "vehicle-index"); _has_custom_vehicle = _parse_errors.is_empty()
		elif argument.begins_with("--pixel-size="): _pixel_size = _parse_int(argument.trim_prefix("--pixel-size="), "pixel-size")
		elif argument.begins_with("--crt="): _crt_enabled = _parse_bool(argument.trim_prefix("--crt="), "crt")
		elif argument.begins_with("--scanlines="): _scanlines_enabled = _parse_bool(argument.trim_prefix("--scanlines="), "scanlines")
		elif argument.begins_with("--seed="): _seed = _parse_int(argument.trim_prefix("--seed="), "seed")
		elif argument.begins_with("--burst-count="): _burst_count = _parse_int(argument.trim_prefix("--burst-count="), "burst-count")
		elif argument.begins_with("--burst-interval="): _burst_interval = _parse_float(argument.trim_prefix("--burst-interval="), "burst-interval")
		elif argument.begins_with("--wait-seconds="): _wait_seconds = _parse_float(argument.trim_prefix("--wait-seconds="), "wait-seconds")
		elif argument.begins_with("--timeout-seconds="): _timeout_seconds = _parse_float(argument.trim_prefix("--timeout-seconds="), "timeout-seconds")
		elif argument.begins_with("--setup-mode="): _setup_mode = argument.trim_prefix("--setup-mode=").strip_edges().to_lower()
		elif argument.begins_with("--dump-metadata="): _dump_metadata = _parse_bool(argument.trim_prefix("--dump-metadata="), "dump-metadata") == 1
		elif argument.begins_with("--contact-sheet="): _contact_sheet = _parse_bool(argument.trim_prefix("--contact-sheet="), "contact-sheet") == 1

func _parse_resolution(value: String) -> void:
	var parts := value.to_lower().split("x")
	if parts.size() != 2 or not parts[0].is_valid_int() or not parts[1].is_valid_int(): _parse_errors.append("invalid resolution: %s" % value); return
	_resolution = Vector2i(int(parts[0]), int(parts[1]))

func _parse_vector3(value: String, label: String) -> Vector3:
	var parts := value.split(",")
	if parts.size() != 3: _parse_errors.append("invalid %s: %s" % [label, value]); return Vector3.ZERO
	for item in parts:
		if not item.strip_edges().is_valid_float(): _parse_errors.append("invalid %s: %s" % [label, value]); return Vector3.ZERO
	return Vector3(float(parts[0]), float(parts[1]), float(parts[2]))

func _parse_int(value: String, label: String) -> int:
	if not value.strip_edges().is_valid_int(): _parse_errors.append("invalid %s: %s" % [label, value]); return 0
	return int(value)

func _parse_float(value: String, label: String) -> float:
	if not value.strip_edges().is_valid_float(): _parse_errors.append("invalid %s: %s" % [label, value]); return 0.0
	return float(value)

func _parse_bool(value: String, label: String) -> int:
	match value.strip_edges().to_lower():
		"true", "1", "yes": return 1
		"false", "0", "no": return 0
		_: _parse_errors.append("invalid %s boolean: %s" % [label, value]); return 0

func _load_registry() -> void:
	var file := FileAccess.open(REGISTRY_PATH, FileAccess.READ)
	if file == null: _parse_errors.append("scenario registry unavailable: %s" % REGISTRY_PATH); return
	var parsed = JSON.parse_string(file.get_as_text())
	if not parsed is Dictionary or int(parsed.get("schema_version", -1)) != 1: _parse_errors.append("scenario registry schema_version must be 1"); return
	_registry = parsed
	if _seed == DEFAULT_SEED and parsed.has("default_seed"): _seed = int(parsed["default_seed"])

func _validate_request() -> void:
	if _resolution.x < 16 or _resolution.y < 16: _parse_errors.append("resolution must be at least 16x16")
	if _seed < 0: _parse_errors.append("seed must be non-negative")
	if _burst_count < 1 or _burst_count > 100: _parse_errors.append("burst-count must be between 1 and 100")
	if _burst_interval < 0.0: _parse_errors.append("burst-interval must be non-negative")
	if _wait_seconds < -1.0: _parse_errors.append("wait-seconds must be non-negative")
	if _timeout_seconds <= 0.0: _parse_errors.append("timeout-seconds must be positive")
	if not ["fixture", "journey"].has(_setup_mode): _parse_errors.append("setup-mode must be fixture or journey")
	if not _output.is_empty() and not _is_absolute(_output): _parse_errors.append("output must be an absolute path when supplied")
	if not _result_path.is_empty() and not _is_absolute(_result_path): _parse_errors.append("result must be an absolute path when supplied")
	if not _profile_dir.is_empty() and not _is_absolute(_profile_dir): _parse_errors.append("profile-dir must be an absolute path when supplied")
	var states: Array = _registry.get("states", {}).keys()
	if not _scene_path.is_empty():
		if not ResourceLoader.exists(_scene_path): _parse_errors.append("scene does not exist: %s" % _scene_path)
	elif not _suite.is_empty():
		var suites: Dictionary = _registry.get("suites", {})
		if not suites.has(_suite):
			for requested in _suite.split(","):
				if not states.has(requested): _parse_errors.append("unknown state in suite: %s" % requested)
				elif _setup_mode == "journey" and _registry.get("states", {}).get(requested, {}).get("setup", "fixture") == "fixture": _parse_errors.append("state %s requires setup-mode=fixture" % requested)
	elif not states.has(_state): _parse_errors.append("unknown state: %s" % _state)
	elif _setup_mode == "journey" and _registry.get("states", {}).get(_state, {}).get("setup", "fixture") == "fixture": _parse_errors.append("state %s requires setup-mode=fixture" % _state)

func _is_absolute(path: String) -> bool: return path.is_absolute_path() or path.begins_with("\\\\")

func _run_harness() -> void:
	if not _scene_path.is_empty(): await _run_scene_viewer(_scene_path)
	elif not _suite.is_empty(): await _run_suite(_suite)
	else: await _run_single_state(_state, _output)

func _run_single_state(state_name: String, custom_output: String) -> void:
	var target := custom_output if not custom_output.is_empty() else ProjectSettings.globalize_path("%s/%s.png" % [DEFAULT_OUTPUT_DIR, state_name])
	var ok := await _stage_and_capture_state(state_name, target)
	_finish(0 if ok and _errors.is_empty() else 1)

func _run_suite(suite_name: String) -> void:
	var states: Array = _registry.get("suites", {}).get(suite_name, [])
	if states.is_empty(): states = suite_name.split(",")
	_captured_images.clear()
	for state_name in states:
		var target := _suite_target(suite_name, str(state_name))
		var ok := await _stage_and_capture_state(str(state_name), target)
		if not ok: _errors.append("suite state failed: %s" % state_name)
		_teardown_scene(); await _physics_ticks(2)
	if _contact_sheet and _captured_images.size() > 1: _generate_contact_sheet(_captured_images, ProjectSettings.globalize_path("%s/suite_%s_contact_sheet.png" % [DEFAULT_OUTPUT_DIR, suite_name]), suite_name)
	_finish(0 if _errors.is_empty() else 1)

func _suite_target(suite_name: String, state_name: String) -> String:
	var root := ProjectSettings.globalize_path(DEFAULT_OUTPUT_DIR)
	if not _output.is_empty():
		root = _output
		if root.to_lower().ends_with(".png"): root = root.get_base_dir()
	return "%s/suite_%s/%s.png" % [root.trim_suffix("\\").trim_suffix("/"), suite_name, state_name]

func _stage_and_capture_state(state_name: String, output_path: String) -> bool:
	var packed := load(MAIN_SCENE) as PackedScene
	if packed == null: _errors.append("unable to load main scene: %s" % MAIN_SCENE); return false
	seed(_seed)
	var instance := packed.instantiate()
	if instance == null: _errors.append("main scene instantiate failed"); return false
	_scene = instance
	var track := _scene.get_node_or_null("TrackBuilder")
	if track == null: _errors.append("TrackBuilder missing before scene add_child"); return false
	track.set("Seed", _seed)
	if not _configure_probe_before_ready(_scene):
		_errors.append("HarnessProbe is required but could not be instantiated")
		return false
	get_root().add_child(_scene)
	await _physics_ticks(1)
	var shell := _scene.get_node_or_null("RetroNeonCabShell")
	var kart := _scene.get_node_or_null("Kart") as RigidBody3D
	var camera := _scene.get_node_or_null("Camera3D") as Camera3D
	_apply_visual_options(shell, kart)
	var stage_ok := await _apply_state_logic(state_name, shell, kart, camera, _scene)
	if not stage_ok: _errors.append("state setup failed: %s" % state_name); return false
	_apply_camera_overrides(camera, kart)
	var observed := _observe_runtime(state_name, shell, kart, camera); _observations[state_name] = observed
	if not _assert_observation_basics(state_name, observed): return false
	if _wait_seconds > 0.0: await _physics_ticks(maxi(1, int(round(_wait_seconds * Engine.get_physics_ticks_per_second()))))
	await RenderingServer.frame_post_draw
	for burst_idx in range(_burst_count):
		var frame_output := output_path
		if _burst_count > 1 and burst_idx > 0: frame_output = "%s_frame_%02d.%s" % [output_path.get_basename(), burst_idx, output_path.get_extension()]
		await RenderingServer.frame_post_draw
		var cap := _save_viewport_capture(frame_output, state_name, shell, kart, camera)
		if cap.is_empty(): return false
		_captured_images.append(cap)
		if burst_idx < _burst_count - 1: await _physics_ticks(maxi(1, int(round(_burst_interval * Engine.get_physics_ticks_per_second()))))
	_release_actions()
	return true

func _configure_probe_before_ready(scene_root: Node) -> bool:
	_probe = null
	var probe_script := load("res://tests/harness/HarnessProbe.cs")
	if probe_script == null:
		return false
	_probe = probe_script.new() as Node
	if _probe == null or not _probe.has_method("ConfigureScene") or not _probe.has_method("Observe"):
		_probe = null
		return false
	_probe.call("ConfigureScene", scene_root, _seed, _profile_dir)
	if _probe.has_method("SetSetupMode"): _probe.call("SetSetupMode", _setup_mode)
	scene_root.add_child(_probe)
	return true

func _apply_visual_options(shell: Node, kart: RigidBody3D) -> void:
	if _pixel_size > 0 and shell != null and shell.has_method("SetPixelation"): shell.call("SetPixelation", _pixel_size)
	if _crt_enabled >= 0 and shell != null and shell.has_method("SetCrtEnabled"): shell.call("SetCrtEnabled", _crt_enabled == 1)
	if _scanlines_enabled >= 0 and shell != null and shell.has_method("SetScanlinesEnabled"): shell.call("SetScanlinesEnabled", _scanlines_enabled == 1)
	if _has_custom_vehicle and kart != null and kart.has_method("SetVehicleOption"): kart.call("SetVehicleOption", _vehicle_index)

func _apply_state_logic(state_name: String, shell: Node, kart: RigidBody3D, camera: Camera3D, scene_root: Node) -> bool:
	match state_name:
		"menu": return await _wait_for_menu(shell)
		"gameplay", "downtown": return await _start_gameplay(shell, true)
		"drift":
			if not await _start_gameplay(shell, true): return false
			if kart == null or not kart.has_method("SetAIInput"): return false
			kart.set("IsAI", true)
			if kart.has_method("SetControlsEnabled"): kart.call("SetControlsEnabled", true)
			kart.linear_velocity = kart.global_transform.basis.z.normalized() * 20.0 + kart.global_transform.basis.x.normalized() * 6.0
			kart.call("SetAIInput", 1.0, -1.0, true)
			var elapsed_ticks := 0
			for _tick in 120:
				await physics_frame
				elapsed_ticks += 1
				if str(_probe_observation().get("drift_phase", "none")) in ["initiate", "holding"]: break
			var drift_observed := _probe_observation()
			var active := str(drift_observed.get("drift_phase", "none")) in ["initiate", "holding"]
			_add_assertion("drift_active_during_capture", active, {"drift_phase": drift_observed.get("drift_phase", "unknown"), "drift_amount": drift_observed.get("drift_amount", 0.0), "physics_ticks": elapsed_ticks, "setup": "fixture velocity and controlled AI input"})
			return active
		"boost":
			if not await _stage_endless(shell): return false
			_press_action("move_forward"); _press_action("boost"); await _physics_ticks(8)
			var boost_observed := _probe_observation()
			var active_boost := bool(boost_observed.get("boost_active", false))
			_add_assertion("boost_active_during_capture", active_boost, {"boost": boost_observed.get("boost", 0.0), "boost_active": boost_observed.get("boost_active", false), "physics_ticks": 8})
			return active_boost
		"vehicle", "garage", "vehicle_0", "vehicle_1", "vehicle_2", "vehicle_3", "vehicle_4":
			var option := _vehicle_index if state_name == "vehicle" or state_name == "garage" else int(state_name.trim_prefix("vehicle_"))
			if kart == null or not kart.has_method("SetVehicleOption"): return false
			if not await _start_gameplay(shell, true): return false
			kart.call("SetVehicleOption", option)
			return true
		"boarding": return await _stage_boarding(shell, kart, scene_root)
		"dropoff": return await _stage_dropoff(shell, kart, camera, scene_root)
		"pitstop", "repair": return await _stage_repair(shell, kart, scene_root)
		"endless_road", "mad_max": return await _stage_endless(shell)
		"pause":
			if not await _start_gameplay(shell, false) or shell == null or not shell.has_method("TogglePause"): return false
			if _setup_mode == "journey":
				var escape := InputEventKey.new()
				escape.keycode = KEY_ESCAPE
				escape.physical_keycode = KEY_ESCAPE
				escape.pressed = true
				get_root().push_input(escape, true)
				escape = escape.duplicate()
				escape.pressed = false
				get_root().push_input(escape, true)
			else:
				shell.call("TogglePause")
			return await _wait_for_named_visible(shell, ["PauseScreen", "PausedScreen"])
		"settings":
			if _setup_mode == "journey": return await _journey_button(shell, ["MainSettingsButton", "SettingsButton", "Settings"])
			if shell == null or not shell.has_method("OpenSettings"): return false
			shell.call("OpenSettings", "main"); return await _wait_for_named_visible(shell, ["SettingsScreen"])
		"credits": return await _journey_button(shell, ["CreditsButton", "Credits"])
		"results":
			if shell == null: return false
			if not await _start_gameplay(shell, false): return false
			if _probe == null or not _probe.has_method("ArrangeResults") or not bool(_probe.call("ArrangeResults", _scene)): return false
			var results := await _wait_for_named_visible(shell, ["ResultsScreen"])
			var result_observed := _probe_observation()
			var actual_results := str(result_observed.get("actual_screen", result_observed.get("screen", ""))) == "results"
			_add_assertion("results_screen_observed", results and actual_results, result_observed)
			return results and actual_results
		"multiplayer_lobby": return await _journey_button(shell, ["MultiplayerButton", "Multiplayer"])
		"city_overview":
			if camera == null or kart == null: return false
			if not await _start_gameplay(shell, false): return false
			_detach_camera_script(camera); camera.global_position = Vector3(0.0, 95.0, 75.0); camera.look_at(Vector3.ZERO, Vector3.UP); camera.fov = 65.0; await _physics_ticks(2); return true
		"chase", "hood", "cockpit", "birds_eye", "orbit", "front", "side", "city_high":
			if not await _start_gameplay(shell, false): return false
			_apply_camera_preset(camera, kart, state_name); await _physics_ticks(2); return true
		_:
			_errors.append("state handler missing: %s" % state_name); return false

func _start_gameplay(shell: Node, settle: bool) -> bool:
	if shell == null: return false
	if _setup_mode == "journey":
		if not await _journey_button(shell, ["StartRunButton", "Start"]): return false
	else:
		if not shell.has_method("StartRun"): return false
		shell.call("StartRun")
	var ready := await _wait_for_gameplay_active(shell)
	_add_assertion("gameplay_active", ready, _probe_observation())
	if settle: await _physics_ticks(4)
	return ready

func _wait_for_gameplay_active(shell: Node) -> bool:
	if shell == null: return false
	for _tick in MAX_PHYSICS_TICKS:
		var state := _probe_observation()
		var screen_ok := str(state.get("actual_screen", state.get("screen", ""))) == "gameplay"
		var phase := str(state.get("phase", ""))
		if screen_ok and phase in ["active", "running"]: return true
		await physics_frame
	return false

func _journey_button(shell: Node, names: Array[String]) -> bool:
	if shell == null: return false
	await process_frame
	await process_frame
	var button: Control = null
	for name in names:
		button = shell.find_child(name, true, false) as Control
		if button != null: break
	if button == null or not button.is_visible_in_tree(): return false
	var event := InputEventMouseButton.new()
	event.button_index = MOUSE_BUTTON_LEFT
	event.pressed = true
	event.position = button.get_global_transform_with_canvas() * (button.size / 2.0)
	event.global_position = event.position
	# Coordinates are already in the stretched viewport's local space.
	get_root().push_input(event, true)
	await _physics_ticks(2)
	event = event.duplicate()
	event.pressed = false
	get_root().push_input(event, true)
	var expected := ""
	for name in names:
		var lowered := name.to_lower()
		if lowered.contains("settings"): expected = "settings"
		elif lowered.contains("credits"): expected = "credits"
		elif lowered.contains("multiplayer"): expected = "multiplayer"
		elif lowered.contains("endless") or lowered.contains("daily"): expected = "gameplay"
		elif lowered.contains("start"): expected = "gameplay"
	if expected.is_empty(): return true
	for _tick in MAX_PHYSICS_TICKS:
		var state := _probe_observation()
		if str(state.get("actual_screen", state.get("screen", ""))) == expected:
			_add_assertion("journey_transition_%s" % expected, true, state)
			return true
		await physics_frame
	_add_assertion("journey_transition_%s" % expected, false, _probe_observation())
	return false

func _stage_boarding(shell: Node, kart: RigidBody3D, scene_root: Node) -> bool:
	if _setup_mode == "journey":
		_errors.append("boarding is fixture-only; journey cannot teleport passenger state")
		return false
	if not await _start_gameplay(shell, true): return false
	if _probe == null or not _probe.has_method("ArrangePassenger") or not bool(_probe.call("ArrangePassenger", _scene, true)): return false
	await _physics_ticks(2)
	var state := _probe_observation(); var loaded := bool(state.get("has_passenger", false)) and str(state.get("passenger_state", "")) == "boarding" and float(state.get("boarding_progress", 0.0)) > 0.0 and float(state.get("boarding_progress", 0.0)) < 1.0
	_add_assertion("boarding_passenger_loaded", loaded, state); return loaded

func _stage_dropoff(shell: Node, kart: RigidBody3D, camera: Camera3D, scene_root: Node) -> bool:
	if _setup_mode == "journey":
		_errors.append("dropoff is fixture-only; journey cannot teleport passenger state")
		return false
	if not await _start_gameplay(shell, true): return false
	if _probe == null or not _probe.has_method("ArrangeDropoff") or not bool(_probe.call("ArrangeDropoff", _scene)): return false
	for _tick in 120:
		if float(_probe_observation().get("dropoff_settle_progress", 0.0)) > 0.0: break
		await physics_frame
	var state := _probe_observation()
	var arranged := bool(state.get("has_passenger", false)) and str(state.get("passenger_state", "")) == "hired" and float(state.get("dropoff_settle_progress", 0.0)) > 0.0
	var taxi := scene_root.get_node_or_null("Modes/TaxiMode")
	if arranged and taxi != null and taxi.has_method("GetPlayerDestination"):
		var destination: Vector3 = taxi.call("GetPlayerDestination", 1)
		arranged = destination != Vector3.ZERO and kart.global_position.distance_to(destination) <= 1.5
	_add_assertion("dropoff_state_observed", arranged, state); return arranged

func _stage_repair(shell: Node, kart: RigidBody3D, scene_root: Node) -> bool:
	if _setup_mode == "journey":
		_errors.append("repair is fixture-only; journey cannot inject damage")
		return false
	if not await _start_gameplay(shell, true): return false
	if _probe == null or not _probe.has_method("ArrangeRepair") or not bool(_probe.call("ArrangeRepair", _scene)): return false
	var state: Dictionary = {}
	for _tick in 120:
		state = _probe_observation()
		if bool(state.get("repair_active", false)): break
		await physics_frame
	var damaged := float(state.get("health", 100.0)) < 100.0
	var track := scene_root.get_node_or_null("TrackBuilder")
	var shop = track.call("GetNearestRepairShop", kart.global_position) if track != null and track.has_method("GetNearestRepairShop") else null
	var at_shop := shop != null and kart.global_position.distance_to(shop.global_position) <= 1.5
	var repair_active := bool(state.get("repair_active", false))
	damaged = damaged and at_shop and repair_active
	_add_assertion("repair_fixture_damage_observed", damaged, state); return damaged

func _stage_endless(shell: Node) -> bool:
	if shell == null: return false
	if _setup_mode == "journey":
		if not await _journey_button(shell, ["EndlessRoadButton", "DailyRunButton", "Endless"]): return false
	elif _probe != null and _probe.has_method("StartSeededEndless"):
		if not bool(_probe.call("StartSeededEndless", _scene, _seed)): return false
	elif shell.has_method("StartEndlessRoadWithSeed"):
		shell.call("StartEndlessRoadWithSeed", _seed)
	else: return false
	var mode := get_root().get_node_or_null("EndlessRoadMode")
	if mode == null and _scene != null: mode = _scene.get_node_or_null("EndlessRoadMode")
	if mode == null: return false
	var ready := await _wait_for_endless_running(mode); var state := _probe_observation(); var seed_ok := int(state.get("seed", mode.get("RunSeed"))) == _seed; _add_assertion("endless_seed_applied", seed_ok, {"expected": _seed, "observed": state}); return ready and seed_ok

func _first_area(parent: Node, prefix: String) -> Area3D:
	if parent == null: return null
	for child in parent.get_children():
		if child is Area3D and child.name.begins_with(prefix): return child as Area3D
	return null

func _wait_for_menu(shell: Node) -> bool:
	if shell == null: return false
	return await _wait_for_control(shell.find_child("MainMenuScreen", true, false) as Control)

func _wait_for_named_visible(shell: Node, names: Array[String]) -> bool:
	if shell == null: return false
	for _tick in MAX_PHYSICS_TICKS:
		for name in names:
			var control := shell.find_child(name, true, false) as Control
			if control != null and control.visible: return true
		await physics_frame
	return false

func _wait_for_control(control: Control) -> bool:
	for _tick in MAX_PHYSICS_TICKS:
		if control != null and control.visible: return true
		await physics_frame
	return false

func _wait_for_endless_running(mode: Node) -> bool:
	for _tick in MAX_PHYSICS_TICKS:
		var state := _probe_observation()
		if str(state.get("phase", "")).to_lower() == "running": return true
		if str(mode.get("State")).to_lower() == "running": return true
		await physics_frame
	return false

func _probe_observation() -> Dictionary:
	if _probe == null or not _probe.has_method("Observe"): return {}
	var observed = _probe.call("Observe", _scene)
	return observed if observed is Dictionary else {}

func _press_action(action: String) -> void:
	Input.action_press(action)
	var key_map := {"move_forward": KEY_W, "move_backward": KEY_S, "move_left": KEY_A, "move_right": KEY_D, "drift": KEY_SPACE, "boost": KEY_SHIFT}
	if key_map.has(action):
		var event := InputEventKey.new()
		event.keycode = key_map[action]
		event.physical_keycode = key_map[action]
		event.pressed = true
		Input.parse_input_event(event)
func _action_active(action: String) -> bool: return Input.is_action_pressed(action)
func _release_actions() -> void:
	var key_map := {"move_forward": KEY_W, "move_backward": KEY_S, "move_left": KEY_A, "move_right": KEY_D, "drift": KEY_SPACE, "boost": KEY_SHIFT}
	for action in ["move_forward", "move_backward", "move_left", "move_right", "drift", "boost"]:
		Input.action_release(action)
		if key_map.has(action):
			var event := InputEventKey.new()
			event.keycode = key_map[action]
			event.physical_keycode = key_map[action]
			event.pressed = false
			Input.parse_input_event(event)
func _physics_ticks(count: int) -> void:
	for _tick in count: await physics_frame

func _observe_runtime(state_name: String, shell: Node, kart: RigidBody3D, camera: Camera3D) -> Dictionary:
	var observed := {"state": state_name, "setup_mode": _setup_mode, "seed": _seed, "renderer": {"display_server": DisplayServer.get_name(), "rendering_method": ProjectSettings.get_setting("rendering/renderer/rendering_method", "unknown"), "rendering_device": RenderingServer.get_video_adapter_name()}, "ui": {}, "kart": {}, "camera": {}}
	if camera != null: observed["camera"] = {"position": _vec3(camera.global_position), "fov": camera.fov, "current": camera.current}
	if kart != null: observed["kart"] = {"position": _vec3(kart.global_position), "velocity": _vec3(kart.linear_velocity), "vehicle_option": kart.get("VehicleOption"), "has_passenger": kart.call("HasPassenger") if kart.has_method("HasPassenger") else null}
	if shell != null:
		var visible_screens: Array[String] = []
		for node in shell.find_children("*", "Control", true, false):
			if node.visible and node.name.ends_with("Screen"): visible_screens.append(node.name)
		observed["ui"] = {"visible_screens": visible_screens, "has_shell": true}
		var content_name: String = {"settings": "SettingsPanelScroll", "credits": "CreditsPanelScroll", "results": "ResultsPanelScroll", "pause": "PausePanelScroll", "multiplayer_lobby": "MultiplayerPanelScroll"}.get(state_name, "")
		if not str(content_name).is_empty():
			var content := shell.find_child(content_name, true, false) as Control
			observed["ui"]["content_viewport"] = {"present": content != null, "size": [content.size.x, content.size.y] if content != null else [0, 0]}
	if _probe != null and _probe.has_method("Observe"): observed["probe"] = _probe.call("Observe", _scene)
	return observed

func _vec3(value: Vector3) -> Array: return [value.x, value.y, value.z]
func _assert_observation_basics(state_name: String, observed: Dictionary) -> bool:
	var renderer_ok := not str(observed.get("renderer", {}).get("rendering_device", "")).is_empty()
	var probe: Dictionary = observed.get("probe", {})
	var probe_ok := bool(probe.get("harness_observed", false)) and not str(probe.get("actual_screen", "")).is_empty()
	var camera: Dictionary = observed.get("camera", {})
	var camera_ok := bool(camera.get("current", false))
	if _has_camera_fov: camera_ok = camera_ok and is_equal_approx(float(camera.get("fov", 0.0)), _camera_fov)
	if _has_camera_pos and camera_ok:
		var position: Array = camera.get("position", [])
		camera_ok = position.size() == 3 and is_equal_approx(float(position[0]), _camera_pos.x) and is_equal_approx(float(position[1]), _camera_pos.y) and is_equal_approx(float(position[2]), _camera_pos.z)
	_add_assertion("renderer_observed", renderer_ok, observed.get("renderer", {}))
	_add_assertion("typed_probe_observed", probe_ok, probe)
	_add_assertion("camera_observed", camera_ok, camera)
	var expected_screen := "gameplay"
	match state_name:
		"menu": expected_screen = "main"
		"settings", "credits", "results": expected_screen = state_name
		"pause": expected_screen = "paused"
		"multiplayer_lobby": expected_screen = "multiplayer"
	var state_ok := str(probe.get("actual_screen", "")) == expected_screen
	state_ok = state_ok and probe.get("visible_screens", []).has(expected_screen)
	if expected_screen == "gameplay": state_ok = state_ok and str(probe.get("phase", "")) in ["active", "running"]
	match state_name:
		"drift": state_ok = state_ok and str(probe.get("drift_phase", "")) in ["initiate", "holding"]
		"boost": state_ok = state_ok and bool(probe.get("boost_active", false))
		"boarding": state_ok = state_ok and str(probe.get("passenger_state", "")) == "boarding" and float(probe.get("boarding_progress", 0.0)) > 0.0
		"dropoff": state_ok = state_ok and float(probe.get("dropoff_settle_progress", 0.0)) > 0.0
		"repair": state_ok = state_ok and bool(probe.get("repair_active", false))
	if state_name.begins_with("vehicle_"): state_ok = state_ok and int(probe.get("vehicle_option", -1)) == int(state_name.trim_prefix("vehicle_"))
	var content: Dictionary = observed.get("ui", {}).get("content_viewport", {})
	if not content.is_empty():
		var dimensions: Array = content.get("size", [0, 0])
		state_ok = state_ok and bool(content.get("present", false)) and float(dimensions[0]) >= 100.0 and float(dimensions[1]) >= 100.0
	_add_assertion("requested_state_observed", state_ok, probe)
	_add_assertion("setup_mode_declared", ["fixture", "journey"].has(observed.get("setup_mode", "")), {"setup_mode": observed.get("setup_mode")})
	if not renderer_ok: _errors.append("renderer/device observation unavailable for %s" % state_name)
	if not probe_ok: _errors.append("typed probe observation unavailable for %s" % state_name)
	return renderer_ok and probe_ok and camera_ok and state_ok
func _add_assertion(name: String, passed: bool, observed: Variant) -> void:
	_assertions.append({"name": name, "passed": passed, "observed": observed}); if not passed: _errors.append("assertion failed: %s" % name)

func _detach_camera_script(camera: Camera3D) -> void:
	if camera == null: return
	camera.set_script(null); camera.set_process(false); camera.set_physics_process(false); camera.current = true

func _apply_camera_overrides(camera: Camera3D, kart: RigidBody3D) -> void:
	if camera == null: return
	if _has_camera_pos or _has_camera_target or _has_camera_fov:
		_detach_camera_script(camera)
		if _has_camera_pos: camera.global_position = _camera_pos
		if _has_camera_target: camera.look_at(_camera_target, Vector3.UP)
		if _has_camera_fov: camera.fov = _camera_fov
	elif _camera_preset != "default": _apply_camera_preset(camera, kart, _camera_preset)

func _apply_camera_preset(camera: Camera3D, kart: RigidBody3D, preset: String) -> void:
	if camera == null or kart == null: return
	_detach_camera_script(camera); var kp := kart.global_position; var vis := kart.get_node_or_null("VisualContainer") as Node3D; var basis: Basis = vis.global_transform.basis if vis != null else kart.global_transform.basis; var forward := basis.z.normalized(); var right := basis.x.normalized(); var up := Vector3.UP
	match preset:
		"chase": camera.global_position = kp - forward * 5.8 + up * 2.3; camera.look_at(kp + up * 0.9, up); camera.fov = 68.0
		"hood": camera.global_position = kp + forward * 0.95 + up * 0.75; camera.look_at(kp + forward * 18.0 + up * 0.65, up); camera.fov = 80.0
		"cockpit": camera.global_position = kp + forward * 0.1 + up * 0.92; camera.look_at(kp + forward * 15.0 + up * 0.8, up); camera.fov = 75.0
		"birds_eye", "top_down": camera.global_position = kp + up * 35.0; camera.look_at(kp, forward); camera.fov = 55.0
		"orbit", "turntable", "beauty": camera.global_position = kp + right * 4.2 - forward * 4.2 + up * 1.8; camera.look_at(kp + up * 0.6, up); camera.fov = 50.0
		"front", "head_on": camera.global_position = kp + forward * 5.2 + up * 0.9; camera.look_at(kp + up * 0.5, up); camera.fov = 60.0
		"side", "profile": camera.global_position = kp + right * 6.5 + up * 1.2; camera.look_at(kp + up * 0.6, up); camera.fov = 52.0
		"city_high": camera.global_position = kp + Vector3(25.0, 60.0, 50.0); camera.look_at(kp, up); camera.fov = 65.0

func _run_scene_viewer(scene_res_path: String) -> void:
	var packed := load(scene_res_path) as PackedScene
	if packed == null: _errors.append("could not load scene: %s" % scene_res_path); _finish(1); return
	seed(_seed)
	var stage := Node3D.new()
	stage.name = "StudioStage"
	_scene = stage
	get_root().add_child(stage)
	var environment_node := WorldEnvironment.new()
	var environment := Environment.new()
	environment.background_mode = Environment.BG_COLOR
	environment.background_color = Color(0.05, 0.05, 0.08)
	environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	environment.ambient_light_color = Color(0.7, 0.7, 0.85)
	environment.ambient_light_energy = 1.2
	environment_node.environment = environment
	stage.add_child(environment_node)
	var light := DirectionalLight3D.new()
	light.light_color = Color(1.0, 0.95, 0.88)
	light.light_energy = 2.0
	light.rotation_degrees = Vector3(-45.0, -30.0, 0.0)
	stage.add_child(light)
	var target := packed.instantiate()
	if target == null or not _configure_probe_before_ready(target):
		_errors.append("HarnessProbe unavailable for scene viewer")
		_finish(1)
		return
	stage.add_child(target)
	var target_shell := target.find_child("RetroNeonCabShell", true, false)
	var target_kart := target.find_child("Kart", true, false) as RigidBody3D
	_apply_visual_options(target_shell, target_kart)
	var cam := Camera3D.new()
	cam.name = "StudioCamera"
	stage.add_child(cam)
	cam.global_position = Vector3(3.8, 2.2, 4.6)
	cam.look_at(Vector3(0, 0.6, 0), Vector3.UP)
	cam.fov = 48.0
	cam.current = true
	if _has_camera_pos: cam.global_position = _camera_pos
	if _has_camera_target: cam.look_at(_camera_target, Vector3.UP)
	if _has_camera_fov: cam.fov = _camera_fov
	if not _has_camera_pos and not _has_camera_target and not _has_camera_fov and _camera_preset != "default": _apply_viewer_camera_preset(cam, _camera_preset)
	await _physics_ticks(12)
	if _wait_seconds > 0.0: await _physics_ticks(maxi(1, int(round(_wait_seconds * Engine.get_physics_ticks_per_second()))))
	await RenderingServer.frame_post_draw
	var out := _output if not _output.is_empty() else ProjectSettings.globalize_path("%s/scenes/%s.png" % [DEFAULT_OUTPUT_DIR, scene_res_path.get_file().get_basename()])
	var observation := _observe_runtime("scene_viewer", target_shell, target_kart, cam)
	observation["kind"] = "scene_viewer"
	observation["scene_path"] = scene_res_path
	observation["target_class"] = target.get_class()
	_observations["scene_viewer"] = observation
	var renderer_ok := not str(observation.get("renderer", {}).get("rendering_device", "")).is_empty()
	var viewer_ok := is_instance_valid(target) and cam.current and renderer_ok
	_add_assertion("scene_loaded", is_instance_valid(target), scene_res_path)
	_add_assertion("viewer_camera_current", cam.current, observation["camera"])
	_add_assertion("renderer_observed", renderer_ok, observation["renderer"])
	for burst_idx in range(_burst_count):
		var frame_out := out
		if _burst_count > 1 and burst_idx > 0: frame_out = "%s_frame_%02d.%s" % [out.get_basename(), burst_idx, out.get_extension()]
		await RenderingServer.frame_post_draw
		if _save_viewport_capture(frame_out, "scene_viewer", target_shell, target_kart, cam).is_empty(): viewer_ok = false
		if burst_idx < _burst_count - 1: await _physics_ticks(maxi(1, int(round(_burst_interval * Engine.get_physics_ticks_per_second()))))
	_finish(0 if viewer_ok else 1)

func _apply_viewer_camera_preset(camera: Camera3D, preset: String) -> void:
	match preset:
		"birds_eye", "top_down": camera.global_position = Vector3(0.0, 35.0, 0.0); camera.look_at(Vector3.ZERO, Vector3.FORWARD); camera.fov = 55.0
		"front", "head_on": camera.global_position = Vector3(0.0, 1.6, 6.0); camera.look_at(Vector3(0.0, 0.8, 0.0), Vector3.UP); camera.fov = 60.0
		"side", "profile": camera.global_position = Vector3(6.0, 1.5, 0.0); camera.look_at(Vector3(0.0, 0.8, 0.0), Vector3.UP); camera.fov = 52.0
		"city_high", "orbit", "turntable", "beauty": camera.global_position = Vector3(7.0, 4.0, 7.0); camera.look_at(Vector3(0.0, 0.8, 0.0), Vector3.UP); camera.fov = 50.0
		"chase", "default": pass

func _save_viewport_capture(output_path: String, state_name: String, shell: Node, kart: RigidBody3D, camera: Camera3D) -> Dictionary:
	if state_name != "scene_viewer":
		var fresh := _observe_runtime(state_name, shell, kart, camera)
		_observations[state_name] = fresh
		if not _assert_observation_basics(state_name, fresh): return {}
	var image := get_root().get_texture().get_image()
	if image == null or image.is_empty(): _errors.append("empty viewport image"); return {}
	var absolute := output_path if _is_absolute(output_path) else ProjectSettings.globalize_path(output_path)
	DirAccess.make_dir_recursive_absolute(absolute.get_base_dir())
	if image.save_png(absolute) != OK:
		_errors.append("capture write failed: %s" % absolute)
		return {}
	_artifacts.append({"kind": "png", "path": absolute})
	if _dump_metadata: _save_metadata_json(absolute, state_name, shell, kart, camera, image)
	return {"state": state_name, "path": absolute, "width": image.get_width(), "height": image.get_height(), "image": image}

func _save_metadata_json(png_path: String, state_name: String, shell: Node, kart: RigidBody3D, camera: Camera3D, image: Image) -> void:
	var path := png_path.get_basename() + ".json"
	var meta := {"schema_version": RESULT_SCHEMA_VERSION, "run_id": _run_id, "state": state_name, "seed": _seed, "setup_mode": _setup_mode, "resolution": {"width": image.get_width(), "height": image.get_height()}, "engine": Engine.get_version_info(), "observations": _observations.get(state_name, {})}
	var file := FileAccess.open(path, FileAccess.WRITE)
	if file == null:
		_errors.append("metadata write failed: %s" % path)
		return
	file.store_string(JSON.stringify(meta, "\t"))
	file.close()
	_artifacts.append({"kind": "metadata", "path": path})

func _generate_contact_sheet(captures: Array[Dictionary], output_path: String, suite_title: String) -> void:
	if captures.is_empty(): return
	var count := captures.size(); var cols := 4 if count >= 8 else (3 if count >= 5 else 2); var rows := int(ceil(float(count) / float(cols))); var sample_w: int = captures[0]["width"]; var sample_h: int = captures[0]["height"]; var cell_w := 640; var cell_h := int(float(cell_w) * float(sample_h) / float(sample_w)); var padding := 12; var sheet := Image.create(cols * cell_w + (cols + 1) * padding, rows * cell_h + (rows + 1) * padding, false, Image.FORMAT_RGBA8); sheet.fill(Color(0.05, 0.05, 0.08))
	for idx in range(count):
		var src: Image = captures[idx]["image"].duplicate(); src.resize(cell_w, cell_h, Image.INTERPOLATE_BILINEAR); sheet.blit_rect(src, Rect2i(0, 0, cell_w, cell_h), Vector2i(padding + (idx % cols) * (cell_w + padding), padding + (idx / cols) * (cell_h + padding)))
	var absolute := output_path if _is_absolute(output_path) else ProjectSettings.globalize_path(output_path); DirAccess.make_dir_recursive_absolute(absolute.get_base_dir()); if sheet.save_png(absolute) == OK: _artifacts.append({"kind": "contact_sheet", "path": absolute})

func _teardown_scene() -> void:
	_release_actions()
	paused = false
	if is_instance_valid(_scene):
		var shell := _scene.find_child("RetroNeonCabShell", true, false)
		if shell != null and shell.has_method("ExitToMainMenu"):
			shell.call("ExitToMainMenu")
		var director := _scene.find_child("EndlessRoadDirector", true, false)
		if director != null and director.has_method("Deactivate"):
			director.call("Deactivate")
		_scene_to_check = _scene
		if current_scene == _scene: current_scene = null
		_scene.queue_free()
	_scene = null
	_probe = null

func _finish(exit_code: int) -> void:
	if _finished: return
	_finished = true
	_exit_code = exit_code if _errors.is_empty() else 1
	call_deferred("_teardown_and_quit")

func _teardown_and_quit() -> void:
	var multiplayer_manager := get_root().get_node_or_null("MultiplayerManager")
	if multiplayer_manager != null and multiplayer_manager.has_method("Disconnect"):
		multiplayer_manager.call("Disconnect")
	var endless := get_root().get_node_or_null("EndlessRoadMode")
	if endless != null and endless.has_method("ResetRun"): endless.call("ResetRun", _seed)
	_teardown_scene()
	_stop_audio_players(get_root())
	var audio_manager := get_root().get_node_or_null("AudioManager")
	if audio_manager != null: audio_manager.queue_free()
	for _frame in 8: await process_frame
	var cleanup_complete := not is_instance_valid(_scene_to_check) and not is_instance_valid(audio_manager)
	var result := {"schema_version": RESULT_SCHEMA_VERSION, "run_id": _run_id, "state": _state if _suite.is_empty() else _suite, "status": "passed" if _exit_code == 0 and _errors.is_empty() else "failed", "errors": _errors, "assertions": _assertions, "artifacts": _artifacts, "observations": _observations, "cleanup_complete": cleanup_complete, "seed": _seed, "setup_mode": _setup_mode, "duration_ms": (Time.get_ticks_usec() - _started_usec) / 1000.0}
	result["resolution"] = {"width": _resolution.x, "height": _resolution.y}
	result["engine"] = Engine.get_version_info()
	if _result_path.is_empty(): _result_path = ProjectSettings.globalize_path("%s/%s.result.json" % [DEFAULT_OUTPUT_DIR, _run_id])
	DirAccess.make_dir_recursive_absolute(_result_path.get_base_dir())
	var file := FileAccess.open(_result_path, FileAccess.WRITE)
	if file == null:
		push_error("Cannot write harness result: %s" % _result_path)
		quit(1)
		return
	file.store_string(JSON.stringify(result, "\t"))
	file.close()
	print("VISUAL_HARNESS_RESULT: %s" % JSON.stringify(result))
	quit(_exit_code)

func _stop_audio_players(node: Node) -> void:
	if node is AudioStreamPlayer: (node as AudioStreamPlayer).stop()
	elif node is AudioStreamPlayer3D: (node as AudioStreamPlayer3D).stop()
	for child in node.get_children(): _stop_audio_players(child)
