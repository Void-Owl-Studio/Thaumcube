namespace VoxelGame.World.Generation;

public sealed class CaveGenerator
{
    private readonly WorldGenerationSettings _settings;
    private readonly NoiseGenerator _noise;

    public CaveGenerator(NoiseGenerator noise, WorldGenerationSettings settings)
    {
        _noise = noise;
        _settings = settings;
    }

    public float Sample(int worldX, int worldY, int worldZ, int surfaceHeight)
    {
        var caves = _settings.Caves;
        if (worldY < caves.MinY || worldY > caves.MaxY)
        {
            return 0f;
        }

        if (surfaceHeight - worldY < caves.MinSurfaceDepth)
        {
            return 0f;
        }

        var depthBelowSurface = surfaceHeight - worldY;
        var depthMask = Math.Clamp((depthBelowSurface - caves.MinSurfaceDepth) / 24f, 0f, 1f);

        // Each cave family contributes a separate scalar field:
        // ridged tunnels, thin spaghetti strands, broader caverns, plus deterministic
        // worm/spiral features anchored to feature cells so nearby chunks stay seamless.
        var tunnelNoise = _noise.Ridged3DNormalized(worldX + 1300, worldY - 900, worldZ - 2100, caves.TunnelScale, 3, 0.55f, 2.1f);
        var spaghettiNoise = 1f - MathF.Abs(_noise.Fractal3D(worldX - 800, worldY + 1200, worldZ + 700, caves.SpaghettiScale, 4, 0.58f, 2.15f));
        var cavernNoise = _noise.Billow3DNormalized(worldX + 4200, worldY - 3400, worldZ - 1700, caves.CavernScale, 3, 0.52f, 2.0f);
        var tunnelField = EvaluateLinearTunnels(worldX, worldY, worldZ);
        var spiralField = EvaluateSpiralCaves(worldX, worldY, worldZ);

        var tunnelValue = SmoothThreshold(tunnelNoise, caves.TunnelThreshold);
        var spaghettiValue = SmoothThreshold(spaghettiNoise, caves.SpaghettiThreshold);
        var cavernValue = SmoothThreshold(cavernNoise, caves.CavernThreshold) * Math.Clamp((surfaceHeight - worldY - 20f) / 40f, 0f, 1f);
        var value = MathF.Max(MathF.Max(tunnelValue, spaghettiValue), MathF.Max(cavernValue, MathF.Max(tunnelField, spiralField)));
        return Math.Clamp(value * depthMask * caves.Density, 0f, 1f);
    }

    private float EvaluateLinearTunnels(int worldX, int worldY, int worldZ)
    {
        var caves = _settings.Caves;
        var cellSize = caves.FeatureCellSize;
        var cellX = FloorDiv(worldX, cellSize);
        var cellY = FloorDiv(worldY, cellSize);
        var cellZ = FloorDiv(worldZ, cellSize);
        var best = 0f;

        for (var offsetZ = -1; offsetZ <= 1; offsetZ++)
        {
            for (var offsetY = -1; offsetY <= 1; offsetY++)
            {
                for (var offsetX = -1; offsetX <= 1; offsetX++)
                {
                    var featureCellX = cellX + offsetX;
                    var featureCellY = cellY + offsetY;
                    var featureCellZ = cellZ + offsetZ;
                    if (_noise.Hash01(featureCellX, featureCellY, featureCellZ) > caves.TunnelChance)
                    {
                        continue;
                    }

                    var centerX = featureCellX * cellSize + cellSize * 0.5f + _noise.HashSigned(featureCellX, 17, featureCellZ) * 10f;
                    var centerY = featureCellY * cellSize + cellSize * 0.5f + _noise.HashSigned(featureCellY, 23, featureCellX) * 8f;
                    var centerZ = featureCellZ * cellSize + cellSize * 0.5f + _noise.HashSigned(featureCellZ, 31, featureCellY) * 10f;

                    var direction = new Float3(
                        _noise.HashSigned(featureCellX, 101, featureCellZ),
                        _noise.HashSigned(featureCellY, 103, featureCellX) * 0.4f,
                        _noise.HashSigned(featureCellZ, 107, featureCellY));
                    direction = Normalize(direction);

                    var length = 16f + _noise.Hash01(featureCellX, 211, featureCellZ) * 24f;
                    var start = new Float3(centerX, centerY, centerZ);
                    var end = new Float3(centerX + direction.X * length, centerY + direction.Y * length, centerZ + direction.Z * length);
                    var distance = DistanceToSegment(new Float3(worldX, worldY, worldZ), start, end);
                    best = MathF.Max(best, 1f - Math.Clamp(distance / caves.TunnelRadius, 0f, 1f));
                }
            }
        }

        return best;
    }

    private float EvaluateSpiralCaves(int worldX, int worldY, int worldZ)
    {
        var caves = _settings.Caves;
        var cellSize = caves.FeatureCellSize * 2;
        var cellX = FloorDiv(worldX, cellSize);
        var cellZ = FloorDiv(worldZ, cellSize);
        var best = 0f;

        for (var offsetZ = -1; offsetZ <= 1; offsetZ++)
        {
            for (var offsetX = -1; offsetX <= 1; offsetX++)
            {
                var featureCellX = cellX + offsetX;
                var featureCellZ = cellZ + offsetZ;
                if (_noise.Hash01(featureCellX, 313, featureCellZ) > caves.SpiralChance)
                {
                    continue;
                }

                var centerX = featureCellX * cellSize + cellSize * 0.5f + _noise.HashSigned(featureCellX, 47, featureCellZ) * 12f;
                var centerZ = featureCellZ * cellSize + cellSize * 0.5f + _noise.HashSigned(featureCellZ, 53, featureCellX) * 12f;
                var baseY = 18f + _noise.Hash01(featureCellX, 59, featureCellZ) * 54f;
                var dx = worldX - centerX;
                var dz = worldZ - centerZ;
                var radius = MathF.Sqrt(dx * dx + dz * dz);
                var angle = MathF.Atan2(dz, dx);
                var spiralY = baseY + angle * 4.4f + _noise.HashSigned(featureCellX, 61, featureCellZ) * 3f;
                var verticalDistance = MathF.Abs(worldY - spiralY);
                var radiusDistance = MathF.Abs(radius - (caves.SpiralRadius + _noise.HashSigned(featureCellX, 67, featureCellZ)));
                var combinedDistance = radiusDistance + verticalDistance * 0.55f;
                best = MathF.Max(best, 1f - Math.Clamp(combinedDistance / caves.SpiralRadius, 0f, 1f));
            }
        }

        return best;
    }

    private static float SmoothThreshold(float value, float threshold)
    {
        return Math.Clamp((value - threshold) / MathF.Max(0.001f, 1f - threshold), 0f, 1f);
    }

    private static int FloorDiv(int value, int divisor)
    {
        var result = value / divisor;
        var remainder = value % divisor;
        return remainder != 0 && (remainder < 0) != (divisor < 0) ? result - 1 : result;
    }

    private static Float3 Normalize(Float3 value)
    {
        var length = MathF.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);
        if (length <= 0.0001f)
        {
            return new Float3(1f, 0f, 0f);
        }

        return new Float3(value.X / length, value.Y / length, value.Z / length);
    }

    private static float DistanceToSegment(Float3 point, Float3 start, Float3 end)
    {
        var segment = end - start;
        var segmentLengthSquared = segment.X * segment.X + segment.Y * segment.Y + segment.Z * segment.Z;
        if (segmentLengthSquared <= 0.0001f)
        {
            return Distance(point, start);
        }

        var projection = Dot(point - start, segment) / segmentLengthSquared;
        projection = Math.Clamp(projection, 0f, 1f);
        var closest = start + segment * projection;
        return Distance(point, closest);
    }

    private static float Dot(Float3 a, Float3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static float Distance(Float3 a, Float3 b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private readonly record struct Float3(float X, float Y, float Z)
    {
        public static Float3 operator -(Float3 a, Float3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Float3 operator +(Float3 a, Float3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Float3 operator *(Float3 a, float factor) => new(a.X * factor, a.Y * factor, a.Z * factor);
    }
}
