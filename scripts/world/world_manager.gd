extends Node3D

const DEFAULT_WORLD_SEED := 1337
const BlockRegistryScript := preload("res://scripts/world/block_registry.gd")
const ChunkGeneratorScript := preload("res://scripts/world/chunk_generator.gd")
const ChunkMeshBuilderScript := preload("res://scripts/world/chunk_mesh_builder.gd")

@export var world_seed := DEFAULT_WORLD_SEED
@export_range(0, 4) var load_radius := 1

@onready var _chunk_manager = $ChunkManager

var _block_registry
var _chunk_generator
var _chunk_mesh_builder
var _player: Node3D


func _ready() -> void:
	_block_registry = BlockRegistryScript.new()
	var atlas = _block_registry.build_texture_atlas()
	_chunk_generator = ChunkGeneratorScript.new(world_seed)
	_chunk_mesh_builder = ChunkMeshBuilderScript.new(_block_registry, atlas)
	_chunk_manager.configure(_chunk_generator, _chunk_mesh_builder, load_radius)


func _process(_delta: float) -> void:
	if _player == null:
		return
	_chunk_manager.sync_chunks_around(_player.global_position)


func bind_player(player: Node3D) -> void:
	_player = player
