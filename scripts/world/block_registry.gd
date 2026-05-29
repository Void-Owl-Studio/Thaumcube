class_name BlockRegistry
extends RefCounted

const BlockDefinitionScript := preload("res://scripts/world/block_definition.gd")
const BlockTextureAtlasScript := preload("res://scripts/world/block_texture_atlas.gd")

const AIR := 0
const GRASS := 1
const DIRT := 2
const STONE := 3

const TEXTURE_GRASS_TOP := "res://assets/textures/block/grass_block_top.png"
const TEXTURE_GRASS_SIDE := "res://assets/textures/block/grass_block_side.png"
const TEXTURE_DIRT := "res://assets/textures/block/dirt.png"
const TEXTURE_STONE := "res://assets/textures/block/stone.png"

var _definitions: Dictionary = {}


func _init() -> void:
	_register_defaults()


func get_definition(block_id: int):
	return _definitions.get(block_id)


func is_solid(block_id: int) -> bool:
	var definition = get_definition(block_id)
	return definition != null and definition.solid


func build_texture_atlas():
	var atlas = BlockTextureAtlasScript.new()
	var unique_texture_paths := _collect_texture_paths()
	if unique_texture_paths.is_empty():
		return atlas

	var first_image := Image.load_from_file(unique_texture_paths[0])
	first_image.convert(Image.FORMAT_RGBA8)
	var tile_width := first_image.get_width()
	var tile_height := first_image.get_height()
	var atlas_image := Image.create(tile_width * unique_texture_paths.size(), tile_height, false, Image.FORMAT_RGBA8)

	for texture_index in unique_texture_paths.size():
		var texture_path: String = unique_texture_paths[texture_index]
		var source_image := Image.load_from_file(texture_path)
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


func _register_defaults() -> void:
	_definitions[AIR] = BlockDefinitionScript.new(AIR, &"Air", false, "", "", "")
	_definitions[GRASS] = BlockDefinitionScript.new(GRASS, &"Grass", true, TEXTURE_GRASS_TOP, TEXTURE_GRASS_SIDE, TEXTURE_DIRT)
	_definitions[DIRT] = BlockDefinitionScript.new(DIRT, &"Dirt", true, TEXTURE_DIRT, TEXTURE_DIRT, TEXTURE_DIRT)
	_definitions[STONE] = BlockDefinitionScript.new(STONE, &"Stone", true, TEXTURE_STONE, TEXTURE_STONE, TEXTURE_STONE)


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
			if not texture_paths.has(texture_path):
				texture_paths.append(texture_path)

	return texture_paths
