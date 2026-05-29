namespace VoxelGame.World.Storage;

public sealed record WorldMetadata
{
    public string Name { get; init; } = string.Empty;
    public int Seed { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime LastPlayedUtc { get; init; }
}
