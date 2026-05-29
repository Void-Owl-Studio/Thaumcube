using System.Numerics;

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
    public float CloudHeightBlocks { get; init; } = 90f;
    public float CloudTileSizeBlocks { get; init; } = 384f;
    public int CloudTileRadius { get; init; } = 1;
    public float SunDistanceBlocks { get; init; } = 180f;
    public float SunSizeBlocks { get; init; } = 28f;
    public Vector3 SkyClearColor { get; init; } = new(0.66f, 0.84f, 0.98f);
}
