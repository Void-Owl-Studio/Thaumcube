extends SceneTree

const PrototypeDatabaseScript := preload("res://scripts/prototype/prototype_database.gd")
const BlockRegistryScript := preload("res://scripts/world/block_registry.gd")
const ItemRegistryScript := preload("res://scripts/items/item_registry.gd")


func _init() -> void:
	var database = PrototypeDatabaseScript.new()
	database.load_directory("res://content/prototypes")

	var dirt_vars: Dictionary = database.get_resolved_vars("/block/dirt")
	_assert_equal(dirt_vars.get("name"), "Dirt", "dirt name")
	_assert_equal(dirt_vars.get("solid"), true, "dirt inherits block solidity")
	_assert_equal(dirt_vars.get("block_texture"), "/textures/block/dirt.png", "dirt texture")

	var block_registry = BlockRegistryScript.new(database)
	_assert_equal(block_registry.get_block_id("/block/air"), 0, "air block id")
	_assert_equal(block_registry.get_block_id("/block/dirt"), 2, "dirt block id")
	_assert_equal(block_registry.get_definition(2).display_name, &"Dirt", "dirt block definition")

	var dirt_instance = block_registry.create_instance("/block/dirt")
	_assert_true(dirt_instance != null, "dirt instance exists")
	_assert_equal(dirt_instance.get_var("desc"), "Regular dirt.", "dirt instance desc")

	var item_registry = ItemRegistryScript.new(database)
	_assert_equal(item_registry.get_item_id("/item/dirt"), 1, "dirt item id")
	_assert_equal(item_registry.get_definition_by_path("/item/stone").stack_size, 64, "stone item stack")

	quit(0)


func _assert_true(value: bool, label: String) -> void:
	if value:
		return
	push_error("Prototype smoke test failed: %s" % label)
	quit(1)


func _assert_equal(actual, expected, label: String) -> void:
	if actual == expected:
		return
	push_error("Prototype smoke test failed: %s expected '%s', got '%s'" % [label, expected, actual])
	quit(1)
