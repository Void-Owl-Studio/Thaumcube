namespace VoxelGame.World.Generation;

public sealed class RoughnessGenerator
{
    private readonly WorldGenerationSettings _settings;
    private readonly NoiseGenerator _noise;

    public RoughnessGenerator(NoiseGenerator noise, WorldGenerationSettings settings)
    {
        _noise = noise;
        _settings = settings;
    }

    public float Sample(int worldX, int worldZ, BiomeType biome, float normalizedHeight)
    {
        var roughnessSettings = _settings.Roughness;
        var general = _noise.Fractal2D(worldX, worldZ, roughnessSettings.GeneralNoise);
        var detail = _noise.Billow2DNormalized(worldX, worldZ, roughnessSettings.DetailNoise) - 0.5f;
        var dunes = MathF.Sin((worldX + _noise.HashSigned(worldX / 8, 0, worldZ / 8) * 9f) / 11f) * 0.5f;
        var heightBoost = 0.65f + normalizedHeight * 0.65f;

        return biome switch
        {
            BiomeType.Plains => general * roughnessSettings.PlainsAmplitude * 0.35f + detail * roughnessSettings.PlainsAmplitude,
            BiomeType.Forest => general * roughnessSettings.ForestAmplitude * 0.45f + detail * roughnessSettings.ForestAmplitude,
            BiomeType.Desert => dunes * roughnessSettings.DesertAmplitude + detail * roughnessSettings.DesertAmplitude * 0.25f,
            BiomeType.Savanna => general * roughnessSettings.SavannaAmplitude * 0.42f + detail * roughnessSettings.SavannaAmplitude * 0.5f,
            BiomeType.Swamp => general * roughnessSettings.SwampAmplitude * 0.28f + detail * roughnessSettings.SwampAmplitude * 0.45f,
            BiomeType.Taiga => general * roughnessSettings.TaigaAmplitude * 0.5f + detail * roughnessSettings.TaigaAmplitude * 0.55f,
            BiomeType.Snow => general * roughnessSettings.SnowAmplitude * 0.35f + detail * roughnessSettings.SnowAmplitude * 0.3f,
            BiomeType.Mountains => (general * 0.65f + detail * 0.9f) * roughnessSettings.MountainAmplitude * heightBoost,
            BiomeType.Beach => general * roughnessSettings.BeachAmplitude * 0.2f,
            _ => 0f
        };
    }
}
