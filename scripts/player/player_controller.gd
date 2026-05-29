extends CharacterBody3D

#const PLAYER_MODEL_PATH := "res://assets/models/playermodel.gltf"
const PLAYER_MODEL_PATH := ""
const WALK_SPEED := 5.5
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
	var input_vector := Input.get_vector("move_left", "move_right", "move_forward", "move_backward")
	var move_direction := (transform.basis * Vector3(input_vector.x, 0.0, input_vector.y)).normalized()

	if move_direction != Vector3.ZERO:
		velocity.x = move_direction.x * WALK_SPEED
		velocity.z = move_direction.z * WALK_SPEED
	else:
		velocity.x = move_toward(velocity.x, 0.0, WALK_SPEED)
		velocity.z = move_toward(velocity.z, 0.0, WALK_SPEED)

	if not is_on_floor():
		velocity.y -= GRAVITY * delta
	elif Input.is_action_just_pressed("jump"):
		velocity.y = JUMP_VELOCITY
	else:
		velocity.y = -0.01

	move_and_slide()


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
