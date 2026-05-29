class_name ChunkTaskQueue
extends RefCounted

const ChunkDataScript := preload("res://scripts/world/chunk_data.gd")

enum TaskKind {
	GENERATION,
	VISUAL,
	COLLISION
}

var max_worker_jobs: int = 1
var generation_starts_per_frame: int = 1
var visual_builds_per_frame: int = 1
var collision_builds_per_frame: int = 1

var _pending_generation: Array[Vector3i] = []
var _pending_visual: Array[Vector3i] = []
var _pending_collision: Array[Vector3i] = []

var _pending_generation_lookup: Dictionary = {}
var _pending_visual_lookup: Dictionary = {}
var _pending_collision_lookup: Dictionary = {}

var _active_generation: Dictionary = {}
var _active_visual: Dictionary = {}
var _active_collision: Dictionary = {}

var _generation_versions: Dictionary = {}
var _visual_versions: Dictionary = {}
var _collision_versions: Dictionary = {}

var _completed_generation_results: Array[Dictionary] = []
var _completed_visual_results: Array[Dictionary] = []
var _completed_collision_results: Array[Dictionary] = []

var _results_mutex := Mutex.new()
var _is_shutting_down: bool = false


func configure(
	next_max_worker_jobs: int,
	next_generation_starts_per_frame: int,
	next_visual_builds_per_frame: int,
	next_collision_builds_per_frame: int
) -> void:
	max_worker_jobs = max(1, next_max_worker_jobs)
	generation_starts_per_frame = max(1, next_generation_starts_per_frame)
	visual_builds_per_frame = max(1, next_visual_builds_per_frame)
	collision_builds_per_frame = max(1, next_collision_builds_per_frame)


func request_generation(chunk_coords: Vector3i) -> void:
	if _pending_generation_lookup.has(chunk_coords) or _active_generation.has(chunk_coords):
		return
	_generation_versions[chunk_coords] = int(_generation_versions.get(chunk_coords, 0)) + 1
	_pending_generation_lookup[chunk_coords] = true
	_pending_generation.append(chunk_coords)


func request_visual(chunk_coords: Vector3i) -> void:
	_visual_versions[chunk_coords] = int(_visual_versions.get(chunk_coords, 0)) + 1
	if _pending_visual_lookup.has(chunk_coords) or _active_visual.has(chunk_coords):
		return
	_pending_visual_lookup[chunk_coords] = true
	_pending_visual.append(chunk_coords)


func request_collision(chunk_coords: Vector3i) -> void:
	_collision_versions[chunk_coords] = int(_collision_versions.get(chunk_coords, 0)) + 1
	if _pending_collision_lookup.has(chunk_coords) or _active_collision.has(chunk_coords):
		return
	_pending_collision_lookup[chunk_coords] = true
	_pending_collision.append(chunk_coords)


func invalidate_chunk(chunk_coords: Vector3i) -> void:
	_generation_versions[chunk_coords] = int(_generation_versions.get(chunk_coords, 0)) + 1
	_visual_versions[chunk_coords] = int(_visual_versions.get(chunk_coords, 0)) + 1
	_collision_versions[chunk_coords] = int(_collision_versions.get(chunk_coords, 0)) + 1
	_pending_generation_lookup.erase(chunk_coords)
	_pending_visual_lookup.erase(chunk_coords)
	_pending_collision_lookup.erase(chunk_coords)
	_pending_generation.erase(chunk_coords)
	_pending_visual.erase(chunk_coords)
	_pending_collision.erase(chunk_coords)


func cancel_not_required(is_required_callback: Callable) -> void:
	_filter_pending(_pending_generation, _pending_generation_lookup, is_required_callback)
	_filter_pending(_pending_visual, _pending_visual_lookup, is_required_callback)
	_filter_pending(_pending_collision, _pending_collision_lookup, is_required_callback)

	for chunk_coords in _active_generation.keys():
		if not bool(is_required_callback.call(chunk_coords)):
			_generation_versions[chunk_coords] = int(_generation_versions.get(chunk_coords, 0)) + 1

	for chunk_coords in _active_visual.keys():
		if not bool(is_required_callback.call(chunk_coords)):
			_visual_versions[chunk_coords] = int(_visual_versions.get(chunk_coords, 0)) + 1

	for chunk_coords in _active_collision.keys():
		if not bool(is_required_callback.call(chunk_coords)):
			_collision_versions[chunk_coords] = int(_collision_versions.get(chunk_coords, 0)) + 1


func prioritize(priority_callback: Callable) -> void:
	_pending_generation.sort_custom(func(a: Vector3i, b: Vector3i) -> bool:
		return float(priority_callback.call(a)) < float(priority_callback.call(b))
	)
	_pending_visual.sort_custom(func(a: Vector3i, b: Vector3i) -> bool:
		return float(priority_callback.call(a)) < float(priority_callback.call(b))
	)
	_pending_collision.sort_custom(func(a: Vector3i, b: Vector3i) -> bool:
		return float(priority_callback.call(a)) < float(priority_callback.call(b))
	)


func collect_completed_generation() -> Array[Dictionary]:
	_collect_completed_tasks(_active_generation)
	return _drain_completed_results(_completed_generation_results)


func collect_completed_visual() -> Array[Dictionary]:
	_collect_completed_tasks(_active_visual)
	return _drain_completed_results(_completed_visual_results)


func collect_completed_collision() -> Array[Dictionary]:
	_collect_completed_tasks(_active_collision)
	return _drain_completed_results(_completed_collision_results)


func start_generation_tasks(generator, is_required_callback: Callable, has_chunk_data_callback: Callable) -> int:
	if _is_shutting_down:
		return 0

	var started := 0
	while (
		started < generation_starts_per_frame
		and get_active_job_count() < max_worker_jobs
		and not _pending_generation.is_empty()
	):
		var chunk_coords: Vector3i = _pending_generation.pop_front()
		_pending_generation_lookup.erase(chunk_coords)
		if not bool(is_required_callback.call(chunk_coords)):
			continue
		if bool(has_chunk_data_callback.call(chunk_coords)):
			continue

		var version := int(_generation_versions.get(chunk_coords, 0))
		var generator_copy = generator.duplicate_for_thread()
		var task_id := WorkerThreadPool.add_task(
			_run_generation_task.bind(generator_copy, chunk_coords, version),
			false,
			"chunk_generation_%s" % [chunk_coords]
		)
		_active_generation[chunk_coords] = {
			"task_id": task_id,
			"version": version
		}
		started += 1
	return started


func start_visual_tasks(
	voxel_builder,
	is_required_callback: Callable,
	get_chunk_data_callback: Callable,
	build_context_callback: Callable
) -> int:
	if _is_shutting_down:
		return 0

	var started := 0
	while (
		started < visual_builds_per_frame
		and get_active_job_count() < max_worker_jobs
		and not _pending_visual.is_empty()
	):
		var chunk_coords: Vector3i = _pending_visual.pop_front()
		_pending_visual_lookup.erase(chunk_coords)
		if not bool(is_required_callback.call(chunk_coords)):
			continue

		var chunk_data = get_chunk_data_callback.call(chunk_coords)
		if chunk_data == null:
			continue

		var version := int(_visual_versions.get(chunk_coords, 0))
		var builder_copy = voxel_builder.duplicate_for_thread()
		var chunk_data_snapshot = chunk_data
		if chunk_data.has_method("duplicate_for_thread"):
			chunk_data_snapshot = chunk_data.duplicate_for_thread()
		var neighbor_context: Dictionary = build_context_callback.call(chunk_data_snapshot)
		var task_id := WorkerThreadPool.add_task(
			_run_visual_task.bind(builder_copy, chunk_coords, chunk_data_snapshot, neighbor_context, version),
			false,
			"chunk_visual_%s" % [chunk_coords]
		)
		_active_visual[chunk_coords] = {
			"task_id": task_id,
			"version": version
		}
		started += 1
	return started


func start_collision_tasks(
	voxel_builder,
	is_required_callback: Callable,
	get_chunk_data_callback: Callable,
	build_context_callback: Callable,
	should_build_collision_callback: Callable
) -> int:
	if _is_shutting_down:
		return 0

	var started := 0
	while (
		started < collision_builds_per_frame
		and get_active_job_count() < max_worker_jobs
		and not _pending_collision.is_empty()
	):
		var chunk_coords: Vector3i = _pending_collision.pop_front()
		_pending_collision_lookup.erase(chunk_coords)
		if not bool(is_required_callback.call(chunk_coords)):
			continue

		var chunk_data = get_chunk_data_callback.call(chunk_coords)
		if chunk_data == null:
			continue

		var version := int(_collision_versions.get(chunk_coords, 0))
		if not bool(should_build_collision_callback.call(chunk_coords)):
			_push_completed_result(_completed_collision_results, {
				"chunk_coords": chunk_coords,
				"version": version,
				"collision_faces": PackedVector3Array(),
				"collision_sections": {},
				"collision_enabled": false,
				"collision_build_usec": 0,
				"stats": {
					"collision_triangle_count": 0,
					"collision_section_count": 0
				}
			})
			started += 1
			continue

		var builder_copy = voxel_builder.duplicate_for_thread()
		var chunk_data_snapshot = chunk_data
		if chunk_data.has_method("duplicate_for_thread"):
			chunk_data_snapshot = chunk_data.duplicate_for_thread()
		var neighbor_context: Dictionary = build_context_callback.call(chunk_data_snapshot)
		var task_id := WorkerThreadPool.add_task(
			_run_collision_task.bind(builder_copy, chunk_coords, chunk_data_snapshot, neighbor_context, version),
			false,
			"chunk_collision_%s" % [chunk_coords]
		)
		_active_collision[chunk_coords] = {
			"task_id": task_id,
			"version": version
		}
		started += 1
	return started


func get_generation_version(chunk_coords: Vector3i) -> int:
	return int(_generation_versions.get(chunk_coords, 0))


func get_visual_version(chunk_coords: Vector3i) -> int:
	return int(_visual_versions.get(chunk_coords, 0))


func get_collision_version(chunk_coords: Vector3i) -> int:
	return int(_collision_versions.get(chunk_coords, 0))


func get_active_job_count() -> int:
	return _active_generation.size() + _active_visual.size() + _active_collision.size()


func get_stats() -> Dictionary:
	return {
		"pending_generation": _pending_generation.size(),
		"active_generation": _active_generation.size(),
		"pending_visual_build": _pending_visual.size(),
		"active_visual_build": _active_visual.size(),
		"pending_collision_build": _pending_collision.size(),
		"active_collision_build": _active_collision.size(),
		"active_jobs": get_active_job_count()
	}


func shutdown() -> void:
	_is_shutting_down = true
	_wait_for_active_tasks_to_finish(_active_generation)
	_wait_for_active_tasks_to_finish(_active_visual)
	_wait_for_active_tasks_to_finish(_active_collision)
	_active_generation.clear()
	_active_visual.clear()
	_active_collision.clear()


func _run_generation_task(generator_copy, chunk_coords: Vector3i, version: int) -> void:
	var started_usec := Time.get_ticks_usec()
	var chunk_data = generator_copy.generate(chunk_coords)
	_push_completed_result(_completed_generation_results, {
		"chunk_coords": chunk_coords,
		"version": version,
		"chunk_data": chunk_data,
		"generation_usec": Time.get_ticks_usec() - started_usec
	})


func _run_visual_task(builder_copy, chunk_coords: Vector3i, chunk_data, neighbor_context: Dictionary, version: int) -> void:
	var started_usec := Time.get_ticks_usec()
	var build_result: Dictionary = builder_copy.build_chunk_visual_layout(chunk_data, neighbor_context)
	build_result["chunk_coords"] = chunk_coords
	build_result["version"] = version
	build_result["visual_build_usec"] = Time.get_ticks_usec() - started_usec
	_push_completed_result(_completed_visual_results, build_result)


func _run_collision_task(builder_copy, chunk_coords: Vector3i, chunk_data, neighbor_context: Dictionary, version: int) -> void:
	var started_usec := Time.get_ticks_usec()
	var build_result: Dictionary = builder_copy.build_chunk_collision_faces(chunk_data, neighbor_context)
	build_result["chunk_coords"] = chunk_coords
	build_result["version"] = version
	build_result["collision_enabled"] = true
	build_result["collision_build_usec"] = Time.get_ticks_usec() - started_usec
	_push_completed_result(_completed_collision_results, build_result)


func _collect_completed_tasks(active_tasks: Dictionary) -> void:
	for chunk_coords in active_tasks.keys():
		var task: Dictionary = active_tasks[chunk_coords]
		var task_id := int(task.get("task_id", -1))
		if task_id >= 0 and WorkerThreadPool.is_task_completed(task_id):
			WorkerThreadPool.wait_for_task_completion(task_id)
			active_tasks.erase(chunk_coords)


func _push_completed_result(target: Array[Dictionary], result: Dictionary) -> void:
	_results_mutex.lock()
	target.append(result)
	_results_mutex.unlock()


func _drain_completed_results(target: Array[Dictionary]) -> Array[Dictionary]:
	_results_mutex.lock()
	var results: Array[Dictionary] = target.duplicate(true)
	target.clear()
	_results_mutex.unlock()
	return results


func _wait_for_active_tasks_to_finish(active_tasks: Dictionary) -> void:
	for task in active_tasks.values():
		var task_id := int(task.get("task_id", -1))
		if task_id >= 0:
			WorkerThreadPool.wait_for_task_completion(task_id)


func _filter_pending(queue: Array[Vector3i], lookup: Dictionary, is_required_callback: Callable) -> void:
	var kept: Array[Vector3i] = []
	lookup.clear()
	for chunk_coords in queue:
		if bool(is_required_callback.call(chunk_coords)):
			kept.append(chunk_coords)
			lookup[chunk_coords] = true
	queue.clear()
	for chunk_coords in kept:
		queue.append(chunk_coords)
