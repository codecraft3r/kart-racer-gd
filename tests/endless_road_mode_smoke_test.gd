extends SceneTree

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var mode = get_node_or_null("/root/Node3D/EndlessRoadMode")
	if mode == null:
		_fail("EndlessRoadMode not found")
		return
	var settings = mode.get("Settings")
	if settings == null:
		_fail("EndlessRoadSettings is null")
		return
	mode.call("ResetRun", 42)
	mode.call("StartRun", 42)
	var max_frames = 240
	for i in range(max_frames):
		await process_frame
		var state = mode.get("State")
		# RunState.Results == 5, GameOver == 4
		if state == 5 or state == 4:
			break
	var score = mode.get("Score")
	var health = mode.get("Health")
	var boost = mode.get("Boost")
	if score < 0:
		_fail("Score is negative")
		return
	if health < 0 or health > 110:
		_fail("Health is out of bounds: %s" % str(health))
		return
	if boost < 0 or boost > 1.1:
		_fail("Boost is out of bounds: %s" % str(boost))
		return
	print("OK EndlessRoad smoke passed (score=%s health=%s boost=%s)" % [str(score), str(health), str(boost)])
	_finish()

func _expect(condition: bool, message: String) -> void:
	if condition:
		print("PASS: %s" % message)
	else:
		push_error("FAIL: %s" % message)
		set_meta("failed", true)

func _fail(message: String) -> void:
	push_error("FAIL: %s" % message)
	quit(1)

func _finish() -> void:
	if get_meta("failed", false):
		quit(1)
	else:
		quit(0)
