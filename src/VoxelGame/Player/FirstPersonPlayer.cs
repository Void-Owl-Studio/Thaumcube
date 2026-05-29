using System.Numerics;
using Silk.NET.Input;
using VoxelGame.Input;
using VoxelGame.Physics;
using VoxelGame.World;
using VoxelGame.World.Storage;

namespace VoxelGame.Player;

public sealed class FirstPersonPlayer
{
    private const float GroundFriction = 0.546f;
    private const float AirFriction = 0.91f;
    private const float VerticalDrag = 0.98f;
    private const float AirAccelerationFactor = 0.02f;
    private const float SprintAirAccelerationFactor = 0.045f;
    private const float HorizontalStopEpsilon = 0.001f;
    private const float SprintJumpBoost = 0.2f;
    private const float WalkBobAmplitude = 0.045f;
    private const float WalkBobRollDegrees = 0.65f;
    private const float WalkBobPitchDegrees = 1.05f;
    private const float WalkBobFrequency = 1.7f;
    private const float WalkBobSmoothing = 0.12f;

    private readonly VoxelWorld _world;
    private readonly float _mouseSensitivity;
    private Vector3 _velocity;
    private bool _grounded;
    private float _walkBobPhase;
    private float _walkBob;

    public Vector3 Position { get; private set; } = new(0, 96, 0);
    public float YawDegrees { get; private set; } = 180f;
    public float PitchDegrees { get; private set; }

    public float WalkSpeed { get; init; } = 5.2f;
    public float SprintSpeed { get; init; } = 8.0f;
    public float JumpSpeed { get; init; } = 7.2f;
    public float Gravity { get; init; } = 22.0f;
    public float EyeHeight { get; init; } = 1.62f;

    public Aabb Body => Aabb.FromCenterHeight(Position, new Vector3(0.72f, 1.8f, 0.72f));
    public CameraState Camera
    {
        get
        {
            var basePosition = Position + new Vector3(0, EyeHeight, 0);
            var bobAmount = _walkBob;
            var bobPhase = _walkBobPhase;
            var horizontalWave = MathF.Sin(bobPhase);
            var verticalWave = 0.5f - 0.5f * MathF.Cos(bobPhase * 2f);
            var horizontalOffset = horizontalWave * bobAmount * 0.12f;
            var verticalOffset = -verticalWave * bobAmount;
            var pitchOffset = verticalWave * bobAmount * WalkBobPitchDegrees;
            var rollOffset = horizontalWave * bobAmount * WalkBobRollDegrees;

            var yawRadians = MathF.PI / 180f * YawDegrees;
            var right = new Vector3(-MathF.Cos(yawRadians), 0f, MathF.Sin(yawRadians));
            var bobPosition = basePosition + right * horizontalOffset + new Vector3(0f, verticalOffset, 0f);

            return new CameraState(bobPosition, YawDegrees, PitchDegrees + pitchOffset, rollOffset);
        }
    }

    public FirstPersonPlayer(VoxelWorld world, float mouseSensitivity)
    {
        _world = world;
        _mouseSensitivity = mouseSensitivity;
    }

    public void SpawnAt(Vector3 position)
    {
        Position = position;
        _velocity = Vector3.Zero;
        _grounded = false;
        _walkBobPhase = 0f;
        _walkBob = 0f;
    }

    public void SpawnAt(Vector3 position, float yawDegrees, float pitchDegrees)
    {
        Position = position;
        YawDegrees = yawDegrees;
        PitchDegrees = Math.Clamp(pitchDegrees, -89f, 89f);
        _velocity = Vector3.Zero;
        _grounded = false;
        _walkBobPhase = 0f;
        _walkBob = 0f;
    }

    public PlayerSaveData CreateSaveData()
    {
        return PlayerSaveData.FromState(Position, YawDegrees, PitchDegrees);
    }

    public void Update(float dt, InputManager input)
    {
        YawDegrees -= input.MouseDelta.X * _mouseSensitivity;
        PitchDegrees = Math.Clamp(PitchDegrees - input.MouseDelta.Y * _mouseSensitivity, -89f, 89f);

        var wasGrounded = _grounded;
        var jumpRequested = wasGrounded && input.IsKeyPressed(Key.Space);
        var move = BuildMoveVector(input);
        var sprinting = input.IsKeyPressed(Key.ShiftLeft);
        var speed = sprinting ? SprintSpeed : WalkSpeed;
        var tickScale = dt * 20f;

        ApplyHorizontalFriction(wasGrounded && !jumpRequested, tickScale);
        ApplyHorizontalAcceleration(move, speed, wasGrounded, sprinting, tickScale);

        _velocity.Y -= Gravity * dt;
        _velocity.Y *= MathF.Pow(VerticalDrag, tickScale);

        if (jumpRequested)
        {
            _velocity.Y = JumpSpeed;
            ApplySprintJumpBoost(move, sprinting);
            _grounded = false;
        }

        var result = VoxelCollisionResolver.Move(_world, Body, _velocity, dt);
        Position += result.Delta;
        _velocity = result.Velocity;
        _grounded = result.Grounded;
        UpdateCameraBobbing(dt, move, result.Grounded);
    }

    public Ray3 CreateLookRay() => new(Camera.Position, Camera.Forward);

    private Vector3 BuildMoveVector(InputManager input)
    {
        var yaw = MathF.PI / 180f * YawDegrees;
        var forward = Vector3.Normalize(new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw)));
        var right = Vector3.Normalize(new Vector3(-forward.Z, 0, forward.X));
        var move = Vector3.Zero;

        if (input.IsKeyPressed(Key.W)) move += forward;
        if (input.IsKeyPressed(Key.S)) move -= forward;
        if (input.IsKeyPressed(Key.D)) move += right;
        if (input.IsKeyPressed(Key.A)) move -= right;

        return move.LengthSquared() > 0.001f ? Vector3.Normalize(move) : Vector3.Zero;
    }

    private void ApplyHorizontalFriction(bool grounded, float tickScale)
    {
        var friction = grounded ? GroundFriction : AirFriction;
        var frictionFactor = MathF.Pow(friction, tickScale);

        _velocity.X *= frictionFactor;
        _velocity.Z *= frictionFactor;

        if (MathF.Abs(_velocity.X) < HorizontalStopEpsilon)
        {
            _velocity.X = 0f;
        }

        if (MathF.Abs(_velocity.Z) < HorizontalStopEpsilon)
        {
            _velocity.Z = 0f;
        }
    }

    private void ApplyHorizontalAcceleration(Vector3 move, float speed, bool grounded, bool sprinting, float tickScale)
    {
        if (move.LengthSquared() <= 0.001f)
        {
            return;
        }

        var acceleration = grounded
            ? speed * (1f - GroundFriction)
            : speed * (sprinting ? SprintAirAccelerationFactor : AirAccelerationFactor);

        _velocity.X += move.X * acceleration * tickScale;
        _velocity.Z += move.Z * acceleration * tickScale;
    }

    private void ApplySprintJumpBoost(Vector3 move, bool sprinting)
    {
        if (!sprinting || move.LengthSquared() <= 0.001f)
        {
            return;
        }

        _velocity.X += move.X * SprintJumpBoost;
        _velocity.Z += move.Z * SprintJumpBoost;
    }

    private void UpdateCameraBobbing(float dt, Vector3 move, bool grounded)
    {
        var walking = grounded && move.LengthSquared() > 0.001f;

        if (walking)
        {
            _walkBobPhase += dt * WalkBobFrequency * MathF.PI * 2f;
        }

        var targetBob = walking ? WalkBobAmplitude : 0f;

        _walkBob += (targetBob - _walkBob) * WalkBobSmoothing;
    }
}
