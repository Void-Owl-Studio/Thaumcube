class_name BlockRegistry
extends RefCounted

const BlockDefinitionScript := preload("res://scripts/world/block_definition.gd")
const BlockTextureAtlasScript := preload("res://scripts/world/block_texture_atlas.gd")
const PrototypeDatabaseScript := preload("res://scripts/prototype/prototype_database.gd")
const ContentPathResolverScript := preload("res://scripts/prototype/content_path_resolver.gd")

const AIR := 0
const GRASS := 1
const DIRT := 2
const STONE := 3

const DEFAULT_PROTOTYPE_DIR := "res://content/prototypes"

var _definitions: Dictionary = {}
var _ids_by_type_path: Dictionary = {}
var _database


func _init(prototype_database = null) -> void:
	if prototype_database == null:
		_database = PrototypeDatabaseScript.new()
		_database.load_directory(DEFAULT_PROTOTYPE_DIR)
	else:
		_database = prototype_database

	_register_from_prototypes()
	_ensure_air_definition()


func get_definition(block_id: int):
	return _definitions.get(block_id)


func get_definition_by_path(type_path: String):
	var block_id := get_block_id(type_path, -1)
	if block_id < 0:
		return null
	return get_definition(block_id)


func get_block_id(type_path: String, fallback: int = AIR) -> int:
	return _ids_by_type_path.get(type_path, fallback)


func create_instance(type_path: String, overrides: Dictionary = {}):
	if _database == null:
		return null
	return _database.create(type_path, overrides)


func is_solid(block_id: int) -> bool:
	var definition = get_definition(block_id)
	return definition != null and definition.solid


func build_texture_atlas():
	var atlas = BlockTextureAtlasScript.new()
	var unique_texture_paths := _collect_texture_paths()
	if unique_texture_paths.is_empty():
		return atlas

	var first_image := Image.load_from_file(unique_texture_paths[0])
	if first_image == null or first_image.is_empty():
		push_error("Cannot load first block atlas texture '%s'" % unique_texture_paths[0])
		return atlas

	first_image.convert(Image.FORMAT_RGBA8)
	var tile_width := first_image.get_width()
	var tile_height := first_image.get_height()
	var atlas_image := Image.create(tile_width * unique_texture_paths.size(), tile_height, false, Image.FORMAT_RGBA8)

	for texture_index in unique_texture_paths.size():
		var texture_path: String = unique_texture_paths[texture_index]
		var source_image := Image.load_from_file(texture_path)
		if source_image == null or source_image.is_empty():
			push_error("Cannot load block atlas texture '%s'" % texture_path)
			continue

		source_image.convert(Image.FORMAT_RGBA8)
		if source_image.get_width() != tile_width or source_image.get_height() != tile_height:
			source_image.resize(tile_width, tile_height, Image.INTERPOLATE_NEAREST)

		atlas_image.blit_rect(
			source_image,
			Rect2i(Vector2i.ZERO, Vector2i(tile_width, tile_height)),
			Vector2i(texture_index * tile_width, 0)
		)

		var uv_origin := Vector2(float(texture_index) / float(unique_texture_paths.size()), 0.0)
		var uv_size := Vector2(1.0 / float(unique_texture_paths.size()), 1.0)
		atlas.uv_rects[texture_path] = Rect2(uv_origin, uv_size)

	atlas.texture = ImageTexture.create_from_image(atlas_image)
	return atlas


func _register_from_prototypes() -> void:
	for prototype in _database.get_definitions_under("/block"):
		if prototype.type_path == "/block":
			continue

		var resolved_vars: Dictionary = _database.get_resolved_vars(prototype.type_path)
		if not resolved_vars.has("numeric_id"):
			continue

		var definition = _build_definition(prototype.type_path, resolved_vars)
		if definition == null:
			continue

		if _definitions.has(definition.id):
			push_error("Duplicate block numeric_id %s for '%s'" % [definition.id, prototype.type_path])
			continue

		_definitions[definition.id] = definition
		_ids_by_type_path[prototype.type_path] = definition.id


func _build_definition(type_path: String, vars: Dictionary):
	var block_id := int(vars.get("numeric_id", -1))
	if block_id < 0:
		push_error("Block '%s' has invalid numeric_id '%s'" % [type_path, vars.get("numeric_id")])
		return null

	var default_texture := ContentPathResolverScript.to_resource_path(str(vars.get("block_texture", "")))
	var top_texture := ContentPathResolverScript.to_resource_path(str(vars.get("block_top_texture", default_texture)))
	var side_texture := ContentPathResolverScript.to_resource_path(str(vars.get("block_side_texture", default_texture)))
	var bottom_texture := ContentPathResolverScript.to_resource_path(str(vars.get("block_bottom_texture", default_texture)))
	var model_path := ContentPathResolverScript.to_resource_path(str(vars.get("block_model", "")))
	var behavior_script_path := ContentPathResolverScript.to_resource_path(str(vars.get("behavior_script", "")))
	var display_name := StringName(str(vars.get("name", _name_from_type_path(type_path))))
	var description := str(vars.get("desc", ""))
	var solid := bool(vars.get("solid", true))

	return BlockDefinitionScript.new(
		block_id,
		type_path,
		display_name,
		description,
		solid,
		top_texture,
		side_texture,
		bottom_texture,
		model_path,
		behavior_script_path
	)


func _ensure_air_definition() -> void:
	if _definitions.has(AIR):
		return

	var air_definition = BlockDefinitionScript.new(
		AIR,
		"/block/air",
		&"Air",
		"",
		false,
		"",
		"",
		""
	)
	_definitions[AIR] = air_definition
	_ids_by_type_path[air_definition.type_path] = AIR


func _collect_texture_paths() -> Array[String]:
	var texture_paths: Array[String] = []
	for definition in _definitions.values():
		if definition == null or not definition.solid:
			continue

		for texture_path in [
			definition.top_texture_path,
			definition.side_texture_path,
			definition.bottom_texture_path
		]:
			if texture_path.is_empty():
				continue
			if not FileAccess.file_exists(texture_path):
				push_error("Block texture '%s' does not exist" % texture_path)
				continue
			if not texture_paths.has(texture_path):
				texture_paths.append(texture_path)

	return texture_paths


func _name_from_type_path(type_path: String) -> String:
	var raw_name := type_path.get_file().replace("_", " ")
	return raw_name.capitalize()
