using VoxelGame.World.Blocks;
using VoxelGame.World.Chunks;

namespace VoxelGame.World.Generation;

public sealed class WorldGenerator
{
    private readonly DeterministicNoise _noise;
    public int Seed { get; }

    public WorldGenerator(int seed)
    {
        Seed = seed;
        _noise = new DeterministicNoise(seed);
    }

    public void Generate(Chunk chunk)
    {
        chunk.Aura = BuildAura(chunk.Coord);
        var baseX = chunk.Coord.X * Chunk.SizeX;
        var baseZ = chunk.Coord.Z * Chunk.SizeZ;

        for (var z = 0; z < Chunk.SizeZ; z++)
        for (var x = 0; x < Chunk.SizeX; x++)
        {
            var worldX = baseX + x;
            var worldZ = baseZ + z;
            var height = GetTerrainHeight(worldX, worldZ);
            var corruption = GetCorruption(worldX, worldZ);
            var lowTerrain = height < 57;

            for (var y = 0; y < Chunk.SizeY; y++)
            {
                var block = BuildTerrainBlock(worldX, y, worldZ, height, corruption, lowTerrain);
                chunk.SetGeneratedBlock(x, y, z, block);
            }
        }

        AddCrystalFormation(chunk);
        chunk.MarkDirty();
    }

    public int GetTerrainHeight(int worldX, int worldZ)
    {
        var hills = _noise.Fractal2D(worldX, worldZ, 0.012f, 4);
        var ridges = _noise.Fractal2D(worldX + 2000, worldZ - 2000, 0.035f, 2);
        return 50 + (int)(hills * 24f) + (int)(ridges * 6f);
    }

    public System.Numerics.Vector3 GetGrassTint(int worldX, int worldY, int worldZ)
    {
        var temperatureNoise = _noise.Fractal2D(worldX - 1900, worldZ + 1400, 0.0042f, 3);
        var moistureNoise = _noise.Fractal2D(worldX + 700, worldZ - 1100, 0.0048f, 3);
        var elevationCooling = Math.Clamp((worldY - 52) / 42f, 0f, 0.35f);
        var temperature = Math.Clamp(0.62f + temperatureNoise * 0.30f - elevationCooling, 0f, 1f);
        var humidity = Math.Clamp(0.58f + moistureNoise * 0.34f + (0.5f - elevationCooling) * 0.08f, 0f, 1f);
        return GrassColorMap.Sample(temperature, humidity);
    }

    private BlockType BuildTerrainBlock(int x, int y, int z, int height, float corruption, bool lowTerrain)
    {
        if (y > height)
        {
            return y <= 54 ? BlockType.Water : BlockType.Air;
        }

        if (y < height - 6)
        {
            var cave = _noise.Value3D(x, y, z, 0.052f);
            if (y > 12 && cave > 0.72f)
            {
                return BlockType.Air;
            }

            var ore = _noise.Value3D(x + 400, y - 80, z - 900, 0.085f);
            if (y < 44 && ore > 0.83f)
            {
                return BlockType.MagicOre;
            }

            return BlockType.Stone;
        }

        if (y == height)
        {
            if (corruption > 0.68f)
            {
                return BlockType.CorruptedGrass;
            }

            return lowTerrain ? BlockType.Sand : BlockType.Grass;
        }

        return lowTerrain ? BlockType.Sand : BlockType.Dirt;
    }

    private void AddCrystalFormation(Chunk chunk)
    {
        var chance = _noise.Value2D(chunk.Coord.X, chunk.Coord.Z, 0.6f);
        if (chance < 0.82f)
        {
            return;
        }

        var localX = 3 + (int)(_noise.Value2D(chunk.Coord.X + 11, chunk.Coord.Z - 7, 1.1f) * 10);
        var localZ = 3 + (int)(_noise.Value2D(chunk.Coord.X - 17, chunk.Coord.Z + 5, 1.1f) * 10);
        var worldX = chunk.Coord.X * Chunk.SizeX + localX;
        var worldZ = chunk.Coord.Z * Chunk.SizeZ + localZ;
        var surface = Math.Clamp(GetTerrainHeight(worldX, worldZ), 8, Chunk.SizeY - 8);
        var height = 2 + (int)(_noise.Value2D(worldX, worldZ, 0.17f) * 4);

        for (var y = 0; y < height; y++)
        {
            chunk.SetGeneratedBlock(localX, surface + y + 1, localZ, BlockType.ArcaneCrystal);
            if (y == 0)
            {
                TrySet(chunk, localX + 1, surface + 1, localZ, BlockType.ArcaneCrystal);
                TrySet(chunk, localX, surface + 1, localZ + 1, BlockType.ArcaneCrystal);
            }
        }
    }

    private ChunkAura BuildAura(ChunkCoord coord)
    {
        var density = _noise.Value2D(coord.X, coord.Z, 0.16f);
        var corruption = _noise.Value2D(coord.X + 900, coord.Z - 350, 0.10f);
        var stability = 1f - MathF.Abs(density - 0.5f);
        var type = corruption > 0.68f ? "Blight" : density > 0.62f ? "Crystal" : "Verdant";
        return new ChunkAura(density, corruption, stability, type);
    }

    private float GetCorruption(int worldX, int worldZ)
    {
        return _noise.Fractal2D(worldX + 1500, worldZ - 800, 0.007f, 3);
    }

    private static void TrySet(Chunk chunk, int x, int y, int z, BlockType type)
    {
        if (Chunk.ContainsLocal(x, y, z))
        {
            chunk.SetGeneratedBlock(x, y, z, type);
        }
    }
}
