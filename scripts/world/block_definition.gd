class_name BlockDefinition
extends RefCounted

var id: int
var display_name: StringName
var solid: bool
var top_texture_path: String
var side_texture_path: String
var bottom_texture_path: String


func _init(
	block_id: int,
	block_display_name: StringName,
	is_solid: bool,
	top_texture: String,
	side_texture: String,
	bottom_texture: String
) -> void:
	id = block_id
	display_name = block_display_name
	solid = is_solid
	top_texture_path = top_texture
	side_texture_path = side_texture
	bottom_texture_path = bottom_texture


func get_texture_path_for_face(face_normal: Vector3i) -> String:
	if face_normal == Vector3i.UP:
		return top_texture_path
	if face_normal == Vector3i.DOWN:
		return bottom_texture_path
	return side_texture_path
