class_name ChunkGenerator
extends RefCounted

const ChunkDataScript := preload("res://scripts/world/chunk_data.gd")
const BlockRegistryScript := preload("res://scripts/world/block_registry.gd")

const BLOCK_AIR_PATH := "/block/air"
const BLOCK_GRASS_PATH := "/block/grass"
const BLOCK_DIRT_PATH := "/block/dirt"
const BLOCK_STONE_PATH := "/block/stone"

var _seed: int
var _air_block_id := BlockRegistryScript.AIR
var _grass_block_id := BlockRegistryScript.GRASS
var _dirt_block_id := BlockRegistryScript.DIRT
var _stone_block_id := BlockRegistryScript.STONE
var _height_noise := FastNoiseLite.new()
var _temperature_noise := FastNoiseLite.new()


func _init(world_seed: int, block_registry = null) -> void:
	_seed = world_seed
	if block_registry != null:
		_air_block_id = block_registry.get_block_id(BLOCK_AIR_PATH, _air_block_id)
		_grass_block_id = block_registry.get_block_id(BLOCK_GRASS_PATH, _grass_block_id)
		_dirt_block_id = block_registry.get_block_id(BLOCK_DIRT_PATH, _dirt_block_id)
		_stone_block_id = block_registry.get_block_id(BLOCK_STONE_PATH, _stone_block_id)

	_height_noise.seed = _seed
	_height_noise.noise_type = FastNoiseLite.TYPE_SIMPLEX
	_height_noise.frequency = 0.035
	_height_noise.fractal_octaves = 4

	_temperature_noise.seed = _seed + 911
	_temperature_noise.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	_temperature_noise.frequency = 0.01


func generate(chunk_coords: Vector3i):
	var chunk_data = ChunkDataScript.new(chunk_coords)

	for local_x in range(ChunkDataScript.SIZE_X):
		for local_z in range(ChunkDataScript.SIZE_Z):
			_generate_column(chunk_data, local_x, local_z)

	return chunk_data


func _generate_column(chunk_data, local_x: int, local_z: int) -> void:
	var world_x = chunk_data.chunk_coords.x * ChunkDataScript.SIZE_X + local_x
	var world_z = chunk_data.chunk_coords.z * ChunkDataScript.SIZE_Z + local_z
	var surface_height := _sample_surface_height(world_x, world_z)
	var biome_temperature := _sample_temperature(world_x, world_z)

	for local_y in range(ChunkDataScript.SIZE_Y):
		var world_y = chunk_data.chunk_coords.y * ChunkDataScript.SIZE_Y + local_y
		var block_id := _resolve_block_for_position(world_y, surface_height, biome_temperature)
		if block_id != _air_block_id:
			chunk_data.set_block_at(local_x, local_y, local_z, block_id)


func _sample_surface_height(world_x: int, world_z: int) -> int:
	var base_height := 10
	var terrain_variation := int(round((_height_noise.get_noise_2d(world_x, world_z) + 1.0) * 5.0))
	return base_height + terrain_variation


func _sample_temperature(world_x: int, world_z: int) -> float:
	return _temperature_noise.get_noise_2d(world_x, world_z)


func get_block_id_at(world_position: Vector3i) -> int:
	var surface_height := _sample_surface_height(world_position.x, world_position.z)
	var biome_temperature := _sample_temperature(world_position.x, world_position.z)
	return _resolve_block_for_position(world_position.y, surface_height, biome_temperature)


func get_surface_height(world_x: int, world_z: int) -> int:
	return _sample_surface_height(world_x, world_z)


func _resolve_block_for_position(world_y: int, surface_height: int, biome_temperature: float) -> int:
	if world_y > surface_height:
		return _air_block_id
	if world_y == surface_height:
		return _select_surface_block_id(biome_temperature)
	if world_y >= surface_height - 3:
		return _dirt_block_id
	return _stone_block_id


func _select_surface_block_id(_biome_temperature: float) -> int:
	return _grass_block_id
