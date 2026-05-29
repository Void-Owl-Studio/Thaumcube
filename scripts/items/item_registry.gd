class_name ItemRegistry
extends RefCounted

const ItemDefinitionScript := preload("res://scripts/items/item_definition.gd")
const PrototypeDatabaseScript := preload("res://scripts/prototype/prototype_database.gd")
const ContentPathResolverScript := preload("res://scripts/prototype/content_path_resolver.gd")

const DEFAULT_PROTOTYPE_DIR := "res://content/prototypes"

var _definitions_by_id: Dictionary = {}
var _ids_by_type_path: Dictionary = {}
var _database


func _init(prototype_database = null) -> void:
	if prototype_database == null:
		_database = PrototypeDatabaseScript.new()
		_database.load_directory(DEFAULT_PROTOTYPE_DIR)
	else:
		_database = prototype_database

	_register_from_prototypes()


func get_definition(item_id: int):
	return _definitions_by_id.get(item_id)


func get_definition_by_path(type_path: String):
	var item_id := get_item_id(type_path, -1)
	if item_id < 0:
		return null
	return get_definition(item_id)


func get_item_id(type_path: String, fallback: int = -1) -> int:
	return _ids_by_type_path.get(type_path, fallback)


func create_instance(type_path: String, overrides: Dictionary = {}):
	if _database == null:
		return null
	return _database.create(type_path, overrides)


func _register_from_prototypes() -> void:
	for prototype in _database.get_definitions_under("/item"):
		if prototype.type_path == "/item":
			continue

		var resolved_vars: Dictionary = _database.get_resolved_vars(prototype.type_path)
		if not resolved_vars.has("numeric_id"):
			continue

		var definition = _build_definition(prototype.type_path, resolved_vars)
		if definition == null:
			continue

		if _definitions_by_id.has(definition.id):
			push_error("Duplicate item numeric_id %s for '%s'" % [definition.id, prototype.type_path])
			continue

		_definitions_by_id[definition.id] = definition
		_ids_by_type_path[prototype.type_path] = definition.id


func _build_definition(type_path: String, vars: Dictionary):
	var item_id := int(vars.get("numeric_id", -1))
	if item_id < 0:
		push_error("Item '%s' has invalid numeric_id '%s'" % [type_path, vars.get("numeric_id")])
		return null

	var display_name := StringName(str(vars.get("name", _name_from_type_path(type_path))))
	var description := str(vars.get("desc", ""))
	var icon_texture_path := ContentPathResolverScript.to_resource_path(str(vars.get("icon_texture", "")))
	var stack_size := int(vars.get("stack_size", 64))
	var behavior_script_path := ContentPathResolverScript.to_resource_path(str(vars.get("behavior_script", "")))

	return ItemDefinitionScript.new(
		item_id,
		type_path,
		display_name,
		description,
		icon_texture_path,
		stack_size,
		behavior_script_path
	)


func _name_from_type_path(type_path: String) -> String:
	var raw_name := type_path.get_file().replace("_", " ")
	return raw_name.capitalize()
