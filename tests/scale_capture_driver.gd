extends SceneTree

const MAIN_SCENE := "res://default_3d.tscn"
const CAPTURE_DIR := "res://captures"
const TOTAL_FRAMES := 360

var scene_root: Node3D
var camera: Camera3D
var kart: Node3D
var visual_container: Node3D
var track_builder: Node
var hud_label: Label
var can_save_stills := false

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	_prepare_capture_dir()

	var packed_scene: PackedScene = load(MAIN_SCENE)
	if packed_scene == null:
		_fail("Unable to load %s" % MAIN_SCENE)
		return

	scene_root = packed_scene.instantiate() as Node3D
	get_root().add_child(scene_root)
	await process_frame
	await physics_frame

	camera = scene_root.get_node_or_null("Camera3D") as Camera3D
	kart = scene_root.get_node_or_null("Kart") as Node3D
	track_builder = scene_root.get_node_or_null("TrackBuilder")
	can_save_stills = DisplayServer.get_name() != "headless"

	if camera == null or kart == null or track_builder == null:
		_fail("Capture scene needs Camera3D, Kart, and TrackBuilder nodes.")
		return

	visual_container = kart.get_node_or_null("VisualContainer") as Node3D
	_take_over_camera()
	_add_hud()
	_print_capture_context()

	for frame in range(TOTAL_FRAMES):
		_apply_frame(frame)

		if can_save_stills and frame in [0, 75, 150, 225, 300]:
			await RenderingServer.frame_post_draw
			_save_still(frame)

		await process_frame

	print("Scale capture complete: %d frames. Movie writer output is controlled by --write-movie." % TOTAL_FRAMES)
	quit(0)

func _take_over_camera() -> void:
	camera.set_script(null)
	camera.set_process(false)
	camera.set_physics_process(false)
	camera.current = true
	camera.near = 0.05
	camera.far = 450.0

func _add_hud() -> void:
	var layer := CanvasLayer.new()
	layer.name = "ScaleCaptureHud"
	get_root().add_child(layer)

	hud_label = Label.new()
	hud_label.name = "ShotLabel"
	hud_label.position = Vector2(18, 16)
	hud_label.add_theme_font_size_override("font_size", 18)
	hud_label.add_theme_color_override("font_color", Color(1.0, 0.92, 0.72, 1.0))
	hud_label.add_theme_color_override("font_shadow_color", Color(0.0, 0.0, 0.0, 0.85))
	hud_label.add_theme_constant_override("shadow_offset_x", 2)
	hud_label.add_theme_constant_override("shadow_offset_y", 2)
	layer.add_child(hud_label)

func _apply_frame(frame: int) -> void:
	var track_radius := float(track_builder.get("TrackRadius"))
	var track_width := float(track_builder.get("TrackWidth"))
	var shoulder_width := float(track_builder.get("ShoulderWidth"))

	if frame < 75:
		_place_camera(Vector3(0.0, 132.0, 0.0), Vector3.ZERO, 54.0, Vector3.FORWARD)
		_set_label("TOP SCALE | radius %.0fm  road %.0fm  shoulder %.0fm" % [track_radius, track_width, shoulder_width])
	elif frame < 150:
		var t: float = _phase_t(frame, 75, 150)
		var angle: float = lerpf(-PI * 0.5, PI * 1.5, t)
		var radial: Vector3 = _radial(angle)
		_place_camera(radial * 72.0 + Vector3.UP * 16.0, radial * track_radius + Vector3.UP * 1.0, 42.0)
		_set_label("ROAD WIDTH ORBIT | curbs and lane markers")
	elif frame < 225:
		var t: float = _phase_t(frame, 150, 225)
		var angle: float = lerpf(-PI * 0.72, -PI * 0.28, t)
		var radial: Vector3 = _radial(angle)
		_place_camera(radial * (track_radius + track_width * 0.5 + 7.0) + Vector3.UP * 4.2, radial * track_radius + Vector3.UP * 0.65, 48.0)
		_set_label("CURB CROSS-SECTION | edge height and overlap")
	elif frame < 300:
		var t: float = _phase_t(frame, 225, 300)
		var angle: float = lerpf(PI * 0.1, PI * 1.1, t)
		var radial: Vector3 = _radial(angle)
		_place_camera(radial * 86.0 + Vector3.UP * 22.0, radial * (track_radius + 12.0) + Vector3.UP * 4.0, 45.0)
		_set_label("ROADSIDE SCALE | buildings and track lights")
	else:
		var t: float = _phase_t(frame, 300, TOTAL_FRAMES)
		_set_kart_reference_pose()
		var angle: float = lerpf(-PI * 0.15, PI * 0.18, t)
		var kart_position: Vector3 = kart.global_position
		var offset := Vector3(sin(angle) * 4.5, 2.35, 6.4 + cos(angle) * 1.2)
		_place_camera(kart_position + offset, kart_position + Vector3.UP * 0.95, 57.0)
		_set_label("KART SCALE | vehicle vs 16m road")

func _set_kart_reference_pose() -> void:
	kart.global_position = Vector3(0.0, 0.5, -50.0)
	if visual_container != null:
		visual_container.global_position = kart.global_position
		visual_container.rotation = Vector3(0.0, PI * 0.5, 0.0)

func _place_camera(position: Vector3, target: Vector3, fov: float, up: Vector3 = Vector3.UP) -> void:
	camera.global_position = position
	camera.look_at(target, up)
	camera.fov = fov

func _phase_t(frame: int, start: int, end: int) -> float:
	return clamp(float(frame - start) / max(1.0, float(end - start - 1)), 0.0, 1.0)

func _radial(angle: float) -> Vector3:
	return Vector3(cos(angle), 0.0, sin(angle))

func _set_label(text: String) -> void:
	if hud_label != null:
		hud_label.text = text

func _save_still(frame: int) -> void:
	var image := get_root().get_texture().get_image()
	var path := "%s/scale_capture_%03d.png" % [CAPTURE_DIR, frame]
	var error := image.save_png(path)
	if error == OK:
		print("Saved scale still: %s" % ProjectSettings.globalize_path(path))
	else:
		push_warning("Unable to save scale still %s: %s" % [path, error_string(error)])

func _prepare_capture_dir() -> void:
	var absolute_dir := ProjectSettings.globalize_path(CAPTURE_DIR)
	var error := DirAccess.make_dir_recursive_absolute(absolute_dir)
	if error != OK:
		push_warning("Unable to create capture directory %s: %s" % [absolute_dir, error_string(error)])

func _print_capture_context() -> void:
	print("Scale capture context: radius=%s width=%s lights=%s seed=%s" % [
		track_builder.get("TrackRadius"),
		track_builder.get("TrackWidth"),
		track_builder.get("TrackLightCount"),
		track_builder.get("Seed")
	])

func _fail(message: String) -> void:
	push_error("FAIL: %s" % message)
	quit(1)
