extends Node3D

const WORLD_SCENE := preload("res://scenes/world/world_manager.tscn")
const PLAYER_SCENE := preload("res://scenes/player/player_controller.tscn")


func _ready() -> void:
	_ensure_input_actions()
	_create_environment()
	_create_sun_light()

	var world := WORLD_SCENE.instantiate()
	world.name = "World"
	add_child(world)

	var player := PLAYER_SCENE.instantiate()
	player.name = "Player"
	add_child(player)
	if world.has_method("get_spawn_position"):
		player.position = world.get_spawn_position()
	else:
		player.position = Vector3(8.5, 20.0, 8.5)

	if world.has_method("bind_player"):
		world.bind_player(player)


func _ensure_input_actions() -> void:
	_register_key_action("move_forward", KEY_W)
	_register_key_action("move_backward", KEY_S)
	_register_key_action("move_left", KEY_A)
	_register_key_action("move_right", KEY_D)
	_register_key_action("jump", KEY_SPACE)
	_register_key_action("toggle_mouse_capture", KEY_ESCAPE)


func _register_key_action(action_name: StringName, keycode: Key) -> void:
	if not InputMap.has_action(action_name):
		InputMap.add_action(action_name)

	for existing_event in InputMap.action_get_events(action_name):
		if existing_event is InputEventKey and existing_event.physical_keycode == keycode:
			return

	var event := InputEventKey.new()
	event.physical_keycode = keycode
	InputMap.action_add_event(action_name, event)


func _create_environment() -> void:
	var environment := Environment.new()
	environment.background_mode = Environment.BG_SKY
	environment.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	environment.tonemap_mode = Environment.TONE_MAPPER_ACES
	environment.fog_enabled = true
	environment.fog_light_energy = 0.35
	environment.fog_density = 0.0025

	var sky := Sky.new()
	var procedural_sky := ProceduralSkyMaterial.new()
	procedural_sky.sky_top_color = Color(0.35, 0.62, 0.96)
	procedural_sky.sky_horizon_color = Color(0.78, 0.89, 1.0)
	procedural_sky.ground_bottom_color = Color(0.09, 0.12, 0.15)
	procedural_sky.ground_horizon_color = Color(0.52, 0.56, 0.60)
	sky.sky_material = procedural_sky
	environment.sky = sky

	var world_environment := WorldEnvironment.new()
	world_environment.environment = environment
	add_child(world_environment)


func _create_sun_light() -> void:
	var light := DirectionalLight3D.new()
	light.rotation_degrees = Vector3(-52.0, -35.0, 0.0)
	light.light_energy = 1.2
	light.shadow_enabled = true
	add_child(light)
