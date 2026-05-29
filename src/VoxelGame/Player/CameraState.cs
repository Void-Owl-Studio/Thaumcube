using System.Numerics;

namespace VoxelGame.Player;

public readonly record struct CameraState(Vector3 Position, float YawDegrees, float PitchDegrees, float RollDegrees = 0f)
{
    public Vector3 Forward
    {
        get
        {
            var yaw = MathF.PI / 180f * YawDegrees;
            var pitch = MathF.PI / 180f * PitchDegrees;
            return Vector3.Normalize(new Vector3(
                MathF.Cos(pitch) * MathF.Sin(yaw),
                MathF.Sin(pitch),
                MathF.Cos(pitch) * MathF.Cos(yaw)));
        }
    }

    public Vector3 Up
    {
        get
        {
            var forward = Forward;
            var rollRadians = MathF.PI / 180f * RollDegrees;
            return Vector3.Normalize(Vector3.Transform(Vector3.UnitY, Quaternion.CreateFromAxisAngle(forward, rollRadians)));
        }
    }

    public Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, Up));

    public Matrix4x4 ViewMatrix
    {
        get
        {
            var forward = Forward;
            return Matrix4x4.CreateLookAt(Position, Position + forward, Up);
        }
    }
}
