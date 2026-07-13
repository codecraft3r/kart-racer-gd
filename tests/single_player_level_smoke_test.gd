extends SceneTree

const MAIN_SCENE := "res://default_3d.tscn"
const LOCAL_PLAYER_ID := 1

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var packed_scene: PackedScene = load(MAIN_SCENE)
	if packed_scene == null:
		_fail("Unable to load %s" % MAIN_SCENE)
		_finish()
		return

	var root: Node = packed_scene.instantiate()
	get_root().add_child(root)
	await process_frame
	await physics_frame

	var shell: Node = root.get_node_or_null("RetroNeonCabShell")
	var mode: Node = root.get_node_or_null("Modes/TaxiMode")
	var manager: Node = root.get_node_or_null("GameManager")
	var kart: RigidBody3D = root.get_node_or_null("Kart")
	_expect(shell != null and mode != null and manager != null and kart != null, "single-player scene dependencies exist")
	if shell == null or mode == null or manager == null or kart == null:
		_finish()
		return

	_expect(int(mode.call("GetCurrentCashQuota")) == 750, "endless run uses the designed starting cash quota")
	mode.set("WinningCashTarget", 100000)
	shell.call("StartRun")
	await process_frame
	await physics_frame

	var countdown: Control = shell.find_child("CountdownLabel", true, false) as Control
	_expect(int(mode.call("GetPhaseValue")) == 1, "solo shift begins with a countdown")
	_expect(not bool(kart.call("GetControlsEnabled")), "driving controls are locked during the countdown")
	_expect(countdown != null and countdown.visible, "countdown is visible to the player")
	_expect(await _wait_for_phase(mode, 2, 300), "countdown transitions into the active match phase")
	_expect(bool(kart.call("GetControlsEnabled")), "driving controls unlock when the shift starts")
	_expect(int(manager.call("GetRegisteredPlayerCount")) == 3, "one player and two AI rivals are registered")
	_expect(bool(kart.get("IsLocalPlayer")), "scene kart is configured as the local player")
	_expect(kart.get_node_or_null("CompassArrow") != null, "local player receives objective navigation")
	_expect(int(mode.call("GetActivePickupCount")) > 0, "fare pickup zones are available")

	var first_fare_completed := await _complete_fare(mode, manager, kart)
	_expect(first_fare_completed, "first passenger can be picked up and delivered")
	var first_payout := int(manager.call("GetPlayerMoney", LOCAL_PLAYER_ID))
	_expect(first_payout > 0, "first delivery awards cash")
	_expect(not bool(kart.call("HasPassenger")), "first delivery clears the passenger")
	_expect(mode.call("GetPlayerDestination", LOCAL_PLAYER_ID) == Vector3.ZERO, "first delivery clears the destination")

	var second_fare_completed := await _complete_fare(mode, manager, kart)
	_expect(second_fare_completed, "a second fare can be completed without restarting")
	_expect(int(manager.call("GetPlayerMoney", LOCAL_PLAYER_ID)) > first_payout, "second delivery increases cash")

	manager.call("ApplyVehicleDamage", LOCAL_PLAYER_ID, 50)
	mode.call("AddCashScore", LOCAL_PLAYER_ID, 100000)
	await process_frame
	var results: Control = shell.find_child("ResultsScreen", true, false) as Control
	var repair_button: Button = shell.find_child("PitRepairButton", true, false) as Button
	_expect(int(mode.call("GetPhaseValue")) == 4, "cash quota clears the shift into intermission")
	_expect(int(mode.call("GetWinnerPeerId")) == LOCAL_PLAYER_ID, "local player is recorded as the winner")
	_expect(int(mode.call("GetShiftNumber")) == 1, "first cleared shift is recorded")
	_expect(results != null and results.visible, "cleared shift opens the intermission screen")
	_expect(paused, "intermission safely pauses the level")
	_expect(repair_button != null and repair_button.visible and not repair_button.disabled, "damaged taxi can buy a pit repair")
	var bank_before_repair := int(manager.call("GetPlayerMoney", LOCAL_PLAYER_ID))
	shell.call("BuyPitRepair")
	_expect(int(manager.call("GetPlayerHealth", LOCAL_PLAYER_ID)) == 100, "pit repair restores taxi health")
	_expect(int(manager.call("GetPlayerMoney", LOCAL_PLAYER_ID)) == bank_before_repair - 100, "pit repair spends earned cash")

	mode.set("MatchDurationSeconds", 0.15)
	mode.set("CountdownSeconds", 0.0)
	mode.set("WinningCashTarget", 100000)
	shell.call("AdvanceOrRestartRun")
	await process_frame
	_expect(int(manager.call("GetRegisteredPlayerCount")) == 3, "advancing keeps rivals without duplicating them")
	_expect(int(mode.call("GetShiftNumber")) == 2, "advancing starts the second shift")
	_expect(int(mode.call("GetCurrentCashQuota")) == 100250, "second shift raises the cash quota")
	_expect(int(mode.call("GetTotalRunCash")) >= 100000, "run cash carries across shifts")
	mode.call("AddCashScore", LOCAL_PLAYER_ID, 100250)
	await process_frame
	_expect(int(mode.call("GetPhaseValue")) == 4, "second quota opens another intermission")
	shell.call("AdvanceOrRestartRun")
	await process_frame
	_expect(int(mode.call("GetShiftNumber")) == 3, "endless run advances into a third shift")
	_expect(int(mode.call("GetCurrentCashQuota")) == 100500, "third shift raises the quota again")
	_expect(int(manager.call("GetRegisteredPlayerCount")) == 4, "third shift adds an escalating Rival")
	var timer_finished := await _wait_for_phase(mode, 3, 120)
	_expect(timer_finished, "missing the quota when time expires ends the run")
	_expect(results.visible, "run failure returns to the results screen")
	shell.call("AdvanceOrRestartRun")
	await process_frame
	_expect(int(mode.call("GetShiftNumber")) == 1, "run-over primary action starts a fresh run")
	_expect(int(mode.call("GetCurrentCashQuota")) == 100000, "fresh run resets to the starting quota")
	_expect(int(mode.call("GetTotalRunCash")) == 0, "fresh run clears accumulated run cash")
	_expect(int(manager.call("GetRegisteredPlayerCount")) == 3, "fresh run resets to the base Rival count")

	_finish()

func _complete_fare(mode: Node, manager: Node, kart: RigidBody3D) -> bool:
	var zone := _find_pickup(mode)
	if zone == null:
		_fail("No valid pickup zone remained for fare test")
		return false

	zone.set("LoadTime", 0.05)
	_teleport_kart(kart, zone.global_position)
	if not await _wait_for_passenger(kart, true, 120):
		_fail("Passenger did not board inside pickup zone")
		return false

	var destination: Vector3 = mode.call("GetPlayerDestination", LOCAL_PLAYER_ID)
	if destination == Vector3.ZERO:
		_fail("Boarded passenger did not receive a destination")
		return false

	_teleport_kart(kart, destination)
	if not await _wait_for_passenger(kart, false, 120):
		_fail("Passenger did not complete drop-off")
		return false

	await process_frame
	return true

func _find_pickup(mode: Node) -> Area3D:
	for child in mode.get_children():
		if child is Area3D and child.name.begins_with("PickupZone") and not child.is_queued_for_deletion():
			return child
	return null

func _teleport_kart(kart: RigidBody3D, position: Vector3) -> void:
	kart.linear_velocity = Vector3.ZERO
	kart.angular_velocity = Vector3.ZERO
	kart.global_position = position + Vector3.UP * 0.65
	kart.sleeping = false

func _wait_for_passenger(kart: Node, expected: bool, max_frames: int) -> bool:
	for _frame in range(max_frames):
		if bool(kart.call("HasPassenger")) == expected:
			return true
		await physics_frame
	return false

func _wait_for_phase(mode: Node, expected_phase: int, max_frames: int) -> bool:
	for _frame in range(max_frames):
		if int(mode.call("GetPhaseValue")) == expected_phase:
			return true
		await physics_frame
	return false

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
		print("Single-player level end-to-end smoke test passed.")
		quit(0)
