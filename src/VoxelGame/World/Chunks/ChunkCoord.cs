namespace VoxelGame.World.Chunks;

public readonly record struct ChunkCoord(int X, int Z)
{
    public override string ToString() => $"{X},{Z}";
}
