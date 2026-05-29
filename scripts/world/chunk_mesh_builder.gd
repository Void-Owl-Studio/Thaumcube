class_name ChunkMeshBuilder
extends RefCounted

const ChunkDataScript := preload("res://scripts/world/chunk_data.gd")

const FACE_RIGHT := 0
const FACE_LEFT := 1
const FACE_UP := 2
const FACE_DOWN := 3
const FACE_BACK := 4
const FACE_FORWARD := 5

const FACE_NORMALS := [
	Vector3i.RIGHT,
	Vector3i.LEFT,
	Vector3i.UP,
	Vector3i.DOWN,
	Vector3i.BACK,
	Vector3i.FORWARD
]

const FACE_VERTICES := [
	[
		Vector3(1, 0, 0),
		Vector3(1, 1, 0),
		Vector3(1, 1, 1),
		Vector3(1, 0, 1)
	],
	[
		Vector3(0, 0, 1),
		Vector3(0, 1, 1),
		Vector3(0, 1, 0),
		Vector3(0, 0, 0)
	],
	[
		Vector3(0, 1, 1),
		Vector3(1, 1, 1),
		Vector3(1, 1, 0),
		Vector3(0, 1, 0)
	],
	[
		Vector3(0, 0, 0),
		Vector3(1, 0, 0),
		Vector3(1, 0, 1),
		Vector3(0, 0, 1)
	],
	[
		Vector3(0, 0, 1),
		Vector3(1, 0, 1),
		Vector3(1, 1, 1),
		Vector3(0, 1, 1)
	],
	[
		Vector3(1, 0, 0),
		Vector3(0, 0, 0),
		Vector3(0, 1, 0),
		Vector3(1, 1, 0)
	]
]

const FACE_UVS := [
	Vector2(0.0, 1.0),
	Vector2(0.0, 0.0),
	Vector2(1.0, 0.0),
	Vector2(1.0, 1.0)
]

var _block_registry
var _atlas
var _material: StandardMaterial3D
var _solid_cache: Dictionary = {}
var _uv_rect_cache: Dictionary = {}


func _init(block_registry, atlas) -> void:
	_block_registry = block_registry
	_atlas = atlas
	_material = StandardMaterial3D.new()
	_material.albedo_texture = _atlas.texture
	_material.texture_filter = BaseMaterial3D.TEXTURE_FILTER_NEAREST
	_material.cull_mode = BaseMaterial3D.CULL_DISABLED
	_material.roughness = 1.0


func build_mesh(chunk_data, generator = null) -> ArrayMesh:
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var uvs := PackedVector2Array()
	var indices := PackedInt32Array()
	var next_index := 0

	for local_x in range(ChunkDataScript.SIZE_X):
		for local_y in range(ChunkDataScript.SIZE_Y):
			for local_z in range(ChunkDataScript.SIZE_Z):
				var block_id: int = chunk_data.get_block_at(local_x, local_y, local_z)
				if not _is_solid(block_id):
					continue

				if not _is_neighbor_solid(chunk_data, local_x + 1, local_y, local_z, generator):
					next_index = _append_face(FACE_RIGHT, local_x, local_y, local_z, block_id, vertices, normals, uvs, indices, next_index)
				if not _is_neighbor_solid(chunk_data, local_x - 1, local_y, local_z, generator):
					next_index = _append_face(FACE_LEFT, local_x, local_y, local_z, block_id, vertices, normals, uvs, indices, next_index)
				if not _is_neighbor_solid(chunk_data, local_x, local_y + 1, local_z, generator):
					next_index = _append_face(FACE_UP, local_x, local_y, local_z, block_id, vertices, normals, uvs, indices, next_index)
				if not _is_neighbor_solid(chunk_data, local_x, local_y - 1, local_z, generator):
					next_index = _append_face(FACE_DOWN, local_x, local_y, local_z, block_id, vertices, normals, uvs, indices, next_index)
				if not _is_neighbor_solid(chunk_data, local_x, local_y, local_z + 1, generator):
					next_index = _append_face(FACE_BACK, local_x, local_y, local_z, block_id, vertices, normals, uvs, indices, next_index)
				if not _is_neighbor_solid(chunk_data, local_x, local_y, local_z - 1, generator):
					next_index = _append_face(FACE_FORWARD, local_x, local_y, local_z, block_id, vertices, normals, uvs, indices, next_index)

	var mesh := ArrayMesh.new()
	if vertices.is_empty():
		return mesh

	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_INDEX] = indices
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	mesh.surface_set_material(0, _material)
	return mesh


func _is_neighbor_solid(chunk_data, local_x: int, local_y: int, local_z: int, generator) -> bool:
	if chunk_data.is_in_bounds_at(local_x, local_y, local_z):
		return _is_solid(chunk_data.get_block_at(local_x, local_y, local_z))

	if generator == null:
		return false

	var world_origin: Vector3 = chunk_data.get_world_origin()
	var world_position := Vector3i(
		int(world_origin.x) + local_x,
		int(world_origin.y) + local_y,
		int(world_origin.z) + local_z
	)
	return _is_solid(generator.get_block_id_at(world_position))


func _append_face(
	face_index: int,
	local_x: int,
	local_y: int,
	local_z: int,
	block_id: int,
	vertices: PackedVector3Array,
	normals: PackedVector3Array,
	uvs: PackedVector2Array,
	indices: PackedInt32Array,
	next_index: int
) -> int:
	var base_position := Vector3(local_x, local_y, local_z)
	var face_vertices: Array = FACE_VERTICES[face_index]
	var face_normal := Vector3(FACE_NORMALS[face_index])
	var uv_rect: Rect2 = _get_uv_rect(block_id, face_index)

	for vertex_index in 4:
		vertices.append(base_position + face_vertices[vertex_index])
		normals.append(face_normal)
		uvs.append(_remap_uv(FACE_UVS[vertex_index], uv_rect))

	indices.append(next_index)
	indices.append(next_index + 1)
	indices.append(next_index + 2)
	indices.append(next_index)
	indices.append(next_index + 2)
	indices.append(next_index + 3)
	return next_index + 4


func _is_solid(block_id: int) -> bool:
	if not _solid_cache.has(block_id):
		_solid_cache[block_id] = _block_registry.is_solid(block_id)
	return _solid_cache[block_id]


func _get_uv_rect(block_id: int, face_index: int) -> Rect2:
	var cache_key := block_id * 6 + face_index
	if _uv_rect_cache.has(cache_key):
		return _uv_rect_cache[cache_key]

	var definition = _block_registry.get_definition(block_id)
	var uv_rect = _atlas.get_uv_rect(definition.get_texture_path_for_face(FACE_NORMALS[face_index]))
	_uv_rect_cache[cache_key] = uv_rect
	return uv_rect


func _remap_uv(base_uv: Vector2, uv_rect: Rect2) -> Vector2:
	return uv_rect.position + Vector2(base_uv.x * uv_rect.size.x, base_uv.y * uv_rect.size.y)
