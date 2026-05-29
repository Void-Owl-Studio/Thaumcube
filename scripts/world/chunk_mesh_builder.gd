class_name ChunkMeshBuilder
extends RefCounted

const ChunkDataScript := preload("res://scripts/world/chunk_data.gd")

const FACE_DEFINITIONS := [
	{
		"normal": Vector3i.RIGHT,
		"neighbor_offset": Vector3i.RIGHT,
		"vertices": [
			Vector3(1, 0, 0),
			Vector3(1, 1, 0),
			Vector3(1, 1, 1),
			Vector3(1, 0, 1)
		]
	},
	{
		"normal": Vector3i.LEFT,
		"neighbor_offset": Vector3i.LEFT,
		"vertices": [
			Vector3(0, 0, 1),
			Vector3(0, 1, 1),
			Vector3(0, 1, 0),
			Vector3(0, 0, 0)
		]
	},
	{
		"normal": Vector3i.UP,
		"neighbor_offset": Vector3i.UP,
		"vertices": [
			Vector3(0, 1, 1),
			Vector3(1, 1, 1),
			Vector3(1, 1, 0),
			Vector3(0, 1, 0)
		]
	},
	{
		"normal": Vector3i.DOWN,
		"neighbor_offset": Vector3i.DOWN,
		"vertices": [
			Vector3(0, 0, 0),
			Vector3(1, 0, 0),
			Vector3(1, 0, 1),
			Vector3(0, 0, 1)
		]
	},
	{
		"normal": Vector3i.FORWARD,
		"neighbor_offset": Vector3i.FORWARD,
		"vertices": [
			Vector3(0, 0, 1),
			Vector3(1, 0, 1),
			Vector3(1, 1, 1),
			Vector3(0, 1, 1)
		]
	},
	{
		"normal": Vector3i.BACK,
		"neighbor_offset": Vector3i.BACK,
		"vertices": [
			Vector3(1, 0, 0),
			Vector3(0, 0, 0),
			Vector3(0, 1, 0),
			Vector3(1, 1, 0)
		]
	}
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


func _init(block_registry, atlas) -> void:
	_block_registry = block_registry
	_atlas = atlas
	_material = StandardMaterial3D.new()
	_material.albedo_texture = _atlas.texture
	_material.texture_filter = BaseMaterial3D.TEXTURE_FILTER_NEAREST
	_material.roughness = 1.0


func build_mesh(chunk_data) -> ArrayMesh:
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var uvs := PackedVector2Array()
	var indices := PackedInt32Array()
	var next_index := 0

	for local_x in range(ChunkDataScript.SIZE_X):
		for local_y in range(ChunkDataScript.SIZE_Y):
			for local_z in range(ChunkDataScript.SIZE_Z):
				var local_position := Vector3i(local_x, local_y, local_z)
				var block_id = chunk_data.get_block(local_position)
				if not _block_registry.is_solid(block_id):
					continue

				var definition = _block_registry.get_definition(block_id)
				for face_definition in FACE_DEFINITIONS:
					var neighbor_position: Vector3i = local_position + face_definition.neighbor_offset
					if _block_registry.is_solid(chunk_data.get_block(neighbor_position)):
						continue

					var face_normal: Vector3i = face_definition.normal
					var uv_rect = _atlas.get_uv_rect(definition.get_texture_path_for_face(face_normal))
					var base_position := Vector3(local_x, local_y, local_z)

					for vertex_index in 4:
						vertices.append(base_position + face_definition.vertices[vertex_index])
						normals.append(Vector3(face_normal))
						uvs.append(_remap_uv(FACE_UVS[vertex_index], uv_rect))

					indices.append_array([
						next_index,
						next_index + 1,
						next_index + 2,
						next_index,
						next_index + 2,
						next_index + 3
					])
					next_index += 4

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


func _remap_uv(base_uv: Vector2, uv_rect: Rect2) -> Vector2:
	return uv_rect.position + Vector2(base_uv.x * uv_rect.size.x, base_uv.y * uv_rect.size.y)
