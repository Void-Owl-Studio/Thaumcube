class_name ChunkApplyScheduler
extends RefCounted

var chunk_apply_budget_ms: float = 2.5
var collision_apply_budget_ms: float = 1.5
var max_visual_applies_per_frame: int = 1
var max_collision_applies_per_frame: int = 1
var collision_apply_interval_frames: int = 3
var max_chunk_nodes_per_frame: int = 1
var max_unloads_per_frame: int = 1
var neighbor_rebuilds_per_frame: int = 1

var _ready_chunk_nodes: Array[Vector3i] = []
var _ready_chunk_lookup: Dictionary = {}
var _ready_visual_chunks: Array[Vector3i] = []
var _ready_visual_lookup: Dictionary = {}
var _ready_collision_chunks: Array[Vector3i] = []
var _ready_collision_lookup: Dictionary = {}
var _neighbor_rebuild_chunks: Array[Vector3i] = []
var _neighbor_rebuild_lookup: Dictionary = {}
var _unload_chunks: Array[Vector3i] = []
var _unload_lookup: Dictionary = {}

var _frame_index: int = 0
var _apply_deadline_usec: int = 0
var _collision_deadline_usec: int = 0
var _frame_profile: Dictionary = {}


func configure(
	next_chunk_apply_budget_ms: float,
	next_collision_apply_budget_ms: float,
	next_visual_applies_per_frame: int,
	next_collision_applies_per_frame: int,
	next_collision_apply_interval_frames: int,
	next_chunk_nodes_per_frame: int,
	next_unloads_per_frame: int,
	next_neighbor_rebuilds_per_frame: int
) -> void:
	chunk_apply_budget_ms = maxf(0.25, next_chunk_apply_budget_ms)
	collision_apply_budget_ms = maxf(0.25, next_collision_apply_budget_ms)
	max_visual_applies_per_frame = max(1, next_visual_applies_per_frame)
	max_collision_applies_per_frame = max(1, next_collision_applies_per_frame)
	collision_apply_interval_frames = max(1, next_collision_apply_interval_frames)
	max_chunk_nodes_per_frame = max(1, next_chunk_nodes_per_frame)
	max_unloads_per_frame = max(1, next_unloads_per_frame)
	neighbor_rebuilds_per_frame = max(1, next_neighbor_rebuilds_per_frame)


func begin_frame(frame_index: int) -> void:
	_frame_index = frame_index
	var now := Time.get_ticks_usec()
	_apply_deadline_usec = now + int(chunk_apply_budget_ms * 1000.0)
	_collision_deadline_usec = now + int(collision_apply_budget_ms * 1000.0)
	_frame_profile = {
		"frame_index": _frame_index,
		"chunk_apply_budget_ms": chunk_apply_budget_ms,
		"collision_apply_budget_ms": collision_apply_budget_ms,
		"nodes_added": 0,
		"nodes_unloaded": 0,
		"visual_applied": 0,
		"collision_applied": 0,
		"neighbor_updates_processed": 0,
		"visual_apply_usec": 0,
		"collision_apply_usec": 0,
		"neighbor_rebuild_usec": 0,
		"unload_usec": 0,
		"budget_exhausted": false
	}


func finalize_frame(extra_queue_stats: Dictionary = {}) -> Dictionary:
	_frame_profile["queue_sizes"] = {
		"ready_nodes": _ready_chunk_nodes.size(),
		"ready_visual_apply": _ready_visual_chunks.size(),
		"ready_collision_apply": _ready_collision_chunks.size(),
		"pending_neighbor_updates": _neighbor_rebuild_chunks.size(),
		"pending_unloads": _unload_chunks.size()
	}
	for key in extra_queue_stats.keys():
		_frame_profile["queue_sizes"][key] = extra_queue_stats[key]
	return _frame_profile.duplicate(true)


func enqueue_chunk_node(chunk_coords: Vector3i) -> void:
	if _ready_chunk_lookup.has(chunk_coords):
		return
	_ready_chunk_lookup[chunk_coords] = true
	_ready_chunk_nodes.append(chunk_coords)


func enqueue_visual_result(chunk_coords: Vector3i, result: Dictionary) -> void:
	if not _ready_visual_lookup.has(chunk_coords):
		_ready_visual_chunks.append(chunk_coords)
	_ready_visual_lookup[chunk_coords] = result


func enqueue_collision_result(chunk_coords: Vector3i, result: Dictionary) -> void:
	if not _ready_collision_lookup.has(chunk_coords):
		_ready_collision_chunks.append(chunk_coords)
	_ready_collision_lookup[chunk_coords] = result


func enqueue_neighbor_rebuild(chunk_coords: Vector3i) -> void:
	if _neighbor_rebuild_lookup.has(chunk_coords):
		return
	_neighbor_rebuild_lookup[chunk_coords] = true
	_neighbor_rebuild_chunks.append(chunk_coords)


func enqueue_unload(chunk_coords: Vector3i) -> void:
	if _unload_lookup.has(chunk_coords):
		return
	_unload_lookup[chunk_coords] = true
	_unload_chunks.append(chunk_coords)


func forget_chunk(chunk_coords: Vector3i) -> void:
	_ready_chunk_lookup.erase(chunk_coords)
	_ready_visual_lookup.erase(chunk_coords)
	_ready_collision_lookup.erase(chunk_coords)
	_neighbor_rebuild_lookup.erase(chunk_coords)
	_unload_lookup.erase(chunk_coords)
	_ready_chunk_nodes.erase(chunk_coords)
	_ready_visual_chunks.erase(chunk_coords)
	_ready_collision_chunks.erase(chunk_coords)
	_neighbor_rebuild_chunks.erase(chunk_coords)
	_unload_chunks.erase(chunk_coords)


func process_unloads(callback: Callable) -> void:
	var processed := 0
	while processed < max_unloads_per_frame and not _unload_chunks.is_empty():
		if _is_apply_budget_exhausted():
			break
		var chunk_coords: Vector3i = _unload_chunks.pop_front()
		_unload_lookup.erase(chunk_coords)
		var started_usec := Time.get_ticks_usec()
		if bool(callback.call(chunk_coords)):
			_frame_profile["nodes_unloaded"] = int(_frame_profile.get("nodes_unloaded", 0)) + 1
		_frame_profile["unload_usec"] = int(_frame_profile.get("unload_usec", 0)) + Time.get_ticks_usec() - started_usec
		processed += 1


func process_neighbor_rebuilds(callback: Callable) -> void:
	var processed := 0
	while processed < neighbor_rebuilds_per_frame and not _neighbor_rebuild_chunks.is_empty():
		if _is_apply_budget_exhausted():
			break
		var chunk_coords: Vector3i = _neighbor_rebuild_chunks.pop_front()
		_neighbor_rebuild_lookup.erase(chunk_coords)
		var started_usec := Time.get_ticks_usec()
		if bool(callback.call(chunk_coords)):
			_frame_profile["neighbor_updates_processed"] = int(_frame_profile.get("neighbor_updates_processed", 0)) + 1
		_frame_profile["neighbor_rebuild_usec"] = int(_frame_profile.get("neighbor_rebuild_usec", 0)) + Time.get_ticks_usec() - started_usec
		processed += 1


func process_ready_chunk_nodes(callback: Callable) -> void:
	var processed := 0
	while processed < max_chunk_nodes_per_frame and not _ready_chunk_nodes.is_empty():
		if _is_apply_budget_exhausted():
			break
		var chunk_coords: Vector3i = _ready_chunk_nodes.pop_front()
		_ready_chunk_lookup.erase(chunk_coords)
		var started_usec := Time.get_ticks_usec()
		if bool(callback.call(chunk_coords)):
			_frame_profile["nodes_added"] = int(_frame_profile.get("nodes_added", 0)) + 1
		_frame_profile["node_add_usec"] = int(_frame_profile.get("node_add_usec", 0)) + Time.get_ticks_usec() - started_usec
		processed += 1


func process_visual_applies(callback: Callable) -> void:
	var processed := 0
	while processed < max_visual_applies_per_frame and not _ready_visual_chunks.is_empty():
		if _is_apply_budget_exhausted():
			break
		var chunk_coords: Vector3i = _ready_visual_chunks.pop_front()
		var result: Dictionary = _ready_visual_lookup.get(chunk_coords, {})
		_ready_visual_lookup.erase(chunk_coords)
		var started_usec := Time.get_ticks_usec()
		if bool(callback.call(chunk_coords, result)):
			_frame_profile["visual_applied"] = int(_frame_profile.get("visual_applied", 0)) + 1
		_frame_profile["visual_apply_usec"] = int(_frame_profile.get("visual_apply_usec", 0)) + Time.get_ticks_usec() - started_usec
		processed += 1


func process_collision_applies(callback: Callable) -> void:
	if _frame_index % collision_apply_interval_frames != 0:
		return

	var processed := 0
	while processed < max_collision_applies_per_frame and not _ready_collision_chunks.is_empty():
		if Time.get_ticks_usec() >= _collision_deadline_usec:
			break
		var chunk_coords: Vector3i = _ready_collision_chunks.pop_front()
		var result: Dictionary = _ready_collision_lookup.get(chunk_coords, {})
		_ready_collision_lookup.erase(chunk_coords)
		var started_usec := Time.get_ticks_usec()
		if bool(callback.call(chunk_coords, result)):
			_frame_profile["collision_applied"] = int(_frame_profile.get("collision_applied", 0)) + 1
		_frame_profile["collision_apply_usec"] = int(_frame_profile.get("collision_apply_usec", 0)) + Time.get_ticks_usec() - started_usec
		processed += 1


func get_stats() -> Dictionary:
	return {
		"ready_nodes": _ready_chunk_nodes.size(),
		"ready_visual_apply": _ready_visual_chunks.size(),
		"ready_collision_apply": _ready_collision_chunks.size(),
		"pending_neighbor_updates": _neighbor_rebuild_chunks.size(),
		"pending_unloads": _unload_chunks.size()
	}


func _is_apply_budget_exhausted() -> bool:
	var exhausted := Time.get_ticks_usec() >= _apply_deadline_usec
	if exhausted:
		_frame_profile["budget_exhausted"] = true
	return exhausted
