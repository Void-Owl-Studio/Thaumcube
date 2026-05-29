namespace VoxelGame.World.Generation;

public readonly record struct WorldColumnSample(
    int BaseHeight,
    int SurfaceHeight,
    int WaterLevel,
    float Continentalness,
    float Erosion,
    float PeaksAndValleys,
    float Temperature,
    float Moisture,
    float River,
    float Lake,
    float Roughness,
    BiomeType Biome)
{
    public bool HasSurfaceWater => WaterLevel > SurfaceHeight;
    public bool IsOcean => Biome == BiomeType.Ocean;
    public bool IsRiver => Biome == BiomeType.River;
    public bool IsLake => Biome == BiomeType.Lake;
    public int VisibleSurfaceHeight => HasSurfaceWater ? WaterLevel : SurfaceHeight;
    public float WaterInfluence => MathF.Max(River, Lake);
}
