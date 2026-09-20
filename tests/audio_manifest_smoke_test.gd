extends SceneTree

# Audio manifest smoke test.
#
# Fails if:
#   - a manifest row points at a file that is not on disk
#   - a shipped audio file on disk has no manifest row
#   - a row has an empty/unknown license or source_page
#   - a CC-BY row has no required_credit
#   - a license value has no matching text under assets/audio/licenses/
#   - a path required by tests/audio_system_smoke_test.gd is missing
#
# Run: Godot --headless --path . --script res://tests/audio_manifest_smoke_test.gd

const MANIFEST := "res://assets/audio/audio_asset_manifest.csv"
const AUDIO_ROOT := "res://assets/audio"
const LICENSE_DIR := "res://assets/audio/licenses"
const AUDIO_EXTENSIONS := ["ogg", "wav"]
const EXPECTED_COLUMNS := [
	"local_path", "source_page", "creator", "license",
	"original_file", "required_credit", "download_date", "notes",
]

# license value -> license text file that must exist under licenses/
const LICENSE_FILES := {
	"CC0": "CC0-1.0.txt",
	"CC BY 3.0": "CC-BY-3.0.txt",
	"Suno Paid-Plan Commercial": "Suno_Commercial_Terms.md",
}

# Paths tests/audio_system_smoke_test.gd loads; must stay listed and on disk.
const RUNTIME_REQUIRED := [
	"assets/audio/ui/confirm.ogg",
	"assets/audio/gameplay/countdown_go.ogg",
	"assets/audio/vehicles/engine_idle.wav",
	"assets/audio/vehicles/tire_skid.wav",
	"assets/audio/ambience/city_traffic.ogg",
	"assets/audio/music/game/PTX_01_MeterGlow_B.ogg",
	"assets/audio/music/game/PTX_02_DispatchAfterDark_A.ogg",
	"assets/audio/music/game/PTX_03_FlagfallFever_B.ogg",
	"assets/audio/music/game/PTX_04_RushHourRiot_B.ogg",
]

var _failed := false

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	# A real child node so the engine's normal shutdown frees the tree cleanly
	# and does not report a leaked ScriptTree root at exit.
	var root_node := Node.new()
	root_node.name = "AudioManifestCheck"
	get_root().add_child(root_node)

	var rows := _read_manifest()
	if rows.is_empty():
		await _finish()
		return

	var listed := {}
	for row in rows:
		var local_path: String = row["local_path"]
		_expect(not listed.has(local_path), "manifest row is unique: %s" % local_path)
		listed[local_path] = row

		var res_path := "res://" + local_path
		_expect(FileAccess.file_exists(res_path), "manifest asset exists on disk: %s" % local_path)

		for column in ["source_page", "creator", "license", "original_file", "required_credit", "download_date"]:
			_expect(String(row[column]).strip_edges() != "",
				"row has non-empty %s: %s" % [column, local_path])

		var license_value := String(row["license"]).strip_edges()
		_expect(LICENSE_FILES.has(license_value),
			"license is a known value (%s): %s" % [license_value, local_path])

		if license_value.begins_with("CC BY"):
			var credit := String(row["required_credit"]).strip_edges()
			_expect(credit != "" and credit.to_lower() != "none",
				"CC-BY asset carries a required_credit: %s" % local_path)

		var source_page := String(row["source_page"]).strip_edges()
		_expect(source_page.begins_with("http"), "source_page is a URL: %s" % local_path)

	for license_value in LICENSE_FILES.keys():
		var license_path: String = LICENSE_DIR + "/" + LICENSE_FILES[license_value]
		_expect(FileAccess.file_exists(license_path),
			"license text present for %s (%s)" % [license_value, LICENSE_FILES[license_value]])

	for on_disk in _scan_audio_files(AUDIO_ROOT):
		_expect(listed.has(on_disk), "on-disk asset is listed in the manifest: %s" % on_disk)

	for required in RUNTIME_REQUIRED:
		_expect(listed.has(required) and FileAccess.file_exists("res://" + required),
			"runtime-required asset listed and present: %s" % required)

	await _finish()

func _read_manifest() -> Array:
	var file := FileAccess.open(MANIFEST, FileAccess.READ)
	if file == null:
		_expect(false, "manifest is readable: %s" % MANIFEST)
		return []

	var header := file.get_csv_line()
	var header_list: Array = Array(header)
	_expect(header_list == EXPECTED_COLUMNS,
		"manifest header matches the expected schema (got %s)" % str(header_list))

	var rows: Array = []
	while not file.eof_reached():
		var line := file.get_csv_line()
		if line.size() <= 1 and String(line[0]).strip_edges() == "":
			continue
		if line.size() != EXPECTED_COLUMNS.size():
			_expect(false, "row has %d columns, expected %d: %s" % [line.size(), EXPECTED_COLUMNS.size(), line[0]])
			continue
		var row := {}
		for i in EXPECTED_COLUMNS.size():
			row[EXPECTED_COLUMNS[i]] = String(line[i]).strip_edges()
		rows.append(row)
	file.close()
	print("Manifest rows parsed: %d" % rows.size())
	return rows

func _scan_audio_files(dir_path: String) -> Array:
	var found: Array = []
	var dir := DirAccess.open(dir_path)
	if dir == null:
		return found
	dir.list_dir_begin()
	var entry := dir.get_next()
	while entry != "":
		if entry.begins_with("."):
			entry = dir.get_next()
			continue
		var full := dir_path + "/" + entry
		if dir.current_is_dir():
			found.append_array(_scan_audio_files(full))
		elif AUDIO_EXTENSIONS.has(entry.get_extension().to_lower()):
			found.append(full.replace("res://", ""))
		entry = dir.get_next()
	dir.list_dir_end()
	dir = null
	found.sort()
	return found

func _expect(condition: bool, message: String) -> void:
	if condition:
		print("PASS: %s" % message)
	else:
		push_error("FAIL: %s" % message)
		printerr("FAIL: %s" % message)
		_failed = true

func _finish() -> void:
	paused = false
	var check := get_root().get_node_or_null("AudioManifestCheck")
	if check != null:
		check.queue_free()
	# Free audio autoload resources so the SceneTree script does not report
	# leaked resources at exit (autoloads are not torn down on --script quit).
	var audio := get_root().get_node_or_null("AudioManager")
	var probe_script: Script = load("res://tests/harness/HarnessProbe.cs")
	var probe := probe_script.new() as Node if probe_script != null else null
	if probe != null:
		get_root().add_child(probe)
		probe.call("ReleaseAudioManagerResources")
	if audio != null:
		for player in audio.get_children():
			if player is AudioStreamPlayer or player is AudioStreamPlayer3D:
				player.stop()
				player.stream = null
		# Defer removal through the scene tree so Godot's audio playback resources
		# receive their normal exit-tree lifecycle before the process quits.
		audio.queue_free()
	audio = null
	# AudioStream playback wrappers release on the audio thread; give that thread a
	# bounded drain window before collecting managed wrappers and quitting.
	for _index in 60:
		await process_frame
	if probe != null:
		probe.call("CollectManagedResources")
		probe.free()
	quit(0 if not _failed else 1)
