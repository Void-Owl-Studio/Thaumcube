namespace VoxelGame.Shared;

public sealed class ChunkStreamingOptions
{
    public int MaxChunkLoadsPerTick { get; init; } = 1;
    public int MaxMeshUploadsPerFrame { get; init; } = 2;
    public int MaxUnloadsPerFrame { get; init; } = 1;
}
