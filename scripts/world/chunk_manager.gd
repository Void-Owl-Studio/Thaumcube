extends Node3D

const CHUNK_SCENE := preload("res://scenes/world/chunk.tscn")
const ChunkDataScript := preload("res://scripts/world/chunk_data.gd")

var _generator
var _mesh_builder
var _loaded_chunks: Dictionary = {}
var _load_radius := 1


func configure(generator, mesh_builder, load_radius: int) -> void:
	_generator = generator
	_mesh_builder = mesh_builder
	_load_radius = max(0, load_radius)


func sync_chunks_around(world_position: Vector3) -> void:
	if _generator == null or _mesh_builder == null:
		return

	var center_coords := Vector3i(
		floori(world_position.x / ChunkDataScript.SIZE_X),
		0,
		floori(world_position.z / ChunkDataScript.SIZE_Z)
	)
	var required_chunks: Dictionary = {}

	for offset_x in range(-_load_radius, _load_radius + 1):
		for offset_z in range(-_load_radius, _load_radius + 1):
			var chunk_coords := Vector3i(center_coords.x + offset_x, 0, center_coords.z + offset_z)
			required_chunks[chunk_coords] = true

			if not _loaded_chunks.has(chunk_coords):
				_create_chunk(chunk_coords)

	for existing_coords in _loaded_chunks.keys():
		if required_chunks.has(existing_coords):
			continue

		var chunk_node: Node3D = _loaded_chunks[existing_coords]
		remove_child(chunk_node)
		chunk_node.queue_free()
		_loaded_chunks.erase(existing_coords)


func _create_chunk(chunk_coords: Vector3i) -> void:
	var chunk_data = _generator.generate(chunk_coords)
	var chunk_node = CHUNK_SCENE.instantiate()
	add_child(chunk_node)
	chunk_node.apply_chunk_data(chunk_data, _mesh_builder)
	_loaded_chunks[chunk_coords] = chunk_node
