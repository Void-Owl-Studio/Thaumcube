class_name ChunkGenerator
extends RefCounted

const ChunkDataScript := preload("res://scripts/world/chunk_data.gd")
const BlockRegistryScript := preload("res://scripts/world/block_registry.gd")

var _seed: int
var _height_noise := FastNoiseLite.new()
var _temperature_noise := FastNoiseLite.new()


func _init(world_seed: int) -> void:
	_seed = world_seed

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
		if block_id != BlockRegistryScript.AIR:
			chunk_data.set_block(Vector3i(local_x, local_y, local_z), block_id)


func _sample_surface_height(world_x: int, world_z: int) -> int:
	var base_height := 10
	var terrain_variation := int(round((_height_noise.get_noise_2d(world_x, world_z) + 1.0) * 5.0))
	return base_height + terrain_variation


func _sample_temperature(world_x: int, world_z: int) -> float:
	return _temperature_noise.get_noise_2d(world_x, world_z)


func _resolve_block_for_position(world_y: int, surface_height: int, biome_temperature: float) -> int:
	if world_y > surface_height:
		return BlockRegistryScript.AIR
	if world_y == surface_height:
		return _select_surface_block_id(biome_temperature)
	if world_y >= surface_height - 3:
		return BlockRegistryScript.DIRT
	return BlockRegistryScript.STONE


func _select_surface_block_id(_biome_temperature: float) -> int:
	return BlockRegistryScript.GRASS
