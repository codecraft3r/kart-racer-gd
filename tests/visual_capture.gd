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
