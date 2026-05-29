class_name ChunkStreamingSystem
extends RefCounted

const ChunkDataScript := preload("res://scripts/world/chunk_data.gd")

const INVALID_CENTER_COORDS := Vector3i(2147483647, 2147483647, 2147483647)

var render_distance: int = 6
var unload_distance: int = 8
var collision_distance: int = 3

var _last_center_coords: Vector3i = INVALID_CENTER_COORDS
var _view_direction: Vector3 = Vector3.FORWARD
var _required_chunks: Dictionary = {}


func configure(next_render_distance: int, next_unload_distance: int, next_collision_distance: int) -> void:
	render_distance = max(0, next_render_distance)
	unload_distance = max(render_distance, next_unload_distance)
	collision_distance = max(0, next_collision_distance)


func set_render_distance(next_render_distance: int) -> void:
	render_distance = max(0, next_render_distance)
	unload_distance = max(unload_distance, render_distance)
	_last_center_coords = INVALID_CENTER_COORDS


func set_unload_distance(next_unload_distance: int) -> void:
	unload_distance = max(render_distance, next_unload_distance)
	_last_center_coords = INVALID_CENTER_COORDS


func update(world_position: Vector3, view_direction: Vector3, loaded_chunk_coords: Array) -> Dictionary:
	if view_direction.length_squared() > 0.0001:
		_view_direction = view_direction.normalized()

	var center_coords: Vector3i = world_to_chunk_coords(world_position)
	if center_coords == _last_center_coords:
		return {
			"center_changed": false,
			"center_coords": center_coords,
			"to_load": [],
			"to_unload": [],
			"required_chunks": _required_chunks.duplicate()
		}

	_last_center_coords = center_coords
	_required_chunks.clear()

	var to_load: Array[Vector3i] = []
	for offset_x in range(-render_distance, render_distance + 1):
		for offset_z in range(-render_distance, render_distance + 1):
			var chunk_coords := Vector3i(center_coords.x + offset_x, 0, center_coords.z + offset_z)
			_required_chunks[chunk_coords] = true
			to_load.append(chunk_coords)

	prioritize(to_load, center_coords)

	var to_unload: Array[Vector3i] = []
	for loaded_coords in loaded_chunk_coords:
		if _is_past_unload_distance(loaded_coords, center_coords):
			to_unload.append(loaded_coords)

	prioritize(to_unload, center_coords)
	to_unload.reverse()

	return {
		"center_changed": true,
		"center_coords": center_coords,
		"to_load": to_load,
		"to_unload": to_unload,
		"required_chunks": _required_chunks.duplicate()
	}


func is_required(chunk_coords: Vector3i) -> bool:
	return _required_chunks.has(chunk_coords)


func is_in_collision_distance(chunk_coords: Vector3i) -> bool:
	if _last_center_coords == INVALID_CENTER_COORDS:
		return false
	return chunk_distance_squared(chunk_coords, _last_center_coords) <= collision_distance * collision_distance


func get_center_coords() -> Vector3i:
	return _last_center_coords


func get_required_chunks() -> Dictionary:
	return _required_chunks.duplicate()


func prioritize(chunks: Array[Vector3i], center_coords: Vector3i = INVALID_CENTER_COORDS) -> void:
	var priority_center := center_coords
	if priority_center == INVALID_CENTER_COORDS:
		priority_center = _last_center_coords
	chunks.sort_custom(func(a: Vector3i, b: Vector3i) -> bool:
		return get_priority_score(a, priority_center) < get_priority_score(b, priority_center)
	)


func get_priority_score(chunk_coords: Vector3i, center_coords: Vector3i = INVALID_CENTER_COORDS) -> float:
	var priority_center := center_coords
	if priority_center == INVALID_CENTER_COORDS:
		priority_center = _last_center_coords
	if priority_center == INVALID_CENTER_COORDS:
		return 0.0

	var dx := float(chunk_coords.x - priority_center.x)
	var dz := float(chunk_coords.z - priority_center.z)
	var distance_score := dx * dx + dz * dz
	var flat_view_direction := Vector3(_view_direction.x, 0.0, _view_direction.z)
	var offset_direction := Vector3(dx, 0.0, dz)
	var forward_bias := 0.0
	if flat_view_direction.length_squared() > 0.0001 and offset_direction.length_squared() > 0.0001:
		forward_bias = offset_direction.normalized().dot(flat_view_direction.normalized())
	return distance_score - forward_bias * 0.35


func chunk_distance_squared(chunk_coords: Vector3i, center_coords: Vector3i) -> int:
	var dx := chunk_coords.x - center_coords.x
	var dz := chunk_coords.z - center_coords.z
	return dx * dx + dz * dz


func world_to_chunk_coords(world_position: Vector3) -> Vector3i:
	return Vector3i(
		floori(world_position.x / float(ChunkDataScript.SIZE_X)),
		0,
		floori(world_position.z / float(ChunkDataScript.SIZE_Z))
	)


func _is_past_unload_distance(chunk_coords: Vector3i, center_coords: Vector3i) -> bool:
	return (
		abs(chunk_coords.x - center_coords.x) > unload_distance
		or abs(chunk_coords.z - center_coords.z) > unload_distance
	)
