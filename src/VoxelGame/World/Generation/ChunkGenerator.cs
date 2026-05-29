using VoxelGame.World.Blocks;
using VoxelGame.World.Chunks;

namespace VoxelGame.World.Generation;

public sealed class ChunkGenerator
{
    private const int TreeLeafRadius = 2;
    private readonly WorldGenerationSettings _settings;
    private readonly HeightMapGenerator _heightMapGenerator;
    private readonly TemperatureMapGenerator _temperatureMapGenerator;
    private readonly MoistureMapGenerator _moistureMapGenerator;
    private readonly RiverGenerator _riverGenerator;
    private readonly BiomeGenerator _biomeGenerator;
    private readonly RoughnessGenerator _roughnessGenerator;
    private readonly CaveGenerator _caveGenerator;
    private readonly NoiseGenerator _noise;

    public ChunkGenerator(
        WorldGenerationSettings settings,
        NoiseGenerator noise,
        HeightMapGenerator heightMapGenerator,
        TemperatureMapGenerator temperatureMapGenerator,
        MoistureMapGenerator moistureMapGenerator,
        RiverGenerator riverGenerator,
        BiomeGenerator biomeGenerator,
        RoughnessGenerator roughnessGenerator,
        CaveGenerator caveGenerator)
    {
        _settings = settings;
        _noise = noise;
        _heightMapGenerator = heightMapGenerator;
        _temperatureMapGenerator = temperatureMapGenerator;
        _moistureMapGenerator = moistureMapGenerator;
        _riverGenerator = riverGenerator;
        _biomeGenerator = biomeGenerator;
        _roughnessGenerator = roughnessGenerator;
        _caveGenerator = caveGenerator;
    }

    public void Generate(Chunk chunk)
    {
        var baseX = chunk.Coord.X * Chunk.SizeX;
        var baseZ = chunk.Coord.Z * Chunk.SizeZ;
        var caveSettings = _settings.Caves;

        for (var z = 0; z < Chunk.SizeZ; z++)
        {
            for (var x = 0; x < Chunk.SizeX; x++)
            {
                var worldX = baseX + x;
                var worldZ = baseZ + z;
                var column = SampleColumn(worldX, worldZ);
                FillColumn(chunk, x, worldX, z, worldZ, column, caveSettings);
            }
        }

        AddCrystalFormation(chunk);
        AddTrees(chunk);
        chunk.MarkDirty();
    }

    public WorldColumnSample SampleColumn(int worldX, int worldZ)
    {
        var heightSample = _heightMapGenerator.Sample(worldX, worldZ);
        var hydrology = _riverGenerator.Sample(worldX, worldZ);
        var carvedHeight = heightSample.ExactHeight - hydrology.RiverCarve * _settings.Rivers.ChannelDepth;
        var baseSurfaceHeight = (int)MathF.Round(Math.Clamp(carvedHeight, _settings.BedrockThickness + 2, _settings.MaxTerrainHeight));
        var preliminaryWaterLevel = ResolveWaterLevel(heightSample.ExactHeight, hydrology, baseSurfaceHeight);

        var preliminaryTemperature = _temperatureMapGenerator.Sample(worldX, worldZ, carvedHeight);
        var preliminaryMoisture = _moistureMapGenerator.Sample(worldX, worldZ, carvedHeight, preliminaryTemperature, hydrology.River, hydrology.Lake);
        var preliminaryBiome = _biomeGenerator.ChooseBiome(
            baseSurfaceHeight,
            preliminaryWaterLevel,
            heightSample.PeaksAndValleys,
            preliminaryTemperature,
            preliminaryMoisture,
            hydrology.River,
            hydrology.Lake);

        var roughness = _roughnessGenerator.Sample(worldX, worldZ, preliminaryBiome, heightSample.NormalizedHeight);
        var finalSurfaceHeight = (int)MathF.Round(Math.Clamp(carvedHeight + roughness, _settings.BedrockThickness + 2, _settings.MaxTerrainHeight));
        var finalTemperature = _temperatureMapGenerator.Sample(worldX, worldZ, finalSurfaceHeight);
        var finalMoisture = _moistureMapGenerator.Sample(worldX, worldZ, finalSurfaceHeight, finalTemperature, hydrology.River, hydrology.Lake);
        var finalWaterLevel = ResolveWaterLevel(heightSample.ExactHeight, hydrology, finalSurfaceHeight);

        var biome = _biomeGenerator.ChooseBiome(
            finalSurfaceHeight,
            finalWaterLevel,
            heightSample.PeaksAndValleys,
            finalTemperature,
            finalMoisture,
            hydrology.River,
            hydrology.Lake);

        return new WorldColumnSample(
            BaseHeight: heightSample.Height,
            SurfaceHeight: finalSurfaceHeight,
            WaterLevel: finalWaterLevel,
            Continentalness: heightSample.Continentalness,
            Erosion: heightSample.Erosion,
            PeaksAndValleys: heightSample.PeaksAndValleys,
            Temperature: finalTemperature,
            Moisture: finalMoisture,
            River: hydrology.River,
            Lake: hydrology.Lake,
            Roughness: roughness,
            Biome: biome);
    }

    // Example usage:
    // var column = chunkGenerator.SampleColumn(worldX, worldZ);
    // var biome = column.Biome;
    // var caveValue = chunkGenerator.GetCaveValue(worldX, worldY, worldZ);
    public WorldBlockSample SampleBlock(int worldX, int worldY, int worldZ)
    {
        var column = SampleColumn(worldX, worldZ);
        return new WorldBlockSample(column, _caveGenerator.Sample(worldX, worldY, worldZ, column.SurfaceHeight));
    }

    public float GetCaveValue(int worldX, int worldY, int worldZ)
    {
        var column = SampleColumn(worldX, worldZ);
        return _caveGenerator.Sample(worldX, worldY, worldZ, column.SurfaceHeight);
    }

    private void FillColumn(Chunk chunk, int localX, int worldX, int localZ, int worldZ, WorldColumnSample column, CaveSettings caveSettings)
    {
        FillRange(chunk, localX, localZ, Chunk.MinY, Math.Min(_settings.BedrockThickness, column.SurfaceHeight), BlockType.Stone);

        if (column.SurfaceHeight < Chunk.MinY)
        {
            return;
        }

        var fillerStartY = Math.Max(_settings.BedrockThickness + 1, column.SurfaceHeight - 4);
        var solidTopY = fillerStartY - 1;
        var caveStartY = Math.Max(_settings.BedrockThickness + 1, caveSettings.MinY);
        var caveTopY = Math.Min(solidTopY, Math.Min(caveSettings.MaxY, column.SurfaceHeight - caveSettings.MinSurfaceDepth));
        var nonCaveTopY = solidTopY;

        if (caveStartY <= caveTopY)
        {
            FillDeepRange(chunk, localX, worldX, localZ, worldZ, caveTopY + 1, solidTopY, column);
            nonCaveTopY = caveStartY - 1;

            for (var y = caveStartY; y <= caveTopY; y++)
            {
                if (_caveGenerator.Sample(worldX, y, worldZ, column.SurfaceHeight) >= caveSettings.CarveThreshold)
                {
                    continue;
                }

                chunk.SetGeneratedBlock(localX, y, localZ, ResolveDeepBlock(worldX, y, worldZ, column));
            }
        }

        FillDeepRange(chunk, localX, worldX, localZ, worldZ, _settings.BedrockThickness + 1, nonCaveTopY, column);
        FillRange(chunk, localX, localZ, fillerStartY, column.SurfaceHeight - 1, GetFillerBlock(column));

        if (column.SurfaceHeight >= _settings.BedrockThickness + 1)
        {
            chunk.SetGeneratedBlock(localX, column.SurfaceHeight, localZ, GetSurfaceBlock(column));
        }

        if (column.WaterLevel != int.MinValue)
        {
            FillRange(chunk, localX, localZ, column.SurfaceHeight + 1, column.WaterLevel, BlockType.Water);
        }
    }

    private BlockType ResolveBlock(int worldX, int worldY, int worldZ, WorldColumnSample column)
    {
        if (worldY <= _settings.BedrockThickness)
        {
            return BlockType.Stone;
        }

        if (worldY > column.SurfaceHeight)
        {
            return worldY <= column.WaterLevel ? BlockType.Water : BlockType.Air;
        }

        var caveValue = _caveGenerator.Sample(worldX, worldY, worldZ, column.SurfaceHeight);
        if (worldY < column.SurfaceHeight - 4 && caveValue >= _settings.Caves.CarveThreshold)
        {
            return BlockType.Air;
        }

        if (worldY < column.SurfaceHeight - 5)
        {
            var oreNoise = _noise.Ridged3DNormalized(worldX + 400, worldY - 80, worldZ - 900, 17f, 3, 0.55f, 2.1f);
            if (worldY < _settings.SeaLevel - 8 && oreNoise > 0.82f)
            {
                return BlockType.MagicOre;
            }

            return BlockType.Stone;
        }

        if (worldY == column.SurfaceHeight)
        {
            return GetSurfaceBlock(column);
        }

        return GetFillerBlock(column);
    }

    private BlockType ResolveDeepBlock(int worldX, int worldY, int worldZ, WorldColumnSample column)
    {
        if (worldY < _settings.SeaLevel - 8)
        {
            var oreNoise = _noise.Ridged3DNormalized(worldX + 400, worldY - 80, worldZ - 900, 17f, 3, 0.55f, 2.1f);
            if (oreNoise > 0.82f)
            {
                return BlockType.MagicOre;
            }
        }

        return BlockType.Stone;
    }

    private int ResolveWaterLevel(float terrainHeight, HydrologySample hydrology, int surfaceHeight)
    {
        if (terrainHeight < _settings.SeaLevel)
        {
            return _settings.SeaLevel;
        }

        if (hydrology.Lake >= 0.72f)
        {
            var lakeLevel = (int)MathF.Round(hydrology.LakeLevel);
            return lakeLevel > surfaceHeight ? lakeLevel : int.MinValue;
        }

        if (hydrology.River >= 0.74f)
        {
            return _settings.SeaLevel > surfaceHeight ? _settings.SeaLevel : int.MinValue;
        }

        return int.MinValue;
    }

    private void AddCrystalFormation(Chunk chunk)
    {
        var settings = _settings.Crystals;
        var chance = _noise.Hash01(chunk.Coord.X, 500, chunk.Coord.Z);
        if (chance > settings.SpawnChance)
        {
            return;
        }

        var localX = 2 + (int)(_noise.Hash01(chunk.Coord.X, 700, chunk.Coord.Z) * (Chunk.SizeX - 4));
        var localZ = 2 + (int)(_noise.Hash01(chunk.Coord.Z, 701, chunk.Coord.X) * (Chunk.SizeZ - 4));
        var worldX = chunk.Coord.X * Chunk.SizeX + localX;
        var worldZ = chunk.Coord.Z * Chunk.SizeZ + localZ;
        var column = SampleColumn(worldX, worldZ);
        if (column.HasSurfaceWater || column.Biome is BiomeType.Ocean or BiomeType.Lake or BiomeType.River)
        {
            return;
        }

        var heightRange = Math.Max(1, settings.MaxHeight - settings.MinHeight + 1);
        var height = settings.MinHeight + (int)(_noise.Hash01(worldX, 999, worldZ) * heightRange);
        for (var y = 0; y < height; y++)
        {
            var crystalY = column.SurfaceHeight + 1 + y;
            if (!Chunk.ContainsLocal(localX, crystalY, localZ))
            {
                continue;
            }

            chunk.SetGeneratedBlock(localX, crystalY, localZ, BlockType.ArcaneCrystal);
            if (y == 0)
            {
                TrySet(chunk, localX + 1, crystalY, localZ, BlockType.ArcaneCrystal);
                TrySet(chunk, localX, crystalY, localZ + 1, BlockType.ArcaneCrystal);
            }
        }
    }

    private void AddTrees(Chunk chunk)
    {
        var baseX = chunk.Coord.X * Chunk.SizeX;
        var baseZ = chunk.Coord.Z * Chunk.SizeZ;
        var minCellX = FloorDiv(baseX - TreeLeafRadius, _settings.Trees.CellSize);
        var maxCellX = FloorDiv(baseX + Chunk.SizeX - 1 + TreeLeafRadius, _settings.Trees.CellSize);
        var minCellZ = FloorDiv(baseZ - TreeLeafRadius, _settings.Trees.CellSize);
        var maxCellZ = FloorDiv(baseZ + Chunk.SizeZ - 1 + TreeLeafRadius, _settings.Trees.CellSize);

        for (var cellZ = minCellZ; cellZ <= maxCellZ; cellZ++)
        for (var cellX = minCellX; cellX <= maxCellX; cellX++)
        {
            TryGenerateTreeFromCell(chunk, cellX, cellZ);
        }
    }

    private void TryGenerateTreeFromCell(Chunk chunk, int cellX, int cellZ)
    {
        var settings = _settings.Trees;
        var cellSize = settings.CellSize;
        var offsetX = (int)(_noise.Hash01(cellX, 1401, cellZ) * cellSize);
        var offsetZ = (int)(_noise.Hash01(cellX, 1402, cellZ) * cellSize);
        var worldX = cellX * cellSize + Math.Min(offsetX, cellSize - 1);
        var worldZ = cellZ * cellSize + Math.Min(offsetZ, cellSize - 1);
        var column = SampleColumn(worldX, worldZ);
        if (!CanGrowOakTree(column))
        {
            return;
        }

        var spawnChance = GetOakTreeChance(column.Biome);
        if (_noise.Hash01(cellX, 1403, cellZ) >= spawnChance)
        {
            return;
        }

        var heightRange = Math.Max(1, settings.MaxTrunkHeight - settings.MinTrunkHeight + 1);
        var trunkHeight = settings.MinTrunkHeight + (int)(_noise.Hash01(worldX, 1404, worldZ) * heightRange);
        var baseY = column.SurfaceHeight + 1;

        if (!CanPlaceOakTree(chunk, worldX, baseY, worldZ, trunkHeight))
        {
            return;
        }

        PlaceOakTree(chunk, worldX, baseY, worldZ, trunkHeight);
    }

    private bool CanPlaceOakTree(Chunk chunk, int worldX, int baseY, int worldZ, int trunkHeight)
    {
        if (baseY < Chunk.MinY + 1 || baseY + trunkHeight + 1 >= Chunk.MaxYExclusive)
        {
            return false;
        }

        var soil = SampleGeneratedBlock(chunk, worldX, baseY - 1, worldZ);
        if (soil is not BlockType.Grass and not BlockType.Dirt)
        {
            return false;
        }

        var topY = baseY + trunkHeight;
        for (var y = baseY; y <= topY + 1; y++)
        {
            var radius = 0;
            if (y == baseY)
            {
                radius = 0;
            }
            else if (y >= topY - 2)
            {
                radius = 2;
            }
            else
            {
                radius = 1;
            }

            for (var x = worldX - radius; x <= worldX + radius; x++)
            for (var z = worldZ - radius; z <= worldZ + radius; z++)
            {
                if (!IsTreeReplaceable(SampleGeneratedBlock(chunk, x, y, z)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private void PlaceOakTree(Chunk chunk, int worldX, int baseY, int worldZ, int trunkHeight)
    {
        TrySetWorld(chunk, worldX, baseY - 1, worldZ, BlockType.Dirt);

        var topY = baseY + trunkHeight;
        for (var y = topY - 3; y <= topY; y++)
        {
            var layerOffset = y - topY;
            var radius = 1 - layerOffset / 2;

            for (var x = worldX - radius; x <= worldX + radius; x++)
            for (var z = worldZ - radius; z <= worldZ + radius; z++)
            {
                var dx = x - worldX;
                var dz = z - worldZ;
                var isCorner = Math.Abs(dx) == radius && Math.Abs(dz) == radius;
                var skipCorner = layerOffset == 0 || _noise.Hash01(x, y + 1500, z) < 0.5f;
                if (isCorner && skipCorner)
                {
                    continue;
                }

                var block = SampleGeneratedBlock(chunk, x, y, z);
                if (block is BlockType.Air or BlockType.OakLeaves)
                {
                    TrySetWorld(chunk, x, y, z, BlockType.OakLeaves);
                }
            }
        }

        for (var y = 0; y < trunkHeight; y++)
        {
            var trunkY = baseY + y;
            var block = SampleGeneratedBlock(chunk, worldX, trunkY, worldZ);
            if (block is BlockType.Air or BlockType.OakLeaves)
            {
                TrySetWorld(chunk, worldX, trunkY, worldZ, BlockType.OakLog);
            }
        }
    }

    private bool CanGrowOakTree(WorldColumnSample column)
    {
        if (column.HasSurfaceWater)
        {
            return false;
        }

        return column.Biome is BiomeType.Plains or BiomeType.Forest or BiomeType.Swamp or BiomeType.Savanna;
    }

    private float GetOakTreeChance(BiomeType biome)
    {
        return biome switch
        {
            BiomeType.Forest => _settings.Trees.ForestChance,
            BiomeType.Swamp => _settings.Trees.SwampChance,
            BiomeType.Savanna => _settings.Trees.SavannaChance,
            _ => _settings.Trees.PlainsChance
        };
    }

    private BlockType SampleGeneratedBlock(Chunk chunk, int worldX, int worldY, int worldZ)
    {
        if (worldY < Chunk.MinY || worldY >= Chunk.MaxYExclusive)
        {
            return BlockType.Air;
        }

        var chunkMinX = chunk.Coord.X * Chunk.SizeX;
        var chunkMinZ = chunk.Coord.Z * Chunk.SizeZ;
        var localX = worldX - chunkMinX;
        var localZ = worldZ - chunkMinZ;
        if (localX >= 0 && localX < Chunk.SizeX && localZ >= 0 && localZ < Chunk.SizeZ)
        {
            return chunk.GetBlock(localX, worldY, localZ);
        }

        var column = SampleColumn(worldX, worldZ);
        return ResolveBlock(worldX, worldY, worldZ, column);
    }

    private static bool IsTreeReplaceable(BlockType block)
    {
        return block is BlockType.Air or BlockType.OakLeaves;
    }

    private static int FloorDiv(int value, int divisor)
    {
        var result = value / divisor;
        var remainder = value % divisor;
        return remainder != 0 && (remainder < 0) != (divisor < 0) ? result - 1 : result;
    }

    private static void TrySetWorld(Chunk chunk, int worldX, int y, int worldZ, BlockType type)
    {
        var localX = worldX - chunk.Coord.X * Chunk.SizeX;
        var localZ = worldZ - chunk.Coord.Z * Chunk.SizeZ;
        if (Chunk.ContainsLocal(localX, y, localZ))
        {
            chunk.SetGeneratedBlock(localX, y, localZ, type);
        }
    }

    private static BlockType GetSurfaceBlock(WorldColumnSample column)
    {
        return column.Biome switch
        {
            BiomeType.Ocean => BlockType.Sand,
            BiomeType.Beach => BlockType.Sand,
            BiomeType.Desert => BlockType.Sand,
            BiomeType.Mountains => column.SurfaceHeight >= 78 || column.PeaksAndValleys >= 0.94f ? BlockType.Stone : BlockType.Grass,
            BiomeType.River => column.SurfaceHeight <= 16 ? BlockType.Sand : BlockType.Grass,
            BiomeType.Lake => column.SurfaceHeight <= 16 ? BlockType.Sand : BlockType.Grass,
            BiomeType.Snow => BlockType.Snow,
            _ => BlockType.Grass
        };
    }

    private static BlockType GetFillerBlock(WorldColumnSample column)
    {
        return column.Biome switch
        {
            BiomeType.Ocean => BlockType.Sand,
            BiomeType.Beach => BlockType.Sand,
            BiomeType.Desert => BlockType.Sand,
            BiomeType.Mountains => column.SurfaceHeight >= 78 || column.PeaksAndValleys >= 0.94f ? BlockType.Stone : BlockType.Dirt,
            BiomeType.River => column.SurfaceHeight <= 16 ? BlockType.Sand : BlockType.Dirt,
            BiomeType.Lake => column.SurfaceHeight <= 16 ? BlockType.Sand : BlockType.Dirt,
            BiomeType.Snow => BlockType.Dirt,
            _ => BlockType.Dirt
        };
    }

    private static void TrySet(Chunk chunk, int x, int y, int z, BlockType type)
    {
        if (Chunk.ContainsLocal(x, y, z))
        {
            chunk.SetGeneratedBlock(x, y, z, type);
        }
    }

    private static void FillRange(Chunk chunk, int localX, int localZ, int startY, int endY, BlockType type)
    {
        if (type == BlockType.Air || startY > endY)
        {
            return;
        }

        var clampedStartY = Math.Max(startY, Chunk.MinY);
        var clampedEndY = Math.Min(endY, Chunk.MaxY);
        for (var y = clampedStartY; y <= clampedEndY; y++)
        {
            chunk.SetGeneratedBlock(localX, y, localZ, type);
        }
    }

    private void FillDeepRange(Chunk chunk, int localX, int worldX, int localZ, int worldZ, int startY, int endY, WorldColumnSample column)
    {
        if (startY > endY)
        {
            return;
        }

        var clampedStartY = Math.Max(startY, Chunk.MinY);
        var clampedEndY = Math.Min(endY, Chunk.MaxY);
        for (var y = clampedStartY; y <= clampedEndY; y++)
        {
            chunk.SetGeneratedBlock(localX, y, localZ, ResolveDeepBlock(worldX, y, worldZ, column));
        }
    }
}
