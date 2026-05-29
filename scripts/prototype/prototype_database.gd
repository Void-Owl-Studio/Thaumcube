class_name PrototypeDatabase
extends RefCounted

const PrototypeInstanceScript := preload("res://scripts/prototype/prototype_instance.gd")
const PrototypeSourceParserScript := preload("res://scripts/prototype/prototype_source_parser.gd")
const ContentPathResolverScript := preload("res://scripts/prototype/content_path_resolver.gd")

var _definitions_by_path: Dictionary = {}
var _resolved_vars_by_path: Dictionary = {}
var _parser := PrototypeSourceParserScript.new()


func load_directory(directory_path: String) -> void:
	var directory := DirAccess.open(directory_path)
	if directory == null:
		push_error("Cannot open prototype directory '%s': error %s" % [directory_path, DirAccess.get_open_error()])
		return

	directory.list_dir_begin()
	var entry_name := directory.get_next()
	while not entry_name.is_empty():
		if entry_name.begins_with("."):
			entry_name = directory.get_next()
			continue

		var entry_path := directory_path.path_join(entry_name)
		if directory.current_is_dir():
			load_directory(entry_path)
		elif entry_name.get_extension() == "tcproto":
			load_file(entry_path)

		entry_name = directory.get_next()
	directory.list_dir_end()


func load_file(file_path: String) -> void:
	for definition in _parser.parse_file(file_path):
		register_definition(definition)


func register_definition(definition) -> void:
	if definition.type_path.is_empty() or not definition.type_path.begins_with("/"):
		push_error("Invalid prototype path '%s' in %s:%s" % [definition.type_path, definition.source_path, definition.source_line])
		return

	if _definitions_by_path.has(definition.type_path):
		push_error("Duplicate prototype path '%s' in %s:%s" % [definition.type_path, definition.source_path, definition.source_line])
		return

	_definitions_by_path[definition.type_path] = definition
	_resolved_vars_by_path.clear()


func get_definition(type_path: String):
	return _definitions_by_path.get(type_path)


func has_definition(type_path: String) -> bool:
	return _definitions_by_path.has(type_path)


func get_resolved_vars(type_path: String) -> Dictionary:
	return _resolve_vars(type_path, {})


func get_definitions_under(parent_path: String) -> Array:
	var definitions: Array = []
	var normalized_parent_path := parent_path
	if normalized_parent_path.ends_with("/"):
		normalized_parent_path = normalized_parent_path.substr(0, normalized_parent_path.length() - 1)
	var prefix := normalized_parent_path + "/"

	for definition in _definitions_by_path.values():
		if definition.type_path == normalized_parent_path or definition.type_path.begins_with(prefix):
			definitions.append(definition)

	definitions.sort_custom(Callable(self, "_sort_definitions_by_path"))
	return definitions


func create(type_path: String, overrides: Dictionary = {}):
	var definition = get_definition(type_path)
	if definition == null:
		push_error("Cannot create unknown prototype '%s'" % type_path)
		return null

	var resolved_vars := get_resolved_vars(type_path)
	for var_name in overrides.keys():
		resolved_vars[str(var_name).to_lower()] = overrides[var_name]

	var instance = PrototypeInstanceScript.new(definition, resolved_vars)
	_attach_behavior(instance)
	return instance


func _resolve_vars(type_path: String, stack: Dictionary) -> Dictionary:
	if _resolved_vars_by_path.has(type_path):
		return _resolved_vars_by_path[type_path].duplicate(true)

	var definition = get_definition(type_path)
	if definition == null:
		push_error("Unknown prototype parent '%s'" % type_path)
		return {}

	if stack.has(type_path):
		push_error("Prototype inheritance cycle detected at '%s'" % type_path)
		return {}

	stack[type_path] = true
	var resolved_vars := {}
	if not definition.parent_path.is_empty() and has_definition(definition.parent_path):
		resolved_vars = _resolve_vars(definition.parent_path, stack)

	for var_name in definition.vars.keys():
		resolved_vars[var_name] = definition.vars[var_name]

	stack.erase(type_path)
	_resolved_vars_by_path[type_path] = resolved_vars.duplicate(true)
	return resolved_vars


func _attach_behavior(instance) -> void:
	var behavior_path := ContentPathResolverScript.to_resource_path(str(instance.get_var("behavior_script", "")))
	if behavior_path.is_empty():
		return
	if not ResourceLoader.exists(behavior_path):
		push_error("Behavior script '%s' for '%s' does not exist" % [behavior_path, instance.type_path])
		return

	var behavior_script := load(behavior_path)
	if behavior_script == null or not behavior_script.can_instantiate():
		push_error("Behavior script '%s' for '%s' cannot be instantiated" % [behavior_path, instance.type_path])
		return

	instance.behavior = behavior_script.new()
	if instance.behavior.has_method("on_new"):
		instance.behavior.on_new(instance)


func _sort_definitions_by_path(left, right) -> bool:
	return left.type_path < right.type_path
