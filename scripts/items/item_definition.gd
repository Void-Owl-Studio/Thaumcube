class_name ItemDefinition
extends RefCounted

var id: int
var type_path: String
var display_name: StringName
var description: String
var icon_texture_path: String
var stack_size: int
var behavior_script_path: String


func _init(
	item_id: int,
	item_type_path: String,
	item_display_name: StringName,
	item_description: String,
	item_icon_texture_path: String,
	item_stack_size: int,
	item_behavior_script_path: String = ""
) -> void:
	id = item_id
	type_path = item_type_path
	display_name = item_display_name
	description = item_description
	icon_texture_path = item_icon_texture_path
	stack_size = max(1, item_stack_size)
	behavior_script_path = item_behavior_script_path
