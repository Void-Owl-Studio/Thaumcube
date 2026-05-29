extends Node3D

const WORLD_SCENE := preload("res://scenes/world/world_manager.tscn")
const PLAYER_SCENE := preload("res://scenes/player/player_controller.tscn")

@export var show_debug_overlay := true

var _player: Node3D
var _debug_label: Label


func _ready() -> void:
	_ensure_input_actions()
	_create_environment()
	_create_sun_light()

	var world := WORLD_SCENE.instantiate()
	world.name = "World"
	add_child(world)

	_player = PLAYER_SCENE.instantiate()
	_player.name = "Player"
	add_child(_player)
	if world.has_method("get_spawn_position"):
		_player.position = world.get_spawn_position()
	else:
		_player.position = Vector3(8.5, 20.0, 8.5)

	if world.has_method("bind_player"):
		world.bind_player(_player)
	if _player.has_method("bind_world"):
		_player.bind_world(world)

	if show_debug_overlay:
		_create_debug_overlay()


func _process(_delta: float) -> void:
	if _debug_label == null or _player == null:
		return

	var camera := get_viewport().get_camera_3d()
	var player_input := Vector2.ZERO
	var player_velocity := Vector3.ZERO
	if _player.has_method("get_debug_movement_input"):
		player_input = _player.get_debug_movement_input()
	if "velocity" in _player:
		player_velocity = _player.velocity

	_debug_label.text = (
		"Player pos: %s\n"
		+ "Velocity: %s\n"
		+ "Input: %s\n"
		+ "Camera: %s\n"
		+ "Focus keys: W=%s A=%s S=%s D=%s"
	) % [
		_format_vector3(_player.global_position),
		_format_vector3(player_velocity),
		player_input,
		camera.get_path() if camera != null else "none",
		Input.is_physical_key_pressed(KEY_W),
		Input.is_physical_key_pressed(KEY_A),
		Input.is_physical_key_pressed(KEY_S),
		Input.is_physical_key_pressed(KEY_D)
	]


func _ensure_input_actions() -> void:
	_register_key_action("move_forward", KEY_W)
	_register_key_action("move_backward", KEY_S)
	_register_key_action("move_left", KEY_A)
	_register_key_action("move_right", KEY_D)
	_register_key_action("jump", KEY_SPACE)
	_register_key_action("toggle_mouse_capture", KEY_ESCAPE)
	_register_mouse_action("break_block", MOUSE_BUTTON_LEFT)


func _register_key_action(action_name: StringName, keycode: Key) -> void:
	if not InputMap.has_action(action_name):
		InputMap.add_action(action_name)

	for existing_event in InputMap.action_get_events(action_name):
		if existing_event is InputEventKey and existing_event.physical_keycode == keycode:
			return

	var event := InputEventKey.new()
	event.keycode = keycode
	event.physical_keycode = keycode
	InputMap.action_add_event(action_name, event)


func _register_mouse_action(action_name: StringName, button_index: MouseButton) -> void:
	if not InputMap.has_action(action_name):
		InputMap.add_action(action_name)

	for existing_event in InputMap.action_get_events(action_name):
		if existing_event is InputEventMouseButton and existing_event.button_index == button_index:
			return

	var event := InputEventMouseButton.new()
	event.button_index = button_index
	InputMap.action_add_event(action_name, event)


func _create_debug_overlay() -> void:
	var layer := CanvasLayer.new()
	layer.name = "DebugOverlay"
	add_child(layer)

	_debug_label = Label.new()
	_debug_label.name = "InputDebug"
	_debug_label.position = Vector2(12.0, 12.0)
	_debug_label.add_theme_font_size_override("font_size", 16)
	layer.add_child(_debug_label)


func _format_vector3(value: Vector3) -> String:
	return "(%.2f, %.2f, %.2f)" % [value.x, value.y, value.z]


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
