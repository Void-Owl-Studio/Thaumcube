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
    public string WorldsRootPath { get; init; } = Path.Combine(Directory.GetCurrentDirectory(), "worlds");
    public float MouseSensitivity { get; init; } = 0.12f;
    public float CloudHeightBlocks { get; init; } = 90f;
    public float CloudTileSizeBlocks { get; init; } = 384f;
    public int CloudTileRadius { get; init; } = 1;
    public float SunDistanceBlocks { get; init; } = 180f;
    public float SunSizeBlocks { get; init; } = 28f;
    public float CameraFieldOfViewDegrees { get; init; } = 70f;
    public float CameraNearPlane { get; init; } = 0.1f;
    public float CameraFarPlane { get; init; } = 256f;
    public Vector3 SunLightDirection { get; init; } = Vector3.Normalize(new Vector3(0.35f, 0.85f, 0.25f));
    public float AmbientLightStrength { get; init; } = 0.34f;
    public float DiffuseLightStrength { get; init; } = 0.88f;
    public float SkyLightStrength { get; init; } = 0.18f;
    public float FogStartDistanceRatio { get; init; } = 0.78f;
    public float FogFullDistanceRatio { get; init; } = 0.98f;
    public float FogHeightFalloff { get; init; } = 0.00065f;
    public Vector3 FogColor { get; init; } = new(0.74f, 0.78f, 0.80f);
    public Vector3 SkyLightColor { get; init; } = new(1.0f, 0.98f, 0.94f);
    public Vector3 SkyClearColor { get; init; } = new(0.66f, 0.84f, 0.98f);
}
