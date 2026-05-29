using System.Numerics;

namespace VoxelGame.World.Storage;

public sealed class PlayerSaveData
{
    public float PositionX { get; init; }
    public float PositionY { get; init; }
    public float PositionZ { get; init; }
    public float YawDegrees { get; init; }
    public float PitchDegrees { get; init; }

    public Vector3 Position => new(PositionX, PositionY, PositionZ);

    public static PlayerSaveData FromState(Vector3 position, float yawDegrees, float pitchDegrees)
    {
        return new PlayerSaveData
        {
            PositionX = position.X,
            PositionY = position.Y,
            PositionZ = position.Z,
            YawDegrees = yawDegrees,
            PitchDegrees = pitchDegrees
        };
    }
}
