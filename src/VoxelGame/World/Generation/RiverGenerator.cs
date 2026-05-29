namespace VoxelGame.World.Generation;

public sealed class RiverGenerator
{
    private readonly WorldGenerationSettings _settings;
    private readonly HeightMapGenerator _heightMapGenerator;
    private readonly NoiseGenerator _noise;
    private readonly LakeGenerator _lakeGenerator;
    private readonly Dictionary<HydrologyTileCoord, HydrologyTile> _tiles = new();

    public RiverGenerator(NoiseGenerator noise, WorldGenerationSettings settings, HeightMapGenerator heightMapGenerator, LakeGenerator lakeGenerator)
    {
        _noise = noise;
        _settings = settings;
        _heightMapGenerator = heightMapGenerator;
        _lakeGenerator = lakeGenerator;
    }

    public HydrologySample Sample(int worldX, int worldZ)
    {
        var rivers = _settings.Rivers;
        var tileWorldSize = rivers.TileSizeCells * rivers.CellSize;
        var tileCoord = new HydrologyTileCoord(FloorDiv(worldX, tileWorldSize), FloorDiv(worldZ, tileWorldSize));
        if (!_tiles.TryGetValue(tileCoord, out var tile))
        {
            tile = BuildTile(tileCoord);
            _tiles[tileCoord] = tile;
        }

        return tile.Sample(worldX, worldZ);
    }

    private HydrologyTile BuildTile(HydrologyTileCoord tileCoord)
    {
        var rivers = _settings.Rivers;
        var tileWorldSize = rivers.TileSizeCells * rivers.CellSize;
        var coreOriginX = tileCoord.X * tileWorldSize;
        var coreOriginZ = tileCoord.Z * tileWorldSize;
        var margin = rivers.TileMarginCells;
        var extendedCells = rivers.TileSizeCells + margin * 2;
        var extendedOriginX = coreOriginX - margin * rivers.CellSize;
        var extendedOriginZ = coreOriginZ - margin * rivers.CellSize;

        var heights = new float[extendedCells * extendedCells];
        var flowTo = new int[heights.Length];
        var riverAccumulation = new float[heights.Length];
        var lakeMask = new float[heights.Length];
        var lakeLevels = new float[heights.Length];

        for (var z = 0; z < extendedCells; z++)
        {
            for (var x = 0; x < extendedCells; x++)
            {
                var worldX = extendedOriginX + x * rivers.CellSize;
                var worldZ = extendedOriginZ + z * rivers.CellSize;
                heights[x + z * extendedCells] = _heightMapGenerator.Sample(worldX, worldZ).ExactHeight;
            }
        }

        for (var z = 0; z < extendedCells; z++)
        {
            for (var x = 0; x < extendedCells; x++)
            {
                var index = x + z * extendedCells;
                flowTo[index] = FindDownhillNeighbor(index, x, z, extendedCells, heights);
            }
        }

        var sourceNoiseSettings = new NoiseLayerSettings
        {
            Scale = rivers.SourceNoiseScale,
            Octaves = 3,
            Persistence = 0.55f,
            Lacunarity = 2.1f,
            HeightMultiplier = 1f,
            OffsetX = 2301,
            OffsetZ = -7121
        };

        for (var z = 1; z < extendedCells - 1; z++)
        {
            for (var x = 1; x < extendedCells - 1; x++)
            {
                var index = x + z * extendedCells;
                var height = heights[index];
                if (height <= _settings.SeaLevel + rivers.MinSourceHeightAboveSea)
                {
                    continue;
                }

                var slope = CalculateSlope(heights, x, z, extendedCells);
                if (slope < rivers.MinSourceSlope)
                {
                    continue;
                }

                var cellWorldX = extendedOriginX + x * rivers.CellSize;
                var cellWorldZ = extendedOriginZ + z * rivers.CellSize;
                var sourceNoise = _noise.Ridged2DNormalized(cellWorldX, cellWorldZ, sourceNoiseSettings);
                var mountainBias = Math.Clamp((height - _settings.SeaLevel) / 30f, 0f, 1f);
                var hashedSparsity = _noise.Hash01(cellWorldX / rivers.CellSize, 37, cellWorldZ / rivers.CellSize);
                var sourceStrength = sourceNoise * 0.65f + mountainBias * 0.35f;
                if (sourceStrength < rivers.SourceThreshold || hashedSparsity < 0.62f)
                {
                    continue;
                }

                TraceRiver(index, heights, flowTo, riverAccumulation, lakeMask, lakeLevels, extendedCells, sourceStrength);
            }
        }

        return new HydrologyTile(
            coreOriginX,
            coreOriginZ,
            rivers.CellSize,
            rivers.TileSizeCells,
            rivers.TileMarginCells,
            riverAccumulation,
            lakeMask,
            lakeLevels,
            heights);
    }

    private void TraceRiver(
        int startIndex,
        float[] heights,
        int[] flowTo,
        float[] riverAccumulation,
        float[] lakeMask,
        float[] lakeLevels,
        int width,
        float sourceStrength)
    {
        var rivers = _settings.Rivers;
        var visited = new HashSet<int>();
        var current = startIndex;
        var flow = 0.6f + sourceStrength * 0.7f;

        // Rivers are traced cell-by-cell along the local downhill vector field,
        // so channels always move toward lower terrain instead of climbing uphill.
        for (var step = 0; step < rivers.MaxRiverSteps; step++)
        {
            if (!visited.Add(current))
            {
                break;
            }

            DepositRiver(riverAccumulation, heights, width, current, flow);
            if (heights[current] <= _settings.SeaLevel + 1f)
            {
                break;
            }

            var next = flowTo[current];
            if (next == current)
            {
                // When flow gets trapped in a depression, we try to form a lake basin
                // and only continue if the temporary lake can spill over a lower rim.
                var lake = _lakeGenerator.TryFillLake(heights, width, width, current, flow);
                if (!lake.Created)
                {
                    break;
                }

                foreach (var cell in lake.Cells)
                {
                    lakeMask[cell] = Math.Max(lakeMask[cell], Math.Clamp((lake.WaterLevel - heights[cell]) / MathF.Max(0.001f, _settings.Lakes.MaxDepth), 0f, 1f));
                    lakeLevels[cell] = Math.Max(lakeLevels[cell], lake.WaterLevel);
                    riverAccumulation[cell] = Math.Max(riverAccumulation[cell], flow * 0.5f);
                }

                if (lake.SpillIndex < 0 || lake.WaterLevel <= heights[lake.SpillIndex] + 0.05f)
                {
                    break;
                }

                current = lake.SpillIndex;
            }
            else
            {
                current = next;
            }

            flow = Math.Min(flow + rivers.FlowGainPerStep, 2.5f);
        }
    }

    private void DepositRiver(float[] riverAccumulation, float[] heights, int width, int index, float flow)
    {
        riverAccumulation[index] += flow;
        var centerHeight = heights[index];
        var x = index % width;
        var z = index / width;

        for (var offsetZ = -1; offsetZ <= 1; offsetZ++)
        {
            for (var offsetX = -1; offsetX <= 1; offsetX++)
            {
                if (offsetX == 0 && offsetZ == 0)
                {
                    continue;
                }

                var neighborX = x + offsetX;
                var neighborZ = z + offsetZ;
                if (neighborX < 0 || neighborX >= width || neighborZ < 0 || neighborZ >= width)
                {
                    continue;
                }

                var neighborIndex = neighborX + neighborZ * width;
                if (heights[neighborIndex] <= centerHeight + 2f)
                {
                    riverAccumulation[neighborIndex] += flow * _settings.Rivers.WidthContribution;
                }
            }
        }
    }

    private int FindDownhillNeighbor(int index, int x, int z, int width, float[] heights)
    {
        var bestIndex = index;
        var bestHeight = heights[index];

        for (var offsetZ = -1; offsetZ <= 1; offsetZ++)
        {
            for (var offsetX = -1; offsetX <= 1; offsetX++)
            {
                if (offsetX == 0 && offsetZ == 0)
                {
                    continue;
                }

                var neighborX = x + offsetX;
                var neighborZ = z + offsetZ;
                if (neighborX < 0 || neighborX >= width || neighborZ < 0 || neighborZ >= width)
                {
                    continue;
                }

                var neighborIndex = neighborX + neighborZ * width;
                var candidateHeight = heights[neighborIndex];
                if (candidateHeight + 0.02f < bestHeight)
                {
                    bestHeight = candidateHeight;
                    bestIndex = neighborIndex;
                }
            }
        }

        return bestIndex;
    }

    private static float CalculateSlope(float[] heights, int x, int z, int width)
    {
        var west = heights[(x - 1) + z * width];
        var east = heights[(x + 1) + z * width];
        var north = heights[x + (z - 1) * width];
        var south = heights[x + (z + 1) * width];
        return (MathF.Abs(east - west) + MathF.Abs(south - north)) * 0.5f;
    }

    private static int FloorDiv(int value, int divisor)
    {
        var result = value / divisor;
        var remainder = value % divisor;
        return remainder != 0 && (remainder < 0) != (divisor < 0) ? result - 1 : result;
    }

    private readonly record struct HydrologyTileCoord(int X, int Z);

    private sealed class HydrologyTile
    {
        private readonly int _originX;
        private readonly int _originZ;
        private readonly int _cellSize;
        private readonly int _coreSizeCells;
        private readonly int _marginCells;
        private readonly float[] _riverAccumulation;
        private readonly float[] _lakeMask;
        private readonly float[] _lakeLevels;
        private readonly float[] _heights;

        public HydrologyTile(
            int originX,
            int originZ,
            int cellSize,
            int coreSizeCells,
            int marginCells,
            float[] riverAccumulation,
            float[] lakeMask,
            float[] lakeLevels,
            float[] heights)
        {
            _originX = originX;
            _originZ = originZ;
            _cellSize = cellSize;
            _coreSizeCells = coreSizeCells;
            _marginCells = marginCells;
            _riverAccumulation = riverAccumulation;
            _lakeMask = lakeMask;
            _lakeLevels = lakeLevels;
            _heights = heights;
        }

        public HydrologySample Sample(int worldX, int worldZ)
        {
            var localX = (worldX - _originX) / (float)_cellSize + _marginCells;
            var localZ = (worldZ - _originZ) / (float)_cellSize + _marginCells;
            var maxIndex = _coreSizeCells + _marginCells * 2 - 1.001f;
            localX = Math.Clamp(localX, 0f, maxIndex);
            localZ = Math.Clamp(localZ, 0f, maxIndex);

            var river = Bilinear(_riverAccumulation, localX, localZ);
            var lake = Bilinear(_lakeMask, localX, localZ);
            var lakeLevel = Bilinear(_lakeLevels, localX, localZ);
            var ground = Bilinear(_heights, localX, localZ);

            return new HydrologySample(
                River: SmoothStep(1.18f, 2.35f, river) * (1f - lake),
                Lake: Math.Clamp(lake, 0f, 1f),
                RiverCarve: SmoothStep(0.85f, 1.9f, river),
                LakeLevel: lakeLevel,
                GroundHeight: ground);
        }

        private float Bilinear(float[] values, float x, float z)
        {
            var width = _coreSizeCells + _marginCells * 2;
            var x0 = Math.Clamp((int)MathF.Floor(x), 0, width - 1);
            var z0 = Math.Clamp((int)MathF.Floor(z), 0, width - 1);
            var x1 = Math.Clamp(x0 + 1, 0, width - 1);
            var z1 = Math.Clamp(z0 + 1, 0, width - 1);
            var tx = x - x0;
            var tz = z - z0;

            var a = values[x0 + z0 * width];
            var b = values[x1 + z0 * width];
            var c = values[x0 + z1 * width];
            var d = values[x1 + z1 * width];
            var ab = Lerp(a, b, tx);
            var cd = Lerp(c, d, tx);
            return Lerp(ab, cd, tz);
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float SmoothStep(float min, float max, float value)
        {
            if (value <= min)
            {
                return 0f;
            }

            if (value >= max)
            {
                return 1f;
            }

            var t = (value - min) / (max - min);
            return t * t * (3f - 2f * t);
        }
    }
}

public readonly record struct HydrologySample(float River, float Lake, float RiverCarve, float LakeLevel, float GroundHeight);
