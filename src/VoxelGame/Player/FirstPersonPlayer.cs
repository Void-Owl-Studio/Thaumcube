using System.Numerics;
using Silk.NET.Input;
using VoxelGame.Input;
using VoxelGame.Physics;
using VoxelGame.World;

namespace VoxelGame.Player;

public sealed class FirstPersonPlayer
{
    private readonly VoxelWorld _world;
    private readonly float _mouseSensitivity;
    private Vector3 _velocity;
    private bool _grounded;

    public Vector3 Position { get; private set; } = new(0, 96, 0);
    public float YawDegrees { get; private set; } = 180f;
    public float PitchDegrees { get; private set; }

    public float WalkSpeed { get; init; } = 5.2f;
    public float SprintSpeed { get; init; } = 8.0f;
    public float JumpSpeed { get; init; } = 7.2f;
    public float Gravity { get; init; } = 22.0f;
    public float EyeHeight { get; init; } = 1.62f;

    public Aabb Body => Aabb.FromCenterHeight(Position, new Vector3(0.72f, 1.8f, 0.72f));
    public CameraState Camera => new(Position + new Vector3(0, EyeHeight, 0), YawDegrees, PitchDegrees);

    public FirstPersonPlayer(VoxelWorld world, float mouseSensitivity)
    {
        _world = world;
        _mouseSensitivity = mouseSensitivity;
    }

    public void SpawnAt(Vector3 position)
    {
        Position = position;
        _velocity = Vector3.Zero;
    }

    public void Update(float dt, InputManager input)
    {
        YawDegrees -= input.MouseDelta.X * _mouseSensitivity;
        PitchDegrees = Math.Clamp(PitchDegrees - input.MouseDelta.Y * _mouseSensitivity, -89f, 89f);

        var move = BuildMoveVector(input);
        var speed = input.IsKeyPressed(Key.ShiftLeft) ? SprintSpeed : WalkSpeed;

        _velocity.X = move.X * speed;
        _velocity.Z = move.Z * speed;
        _velocity.Y -= Gravity * dt;

        if (_grounded && input.IsKeyPressed(Key.Space))
        {
            _velocity.Y = JumpSpeed;
            _grounded = false;
        }

        var result = VoxelCollisionResolver.Move(_world, Body, _velocity, dt);
        Position += result.Delta;
        _velocity = result.Velocity;
        _grounded = result.Grounded;
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
}
