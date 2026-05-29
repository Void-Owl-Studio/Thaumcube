class_name PrototypeDefinition
extends RefCounted

var type_path: String
var parent_path: String
var source_path: String
var source_line: int
var vars: Dictionary = {}


func _init(prototype_type_path: String, prototype_source_path: String = "", prototype_source_line: int = 0) -> void:
	type_path = prototype_type_path.strip_edges()
	parent_path = _infer_parent_path(type_path)
	source_path = prototype_source_path
	source_line = prototype_source_line


func set_var(var_name: String, value) -> void:
	var normalized_name := var_name.strip_edges().to_lower()
	if normalized_name.is_empty():
		return

	if normalized_name == "parent":
		parent_path = str(value).strip_edges()
		return

	vars[normalized_name] = value


func get_var(var_name: String, default_value = null):
	return vars.get(var_name.to_lower(), default_value)


func _infer_parent_path(prototype_type_path: String) -> String:
	var last_separator := prototype_type_path.rfind("/")
	if last_separator <= 0:
		return ""
	return prototype_type_path.substr(0, last_separator)
