extends StaticBody3D

const ChunkDataScript := preload("res://scripts/world/chunk_data.gd")

@onready var _mesh_instance: MeshInstance3D = $Mesh
@onready var _collision_shape: CollisionShape3D = $Collision

var chunk_coords: Vector3i
var chunk_data


func apply_chunk_data(chunk_data, mesh_builder, generator = null) -> void:
	self.chunk_data = chunk_data
	chunk_coords = chunk_data.chunk_coords
	position = chunk_data.get_world_origin()

	rebuild_mesh(mesh_builder, generator)


func rebuild_mesh(mesh_builder, generator = null) -> void:
	if chunk_data == null:
		return

	var mesh: ArrayMesh = mesh_builder.build_mesh(chunk_data, generator)
	_mesh_instance.mesh = mesh

	if mesh.get_surface_count() == 0:
		_collision_shape.shape = null
		return

	_collision_shape.shape = mesh.create_trimesh_shape()
