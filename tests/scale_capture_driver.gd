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
	var city_columns := int(track_builder.get("CityColumns"))
	var city_rows := int(track_builder.get("CityRows"))
	var city_block_size := float(track_builder.get("CityBlockSize"))
	var track_width := float(track_builder.get("TrackWidth"))
	var city_width := _city_width(city_columns, city_block_size, track_width)
	var city_depth := _city_width(city_rows, city_block_size, track_width)
	var city_span: float = max(city_width, city_depth)

	if frame < 75:
		_place_camera(Vector3(0.0, city_span * 0.95, 0.0), Vector3.ZERO, 58.0, Vector3.FORWARD)
		_set_label("CITY PLAN | %dx%d blocks  road %.0fm" % [city_columns, city_rows, track_width])
	elif frame < 150:
		var t: float = _phase_t(frame, 75, 150)
		var z: float = lerpf(-city_depth * 0.43, city_depth * 0.35, t)
		_place_camera(Vector3(track_width * 1.4, 15.0, z - 34.0), Vector3(0.0, 1.0, z + 20.0), 43.0)
		_set_label("MAIN AVENUE | varied street widths and long sightline")
	elif frame < 225:
		var t: float = _phase_t(frame, 150, 225)
		var angle: float = lerpf(-PI * 0.25, PI * 0.8, t)
		var offset := Vector3(cos(angle) * 34.0, 13.0, sin(angle) * 34.0)
		_place_camera(offset, Vector3.ZERO + Vector3.UP * 1.1, 47.0)
		_set_label("INTERSECTION | turns, crosswalks, curbs")
	elif frame < 300:
		var t: float = _phase_t(frame, 225, 300)
		var x: float = lerpf(-city_width * 0.35, city_width * 0.28, t)
		_place_camera(Vector3(x, 24.0, -city_depth * 0.18), Vector3(x + 15.0, 3.0, city_depth * 0.08), 44.0)
		_set_label("CITY BLOCKS | buildings placed inside lots")
	else:
		var t: float = _phase_t(frame, 300, TOTAL_FRAMES)
		_set_kart_reference_pose(city_depth)
		var angle: float = lerpf(-PI * 0.15, PI * 0.18, t)
		var kart_position: Vector3 = kart.global_position
		var offset := Vector3(sin(angle) * 4.5, 2.35, 6.4 + cos(angle) * 1.2)
		_place_camera(kart_position + offset, kart_position + Vector3.UP * 0.95, 57.0)
		_set_label("KART SCALE | vehicle on city avenue")

func _set_kart_reference_pose(city_depth: float) -> void:
	kart.global_position = Vector3(0.0, 0.5, -city_depth * 0.32)
	if visual_container != null:
		visual_container.global_position = kart.global_position
		visual_container.rotation = Vector3.ZERO

func _place_camera(position: Vector3, target: Vector3, fov: float, up: Vector3 = Vector3.UP) -> void:
	camera.global_position = position
	camera.look_at(target, up)
	camera.fov = fov

func _phase_t(frame: int, start: int, end: int) -> float:
	return clamp(float(frame - start) / max(1.0, float(end - start - 1)), 0.0, 1.0)

func _city_width(block_count: int, block_size: float, road_width: float) -> float:
	return block_count * block_size + (block_count + 1) * road_width

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
	print("Scale capture context: columns=%s rows=%s width=%s lights=%s seed=%s" % [
		track_builder.get("CityColumns"),
		track_builder.get("CityRows"),
		track_builder.get("TrackWidth"),
		track_builder.get("TrackLightCount"),
		track_builder.get("Seed")
	])

func _fail(message: String) -> void:
	push_error("FAIL: %s" % message)
	quit(1)
