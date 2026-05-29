namespace VoxelGame.World.Generation;

public sealed class BiomeGenerator
{
    private readonly WorldGenerationSettings _settings;

    public BiomeGenerator(WorldGenerationSettings settings)
    {
        _settings = settings;
    }

    public BiomeType ChooseBiome(int surfaceHeight, int waterLevel, float peaksAndValleys, float temperature, float moisture, float river, float lake)
    {
        var nearSeaLevel = Math.Abs(surfaceHeight - _settings.SeaLevel) <= 1;
        var hasWater = waterLevel > surfaceHeight;

        if (lake >= 0.72f && hasWater)
        {
            return BiomeType.Lake;
        }

        if (river >= 0.74f && hasWater)
        {
            return BiomeType.River;
        }

        if (surfaceHeight < _settings.SeaLevel - 4 && hasWater)
        {
            return BiomeType.Ocean;
        }

        if (nearSeaLevel && hasWater)
        {
            return BiomeType.Beach;
        }

        if (surfaceHeight >= _settings.SeaLevel + 42 || (surfaceHeight >= _settings.SeaLevel + 30 && peaksAndValleys >= 0.90f))
        {
            return BiomeType.Mountains;
        }

        if (temperature <= 0.18f)
        {
            return BiomeType.Snow;
        }

        if (temperature <= 0.34f)
        {
            return moisture >= 0.38f ? BiomeType.Taiga : BiomeType.Snow;
        }

        if (temperature >= 0.78f && moisture <= 0.30f)
        {
            return BiomeType.Desert;
        }

        if (temperature >= 0.66f && moisture <= 0.42f)
        {
            return BiomeType.Savanna;
        }

        if (moisture >= 0.72f && temperature >= 0.50f)
        {
            return BiomeType.Swamp;
        }

        if (moisture >= 0.52f)
        {
            return BiomeType.Forest;
        }

        return BiomeType.Plains;
    }
}
