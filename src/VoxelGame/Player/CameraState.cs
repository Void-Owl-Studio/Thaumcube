using System.Numerics;

namespace VoxelGame.Player;

public readonly record struct CameraState(Vector3 Position, float YawDegrees, float PitchDegrees)
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
}
