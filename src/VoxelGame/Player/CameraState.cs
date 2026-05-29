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

    public Matrix4x4 ViewMatrix
    {
        get
        {
            var forward = Forward;
            var rollRadians = MathF.PI / 180f * RollDegrees;
            var up = Vector3.Transform(Vector3.UnitY, Matrix4x4.CreateFromAxisAngle(forward, rollRadians));
            return Matrix4x4.CreateLookAt(Position, Position + forward, up);
        }
    }
}
