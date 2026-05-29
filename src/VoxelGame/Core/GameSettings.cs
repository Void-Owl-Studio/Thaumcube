namespace VoxelGame.Core;

public sealed class GameSettings
{
    public string WindowTitle { get; init; } = "VoxelGame - Arcane Voxel Prototype";
    public int WindowWidth { get; init; } = 1280;
    public int WindowHeight { get; init; } = 720;
    public int WorldSeed { get; init; } = 734_241;
    public int RenderDistanceChunks { get; init; } = 1;
    public int MinRenderDistanceChunks { get; init; } = 1;
    public int MaxRenderDistanceChunks { get; init; } = 5;
    public float MouseSensitivity { get; init; } = 0.12f;
}
