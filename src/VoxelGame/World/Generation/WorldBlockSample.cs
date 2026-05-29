namespace VoxelGame.World.Generation;

public readonly record struct WorldBlockSample(WorldColumnSample Column, float CaveValue)
{
    public bool IsCave => CaveValue >= 0.5f;
}
