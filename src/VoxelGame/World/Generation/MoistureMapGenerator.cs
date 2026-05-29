namespace VoxelGame.World.Generation;

public sealed class MoistureMapGenerator
{
    private readonly WorldGenerationSettings _settings;
    private readonly NoiseGenerator _noise;

    public MoistureMapGenerator(NoiseGenerator noise, WorldGenerationSettings settings)
    {
        _noise = noise;
        _settings = settings;
    }

    public float Sample(int worldX, int worldZ, float surfaceHeight, float temperature, float river, float lake)
    {
        var moistureSettings = _settings.Moisture;
        var broad = _noise.Fractal2DNormalized(worldX, worldZ, moistureSettings.BroadNoise);
        var detail = _noise.Billow2DNormalized(worldX, worldZ, moistureSettings.DetailNoise);
        var altitudeDrying = Math.Clamp((surfaceHeight - _settings.SeaLevel) / moistureSettings.AltitudeDryingRange, 0f, 1f) * moistureSettings.AltitudeDrying;
        var thermalLift = (1f - MathF.Abs(temperature - 0.55f)) * 0.08f;
        var waterBoost = MathF.Max(river, lake) * moistureSettings.WaterBoost;

        var value =
            moistureSettings.Baseline
            + broad * moistureSettings.BroadNoiseStrength
            + detail * moistureSettings.DetailNoiseStrength
            + thermalLift
            + waterBoost
            - altitudeDrying;

        return Math.Clamp(value, 0f, 1f);
    }
}
