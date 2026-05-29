namespace VoxelGame.World.Generation;

public sealed class HeightMapGenerator
{
    private readonly WorldGenerationSettings _settings;
    private readonly NoiseGenerator _noise;

    public HeightMapGenerator(NoiseGenerator noise, WorldGenerationSettings settings)
    {
        _noise = noise;
        _settings = settings;
    }

    public HeightMapSample Sample(int worldX, int worldZ)
    {
        var heightSettings = _settings.Height;
        var continentalness = _noise.Fractal2DNormalized(worldX, worldZ, heightSettings.Continentalness);
        var erosion = _noise.Fractal2DNormalized(worldX, worldZ, heightSettings.Erosion);
        var peaksAndValleys = _noise.Ridged2DNormalized(worldX, worldZ, heightSettings.PeaksAndValleys);
        var detail = _noise.Billow2DNormalized(worldX, worldZ, heightSettings.Detail);

        var landMask = SmoothStep(heightSettings.OceanThreshold - 0.03f, heightSettings.OceanThreshold + 0.12f, continentalness);
        var oceanMask = 1f - landMask;
        var erosionWeight = 1f - erosion;
        var mountainMask = SmoothStep(0.54f, 0.82f, continentalness) * SmoothStep(0.28f, 0.92f, erosionWeight);
        var peakMask = mountainMask * peaksAndValleys * peaksAndValleys;

        var oceanDepth = oceanMask * heightSettings.OceanDepth * SmoothStep(0f, heightSettings.OceanThreshold, continentalness);
        var plains = landMask * (continentalness - heightSettings.OceanThreshold) * heightSettings.PlainsHeight;
        var hills = landMask * (detail - 0.5f) * 2f * heightSettings.HillHeight * (0.45f + erosionWeight * 0.55f);
        var mountains = mountainMask * peaksAndValleys * heightSettings.MountainHeight;
        var peaks = peakMask * heightSettings.PeakHeight;
        var valleys = mountainMask * (1f - peaksAndValleys) * heightSettings.ValleyDepth;

        var exactHeight =
            heightSettings.BaseLandHeight
            + (plains + hills + mountains + peaks - valleys - oceanDepth) * heightSettings.HeightMultiplier;

        exactHeight = Math.Clamp(exactHeight, _settings.BedrockThickness + 3, _settings.MaxTerrainHeight);
        var normalizedHeight = Math.Clamp(exactHeight / MathF.Max(1f, _settings.MaxTerrainHeight), 0f, 1f);
        return new HeightMapSample((int)MathF.Round(exactHeight), exactHeight, continentalness, erosion, peaksAndValleys, detail, normalizedHeight);
    }

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

public readonly record struct HeightMapSample(
    int Height,
    float ExactHeight,
    float Continentalness,
    float Erosion,
    float PeaksAndValleys,
    float Detail,
    float NormalizedHeight);
