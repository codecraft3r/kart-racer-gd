extends SceneTree

const MAIN_SCENE := "res://default_3d.tscn"
const REQUIRED_BUSES := ["Master", "Music", "Ambience", "Vehicles", "Weapons", "Impacts", "UI", "Voice"]
const REQUIRED_ASSETS := [
	"res://assets/audio/ui/confirm.ogg",
	"res://assets/audio/gameplay/countdown_go.ogg",
	"res://assets/audio/vehicles/engine_idle.wav",
	"res://assets/audio/vehicles/tire_skid.wav",
	"res://assets/audio/ambience/city_traffic.ogg",
	"res://assets/audio/music/game/PTX_01_MeterGlow_B.ogg",
	"res://assets/audio/music/game/PTX_02_DispatchAfterDark_A.ogg",
	"res://assets/audio/music/game/PTX_03_FlagfallFever_B.ogg",
	"res://assets/audio/music/game/PTX_04_RushHourRiot_B.ogg",
]

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var audio_manager: Node = get_root().get_node_or_null("AudioManager")
	_expect(audio_manager != null, "AudioManager autoload exists")
	if audio_manager == null:
		_finish()
		return

	for bus_name in REQUIRED_BUSES:
		_expect(AudioServer.get_bus_index(bus_name) >= 0, "audio bus exists: %s" % bus_name)

	for asset_path in REQUIRED_ASSETS:
		_expect(load(asset_path) != null, "audio asset imports: %s" % asset_path.get_file())

	var packed_scene: PackedScene = load(MAIN_SCENE)
	var scene_root: Node = packed_scene.instantiate()
	get_root().add_child(scene_root)
	await process_frame
	await process_frame

	var kart: Node = scene_root.get_node_or_null("Kart")
	_expect(kart != null and kart.get_node_or_null("VehicleAudio") != null, "kart has positional vehicle audio controller")
	_expect(audio_manager.get_node_or_null("CityTraffic") != null, "city ambience player is active")
	_expect(_has_music(audio_manager, "PTX_01_MeterGlow_B.ogg"), "main menu selects Meter Glow B")

	var shell: Node = scene_root.get_node_or_null("RetroNeonCabShell")
	if shell != null:
		shell.call("StartRun")
		await process_frame
		await process_frame
		_expect(_has_music(audio_manager, "PTX_03_FlagfallFever_B.ogg"), "gameplay starts with Flagfall Fever B")

	_finish()

func _has_music(audio_manager: Node, filename: String) -> bool:
	for child_name in ["MusicA", "MusicB"]:
		var player := audio_manager.get_node_or_null(child_name) as AudioStreamPlayer
		if player != null and player.stream != null and player.stream.resource_path.ends_with(filename):
			return true
	return false

func _expect(condition: bool, message: String) -> void:
	if condition:
		print("PASS: %s" % message)
	else:
		push_error("FAIL: %s" % message)
		set_meta("failed", true)

func _finish() -> void:
	paused = false
	if get_meta("failed", false):
		quit(1)
	else:
		print("Audio system smoke test passed.")
		quit(0)
