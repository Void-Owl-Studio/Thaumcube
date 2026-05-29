extends Node3D

const CHUNK_SCENE := preload("res://scenes/world/chunk.tscn")
const ChunkDataScript := preload("res://scripts/world/chunk_data.gd")

const CHUNK_LEFT := Vector3i.LEFT
const CHUNK_RIGHT := Vector3i.RIGHT
const CHUNK_FORWARD := Vector3i.FORWARD
const CHUNK_BACK := Vector3i.BACK

var _generator
var _mesh_builder
var _loaded_chunks: Dictionary = {}
var _required_chunks: Dictionary = {}
var _pending_create_chunks: Array[Vector3i] = []
var _pending_create_lookup: Dictionary = {}
var _pending_rebuild_chunks: Array[Vector3i] = []
var _pending_rebuild_lookup: Dictionary = {}
var _load_radius := 1
var _chunk_jobs_per_frame := 1
var _last_center_coords := Vector3i(2147483647, 2147483647, 2147483647)


func configure(generator, mesh_builder, load_radius: int, chunk_jobs_per_frame: int = 1) -> void:
	_generator = generator
	_mesh_builder = mesh_builder
	_load_radius = max(0, load_radius)
	_chunk_jobs_per_frame = max(1, chunk_jobs_per_frame)


func sync_chunks_around(world_position: Vector3) -> void:
	if _generator == null or _mesh_builder == null:
		return

	var center_coords := Vector3i(
		floori(world_position.x / ChunkDataScript.SIZE_X),
		0,
		floori(world_position.z / ChunkDataScript.SIZE_Z)
	)

	if center_coords != _last_center_coords:
		_last_center_coords = center_coords
		_refresh_required_chunks(center_coords)

	_process_chunk_jobs()


func _refresh_required_chunks(center_coords: Vector3i) -> void:
	_required_chunks.clear()
	var chunks_to_create: Array[Vector3i] = []

	for offset_x in range(-_load_radius, _load_radius + 1):
		for offset_z in range(-_load_radius, _load_radius + 1):
			var chunk_coords := Vector3i(center_coords.x + offset_x, 0, center_coords.z + offset_z)
			_required_chunks[chunk_coords] = true

			if not _loaded_chunks.has(chunk_coords):
				chunks_to_create.append(chunk_coords)

	chunks_to_create.sort_custom(func(a: Vector3i, b: Vector3i) -> bool:
		return _chunk_distance_squared(a, center_coords) < _chunk_distance_squared(b, center_coords)
	)
	for chunk_coords in chunks_to_create:
		_enqueue_chunk_create(chunk_coords)

	for existing_coords in _loaded_chunks.keys():
		if _required_chunks.has(existing_coords):
			continue

		var chunk_node: Node3D = _loaded_chunks[existing_coords]
		remove_child(chunk_node)
		chunk_node.queue_free()
		_loaded_chunks.erase(existing_coords)
		_pending_rebuild_lookup.erase(existing_coords)


func set_block_at_world(world_position: Vector3i, block_id: int) -> bool:
	var chunk_coords := _world_to_chunk_coords(world_position)
	var chunk_node = _loaded_chunks.get(chunk_coords)
	if chunk_node == null or chunk_node.chunk_data == null:
		return false

	var local_position := _world_to_local_block_position(world_position, chunk_coords)
	if chunk_node.chunk_data.get_block(local_position) == block_id:
		return false

	chunk_node.chunk_data.set_block(local_position, block_id)
	_enqueue_chunk_rebuild(chunk_coords)
	_enqueue_neighbor_rebuilds_for_border(local_position, chunk_coords)
	_process_chunk_jobs()
	return true


func get_block_id_at(world_position: Vector3i) -> int:
	var chunk_coords := _world_to_chunk_coords(world_position)
	var chunk_node = _loaded_chunks.get(chunk_coords)
	if chunk_node != null and chunk_node.chunk_data != null:
		var local_position := _world_to_local_block_position(world_position, chunk_coords)
		return chunk_node.chunk_data.get_block(local_position)

	if _generator != null:
		return _generator.get_block_id_at(world_position)

	return ChunkDataScript.AIR_BLOCK_ID


func _create_chunk(chunk_coords: Vector3i) -> void:
	var chunk_data = _generator.generate(chunk_coords)
	var chunk_node = CHUNK_SCENE.instantiate()
	add_child(chunk_node)
	chunk_node.apply_chunk_data(chunk_data, _mesh_builder, self)
	_loaded_chunks[chunk_coords] = chunk_node


func _enqueue_chunk_create(chunk_coords: Vector3i) -> void:
	if _loaded_chunks.has(chunk_coords) or _pending_create_lookup.has(chunk_coords):
		return

	_pending_create_lookup[chunk_coords] = true
	_pending_create_chunks.append(chunk_coords)


func _enqueue_chunk_rebuild(chunk_coords: Vector3i) -> void:
	if not _loaded_chunks.has(chunk_coords) or _pending_rebuild_lookup.has(chunk_coords):
		return

	_pending_rebuild_lookup[chunk_coords] = true
	_pending_rebuild_chunks.append(chunk_coords)


func _process_chunk_jobs() -> void:
	var jobs_done := 0
	while jobs_done < _chunk_jobs_per_frame and not _pending_rebuild_chunks.is_empty():
		var chunk_coords: Vector3i = _pending_rebuild_chunks.pop_front()
		_pending_rebuild_lookup.erase(chunk_coords)
		if not _loaded_chunks.has(chunk_coords):
			continue

		_rebuild_chunk(chunk_coords)
		jobs_done += 1

	while jobs_done < _chunk_jobs_per_frame and not _pending_create_chunks.is_empty():
		var chunk_coords: Vector3i = _pending_create_chunks.pop_front()
		_pending_create_lookup.erase(chunk_coords)
		if not _required_chunks.has(chunk_coords) or _loaded_chunks.has(chunk_coords):
			continue

		_create_chunk(chunk_coords)
		jobs_done += 1


func _rebuild_chunk(chunk_coords: Vector3i) -> void:
	var chunk_node = _loaded_chunks.get(chunk_coords)
	if chunk_node == null:
		return
	chunk_node.rebuild_mesh(_mesh_builder, self)


func _enqueue_neighbor_rebuilds_for_border(local_position: Vector3i, chunk_coords: Vector3i) -> void:
	if local_position.x == 0:
		_enqueue_chunk_rebuild(chunk_coords + CHUNK_LEFT)
	elif local_position.x == ChunkDataScript.SIZE_X - 1:
		_enqueue_chunk_rebuild(chunk_coords + CHUNK_RIGHT)

	if local_position.z == 0:
		_enqueue_chunk_rebuild(chunk_coords + CHUNK_FORWARD)
	elif local_position.z == ChunkDataScript.SIZE_Z - 1:
		_enqueue_chunk_rebuild(chunk_coords + CHUNK_BACK)


func _chunk_distance_squared(chunk_coords: Vector3i, center_coords: Vector3i) -> int:
	var dx := chunk_coords.x - center_coords.x
	var dz := chunk_coords.z - center_coords.z
	return dx * dx + dz * dz


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
