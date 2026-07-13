extends SceneTree

const MAIN_SCENE := "res://default_3d.tscn"

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var packed_scene: PackedScene = load(MAIN_SCENE)
	if packed_scene == null:
		_fail("Unable to load %s" % MAIN_SCENE)
		return

	var root: Node = packed_scene.instantiate()
	get_root().add_child(root)
	await process_frame
	await process_frame

	var shell: Node = root.get_node_or_null("RetroNeonCabShell")
	_expect(shell != null, "RetroNeonCabShell is instanced in default_3d")
	if shell == null:
		_finish()
		return

	var main_menu: Control = _find_control(shell, "MainMenuScreen")
	var gameplay: Control = _find_control(shell, "GameplayScreen")
	var pause: Control = _find_control(shell, "PauseScreen")
	var settings: Control = _find_control(shell, "SettingsScreen")
	var credits: Control = _find_control(shell, "CreditsScreen")
	var results: Control = _find_control(shell, "ResultsScreen")
	_expect(main_menu != null and gameplay != null and pause != null and settings != null and credits != null and results != null, "all shell screens exist")
	_expect(main_menu.visible and not gameplay.visible and not pause.visible and not settings.visible and not credits.visible and not results.visible, "main menu is the default screen")
	_expect(paused, "game starts paused behind the main menu")

	shell.call("StartRun")
	_expect(gameplay.visible and not main_menu.visible and not paused, "StartRun enters active gameplay")
	var countdown: Control = _find_control(shell, "CountdownLabel")
	_expect(countdown != null and countdown.visible, "single-player run displays its start countdown")

	shell.call("TogglePause")
	_expect(pause.visible and gameplay.visible and paused, "TogglePause shows pause overlay and pauses gameplay")

	shell.call("OpenSettings", "pause")
	_expect(settings.visible and pause.visible and paused, "settings opened from pause keeps pause context")

	shell.call("SetPixelation", 8)
	var postprocess: MeshInstance3D = root.get_node_or_null("Camera3D/MeshInstance3D")
	var material := postprocess.material_override as ShaderMaterial
	_expect(material != null and int(material.get_shader_parameter("pixel_size")) == 8, "pixelation button path updates shader uniform")

	shell.call("CloseSettings")
	_expect(pause.visible and not settings.visible and paused, "closing pause settings returns to pause")

	shell.call("ResumeRun")
	_expect(gameplay.visible and not pause.visible and not paused, "ResumeRun returns to gameplay")

	shell.call("ExitToMainMenu")
	_expect(main_menu.visible and not gameplay.visible and paused, "ExitToMainMenu restores main menu")

	shell.call("OpenCredits")
	_expect(credits.visible and not main_menu.visible, "OpenCredits shows credits")

	shell.call("CloseCredits")
	_expect(main_menu.visible and not credits.visible, "CloseCredits returns to main")

	_finish()

func _find_control(parent: Node, node_name: String) -> Control:
	return parent.find_child(node_name, true, false) as Control

func _expect(condition: bool, message: String) -> void:
	if condition:
		print("PASS: %s" % message)
	else:
		_fail(message)

func _fail(message: String) -> void:
	push_error("FAIL: %s" % message)
	set_meta("failed", true)

func _finish() -> void:
	paused = false
	if get_meta("failed", false):
		quit(1)
	else:
		print("Retro Neon Cab UI shell smoke test passed.")
		quit(0)
