using VoxelGame.World.Chunks;

namespace VoxelGame.World.Generation;

public sealed class WorldGenerationSettings
{
    public int SeaLevel { get; init; } = 63;
    public int BedrockThickness { get; init; } = 2;
    public int MaxTerrainHeight { get; init; } = Chunk.MaxY - 2;
    public HeightMapSettings Height { get; init; } = new();
    public TemperatureSettings Temperature { get; init; } = new();
    public MoistureSettings Moisture { get; init; } = new();
    public RiverSettings Rivers { get; init; } = new();
    public LakeSettings Lakes { get; init; } = new();
    public RoughnessSettings Roughness { get; init; } = new();
    public CaveSettings Caves { get; init; } = new();
    public CrystalSettings Crystals { get; init; } = new();
    public TreeSettings Trees { get; init; } = new();

    public static WorldGenerationSettings CreateDefault() => new();
}

public sealed class HeightMapSettings
{
    public float BaseLandHeight { get; init; } = 63f;

    public NoiseLayerSettings Continentalness { get; init; } = new()
    {
        Scale = 640f,
        Octaves = 5,
        Persistence = 0.52f,
        Lacunarity = 2.05f,
        HeightMultiplier = 1f,
        OffsetX = 913,
        OffsetZ = -271
    };

    public NoiseLayerSettings Erosion { get; init; } = new()
    {
        Scale = 340f,
        Octaves = 4,
        Persistence = 0.55f,
        Lacunarity = 2.1f,
        HeightMultiplier = 1f,
        OffsetX = -1411,
        OffsetZ = 733
    };

    public NoiseLayerSettings PeaksAndValleys { get; init; } = new()
    {
        Scale = 180f,
        Octaves = 4,
        Persistence = 0.58f,
        Lacunarity = 2.18f,
        HeightMultiplier = 1f,
        OffsetX = 2783,
        OffsetZ = -1181
    };

    public NoiseLayerSettings Detail { get; init; } = new()
    {
        Scale = 62f,
        Octaves = 3,
        Persistence = 0.45f,
        Lacunarity = 2.35f,
        HeightMultiplier = 1f,
        OffsetX = -391,
        OffsetZ = 1987
    };

    public float OceanThreshold { get; init; } = 0.12f;
    public float OceanDepth { get; init; } = 24f;
    public float PlainsHeight { get; init; } = 18f;
    public float HillHeight { get; init; } = 8f;
    public float MountainHeight { get; init; } = 24f;
    public float PeakHeight { get; init; } = 14f;
    public float ValleyDepth { get; init; } = 6f;
    public float HeightMultiplier { get; init; } = 1f;
}

public sealed class TemperatureSettings
{
    public NoiseLayerSettings Noise { get; init; } = new()
    {
        Scale = 768f,
        Octaves = 4,
        Persistence = 0.56f,
        Lacunarity = 2.0f,
        HeightMultiplier = 1f,
        OffsetX = -3001,
        OffsetZ = 441
    };

    public float LatitudeBandScale { get; init; } = 2048f;
    public float LatitudeStrength { get; init; } = 0.14f;
    public float NoiseStrength { get; init; } = 0.74f;
    public float Baseline { get; init; } = 0.08f;
    public float AltitudeCooling { get; init; } = 0.42f;
    public float AltitudeCoolingRange { get; init; } = 58f;
}

public sealed class MoistureSettings
{
    public NoiseLayerSettings BroadNoise { get; init; } = new()
    {
        Scale = 704f,
        Octaves = 4,
        Persistence = 0.58f,
        Lacunarity = 2.0f,
        HeightMultiplier = 1f,
        OffsetX = 2501,
        OffsetZ = -901
    };

    public NoiseLayerSettings DetailNoise { get; init; } = new()
    {
        Scale = 220f,
        Octaves = 3,
        Persistence = 0.5f,
        Lacunarity = 2.28f,
        HeightMultiplier = 1f,
        OffsetX = -721,
        OffsetZ = 1903
    };

    public float Baseline { get; init; } = 0.02f;
    public float BroadNoiseStrength { get; init; } = 0.56f;
    public float DetailNoiseStrength { get; init; } = 0.22f;
    public float WaterBoost { get; init; } = 0.26f;
    public float AltitudeDrying { get; init; } = 0.22f;
    public float AltitudeDryingRange { get; init; } = 48f;
}

public sealed class RiverSettings
{
    public int CellSize { get; init; } = 4;
    public int TileSizeCells { get; init; } = 64;
    public int TileMarginCells { get; init; } = 24;
    public int MaxRiverSteps { get; init; } = 280;
    public float SourceNoiseScale { get; init; } = 160f;
    public float MinSourceHeightAboveSea { get; init; } = 12f;
    public float MinSourceSlope { get; init; } = 1.3f;
    public float SourceThreshold { get; init; } = 0.71f;
    public float FlowGainPerStep { get; init; } = 0.010f;
    public float FlowThreshold { get; init; } = 1.18f;
    public float ChannelDepth { get; init; } = 4.5f;
    public float WidthContribution { get; init; } = 0.35f;
}

public sealed class LakeSettings
{
    public int MaxRadiusCells { get; init; } = 7;
    public float MaxDepth { get; init; } = 4.5f;
    public float MinimumDepth { get; init; } = 0.7f;
    public float FlowDepthMultiplier { get; init; } = 2.8f;
}

public sealed class RoughnessSettings
{
    public NoiseLayerSettings GeneralNoise { get; init; } = new()
    {
        Scale = 28f,
        Octaves = 3,
        Persistence = 0.5f,
        Lacunarity = 2.2f,
        HeightMultiplier = 1f,
        OffsetX = 301,
        OffsetZ = -7021
    };

    public NoiseLayerSettings DetailNoise { get; init; } = new()
    {
        Scale = 12f,
        Octaves = 2,
        Persistence = 0.45f,
        Lacunarity = 2.4f,
        HeightMultiplier = 1f,
        OffsetX = -1103,
        OffsetZ = 5021
    };

    public float PlainsAmplitude { get; init; } = 0.28f;
    public float ForestAmplitude { get; init; } = 0.48f;
    public float DesertAmplitude { get; init; } = 0.42f;
    public float SavannaAmplitude { get; init; } = 0.36f;
    public float SwampAmplitude { get; init; } = 0.20f;
    public float TaigaAmplitude { get; init; } = 0.44f;
    public float SnowAmplitude { get; init; } = 0.34f;
    public float MountainAmplitude { get; init; } = 1.45f;
    public float BeachAmplitude { get; init; } = 0.08f;
}

public sealed class CaveSettings
{
    public int MinY { get; init; } = Chunk.MinY + 6;
    public int MaxY { get; init; } = Chunk.MaxY - 8;
    public int MinSurfaceDepth { get; init; } = 10;
    public float TunnelScale { get; init; } = 34f;
    public float SpaghettiScale { get; init; } = 26f;
    public float CavernScale { get; init; } = 58f;
    public float Density { get; init; } = 0.72f;
    public float CarveThreshold { get; init; } = 0.62f;
    public float TunnelThreshold { get; init; } = 0.70f;
    public float SpaghettiThreshold { get; init; } = 0.76f;
    public float CavernThreshold { get; init; } = 0.83f;
    public float TunnelRadius { get; init; } = 1.55f;
    public float SpiralRadius { get; init; } = 2.45f;
    public float CavernRadius { get; init; } = 4.8f;
    public int FeatureCellSize { get; init; } = 32;
    public float TunnelChance { get; init; } = 0.14f;
    public float SpiralChance { get; init; } = 0.06f;
}

public sealed class CrystalSettings
{
    public float SpawnChance { get; init; } = 0.17f;
    public int MinHeight { get; init; } = 2;
    public int MaxHeight { get; init; } = 5;
}

public sealed class TreeSettings
{
    public int CellSize { get; init; } = 8;
    public int MinTrunkHeight { get; init; } = 4;
    public int MaxTrunkHeight { get; init; } = 6;
    public float PlainsChance { get; init; } = 0.10f;
    public float ForestChance { get; init; } = 0.58f;
    public float SwampChance { get; init; } = 0.18f;
    public float SavannaChance { get; init; } = 0.06f;
}

public sealed class NoiseLayerSettings
{
    public float Scale { get; init; } = 64f;
    public int Octaves { get; init; } = 4;
    public float Persistence { get; init; } = 0.5f;
    public float Lacunarity { get; init; } = 2f;
    public float HeightMultiplier { get; init; } = 1f;
    public int OffsetX { get; init; }
    public int OffsetY { get; init; }
    public int OffsetZ { get; init; }
}
