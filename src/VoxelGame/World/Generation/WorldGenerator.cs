using System.Numerics;
using VoxelGame.World.Chunks;

namespace VoxelGame.World.Generation;

public sealed class WorldGenerator
{
    private readonly WorldGenerationSettings _settings;
    private readonly NoiseGenerator _noise;
    private readonly ChunkGenerator _chunkGenerator;
    public int Seed { get; }
    public WorldGenerationSettings Settings => _settings;

    public WorldGenerator(int seed, WorldGenerationSettings? settings = null)
    {
        Seed = seed;
        _settings = settings ?? WorldGenerationSettings.CreateDefault();
        _noise = new NoiseGenerator(seed);

        var heightMapGenerator = new HeightMapGenerator(_noise, _settings);
        var temperatureMapGenerator = new TemperatureMapGenerator(_noise, _settings);
        var moistureMapGenerator = new MoistureMapGenerator(_noise, _settings);
        var lakeGenerator = new LakeGenerator(_settings);
        var riverGenerator = new RiverGenerator(_noise, _settings, heightMapGenerator, lakeGenerator);
        var biomeGenerator = new BiomeGenerator(_settings);
        var roughnessGenerator = new RoughnessGenerator(_noise, _settings);
        var caveGenerator = new CaveGenerator(_noise, _settings);

        _chunkGenerator = new ChunkGenerator(
            _settings,
            _noise,
            heightMapGenerator,
            temperatureMapGenerator,
            moistureMapGenerator,
            riverGenerator,
            biomeGenerator,
            roughnessGenerator,
            caveGenerator);
    }

    public void Generate(Chunk chunk)
    {
        chunk.Aura = BuildAura(chunk.Coord);
        _chunkGenerator.Generate(chunk);
    }

    public int GetTerrainHeight(int worldX, int worldZ) => _chunkGenerator.SampleColumn(worldX, worldZ).VisibleSurfaceHeight;

    public Vector3 GetGrassTint(int worldX, int worldY, int worldZ)
    {
        var column = _chunkGenerator.SampleColumn(worldX, worldZ);
        var tint = GrassColorMap.Sample(column.Temperature, column.Moisture);
        return column.Biome switch
        {
            BiomeType.Swamp => Lerp(tint, new Vector3(0.18f, 0.42f, 0.18f), 0.35f),
            BiomeType.Taiga => Lerp(tint, new Vector3(0.34f, 0.46f, 0.30f), 0.22f),
            BiomeType.Snow => new Vector3(0.82f, 0.86f, 0.84f),
            BiomeType.Savanna => Lerp(tint, new Vector3(0.56f, 0.58f, 0.22f), 0.28f),
            _ => tint
        };
    }

    public Vector3 GetFoliageTint(int worldX, int worldY, int worldZ)
    {
        var column = _chunkGenerator.SampleColumn(worldX, worldZ);
        var tint = GrassColorMap.Sample(column.Temperature, column.Moisture);
        return column.Biome switch
        {
            BiomeType.Swamp => Lerp(tint, new Vector3(0.24f, 0.44f, 0.20f), 0.32f),
            BiomeType.Savanna => Lerp(tint, new Vector3(0.64f, 0.62f, 0.25f), 0.24f),
            BiomeType.Snow => new Vector3(0.76f, 0.82f, 0.78f),
            _ => tint
        };
    }

    public WorldColumnSample GetColumnSample(int worldX, int worldZ) => _chunkGenerator.SampleColumn(worldX, worldZ);

    public BiomeType GetBiome(int worldX, int worldZ) => _chunkGenerator.SampleColumn(worldX, worldZ).Biome;

    public float GetTemperature(int worldX, int worldZ) => _chunkGenerator.SampleColumn(worldX, worldZ).Temperature;

    public float GetCaveValue(int worldX, int worldY, int worldZ) => _chunkGenerator.GetCaveValue(worldX, worldY, worldZ);

    public WorldBlockSample GetBlockSample(int worldX, int worldY, int worldZ) => _chunkGenerator.SampleBlock(worldX, worldY, worldZ);

    private ChunkAura BuildAura(ChunkCoord coord)
    {
        var worldX = coord.X * Chunk.SizeX + Chunk.SizeX / 2;
        var worldZ = coord.Z * Chunk.SizeZ + Chunk.SizeZ / 2;
        var column = _chunkGenerator.SampleColumn(worldX, worldZ);
        var density = 0.25f + column.Moisture * 0.55f + column.River * 0.20f;
        var stability = 1f - MathF.Abs(column.Continentalness - 0.55f);
        var type = column.Biome switch
        {
            BiomeType.Mountains => "Highland",
            BiomeType.Ocean => "Tidal",
            BiomeType.Swamp => "Mist",
            BiomeType.Snow => "Frost",
            _ => "Verdant"
        };

        return new ChunkAura(density, column.Lake, stability, type);
    }

    private static Vector3 Lerp(Vector3 a, Vector3 b, float t)
    {
        return a + (b - a) * t;
    }
}
