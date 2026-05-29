class_name ChunkData
extends RefCounted

const AIR_BLOCK_ID := 0

const SIZE_X := 16
const SIZE_Y := 32
const SIZE_Z := 16

var chunk_coords: Vector3i
var _blocks: PackedInt32Array


func _init(coords: Vector3i) -> void:
	chunk_coords = coords
	_blocks = PackedInt32Array()
	_blocks.resize(SIZE_X * SIZE_Y * SIZE_Z)


func is_in_bounds(local_position: Vector3i) -> bool:
	return (
		local_position.x >= 0 and local_position.x < SIZE_X
		and local_position.y >= 0 and local_position.y < SIZE_Y
		and local_position.z >= 0 and local_position.z < SIZE_Z
	)


func set_block(local_position: Vector3i, block_id: int) -> void:
	if not is_in_bounds(local_position):
		return
	_blocks[_to_index(local_position)] = block_id


func get_block(local_position: Vector3i) -> int:
	if not is_in_bounds(local_position):
		return AIR_BLOCK_ID
	return _blocks[_to_index(local_position)]


func get_world_origin() -> Vector3:
	return Vector3(
		chunk_coords.x * SIZE_X,
		chunk_coords.y * SIZE_Y,
		chunk_coords.z * SIZE_Z
	)


func _to_index(local_position: Vector3i) -> int:
	return (
		local_position.x
		+ local_position.y * SIZE_X
		+ local_position.z * SIZE_X * SIZE_Y
	)
