extends Node3D

const CHUNK_SCENE := preload("res://scenes/world/chunk.tscn")
const ChunkDataScript := preload("res://scripts/world/chunk_data.gd")
const ChunkStreamingSystemScript := preload("res://scripts/world/chunk_streaming_system.gd")
const ChunkTaskQueueScript := preload("res://scripts/world/chunk_task_queue.gd")
const ChunkApplySchedulerScript := preload("res://scripts/world/chunk_apply_scheduler.gd")

const CHUNK_LEFT := Vector3i.LEFT
const CHUNK_RIGHT := Vector3i.RIGHT
const CHUNK_FORWARD := Vector3i.FORWARD
const CHUNK_BACK := Vector3i.BACK
const LIGHT_DIRECTIONS := [
	Vector3i.RIGHT,
	Vector3i.LEFT,
	Vector3i.UP,
	Vector3i.DOWN,
	Vector3i.BACK,
	Vector3i.FORWARD
]

enum ChunkState {
	UNLOADED,
	QUEUED,
	GENERATING_DATA,
	BUILDING_SURFACE,
	READY_TO_APPLY,
	VISIBLE,
	COLLISION_PENDING,
	FULLY_READY,
	UNLOADING
}

var _generator
var _voxel_builder

var _streaming: ChunkStreamingSystem
var _task_queue: ChunkTaskQueue
var _apply_scheduler: ChunkApplyScheduler

var _loaded_chunks: Dictionary = {}
var _chunk_data_cache: Dictionary = {}
var _chunk_states: Dictionary = {}
var _chunk_node_pool: Array[Node3D] = []
var _cache_order: Array[Vector3i] = []

var _max_cached_chunks: int = 256
var _frame_index: int = 0
var _last_center_coords: Vector3i = ChunkStreamingSystem.INVALID_CENTER_COORDS
var _last_chunk_profile: Dictionary = {}
var _last_frame_profile: Dictionary = {}
var _is_shutting_down: bool = false


func configure(
	generator,
	voxel_builder,
	render_distance: int,
	unload_distance: int,
	collision_distance: int,
	chunk_jobs_per_frame: int = 1,
	max_worker_jobs: int = 2,
	chunk_nodes_per_frame: int = 1,
	chunk_apply_budget_ms: float = 2.5,
	collision_apply_budget_ms: float = 1.5,
	max_visual_applies_per_frame: int = 1,
	collision_apply_interval_frames: int = 3,
	collision_build_jobs_per_frame: int = 1,
	neighbor_rebuilds_per_frame: int = 1,
	max_collision_applies_per_frame: int = 1,
	unloads_per_frame: int = 1
) -> void:
	_generator = generator
	_voxel_builder = voxel_builder

	_streaming = ChunkStreamingSystemScript.new()
	_streaming.configure(render_distance, unload_distance, collision_distance)

	_task_queue = ChunkTaskQueueScript.new()
	_task_queue.configure(max_worker_jobs, chunk_jobs_per_frame, chunk_jobs_per_frame, collision_build_jobs_per_frame)

	_apply_scheduler = ChunkApplySchedulerScript.new()
	_apply_scheduler.configure(
		chunk_apply_budget_ms,
		collision_apply_budget_ms,
		max_visual_applies_per_frame,
		max_collision_applies_per_frame,
		collision_apply_interval_frames,
		chunk_nodes_per_frame,
		unloads_per_frame,
		neighbor_rebuilds_per_frame
	)


func set_load_radius(load_radius: int) -> void:
	set_render_distance(load_radius)


func set_render_distance(render_distance: int) -> void:
	if _streaming == null:
		return
	_streaming.set_render_distance(render_distance)


func sync_chunks_around(world_position: Vector3, view_direction: Vector3 = Vector3.ZERO) -> void:
	if _is_shutting_down or _generator == null or _voxel_builder == null:
		return
	if _streaming == null or _task_queue == null or _apply_scheduler == null:
		return

	_frame_index += 1
	_apply_scheduler.begin_frame(_frame_index)

	var current_center := _streaming.world_to_chunk_coords(world_position)
	var loaded_chunk_coords: Array = []
	if current_center != _streaming.get_center_coords():
		loaded_chunk_coords = _loaded_chunks.keys()
	var streaming_update: Dictionary = _streaming.update(world_position, view_direction, loaded_chunk_coords)
	if bool(streaming_update.get("center_changed", false)):
		_handle_streaming_update(streaming_update)

	_collect_worker_results()
	_process_main_thread_queues()
	_start_worker_jobs()

	_last_frame_profile = _apply_scheduler.finalize_frame(_task_queue.get_stats())


func get_profile_last_chunk() -> Dictionary:
	return _last_chunk_profile.duplicate(true)


func get_debug_stats() -> Dictionary:
	var scheduler_stats: Dictionary = {}
	var task_stats: Dictionary = {}
	if _apply_scheduler != null:
		scheduler_stats = _apply_scheduler.get_stats()
	if _task_queue != null:
		task_stats = _task_queue.get_stats()
	var current_render_distance := 0
	var current_unload_distance := 0
	var current_collision_distance := 0
	if _streaming != null:
		current_render_distance = _streaming.render_distance
		current_unload_distance = _streaming.unload_distance
		current_collision_distance = _streaming.collision_distance
	return {
		"render_distance": current_render_distance,
		"unload_distance": current_unload_distance,
		"collision_distance": current_collision_distance,
		"loaded_chunks": _loaded_chunks.size(),
		"cached_chunks": _chunk_data_cache.size(),
		"pooled_chunk_nodes": _chunk_node_pool.size(),
		"queued_chunks": _count_queued_chunks(task_stats, scheduler_stats),
		"active_jobs": int(task_stats.get("active_jobs", 0)),
		"pending_generation": int(task_stats.get("pending_generation", 0)),
		"active_generation": int(task_stats.get("active_generation", 0)),
		"pending_visual_build": int(task_stats.get("pending_visual_build", 0)),
		"active_visual_build": int(task_stats.get("active_visual_build", 0)),
		"ready_visual_apply": int(scheduler_stats.get("ready_visual_apply", 0)),
		"pending_collision_build": int(task_stats.get("pending_collision_build", 0)),
		"active_collision_build": int(task_stats.get("active_collision_build", 0)),
		"ready_collision_apply": int(scheduler_stats.get("ready_collision_apply", 0)),
		"pending_neighbor_updates": int(scheduler_stats.get("pending_neighbor_updates", 0)),
		"pending_unloads": int(scheduler_stats.get("pending_unloads", 0)),
		"last_chunk_profile": get_profile_last_chunk(),
		"frame_profile": _last_frame_profile.duplicate(true)
	}


func set_block_at_world(world_position: Vector3i, block_id: int) -> bool:
	if _is_shutting_down:
		return false
	var chunk_coords: Vector3i = _world_to_chunk_coords(world_position)
	var chunk_data = _chunk_data_cache.get(chunk_coords)
	if chunk_data == null:
		return false

	var local_position: Vector3i = _world_to_local_block_position(world_position, chunk_coords)
	if chunk_data.get_block(local_position) == block_id:
		return false

	chunk_data.set_block(local_position, block_id)
	_recalculate_loaded_chunk_lighting()
	_request_visual_rebuild(chunk_coords, true)
	_request_collision_rebuild(chunk_coords)
	_enqueue_neighbor_rebuilds_for_border(local_position, chunk_coords)
	return true


func get_block_id_at(world_position: Vector3i) -> int:
	var chunk_coords: Vector3i = _world_to_chunk_coords(world_position)
	var chunk_data = _chunk_data_cache.get(chunk_coords)
	if chunk_data != null:
		var local_position: Vector3i = _world_to_local_block_position(world_position, chunk_coords)
		return chunk_data.get_block(local_position)

	if _generator != null:
		return _generator.get_block_id_at(world_position)

	return ChunkDataScript.AIR_BLOCK_ID


func get_light_level_at(world_position: Vector3i) -> int:
	var chunk_coords: Vector3i = _world_to_chunk_coords(world_position)
	var chunk_data = _chunk_data_cache.get(chunk_coords)
	if chunk_data != null:
		var local_position: Vector3i = _world_to_local_block_position(world_position, chunk_coords)
		return chunk_data.get_light_level(local_position)

	if _generator == null:
		return 0

	if _is_solid(_generator.get_block_id_at(world_position)):
		return 0

	var surface_height: int = _generator.get_surface_height(world_position.x, world_position.z)
	if world_position.y > surface_height:
		return 15
	return 0


func _handle_streaming_update(streaming_update: Dictionary) -> void:
	_last_center_coords = streaming_update.get("center_coords", _last_center_coords)

	for chunk_coords in streaming_update.get("to_unload", []):
		_request_chunk_unload(chunk_coords)

	for chunk_coords in streaming_update.get("to_load", []):
		_request_chunk_load(chunk_coords)

	_enqueue_collision_window(_last_center_coords)
	_task_queue.cancel_not_required(Callable(self, "_is_chunk_required_or_loaded"))
	_task_queue.prioritize(Callable(self, "_get_chunk_priority_score"))
	_trim_chunk_cache()


func _request_chunk_load(chunk_coords: Vector3i) -> void:
	if _loaded_chunks.has(chunk_coords):
		return
	if _chunk_data_cache.has(chunk_coords):
		_set_chunk_state(chunk_coords, ChunkState.READY_TO_APPLY)
		_apply_scheduler.enqueue_chunk_node(chunk_coords)
		return
	if _get_chunk_state(chunk_coords) == ChunkState.UNLOADING:
		_set_chunk_state(chunk_coords, ChunkState.VISIBLE)
		return
	_set_chunk_state(chunk_coords, ChunkState.QUEUED)
	_task_queue.request_generation(chunk_coords)


func _request_chunk_unload(chunk_coords: Vector3i) -> void:
	if not _loaded_chunks.has(chunk_coords):
		return
	_set_chunk_state(chunk_coords, ChunkState.UNLOADING)
	_task_queue.invalidate_chunk(chunk_coords)
	_apply_scheduler.enqueue_unload(chunk_coords)


func _collect_worker_results() -> void:
	for result in _task_queue.collect_completed_generation():
		var chunk_coords: Vector3i = result.get("chunk_coords", Vector3i.ZERO)
		var version := int(result.get("version", 0))
		if version != _task_queue.get_generation_version(chunk_coords):
			continue
		if not _is_chunk_required(chunk_coords):
			continue

		var chunk_data = result.get("chunk_data")
		if chunk_data == null:
			continue

		_store_chunk_data(chunk_coords, chunk_data)
		_set_chunk_state(chunk_coords, ChunkState.READY_TO_APPLY)
		_apply_scheduler.enqueue_chunk_node(chunk_coords)
		_last_chunk_profile = {
			"chunk_coords": chunk_coords,
			"generation_usec": int(result.get("generation_usec", 0))
		}

	for result in _task_queue.collect_completed_visual():
		var chunk_coords: Vector3i = result.get("chunk_coords", Vector3i.ZERO)
		var version := int(result.get("version", 0))
		if version != _task_queue.get_visual_version(chunk_coords):
			if _loaded_chunks.has(chunk_coords) and _is_chunk_required(chunk_coords):
				_request_visual_rebuild(chunk_coords, true)
			continue
		if not _loaded_chunks.has(chunk_coords) or not _is_chunk_required(chunk_coords):
			continue
		_set_chunk_state(chunk_coords, ChunkState.READY_TO_APPLY)
		_apply_scheduler.enqueue_visual_result(chunk_coords, result)

	for result in _task_queue.collect_completed_collision():
		var chunk_coords: Vector3i = result.get("chunk_coords", Vector3i.ZERO)
		var version := int(result.get("version", 0))
		if version != _task_queue.get_collision_version(chunk_coords):
			if _loaded_chunks.has(chunk_coords) and _is_chunk_required(chunk_coords):
				_request_collision_rebuild(chunk_coords)
			continue
		if not _loaded_chunks.has(chunk_coords) or not _is_chunk_required(chunk_coords):
			continue
		_apply_scheduler.enqueue_collision_result(chunk_coords, result)


func _process_main_thread_queues() -> void:
	_apply_scheduler.process_unloads(Callable(self, "_apply_unload"))
	_apply_scheduler.process_neighbor_rebuilds(Callable(self, "_process_neighbor_rebuild"))
	_apply_scheduler.process_ready_chunk_nodes(Callable(self, "_apply_chunk_node"))
	_apply_scheduler.process_visual_applies(Callable(self, "_apply_visual_result"))
	_apply_scheduler.process_collision_applies(Callable(self, "_apply_collision_result"))


func _start_worker_jobs() -> void:
	_task_queue.start_generation_tasks(
		_generator,
		Callable(self, "_is_chunk_required"),
		Callable(self, "_has_chunk_data")
	)
	_task_queue.start_visual_tasks(
		_voxel_builder,
		Callable(self, "_is_chunk_required"),
		Callable(self, "_get_chunk_data"),
		Callable(self, "_build_chunk_neighbor_context")
	)
	_task_queue.start_collision_tasks(
		_voxel_builder,
		Callable(self, "_is_chunk_required"),
		Callable(self, "_get_chunk_data"),
		Callable(self, "_build_chunk_neighbor_context"),
		Callable(self, "_should_build_collision_for_chunk")
	)


func _apply_chunk_node(chunk_coords: Vector3i) -> bool:
	if not _is_chunk_required(chunk_coords) or _loaded_chunks.has(chunk_coords):
		return false

	var chunk_data = _chunk_data_cache.get(chunk_coords)
	if chunk_data == null:
		return false

	_initialize_chunk_skylight(chunk_data)
	var chunk_node := _get_chunk_node_from_pool()
	chunk_node.call("set_chunk_data", chunk_data)
	_loaded_chunks[chunk_coords] = chunk_node
	_set_chunk_state(chunk_coords, ChunkState.VISIBLE)

	_request_visual_rebuild(chunk_coords, true)
	_request_collision_rebuild(chunk_coords)
	_enqueue_neighbor_chunk_update(chunk_coords + CHUNK_LEFT)
	_enqueue_neighbor_chunk_update(chunk_coords + CHUNK_RIGHT)
	_enqueue_neighbor_chunk_update(chunk_coords + CHUNK_FORWARD)
	_enqueue_neighbor_chunk_update(chunk_coords + CHUNK_BACK)

	_last_chunk_profile = {
		"chunk_coords": chunk_coords,
		"state": "VISIBLE"
	}
	return true


func _apply_visual_result(chunk_coords: Vector3i, result: Dictionary) -> bool:
	var chunk_node = _loaded_chunks.get(chunk_coords)
	if chunk_node == null or not _is_chunk_required(chunk_coords):
		return false

	chunk_node.apply_visual_layout(_voxel_builder, result)
	_set_chunk_state(chunk_coords, ChunkState.COLLISION_PENDING)
	_last_chunk_profile = {
		"chunk_coords": chunk_coords,
		"visual_build_usec": int(result.get("visual_build_usec", 0)),
		"stats": result.get("stats", {})
	}
	return true


func _apply_collision_result(chunk_coords: Vector3i, result: Dictionary) -> bool:
	var chunk_node = _loaded_chunks.get(chunk_coords)
	if chunk_node == null or not _is_chunk_required(chunk_coords):
		return false

	var collision_sections: Dictionary = result.get("collision_sections", {})
	if not collision_sections.is_empty() and chunk_node.has_method("apply_collision_sections"):
		chunk_node.apply_collision_sections(collision_sections)
	else:
		chunk_node.apply_collision_faces(result.get("collision_faces", PackedVector3Array()))

	_set_chunk_state(chunk_coords, ChunkState.FULLY_READY)
	_last_chunk_profile = {
		"chunk_coords": chunk_coords,
		"collision_build_usec": int(result.get("collision_build_usec", 0)),
		"collision_enabled": bool(result.get("collision_enabled", false)),
		"stats": result.get("stats", {})
	}
	return true


func _apply_unload(chunk_coords: Vector3i) -> bool:
	if _is_chunk_required(chunk_coords):
		_set_chunk_state(chunk_coords, ChunkState.VISIBLE)
		return false

	var chunk_node = _loaded_chunks.get(chunk_coords)
	if chunk_node == null:
		_set_chunk_state(chunk_coords, ChunkState.UNLOADED)
		_apply_scheduler.forget_chunk(chunk_coords)
		return false

	_loaded_chunks.erase(chunk_coords)
	if chunk_node.has_method("reset_for_pool"):
		chunk_node.reset_for_pool()
	else:
		chunk_node.visible = false
	_chunk_node_pool.append(chunk_node)
	_apply_scheduler.forget_chunk(chunk_coords)
	_set_chunk_state(chunk_coords, ChunkState.UNLOADED)
	_last_chunk_profile = {
		"chunk_coords": chunk_coords,
		"state": "UNLOADED"
	}
	return true


func _process_neighbor_rebuild(chunk_coords: Vector3i) -> bool:
	if not _loaded_chunks.has(chunk_coords) or not _is_chunk_required(chunk_coords):
		return false
	_request_visual_rebuild(chunk_coords, true)
	_request_collision_rebuild(chunk_coords)
	return true


func _request_visual_rebuild(chunk_coords: Vector3i, force: bool = false) -> void:
	if not _loaded_chunks.has(chunk_coords) or not _is_chunk_required(chunk_coords):
		return
	var chunk_data = _chunk_data_cache.get(chunk_coords)
	if chunk_data == null:
		return
	if not force and chunk_data.has_method("is_dirty") and not chunk_data.is_dirty():
		return
	_set_chunk_state(chunk_coords, ChunkState.BUILDING_SURFACE)
	_task_queue.request_visual(chunk_coords)
	_task_queue.prioritize(Callable(self, "_get_chunk_priority_score"))


func _request_collision_rebuild(chunk_coords: Vector3i) -> void:
	if not _loaded_chunks.has(chunk_coords) or not _is_chunk_required(chunk_coords):
		return
	_set_chunk_state(chunk_coords, ChunkState.COLLISION_PENDING)
	_task_queue.request_collision(chunk_coords)
	_task_queue.prioritize(Callable(self, "_get_chunk_priority_score"))


func _enqueue_neighbor_chunk_update(chunk_coords: Vector3i) -> void:
	if not _loaded_chunks.has(chunk_coords):
		return
	_apply_scheduler.enqueue_neighbor_rebuild(chunk_coords)


func _enqueue_neighbor_rebuilds_for_border(local_position: Vector3i, chunk_coords: Vector3i) -> void:
	if local_position.x == 0:
		_enqueue_neighbor_chunk_update(chunk_coords + CHUNK_LEFT)
	elif local_position.x == ChunkDataScript.SIZE_X - 1:
		_enqueue_neighbor_chunk_update(chunk_coords + CHUNK_RIGHT)

	if local_position.z == 0:
		_enqueue_neighbor_chunk_update(chunk_coords + CHUNK_FORWARD)
	elif local_position.z == ChunkDataScript.SIZE_Z - 1:
		_enqueue_neighbor_chunk_update(chunk_coords + CHUNK_BACK)


func _enqueue_collision_window(center_coords: Vector3i) -> void:
	if center_coords == ChunkStreamingSystem.INVALID_CENTER_COORDS:
		return
	for offset_x in range(-_streaming.collision_distance, _streaming.collision_distance + 1):
		for offset_z in range(-_streaming.collision_distance, _streaming.collision_distance + 1):
			_request_collision_rebuild(Vector3i(center_coords.x + offset_x, 0, center_coords.z + offset_z))


func _get_chunk_node_from_pool() -> Node3D:
	if not _chunk_node_pool.is_empty():
		return _chunk_node_pool.pop_back()
	var chunk_node: Node3D = CHUNK_SCENE.instantiate()
	add_child(chunk_node)
	return chunk_node


func _store_chunk_data(chunk_coords: Vector3i, chunk_data) -> void:
	_chunk_data_cache[chunk_coords] = chunk_data
	_cache_order.erase(chunk_coords)
	_cache_order.append(chunk_coords)


func _trim_chunk_cache() -> void:
	while _chunk_data_cache.size() > _max_cached_chunks and not _cache_order.is_empty():
		var oldest_coords: Vector3i = _cache_order.pop_front()
		if _loaded_chunks.has(oldest_coords) or _is_chunk_required(oldest_coords):
			_cache_order.append(oldest_coords)
			if _cache_order.size() <= _loaded_chunks.size():
				break
			continue
		_chunk_data_cache.erase(oldest_coords)


func _initialize_chunk_skylight(chunk_data) -> void:
	if chunk_data == null or chunk_data.has_initialized_light_levels():
		return

	chunk_data.clear_light_levels()
	var ignored_queue: Array[Vector3i] = []
	_seed_skylight_for_chunk(chunk_data, ignored_queue)
	chunk_data.mark_light_levels_initialized()


func _recalculate_loaded_chunk_lighting() -> void:
	if _chunk_data_cache.is_empty():
		return

	for chunk_data in _chunk_data_cache.values():
		if chunk_data == null:
			continue
		chunk_data.clear_light_levels()

	var light_queue: Array[Vector3i] = []
	for chunk_data in _chunk_data_cache.values():
		if chunk_data == null:
			continue
		_seed_skylight_for_chunk(chunk_data, light_queue)
		chunk_data.mark_light_levels_initialized()

	var queue_index := 0
	while queue_index < light_queue.size():
		var world_position: Vector3i = light_queue[queue_index]
		queue_index += 1

		var current_light: int = get_light_level_at(world_position)
		if current_light <= 1:
			continue

		for offset in LIGHT_DIRECTIONS:
			var neighbor_position: Vector3i = world_position + offset
			if _is_solid(get_block_id_at(neighbor_position)):
				continue

			var propagated_light := current_light - 1
			if propagated_light <= get_light_level_at(neighbor_position):
				continue

			if _try_set_loaded_light_level(neighbor_position, propagated_light):
				light_queue.append(neighbor_position)


func _seed_skylight_for_chunk(chunk_data, light_queue: Array[Vector3i]) -> void:
	for local_x in range(ChunkDataScript.SIZE_X):
		for local_z in range(ChunkDataScript.SIZE_Z):
			var sky_visible := true
			for local_y in range(ChunkDataScript.SIZE_Y - 1, -1, -1):
				var block_id: int = chunk_data.get_block_at(local_x, local_y, local_z)
				if _is_solid(block_id):
					sky_visible = false
					continue

				if not sky_visible:
					continue

				chunk_data.set_light_level_at(local_x, local_y, local_z, 15)
				light_queue.append(_local_to_world_position(chunk_data, Vector3i(local_x, local_y, local_z)))


func _try_set_loaded_light_level(world_position: Vector3i, light_level: int) -> bool:
	var chunk_coords: Vector3i = _world_to_chunk_coords(world_position)
	var chunk_data = _chunk_data_cache.get(chunk_coords)
	if chunk_data == null:
		return false

	var local_position: Vector3i = _world_to_local_block_position(world_position, chunk_coords)
	if chunk_data.get_light_level(local_position) >= light_level:
		return false

	chunk_data.set_light_level(local_position, light_level)
	return true


func _build_chunk_neighbor_context(chunk_data) -> Dictionary:
	var neighbor_blocks: Dictionary = {}
	var neighbor_lights: Dictionary = {}
	var max_x := ChunkDataScript.SIZE_X - 1
	var max_y := ChunkDataScript.SIZE_Y - 1
	var max_z := ChunkDataScript.SIZE_Z - 1

	for local_y in range(ChunkDataScript.SIZE_Y):
		for local_z in range(ChunkDataScript.SIZE_Z):
			_store_neighbor_sample(chunk_data, Vector3i(-1, local_y, local_z), neighbor_blocks, neighbor_lights)
			_store_neighbor_sample(chunk_data, Vector3i(max_x + 1, local_y, local_z), neighbor_blocks, neighbor_lights)

	for local_x in range(ChunkDataScript.SIZE_X):
		for local_z in range(ChunkDataScript.SIZE_Z):
			_store_neighbor_sample(chunk_data, Vector3i(local_x, -1, local_z), neighbor_blocks, neighbor_lights)
			_store_neighbor_sample(chunk_data, Vector3i(local_x, max_y + 1, local_z), neighbor_blocks, neighbor_lights)

	for local_x in range(ChunkDataScript.SIZE_X):
		for local_y in range(ChunkDataScript.SIZE_Y):
			_store_neighbor_sample(chunk_data, Vector3i(local_x, local_y, -1), neighbor_blocks, neighbor_lights)
			_store_neighbor_sample(chunk_data, Vector3i(local_x, local_y, max_z + 1), neighbor_blocks, neighbor_lights)

	return {
		"neighbor_blocks": neighbor_blocks,
		"neighbor_lights": neighbor_lights
	}


func _store_neighbor_sample(chunk_data, local_position: Vector3i, neighbor_blocks: Dictionary, neighbor_lights: Dictionary) -> void:
	var world_position: Vector3i = _local_to_world_position(chunk_data, local_position)
	neighbor_blocks[local_position] = get_block_id_at(world_position)
	neighbor_lights[local_position] = get_light_level_at(world_position)


func _local_to_world_position(chunk_data, local_position: Vector3i) -> Vector3i:
	return Vector3i(
		chunk_data.chunk_coords.x * ChunkDataScript.SIZE_X + local_position.x,
		chunk_data.chunk_coords.y * ChunkDataScript.SIZE_Y + local_position.y,
		chunk_data.chunk_coords.z * ChunkDataScript.SIZE_Z + local_position.z
	)


func _is_chunk_required(chunk_coords: Vector3i) -> bool:
	return _streaming != null and _streaming.is_required(chunk_coords)


func _is_chunk_required_or_loaded(chunk_coords: Vector3i) -> bool:
	return _is_chunk_required(chunk_coords) or _loaded_chunks.has(chunk_coords)


func _has_chunk_data(chunk_coords: Vector3i) -> bool:
	return _chunk_data_cache.has(chunk_coords)


func _get_chunk_data(chunk_coords: Vector3i):
	return _chunk_data_cache.get(chunk_coords)


func _should_build_collision_for_chunk(chunk_coords: Vector3i) -> bool:
	return _streaming != null and _streaming.is_in_collision_distance(chunk_coords)


func _get_chunk_priority_score(chunk_coords: Vector3i) -> float:
	if _streaming == null:
		return 0.0
	return _streaming.get_priority_score(chunk_coords)


func _get_chunk_state(chunk_coords: Vector3i) -> int:
	return int(_chunk_states.get(chunk_coords, ChunkState.UNLOADED))


func _set_chunk_state(chunk_coords: Vector3i, state: int) -> void:
	if state == ChunkState.UNLOADED:
		_chunk_states.erase(chunk_coords)
	else:
		_chunk_states[chunk_coords] = state


func _is_solid(block_id: int) -> bool:
	return block_id != ChunkDataScript.AIR_BLOCK_ID


func _world_to_chunk_coords(world_position: Vector3i) -> Vector3i:
	return Vector3i(
		floori(float(world_position.x) / float(ChunkDataScript.SIZE_X)),
		floori(float(world_position.y) / float(ChunkDataScript.SIZE_Y)),
		floori(float(world_position.z) / float(ChunkDataScript.SIZE_Z))
	)


func _world_to_local_block_position(world_position: Vector3i, chunk_coords: Vector3i) -> Vector3i:
	return Vector3i(
		world_position.x - chunk_coords.x * ChunkDataScript.SIZE_X,
		world_position.y - chunk_coords.y * ChunkDataScript.SIZE_Y,
		world_position.z - chunk_coords.z * ChunkDataScript.SIZE_Z
	)


func _count_queued_chunks(task_stats: Dictionary, scheduler_stats: Dictionary) -> int:
	return (
		int(task_stats.get("pending_generation", 0))
		+ int(task_stats.get("pending_visual_build", 0))
		+ int(task_stats.get("pending_collision_build", 0))
		+ int(scheduler_stats.get("ready_nodes", 0))
		+ int(scheduler_stats.get("ready_visual_apply", 0))
		+ int(scheduler_stats.get("ready_collision_apply", 0))
		+ int(scheduler_stats.get("pending_neighbor_updates", 0))
		+ int(scheduler_stats.get("pending_unloads", 0))
	)


func _exit_tree() -> void:
	_is_shutting_down = true
	if _task_queue != null:
		_task_queue.shutdown()
