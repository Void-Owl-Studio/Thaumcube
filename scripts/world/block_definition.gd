class_name BlockDefinition
extends RefCounted

var id: int
var type_path: String
var display_name: StringName
var description: String
var solid: bool
var top_texture_path: String
var side_texture_path: String
var bottom_texture_path: String
var model_path: String
var behavior_script_path: String


func _init(
	block_id: int,
	block_type_path: String,
	block_display_name: StringName,
	block_description: String,
	is_solid: bool,
	top_texture: String,
	side_texture: String,
	bottom_texture: String,
	block_model_path: String = "",
	block_behavior_script_path: String = ""
) -> void:
	id = block_id
	type_path = block_type_path
	display_name = block_display_name
	description = block_description
	solid = is_solid
	top_texture_path = top_texture
	side_texture_path = side_texture
	bottom_texture_path = bottom_texture
	model_path = block_model_path
	behavior_script_path = block_behavior_script_path


func get_texture_path_for_face(face_normal: Vector3i) -> String:
	if face_normal == Vector3i.UP:
		return top_texture_path
	if face_normal == Vector3i.DOWN:
		return bottom_texture_path
	return side_texture_path
