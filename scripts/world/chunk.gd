extends StaticBody3D

const ChunkDataScript := preload("res://scripts/world/chunk_data.gd")

@onready var _voxel_root: Node3D = $Voxels
@onready var _collision_shape: CollisionShape3D = $Collision

var chunk_coords: Vector3i
var chunk_data
var _mesh_instance: MeshInstance3D
var _collision_shapes: Array[CollisionShape3D] = []
var _active_collision_shape_count: int = 0


func _ready() -> void:
	_ensure_mesh_instance()
	_collect_collision_shapes()


func set_chunk_data(next_chunk_data) -> void:
	chunk_data = next_chunk_data
	if chunk_data == null:
		chunk_coords = Vector3i.ZERO
		clear_visuals()
		clear_collision()
		return

	chunk_coords = chunk_data.chunk_coords
	position = chunk_data.get_world_origin()
	visible = true


func apply_chunk_data(chunk_data, voxel_builder, generator = null) -> void:
	set_chunk_data(chunk_data)

	if voxel_builder != null:
		rebuild_voxels(voxel_builder, generator)


func rebuild_voxels(voxel_builder, generator = null, include_collision: bool = true) -> void:
	if chunk_data == null:
		return

	apply_visual_layout(voxel_builder, voxel_builder.build_chunk_visual_layout(chunk_data, generator))
	if include_collision:
		apply_collision_faces(voxel_builder.build_chunk_collision_faces(chunk_data, generator).get("collision_faces", PackedVector3Array()))
	else:
		clear_collision()


func apply_visual_layout(voxel_builder, build_result: Dictionary) -> void:
	_ensure_mesh_instance()
	_mesh_instance.mesh = voxel_builder.create_mesh_from_layout(build_result)
	if chunk_data != null and chunk_data.has_method("mark_clean"):
		chunk_data.mark_clean()


func apply_collision_faces(collision_faces: PackedVector3Array) -> void:
	apply_collision_sections({0: collision_faces})


func apply_collision_sections(collision_sections: Dictionary) -> void:
	clear_collision()
	var section_keys := collision_sections.keys()
	section_keys.sort()
	for section_key in section_keys:
		var collision_faces: PackedVector3Array = collision_sections.get(section_key, PackedVector3Array())
		if collision_faces.is_empty():
			continue
		var shape_node := _get_collision_shape(_active_collision_shape_count)
		var collision_shape := ConcavePolygonShape3D.new()
		collision_shape.set_faces(collision_faces)
		shape_node.shape = collision_shape
		shape_node.disabled = false
		_active_collision_shape_count += 1


func reset_for_pool() -> void:
	chunk_data = null
	chunk_coords = Vector3i.ZERO
	clear_visuals()
	clear_collision()
	visible = false


func apply_collision_faces_legacy(collision_faces: PackedVector3Array) -> void:
	if collision_faces.is_empty():
		_collision_shape.shape = null
		return

	var collision_shape := ConcavePolygonShape3D.new()
	collision_shape.set_faces(collision_faces)
	_collision_shape.shape = collision_shape


func clear_visuals() -> void:
	if _mesh_instance != null:
		_mesh_instance.mesh = null


func clear_collision() -> void:
	_ensure_base_collision_shape()
	for shape_node in _collision_shapes:
		shape_node.shape = null
		shape_node.disabled = true
	_active_collision_shape_count = 0


func _ensure_mesh_instance() -> void:
	if _mesh_instance != null:
		return
	_mesh_instance = MeshInstance3D.new()
	_mesh_instance.name = "ChunkMesh"
	_voxel_root.add_child(_mesh_instance)


func _ensure_base_collision_shape() -> void:
	if _collision_shapes.is_empty() and _collision_shape != null:
		_collision_shapes.append(_collision_shape)


func _collect_collision_shapes() -> void:
	_collision_shapes.clear()
	for child in get_children():
		if child is CollisionShape3D:
			_collision_shapes.append(child)
	if _collision_shapes.is_empty() and _collision_shape != null:
		_collision_shapes.append(_collision_shape)


func _get_collision_shape(index: int) -> CollisionShape3D:
	_ensure_base_collision_shape()
	while _collision_shapes.size() <= index:
		var shape_node := CollisionShape3D.new()
		shape_node.name = "CollisionSection%d" % [_collision_shapes.size()]
		add_child(shape_node)
		_collision_shapes.append(shape_node)
	return _collision_shapes[index]
