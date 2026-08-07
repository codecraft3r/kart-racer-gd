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
	mode.call("ResetRun", 42)
	mode.call("StartRun", 42)
	var max_frames := 240
	for i in range(max_frames):
		await process_frame
		var state: int = int(mode.get("State"))
		if state == 5 or state == 4: # Results / GameOver
			break
	var score: int = int(mode.get("Score"))
	var health: float = float(mode.get("Health"))
	var boost: float = float(mode.get("Boost"))
	if score < 0:
		_fail("Score is negative")
		return
	if health < 0 or health > 110:
		_fail("Health is out of bounds: %s" % str(health))
		return
	if boost < 0 or boost > 1.1:
		_fail("Boost is out of bounds: %s" % str(boost))
		return
	print("OK EndlessRoad smoke passed (score=%s health=%.1f boost=%.2f)" % [str(score), health, boost])
	# Cleanup borrowed from neon_cab smoke test pattern
	scene_root.queue_free()
	await process_frame
	_finish()

func _fail(message: String) -> void:
	push_error("FAIL: %s" % message)
	quit(1)

func _finish() -> void:
	if get_meta("failed", false):
		quit(1)
	else:
		quit(0)
