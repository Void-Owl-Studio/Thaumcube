class_name PrototypeInstance
extends RefCounted

var type_path: String
var definition
var vars: Dictionary = {}
var behavior: Object


func _init(prototype_definition, resolved_vars: Dictionary) -> void:
	definition = prototype_definition
	type_path = prototype_definition.type_path
	vars = resolved_vars.duplicate(true)


func get_var(var_name: String, default_value = null):
	return vars.get(var_name.to_lower(), default_value)


func set_var(var_name: String, value) -> void:
	var normalized_name := var_name.strip_edges().to_lower()
	if normalized_name.is_empty():
		return
	vars[normalized_name] = value


func call_behavior(method_name: StringName, args: Array = []):
	if behavior == null or not behavior.has_method(method_name):
		return null
	return behavior.callv(method_name, args)
