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
	await physics_frame

	var track_builder: Node = root.get_node_or_null("TrackBuilder")
	if track_builder == null:
		_fail("TrackBuilder node missing")
		return

	var road_segments := _count_children_with_prefix(track_builder, "RoadSegment")
	var lane_markers := _count_children_with_prefix(track_builder, "LaneMarker")
	var curb_markers := _count_children_with_prefix(track_builder, "CurbMarker")
	var track_light_glows := _count_children_with_prefix(track_builder, "TrackLightGlow")
	var scenery_nodes := _get_scenery_nodes(track_builder)

	var road_segment_count := int(track_builder.get("RoadSegmentCount"))
	var lane_marker_count := int(track_builder.get("LaneMarkerCount"))
	var track_light_count := int(track_builder.get("TrackLightCount"))
	var track_radius := float(track_builder.get("TrackRadius"))
	var track_width := float(track_builder.get("TrackWidth"))
	var shoulder_width := float(track_builder.get("ShoulderWidth"))
	var curb_count: int = int(max(24, road_segment_count / 2) * 2)
	var expected_lane_markers: int = int(ceil(lane_marker_count / 2.0))

	_expect(road_segments == road_segment_count, "road segment count: expected %d, got %d" % [road_segment_count, road_segments])
	_expect(lane_markers == expected_lane_markers, "lane marker count: expected %d, got %d" % [expected_lane_markers, lane_markers])
	_expect(curb_markers == curb_count, "curb marker count: expected %d, got %d" % [curb_count, curb_markers])
	_expect(track_light_glows == track_light_count, "track light count: expected %d, got %d" % [track_light_count, track_light_glows])
	_expect(scenery_nodes.size() > 0, "scenery nodes generated")
	_expect(_scenery_clear_of_lane(scenery_nodes, track_radius, track_width, shoulder_width), "scenery kept outside the drivable lane")

	if get_meta("failed", false):
		quit(1)
	else:
		print("Road generation smoke test passed: %d road segments, %d lane markers, %d curbs, %d track lights, %d scenery nodes." % [
			road_segments,
			lane_markers,
			curb_markers,
			track_light_glows,
			scenery_nodes.size()
		])
		quit(0)

func _count_children_with_prefix(parent: Node, child_prefix: String) -> int:
	var count := 0
	for child in parent.get_children():
		if child.name.begins_with(child_prefix):
			count += 1
	return count

func _get_scenery_nodes(parent: Node) -> Array[Node3D]:
	var result: Array[Node3D] = []
	for child in parent.get_children():
		if child is Node3D and !child.name.begins_with("RoadSegment") and !child.name.begins_with("LaneMarker") and !child.name.begins_with("CurbMarker"):
			result.append(child)
	return result

func _scenery_clear_of_lane(nodes: Array[Node3D], track_radius: float, track_width: float, shoulder_width: float) -> bool:
	var lane_half_width: float = track_width * 0.5
	var required_clearance: float = lane_half_width + min(2.0, shoulder_width)
	for node in nodes:
		var radius_delta: float = abs(Vector2(node.position.x, node.position.z).length() - track_radius)
		if radius_delta < required_clearance:
			push_error("Scenery node %s is too close to the lane: radius delta %.2f" % [node.name, radius_delta])
			return false
	return true

func _expect(condition: bool, message: String) -> void:
	if condition:
		print("PASS: %s" % message)
	else:
		_fail(message)

func _fail(message: String) -> void:
	push_error("FAIL: %s" % message)
	set_meta("failed", true)
