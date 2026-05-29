extends CharacterBody3D

const PLAYER_MODEL_PATH := "res://assets/models/playermodel.gltf"
const WALK_SPEED := 5.5
const GROUND_ACCELERATION := 35.0
const AIR_ACCELERATION := 12.0
const JUMP_VELOCITY := 5.6
const GRAVITY := 18.0
const MOUSE_SENSITIVITY := 0.08
const BLOCK_INTERACTION_DISTANCE := 6.0
const BLOCK_FACE_EPSILON := 0.01
const LOCAL_PLAYER_VISUAL_LAYER := 1 << 1
const MAX_FALL_SPEED := 45.0
const VOXEL_GROUND_SNAP_DISTANCE := 0.08

@onready var _head: Node3D = $Head
@onready var _camera: Camera3D = $Head/Camera3D
@onready var _model_anchor: Node3D = $ModelAnchor

var _mouse_captured := true
var _world


func _ready() -> void:
	_hide_local_player_visuals_from_camera()
	_set_mouse_captured(true)
	_try_attach_player_model()
	_camera.current = true
	_camera.make_current()


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("toggle_mouse_capture"):
		_set_mouse_captured(not _mouse_captured)
		return

	if not _mouse_captured:
		return

	if event is InputEventMouseMotion:
		rotate_y(-deg_to_rad(event.relative.x * MOUSE_SENSITIVITY))
		_head.rotate_x(-deg_to_rad(event.relative.y * MOUSE_SENSITIVITY))
		_head.rotation.x = clamp(_head.rotation.x, deg_to_rad(-89.0), deg_to_rad(89.0))
	elif _is_break_block_event(event):
		_try_break_targeted_block()


func _physics_process(delta: float) -> void:
	var input_vector := _get_movement_input()
	var move_direction := (transform.basis * Vector3(input_vector.x, 0.0, input_vector.y)).normalized()
	var is_grounded := is_on_floor() or _is_on_voxel_ground()
	var acceleration := GROUND_ACCELERATION if is_grounded else AIR_ACCELERATION
	var position_before_move := global_position
	var wants_jump := _wants_jump()

	if move_direction != Vector3.ZERO:
		velocity.x = move_toward(velocity.x, move_direction.x * WALK_SPEED, acceleration * delta)
		velocity.z = move_toward(velocity.z, move_direction.z * WALK_SPEED, acceleration * delta)
	else:
		velocity.x = move_toward(velocity.x, 0.0, GROUND_ACCELERATION * delta)
		velocity.z = move_toward(velocity.z, 0.0, GROUND_ACCELERATION * delta)

	if not is_grounded:
		velocity.y = max(velocity.y - GRAVITY * delta, -MAX_FALL_SPEED)
	elif wants_jump:
		velocity.y = JUMP_VELOCITY
	else:
		velocity.y = -0.01

	move_and_slide()
	if not wants_jump:
		_snap_to_voxel_ground()

	if move_direction != Vector3.ZERO and _horizontal_distance_squared(position_before_move, global_position) < 0.000001:
		global_position += Vector3(velocity.x, 0.0, velocity.z) * delta
		if not wants_jump:
			_snap_to_voxel_ground()


func _get_movement_input() -> Vector2:
	var physical_input := _get_physical_movement_input()
	if physical_input != Vector2.ZERO:
		return physical_input

	var has_movement_actions := (
		InputMap.has_action("move_left")
		and InputMap.has_action("move_right")
		and InputMap.has_action("move_forward")
		and InputMap.has_action("move_backward")
	)
	if has_movement_actions:
		var input_vector := Input.get_vector("move_left", "move_right", "move_forward", "move_backward")
		if input_vector != Vector2.ZERO:
			return input_vector

	return Vector2.ZERO


func _get_physical_movement_input() -> Vector2:
	var input := Vector2.ZERO
	if Input.is_physical_key_pressed(KEY_A):
		input.x -= 1.0
	if Input.is_physical_key_pressed(KEY_D):
		input.x += 1.0
	if Input.is_physical_key_pressed(KEY_W):
		input.y -= 1.0
	if Input.is_physical_key_pressed(KEY_S):
		input.y += 1.0
	return input.normalized()


func _wants_jump() -> bool:
	if InputMap.has_action("jump") and Input.is_action_just_pressed("jump"):
		return true
	return Input.is_physical_key_pressed(KEY_SPACE)


func _is_break_block_event(event: InputEvent) -> bool:
	if InputMap.has_action("break_block") and event.is_action_pressed("break_block"):
		return true
	return (
		event is InputEventMouseButton
		and event.button_index == MOUSE_BUTTON_LEFT
		and event.pressed
	)


func _set_mouse_captured(captured: bool) -> void:
	_mouse_captured = captured
	Input.mouse_mode = Input.MOUSE_MODE_CAPTURED if captured else Input.MOUSE_MODE_VISIBLE


func bind_world(world) -> void:
	_world = world


func get_debug_movement_input() -> Vector2:
	return _get_movement_input()


func _is_on_voxel_ground() -> bool:
	var floor_y := _get_voxel_floor_y()
	if is_nan(floor_y):
		return false
	return absf(global_position.y - floor_y) <= VOXEL_GROUND_SNAP_DISTANCE


func _snap_to_voxel_ground() -> void:
	if velocity.y > 0.0:
		return

	var floor_y := _get_voxel_floor_y()
	if is_nan(floor_y):
		return
	if global_position.y > floor_y + VOXEL_GROUND_SNAP_DISTANCE:
		return

	global_position.y = floor_y
	velocity.y = -0.01


func _get_voxel_floor_y() -> float:
	if _world == null or not _world.has_method("get_player_floor_y"):
		return NAN
	return _world.get_player_floor_y(global_position)


func _horizontal_distance_squared(from_position: Vector3, to_position: Vector3) -> float:
	var x_delta := to_position.x - from_position.x
	var z_delta := to_position.z - from_position.z
	return x_delta * x_delta + z_delta * z_delta


func _try_break_targeted_block() -> void:
	if _world == null or not _world.has_method("break_block"):
		return

	var space_state := get_world_3d().direct_space_state
	var ray_origin := _camera.global_position
	var ray_end := ray_origin + -_camera.global_transform.basis.z * BLOCK_INTERACTION_DISTANCE
	var query := PhysicsRayQueryParameters3D.create(ray_origin, ray_end)
	query.exclude = [get_rid()]

	var hit := space_state.intersect_ray(query)
	if hit.is_empty() or not hit.has("position") or not hit.has("normal"):
		return

	var block_position := _hit_to_block_position(hit.position, hit.normal)
	_world.break_block(block_position)


func _hit_to_block_position(hit_position: Vector3, hit_normal: Vector3) -> Vector3i:
	var inside_block_position := hit_position - hit_normal * BLOCK_FACE_EPSILON
	return Vector3i(
		floori(inside_block_position.x),
		floori(inside_block_position.y),
		floori(inside_block_position.z)
	)


func _try_attach_player_model() -> void:
	if not ResourceLoader.exists(PLAYER_MODEL_PATH):
		return

	var player_scene = load(PLAYER_MODEL_PATH)
	if player_scene is PackedScene:
		var visual_root: Node = player_scene.instantiate()
		visual_root.position = Vector3(0.0, 0.0, 0.0)
		visual_root.scale = Vector3.ONE
		visual_root.rotation_degrees.y = 180.0
		_assign_visual_layer(visual_root, LOCAL_PLAYER_VISUAL_LAYER)
		_model_anchor.add_child(visual_root)


func _hide_local_player_visuals_from_camera() -> void:
	_camera.cull_mask &= ~LOCAL_PLAYER_VISUAL_LAYER


func _assign_visual_layer(node: Node, layer_mask: int) -> void:
	if node is VisualInstance3D:
		node.layers = layer_mask

	for child in node.get_children():
		_assign_visual_layer(child, layer_mask)
