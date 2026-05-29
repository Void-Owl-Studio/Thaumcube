extends CharacterBody3D

#const PLAYER_MODEL_PATH := "res://assets/models/playermodel.gltf"#
const PLAYER_MODEL_PATH := ""
const WALK_SPEED := 5.5
const GROUND_ACCELERATION := 35.0
const AIR_ACCELERATION := 12.0
const JUMP_VELOCITY := 5.6
const GRAVITY := 18.0
const MOUSE_SENSITIVITY := 0.08

@onready var _head: Node3D = $Head
@onready var _camera: Camera3D = $Head/Camera3D
@onready var _model_anchor: Node3D = $ModelAnchor

var _mouse_captured := true


func _ready() -> void:
	_set_mouse_captured(true)
	_try_attach_player_model()


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


func _physics_process(delta: float) -> void:
	var input_vector := _get_movement_input()
	var move_direction := (transform.basis * Vector3(input_vector.x, 0.0, input_vector.y)).normalized()
	var acceleration := GROUND_ACCELERATION if is_on_floor() else AIR_ACCELERATION

	if move_direction != Vector3.ZERO:
		velocity.x = move_toward(velocity.x, move_direction.x * WALK_SPEED, acceleration * delta)
		velocity.z = move_toward(velocity.z, move_direction.z * WALK_SPEED, acceleration * delta)
	else:
		velocity.x = move_toward(velocity.x, 0.0, GROUND_ACCELERATION * delta)
		velocity.z = move_toward(velocity.z, 0.0, GROUND_ACCELERATION * delta)

	if not is_on_floor():
		velocity.y -= GRAVITY * delta
	elif _wants_jump():
		velocity.y = JUMP_VELOCITY
	else:
		velocity.y = -0.01

	move_and_slide()


func _get_movement_input() -> Vector2:
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

	var fallback_input := Vector2.ZERO
	if Input.is_key_pressed(KEY_A):
		fallback_input.x -= 1.0
	if Input.is_key_pressed(KEY_D):
		fallback_input.x += 1.0
	if Input.is_key_pressed(KEY_W):
		fallback_input.y -= 1.0
	if Input.is_key_pressed(KEY_S):
		fallback_input.y += 1.0
	return fallback_input.normalized()


func _wants_jump() -> bool:
	if InputMap.has_action("jump") and Input.is_action_just_pressed("jump"):
		return true
	return Input.is_key_pressed(KEY_SPACE)


func _set_mouse_captured(captured: bool) -> void:
	_mouse_captured = captured
	Input.mouse_mode = Input.MOUSE_MODE_CAPTURED if captured else Input.MOUSE_MODE_VISIBLE


func _try_attach_player_model() -> void:
	if not ResourceLoader.exists(PLAYER_MODEL_PATH):
		return

	var player_scene = load(PLAYER_MODEL_PATH)
	if player_scene is PackedScene:
		var visual_root: Node = player_scene.instantiate()
		visual_root.position = Vector3(0.0, 0.0, 0.0)
		visual_root.scale = Vector3.ONE
		visual_root.rotation_degrees.y = 180.0
		_model_anchor.add_child(visual_root)
