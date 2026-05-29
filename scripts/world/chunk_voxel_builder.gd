class_name ChunkVoxelBuilder
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
	[
		Vector2(0.0, 1.0),
		Vector2(0.0, 0.0),
		Vector2(1.0, 0.0),
		Vector2(1.0, 1.0)
	],
	[
		Vector2(0.0, 1.0),
		Vector2(0.0, 0.0),
		Vector2(1.0, 0.0),
		Vector2(1.0, 1.0)
	],
	[
		Vector2(0.0, 1.0),
		Vector2(1.0, 1.0),
		Vector2(1.0, 0.0),
		Vector2(0.0, 0.0)
	],
	[
		Vector2(0.0, 0.0),
		Vector2(1.0, 0.0),
		Vector2(1.0, 1.0),
		Vector2(0.0, 1.0)
	],
	[
		Vector2(0.0, 1.0),
		Vector2(1.0, 1.0),
		Vector2(1.0, 0.0),
		Vector2(0.0, 0.0)
	],
	[
		Vector2(0.0, 1.0),
		Vector2(1.0, 1.0),
		Vector2(1.0, 0.0),
		Vector2(0.0, 0.0)
	]
]

const FACE_LIGHT_MULTIPLIERS := [
	0.86,
	0.86,
	1.0,
	0.72,
	0.8,
	0.8
]
const COLLISION_SECTION_HEIGHT := 8

var _block_registry
var _atlas
var _solid_cache: Dictionary = {}
var _uv_rect_cache: Dictionary = {}
var _material: StandardMaterial3D


func _init(block_registry, atlas) -> void:
	_block_registry = block_registry
	_atlas = atlas
	_material = StandardMaterial3D.new()
	_material.albedo_texture = _atlas.texture
	_material.texture_filter = BaseMaterial3D.TEXTURE_FILTER_NEAREST
	_material.cull_mode = BaseMaterial3D.CULL_DISABLED
	_material.roughness = 1.0
	_material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	_material.vertex_color_use_as_albedo = true


func duplicate_for_thread() -> ChunkVoxelBuilder:
	return ChunkVoxelBuilder.new(_block_registry, _atlas)


func build_chunk(chunk_data, world_accessor = null, include_collision: bool = true) -> Dictionary:
	var visual_result: Dictionary = build_chunk_visual_layout(chunk_data, world_accessor)
	var collision_result: Dictionary = {}
	if include_collision:
		collision_result = build_chunk_collision_faces(chunk_data, world_accessor)

	return {
		"mesh": create_mesh_from_layout(visual_result),
		"collision_faces": collision_result.get("collision_faces", PackedVector3Array()),
		"stats": {
			"face_count": visual_result.get("stats", {}).get("face_count", 0),
			"vertex_count": visual_result.get("stats", {}).get("vertex_count", 0),
			"triangle_count": visual_result.get("stats", {}).get("triangle_count", 0),
			"collision_triangle_count": collision_result.get("stats", {}).get("collision_triangle_count", 0)
		}
	}


func build_chunk_visual_layout(chunk_data, world_accessor = null) -> Dictionary:
	var vertices: PackedVector3Array = PackedVector3Array()
	var normals: PackedVector3Array = PackedVector3Array()
	var uvs: PackedVector2Array = PackedVector2Array()
	var colors: PackedColorArray = PackedColorArray()
	var indices: PackedInt32Array = PackedInt32Array()
	var rendered_face_count: int = 0
	var next_index: int = 0

	for local_x in range(ChunkDataScript.SIZE_X):
		for local_y in range(ChunkDataScript.SIZE_Y):
			for local_z in range(ChunkDataScript.SIZE_Z):
				var block_id: int = chunk_data.get_block_at(local_x, local_y, local_z)
				if not _is_solid(block_id):
					continue

				for face_index in range(FACE_NORMALS.size()):
					if _is_neighbor_solid_for_face(chunk_data, local_x, local_y, local_z, face_index, world_accessor):
						continue

					next_index = _append_visual_face(
						face_index,
						local_x,
						local_y,
						local_z,
						block_id,
						chunk_data,
						world_accessor,
						vertices,
						normals,
						uvs,
						colors,
						indices,
						next_index
					)
					rendered_face_count += 1

	return {
		"vertices": vertices,
		"normals": normals,
		"uvs": uvs,
		"colors": colors,
		"indices": indices,
		"stats": {
			"face_count": rendered_face_count,
			"vertex_count": rendered_face_count * 4,
			"triangle_count": rendered_face_count * 2
		}
	}


func build_chunk_collision_faces(chunk_data, world_accessor = null) -> Dictionary:
	var collision_sections: Dictionary = {}
	var collision_triangle_count: int = 0

	for local_x in range(ChunkDataScript.SIZE_X):
		for local_y in range(ChunkDataScript.SIZE_Y):
			for local_z in range(ChunkDataScript.SIZE_Z):
				var block_id: int = chunk_data.get_block_at(local_x, local_y, local_z)
				if not _is_solid(block_id):
					continue

				for face_index in range(FACE_NORMALS.size()):
					if _is_neighbor_solid_for_face(chunk_data, local_x, local_y, local_z, face_index, world_accessor):
						continue
					var section_index: int = int(local_y / COLLISION_SECTION_HEIGHT)
					if not collision_sections.has(section_index):
						collision_sections[section_index] = PackedVector3Array()
					_append_collision_face(collision_sections[section_index], local_x, local_y, local_z, face_index)
					collision_triangle_count += 2

	var merged_collision_faces := PackedVector3Array()
	for section_faces in collision_sections.values():
		merged_collision_faces.append_array(section_faces)

	return {
		"collision_faces": merged_collision_faces,
		"collision_sections": collision_sections,
		"stats": {
			"collision_triangle_count": collision_triangle_count,
			"collision_section_count": collision_sections.size()
		}
	}


func create_mesh_from_layout(build_result: Dictionary) -> ArrayMesh:
	var mesh := ArrayMesh.new()
	var vertices: PackedVector3Array = build_result.get("vertices", PackedVector3Array())
	if vertices.is_empty():
		return mesh

	var arrays: Array = []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_NORMAL] = build_result.get("normals", PackedVector3Array())
	arrays[Mesh.ARRAY_TEX_UV] = build_result.get("uvs", PackedVector2Array())
	arrays[Mesh.ARRAY_COLOR] = build_result.get("colors", PackedColorArray())
	arrays[Mesh.ARRAY_INDEX] = build_result.get("indices", PackedInt32Array())
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	mesh.surface_set_material(0, _material)
	return mesh


func _is_neighbor_solid_for_face(chunk_data, local_x: int, local_y: int, local_z: int, face_index: int, world_accessor) -> bool:
	var offset: Vector3i = FACE_NORMALS[face_index]
	return _is_neighbor_solid(chunk_data, local_x + offset.x, local_y + offset.y, local_z + offset.z, world_accessor)


func _append_collision_face(
	collision_faces: PackedVector3Array,
	local_x: int,
	local_y: int,
	local_z: int,
	face_index: int
) -> void:
	var base_position: Vector3 = Vector3(local_x, local_y, local_z)
	var face_vertices: Array = FACE_VERTICES[face_index]
	collision_faces.append(base_position + face_vertices[0])
	collision_faces.append(base_position + face_vertices[1])
	collision_faces.append(base_position + face_vertices[2])
	collision_faces.append(base_position + face_vertices[0])
	collision_faces.append(base_position + face_vertices[2])
	collision_faces.append(base_position + face_vertices[3])


func _append_visual_face(
	face_index: int,
	local_x: int,
	local_y: int,
	local_z: int,
	block_id: int,
	chunk_data,
	world_accessor,
	vertices: PackedVector3Array,
	normals: PackedVector3Array,
	uvs: PackedVector2Array,
	colors: PackedColorArray,
	indices: PackedInt32Array,
	next_index: int
) -> int:
	var base_position: Vector3 = Vector3(local_x, local_y, local_z)
	var face_vertices: Array = FACE_VERTICES[face_index]
	var face_uvs: Array = FACE_UVS[face_index]
	var uv_rect: Rect2 = _get_uv_rect(block_id, face_index)
	var face_normal: Vector3 = Vector3(FACE_NORMALS[face_index])
	var face_light_color: Color = _get_face_light_color(chunk_data, world_accessor, local_x, local_y, local_z, face_index)

	for vertex_index in range(4):
		vertices.append(base_position + face_vertices[vertex_index])
		normals.append(face_normal)
		uvs.append(_remap_uv(face_uvs[vertex_index], uv_rect))
		colors.append(face_light_color)

	indices.append(next_index)
	indices.append(next_index + 1)
	indices.append(next_index + 2)
	indices.append(next_index)
	indices.append(next_index + 2)
	indices.append(next_index + 3)
	return next_index + 4


func _is_neighbor_solid(chunk_data, local_x: int, local_y: int, local_z: int, world_accessor) -> bool:
	if chunk_data.is_in_bounds_at(local_x, local_y, local_z):
		return _is_solid(chunk_data.get_block_at(local_x, local_y, local_z))

	if world_accessor == null:
		return false

	if world_accessor is Dictionary:
		var neighbor_blocks: Dictionary = world_accessor.get("neighbor_blocks", {})
		return _is_solid(int(neighbor_blocks.get(Vector3i(local_x, local_y, local_z), ChunkDataScript.AIR_BLOCK_ID)))

	return _is_solid(world_accessor.get_block_id_at(_to_world_position(chunk_data, local_x, local_y, local_z)))


func _is_solid(block_id: int) -> bool:
	if not _solid_cache.has(block_id):
		_solid_cache[block_id] = _block_registry.is_solid(block_id)
	return _solid_cache[block_id]


func _get_uv_rect(block_id: int, face_index: int) -> Rect2:
	var cache_key: int = block_id * 6 + face_index
	if _uv_rect_cache.has(cache_key):
		return _uv_rect_cache[cache_key]

	var definition: BlockDefinition = _block_registry.get_definition(block_id)
	var uv_rect: Rect2 = _atlas.get_uv_rect(definition.get_texture_path_for_face(FACE_NORMALS[face_index]))
	_uv_rect_cache[cache_key] = uv_rect
	return uv_rect


func _remap_uv(base_uv: Vector2, uv_rect: Rect2) -> Vector2:
	return uv_rect.position + Vector2(base_uv.x * uv_rect.size.x, base_uv.y * uv_rect.size.y)


func _get_face_light_color(chunk_data, world_accessor, local_x: int, local_y: int, local_z: int, face_index: int) -> Color:
	var face_offset: Vector3i = FACE_NORMALS[face_index]
	var light_level: int = _get_light_level(chunk_data, world_accessor, local_x + face_offset.x, local_y + face_offset.y, local_z + face_offset.z)
	var brightness: float = lerpf(0.06, 1.0, float(light_level) / 15.0) * float(FACE_LIGHT_MULTIPLIERS[face_index])
	brightness = clampf(brightness, 0.0, 1.0)
	return Color(brightness, brightness, brightness, 1.0)


func _get_light_level(chunk_data, world_accessor, local_x: int, local_y: int, local_z: int) -> int:
	if chunk_data.is_in_bounds_at(local_x, local_y, local_z):
		return chunk_data.get_light_level_at(local_x, local_y, local_z)

	if world_accessor == null:
		return 0

	if world_accessor is Dictionary:
		var neighbor_lights: Dictionary = world_accessor.get("neighbor_lights", {})
		return int(neighbor_lights.get(Vector3i(local_x, local_y, local_z), 0))

	return world_accessor.get_light_level_at(_to_world_position(chunk_data, local_x, local_y, local_z))


func _to_world_position(chunk_data, local_x: int, local_y: int, local_z: int) -> Vector3i:
	var world_origin: Vector3 = chunk_data.get_world_origin()
	return Vector3i(
		int(world_origin.x) + local_x,
		int(world_origin.y) + local_y,
		int(world_origin.z) + local_z
	)
