extends Node3D

const DEFAULT_WORLD_SEED := 1337
const BlockRegistryScript := preload("res://scripts/world/block_registry.gd")
const ChunkDataScript := preload("res://scripts/world/chunk_data.gd")
const ChunkGeneratorScript := preload("res://scripts/world/chunk_generator.gd")
const ChunkVoxelBuilderScript := preload("res://scripts/world/chunk_voxel_builder.gd")

@export var world_seed := DEFAULT_WORLD_SEED
@export_range(0, 32) var render_distance := 6
@export_range(0, 40) var unload_distance := 8
@export_range(0, 8) var collision_distance := 3
@export_range(1, 8) var chunk_jobs_per_frame := 1
@export_range(1, 8) var chunk_nodes_per_frame := 1
@export_range(0, 32) var max_worker_jobs := 0
@export_range(0.5, 8.0, 0.1) var chunk_apply_budget_ms := 2.5
@export_range(0.5, 8.0, 0.1) var collision_apply_budget_ms := 1.5
@export_range(1, 4) var max_visual_applies_per_frame := 1
@export_range(1, 4) var max_collision_applies_per_frame := 1
@export_range(1, 8) var collision_apply_interval_frames := 3
@export_range(1, 8) var collision_build_jobs_per_frame := 1
@export_range(1, 8) var neighbor_rebuilds_per_frame := 1
@export_range(1, 8) var unloads_per_frame := 1
@export var enable_dynamic_load_radius := true
@export_range(0, 32) var min_dynamic_load_radius := 1
@export_range(15, 120) var reduce_load_below_fps := 50
@export_range(15, 120) var restore_load_above_fps := 58
@export_range(0.2, 5.0, 0.1) var load_radius_restore_delay_sec := 1.5
@export_range(0.0, 20.0, 0.1) var fast_move_speed_threshold := 9.0

@onready var _chunk_manager = $ChunkManager

var _block_registry
var _chunk_generator
var _chunk_voxel_builder
var _player: Node3D
var _active_load_radius: int
var _stable_time_sec: float = 0.0


func _ready() -> void:
	_block_registry = BlockRegistryScript.new()
	var atlas = _block_registry.build_texture_atlas()
	_chunk_generator = ChunkGeneratorScript.new(world_seed, _block_registry)
	_chunk_voxel_builder = ChunkVoxelBuilderScript.new(_block_registry, atlas)
	_active_load_radius = render_distance
	_chunk_manager.configure(
		_chunk_generator,
		_chunk_voxel_builder,
		_active_load_radius,
		max(unload_distance, _active_load_radius + 2),
		collision_distance,
		chunk_jobs_per_frame,
		_resolve_max_worker_jobs(),
		chunk_nodes_per_frame,
		chunk_apply_budget_ms,
		collision_apply_budget_ms,
		max_visual_applies_per_frame,
		collision_apply_interval_frames,
		collision_build_jobs_per_frame,
		neighbor_rebuilds_per_frame,
		max_collision_applies_per_frame,
		unloads_per_frame
	)


func _process(_delta: float) -> void:
	if _player == null:
		return
	_update_dynamic_load_radius(_delta)
	_chunk_manager.sync_chunks_around(_player.global_position, -_player.global_transform.basis.z)


func bind_player(player: Node3D) -> void:
	_player = player
	if _chunk_generator != null and _chunk_voxel_builder != null:
		_chunk_manager.sync_chunks_around(_player.global_position, -_player.global_transform.basis.z)


func break_block(world_position: Vector3i) -> bool:
	if world_position.y < 0 or world_position.y >= ChunkDataScript.SIZE_Y:
		return false
	return _chunk_manager.set_block_at_world(world_position, ChunkDataScript.AIR_BLOCK_ID)


func get_spawn_position(spawn_x: int = 8, spawn_z: int = 8) -> Vector3:
	if _chunk_generator == null:
		if _block_registry == null:
			_block_registry = BlockRegistryScript.new()
		_chunk_generator = ChunkGeneratorScript.new(world_seed, _block_registry)

	var surface_height: int = _chunk_generator.get_surface_height(spawn_x, spawn_z)
	return Vector3(spawn_x + 0.5, surface_height + 1.05, spawn_z + 0.5)


func get_player_floor_y(world_position: Vector3) -> float:
	if _chunk_generator == null:
		if _block_registry == null:
			_block_registry = BlockRegistryScript.new()
		_chunk_generator = ChunkGeneratorScript.new(world_seed, _block_registry)

	var surface_height: int = _chunk_generator.get_surface_height(
		floori(world_position.x),
		floori(world_position.z)
	)
	return float(surface_height) + 1.05


func get_chunk_loading_stats() -> Dictionary:
	if _chunk_manager != null and _chunk_manager.has_method("get_debug_stats"):
		return _chunk_manager.get_debug_stats()
	return {}


func _update_dynamic_load_radius(delta: float) -> void:
	if not enable_dynamic_load_radius or _chunk_manager == null:
		return

	var fps: float = Engine.get_frames_per_second()
	var is_moving_fast: bool = _is_player_moving_fast()
	var target_radius: int = render_distance

	if fps < float(reduce_load_below_fps) or is_moving_fast:
		target_radius = min_dynamic_load_radius
		_stable_time_sec = 0.0
	elif fps >= float(restore_load_above_fps):
		_stable_time_sec += delta
		if _stable_time_sec < load_radius_restore_delay_sec:
			target_radius = _active_load_radius
	else:
		_stable_time_sec = 0.0
		target_radius = _active_load_radius

	if target_radius == _active_load_radius:
		return

	if target_radius > _active_load_radius:
		_active_load_radius += 1
	else:
		_active_load_radius = target_radius

	_active_load_radius = clampi(_active_load_radius, min_dynamic_load_radius, render_distance)
	_chunk_manager.set_render_distance(_active_load_radius)


func _is_player_moving_fast() -> bool:
	if _player is CharacterBody3D:
		var body := _player as CharacterBody3D
		return body != null and body.velocity.length() >= fast_move_speed_threshold
	return false


func _resolve_max_worker_jobs() -> int:
	if max_worker_jobs > 0:
		return max_worker_jobs
	return max(1, OS.get_processor_count() - 1)
