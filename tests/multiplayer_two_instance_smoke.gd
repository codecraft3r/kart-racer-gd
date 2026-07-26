extends Node

const CONNECT_TIMEOUT := 15.0
const MATCH_READY_DELAY := 3.6
const MOVE_DURATION := 1.5
const SETTLE_DURATION := 3.0

var _role := ""

func _ready() -> void:
	_role = _argument_value("--multiplayer-smoke-role=")
	if _role != "host" and _role != "client":
		push_error("MULTIPLAYER_SMOKE_FAIL invalid role: %s" % _role)
		get_tree().quit(1)
		return
	call_deferred("_run")

func _run() -> void:
	var game_manager := get_parent() as Node
	if not await _wait_for_two_players(game_manager):
		_fail("timed out waiting for two spawned karts")
		return

	var local_id := multiplayer.get_unique_id()
	var local_kart: Node = game_manager.GetKart(local_id)
	if local_kart == null or not local_kart.get("IsLocalPlayer") or not local_kart.get("UseLocalInput"):
		_fail("local dynamic kart ownership/input was not configured")
		return

	# Both peers must wait for the server-owned fare countdown to release
	# controls before validating their independent input streams.
	await get_tree().create_timer(MATCH_READY_DELAY).timeout
	var initial_position: Vector3 = local_kart.get("global_position")
	Input.action_press("move_forward")
	await get_tree().create_timer(MOVE_DURATION).timeout
	Input.action_release("move_forward")
	await get_tree().create_timer(SETTLE_DURATION).timeout

	var current_position: Vector3 = local_kart.get("global_position")
	if current_position.distance_to(initial_position) < 2.0:
		_fail("local kart did not move two meters")
		return

	if _role == "client":
		var snapshot_target: Vector3 = local_kart.get("NetworkTargetPosition")
		if current_position.distance_to(snapshot_target) > 0.75:
			_fail("local replica did not converge within 0.75m (distance=%0.2f current=%s target=%s)" % [current_position.distance_to(snapshot_target), current_position, snapshot_target])
			return
		print("MULTIPLAYER_SMOKE_CLIENT_PASS moved=%0.2f convergence=%0.2f" % [current_position.distance_to(initial_position), current_position.distance_to(snapshot_target)])
		await _shutdown(0)
		return

	# The host stays up while the runner stops client one and starts client two.
	if not await _wait_for_player_count(game_manager, 1, 12.0):
		_fail("host did not remove disconnected client")
		return
	print("MULTIPLAYER_SMOKE_DISCONNECT_PASS")
	if not await _wait_for_player_count(game_manager, 2, 12.0):
		_fail("host did not accept reconnect")
		return
	print("MULTIPLAYER_SMOKE_RECONNECT_PASS")
	# Keep the host alive while the reconnecting peer completes its movement
	# and replication assertions.
	await get_tree().create_timer(10.5).timeout
	await _shutdown(0)

func _wait_for_two_players(game_manager: Node) -> bool:
	return await _wait_for_player_count(game_manager, 2, CONNECT_TIMEOUT)

func _wait_for_player_count(game_manager: Node, expected: int, timeout: float) -> bool:
	var deadline := Time.get_ticks_msec() + int(timeout * 1000.0)
	while Time.get_ticks_msec() < deadline:
		if game_manager.GetRegisteredPlayerCount() == expected:
			return true
		await get_tree().process_frame
	return false

func _argument_value(prefix: String) -> String:
	for argument in OS.get_cmdline_args():
		if argument.begins_with(prefix):
			return argument.trim_prefix(prefix)
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with(prefix):
			return argument.trim_prefix(prefix)
	return ""

func _fail(message: String) -> void:
	push_error("MULTIPLAYER_SMOKE_FAIL %s" % message)
	call_deferred("_shutdown", 1)

func _shutdown(exit_code: int) -> void:
	var tree := get_tree()
	Input.action_release("move_forward")
	Input.action_release("move_backward")
	Input.action_release("move_left")
	Input.action_release("move_right")
	if multiplayer.has_multiplayer_peer():
		multiplayer.multiplayer_peer.close()
		multiplayer.multiplayer_peer = OfflineMultiplayerPeer.new()
	await tree.process_frame
	await tree.process_frame
	var scene := tree.current_scene
	if scene != null:
		scene.free()
	tree.quit(exit_code)
