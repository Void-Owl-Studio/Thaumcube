namespace VoxelGame.World.Generation;

public sealed class TemperatureMapGenerator
{
    private readonly WorldGenerationSettings _settings;
    private readonly NoiseGenerator _noise;

    public TemperatureMapGenerator(NoiseGenerator noise, WorldGenerationSettings settings)
    {
        _noise = noise;
        _settings = settings;
    }

    public float Sample(int worldX, int worldZ, float surfaceHeight)
    {
        var temperatureSettings = _settings.Temperature;
        var latitude = MathF.Abs(MathF.Sin(worldZ / MathF.Max(1f, temperatureSettings.LatitudeBandScale)));
        var latitudeHeat = 1f - latitude;
        var noiseHeat = _noise.Fractal2DNormalized(worldX, worldZ, temperatureSettings.Noise);
        var altitudeCooling = Math.Clamp((surfaceHeight - _settings.SeaLevel) / temperatureSettings.AltitudeCoolingRange, 0f, 1f) * temperatureSettings.AltitudeCooling;
        var value =
            temperatureSettings.Baseline
            + latitudeHeat * temperatureSettings.LatitudeStrength
            + noiseHeat * temperatureSettings.NoiseStrength
            - altitudeCooling;

        return Math.Clamp(value, 0f, 1f);
    }
}
