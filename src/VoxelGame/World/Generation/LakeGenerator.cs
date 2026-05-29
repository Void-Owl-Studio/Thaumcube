namespace VoxelGame.World.Generation;

public sealed class LakeGenerator
{
    private readonly WorldGenerationSettings _settings;

    public LakeGenerator(WorldGenerationSettings settings)
    {
        _settings = settings;
    }

    public LakeFillResult TryFillLake(float[] heights, int width, int height, int startIndex, float flow)
    {
        var lakeSettings = _settings.Lakes;
        var sinkHeight = heights[startIndex];
        var startX = startIndex % width;
        var startZ = startIndex / width;
        var maxRadius = lakeSettings.MaxRadiusCells;
        var provisionalLevel = sinkHeight + MathF.Min(lakeSettings.MaxDepth, lakeSettings.MinimumDepth + flow * lakeSettings.FlowDepthMultiplier);

        var visited = new bool[heights.Length];
        var queue = new Queue<int>();
        var basin = new List<int>(32);
        var rimHeight = float.MaxValue;
        var spillIndex = -1;

        visited[startIndex] = true;
        queue.Enqueue(startIndex);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            basin.Add(current);

            var x = current % width;
            var z = current / width;
            if (Math.Abs(x - startX) > maxRadius || Math.Abs(z - startZ) > maxRadius)
            {
                continue;
            }

            foreach (var neighbor in EnumerateNeighbors(current, width, height))
            {
                var neighborHeight = heights[neighbor];
                if (!visited[neighbor] && neighborHeight <= provisionalLevel)
                {
                    visited[neighbor] = true;
                    queue.Enqueue(neighbor);
                    continue;
                }

                if (neighborHeight < rimHeight)
                {
                    rimHeight = neighborHeight;
                    spillIndex = neighbor;
                }
            }
        }

        if (rimHeight == float.MaxValue)
        {
            rimHeight = provisionalLevel;
        }

        var waterLevel = MathF.Min(provisionalLevel, rimHeight);
        var depth = waterLevel - sinkHeight;
        if (depth < lakeSettings.MinimumDepth || basin.Count < 3)
        {
            return new LakeFillResult(false, -1, 0f, []);
        }

        return new LakeFillResult(true, spillIndex, waterLevel, basin);
    }

    private static IEnumerable<int> EnumerateNeighbors(int index, int width, int height)
    {
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
                if (neighborX < 0 || neighborX >= width || neighborZ < 0 || neighborZ >= height)
                {
                    continue;
                }

                yield return neighborX + neighborZ * width;
            }
        }
    }
}

public readonly record struct LakeFillResult(bool Created, int SpillIndex, float WaterLevel, IReadOnlyList<int> Cells);
