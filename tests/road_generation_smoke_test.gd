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
	var intersections := _count_children_with_prefix(track_builder, "Intersection")
	var lane_markers := _count_children_with_prefix(track_builder, "LaneMarker")
	var curb_markers := _count_children_with_prefix(track_builder, "CurbMarker")
	var crosswalks := _count_children_with_prefix(track_builder, "Crosswalk")
	var track_light_glows := _count_children_with_prefix(track_builder, "TrackLightGlow")
	var building_nodes := _get_children_with_prefix(track_builder, "Building")
	var decoration_nodes := _get_children_with_prefix(track_builder, "Decoration")
	var building_colliders := _count_children_with_prefix(track_builder, "WorldCollider")
	var world_boundaries := _count_children_with_prefix(track_builder, "WorldBoundary")
	var depot: Node3D = track_builder.get_node_or_null("TaxiDepot")

	var city_columns := int(track_builder.get("CityColumns"))
	var city_rows := int(track_builder.get("CityRows"))
	var building_count := int(track_builder.get("BuildingCount"))
	var buildings_per_block_max := int(track_builder.get("BuildingsPerBlockMax"))
	var decoration_count := int(track_builder.get("DecorationCount"))
	var track_light_count := int(track_builder.get("TrackLightCount"))
	var track_width := float(track_builder.get("TrackWidth"))
	var expected_road_segments: int = city_columns + city_rows + 2
	var expected_intersections: int = (city_columns + 1) * (city_rows + 1)
	var expected_curbs: int = (city_rows + 1) * city_columns * 2 + (city_columns + 1) * city_rows * 2
	var expected_crosswalks: int = expected_intersections * 4
	var expected_buildings: int = min(building_count, city_columns * city_rows * buildings_per_block_max)

	_expect(road_segments == expected_road_segments, "city road panel count: expected %d, got %d" % [expected_road_segments, road_segments])
	_expect(intersections == expected_intersections, "intersection count: expected %d, got %d" % [expected_intersections, intersections])
	_expect(lane_markers > expected_road_segments * 6, "lane markers generated for city streets")
	_expect(curb_markers == expected_curbs, "curb segment count: expected %d, got %d" % [expected_curbs, curb_markers])
	_expect(crosswalks == expected_crosswalks, "crosswalk count: expected %d, got %d" % [expected_crosswalks, crosswalks])
	_expect(track_light_glows == track_light_count, "track light count: expected %d, got %d" % [track_light_count, track_light_glows])
	_expect(building_nodes.size() == expected_buildings, "building count: expected %d, got %d" % [expected_buildings, building_nodes.size()])
	_expect(decoration_nodes.size() == decoration_count, "decoration count: expected %d, got %d" % [decoration_count, decoration_nodes.size()])
	_expect(_nodes_scaled_above_import_size(building_nodes, 1.1), "buildings scaled above raw import size")
	_expect(_roads_have_width_hierarchy(track_builder, track_width), "city has wider avenues and narrower side streets")
	_expect(_nodes_clear_of_road_bands(building_nodes, track_builder), "buildings stay inside blocks and clear of roads")
	_expect(building_colliders == expected_buildings, "every generated building has coarse gameplay collision")
	_expect(world_boundaries == 4, "city has four out-of-bounds barriers")
	_expect(depot != null, "single-player taxi depot is generated")

	if get_meta("failed", false):
		quit(1)
	else:
		print("City road generation smoke test passed: %d road panels, %d intersections, %d lane markers, %d curbs, %d track lights, %d buildings." % [
			road_segments,
			intersections,
			lane_markers,
			curb_markers,
			track_light_glows,
			building_nodes.size()
		])
		quit(0)

func _count_children_with_prefix(parent: Node, child_prefix: String) -> int:
	var count := 0
	for child in parent.get_children():
		if child.name.begins_with(child_prefix):
			count += 1
	return count

func _get_children_with_prefix(parent: Node, child_prefix: String) -> Array[Node3D]:
	var result: Array[Node3D] = []
	for child in parent.get_children():
		if child is Node3D and child.name.begins_with(child_prefix):
			result.append(child)
	return result

func _expect(condition: bool, message: String) -> void:
	if condition:
		print("PASS: %s" % message)
	else:
		_fail(message)

func _nodes_scaled_above_import_size(nodes: Array[Node3D], minimum_scale: float) -> bool:
	for node in nodes:
		var uniform_scale: float = min(node.scale.x, min(node.scale.y, node.scale.z))
		if uniform_scale < minimum_scale:
			push_error("Node %s is still near raw import scale: %.2f" % [node.name, uniform_scale])
			return false
	return true

func _roads_have_width_hierarchy(parent: Node, base_width: float) -> bool:
	var min_width := INF
	var max_width := 0.0
	for child in parent.get_children():
		if child is MeshInstance3D and child.name.begins_with("RoadSegmentHorizontal"):
			min_width = min(min_width, child.mesh.size.z)
			max_width = max(max_width, child.mesh.size.z)
		elif child is MeshInstance3D and child.name.begins_with("RoadSegmentVertical"):
			min_width = min(min_width, child.mesh.size.x)
			max_width = max(max_width, child.mesh.size.x)

	if max_width < base_width * 1.2:
		push_error("Expected at least one avenue wider than base %.2f, got max %.2f" % [base_width, max_width])
		return false
	if min_width > base_width * 1.08:
		push_error("Expected side streets near or below base %.2f, got min %.2f" % [base_width, min_width])
		return false

	return true

func _nodes_clear_of_road_bands(nodes: Array[Node3D], parent: Node) -> bool:
	var vertical_bands: Array[Vector2] = []
	var horizontal_bands: Array[Vector2] = []

	for child in parent.get_children():
		if child is MeshInstance3D and child.name.begins_with("RoadSegmentVertical"):
			vertical_bands.append(Vector2(child.position.x, child.mesh.size.x * 0.5 + 1.0))
		elif child is MeshInstance3D and child.name.begins_with("RoadSegmentHorizontal"):
			horizontal_bands.append(Vector2(child.position.z, child.mesh.size.z * 0.5 + 1.0))

	for node in nodes:
		for band in vertical_bands:
			if abs(node.position.x - band.x) < band.y:
				push_error("Node %s is inside a vertical road band." % node.name)
				return false
		for band in horizontal_bands:
			if abs(node.position.z - band.x) < band.y:
				push_error("Node %s is inside a horizontal road band." % node.name)
				return false

	return true

func _fail(message: String) -> void:
	push_error("FAIL: %s" % message)
	set_meta("failed", true)
