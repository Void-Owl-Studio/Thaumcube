using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace VoxelGame.World.Generation;

public static class GrassColorMap
{
    private static readonly object Sync = new();
    private const float AverageTemperature = 0.62f;
    private const float AverageHumidity = 0.58f;
    private const float CorruptionThreshold = 0.68f;
    private static Rgba32[]? _pixels;
    private static int _width;
    private static int _height;

    public static Vector3 Sample(float temperature, float humidity, float corruption = 0f)
    {
        EnsureLoaded();
        if (_pixels is null || _width <= 0 || _height <= 0)
        {
            return new Vector3(0.28f, 0.56f, 0.22f);
        }

        temperature = Math.Clamp(temperature, 0f, 1f);
        humidity = Math.Clamp(humidity, 0f, 1f);
        corruption = Math.Clamp(corruption, 0f, 1f);

        float u;
        float v;

        if (corruption >= CorruptionThreshold)
        {
            var infection = Math.Clamp((corruption - CorruptionThreshold) / (1f - CorruptionThreshold), 0f, 1f);
            u = 0.08f + (0.02f - 0.08f) * infection;
            v = 0.12f + (0.02f - 0.12f) * infection;
        }
        else
        {
            var dryness = 1f - humidity;
            var heatOffset = temperature - AverageTemperature;
            var dryOffset = dryness - (1f - AverageHumidity);

            // Keep average biomes near the center, push humid/swamp tones up-left,
            // and move hot, dry biomes toward the lower-right corner.
            u = Math.Clamp(0.5f + heatOffset * 0.35f + dryOffset * 0.90f, 0f, 1f);
            v = Math.Clamp(0.5f + dryOffset * 0.95f + heatOffset * 0.20f, 0f, 1f);
        }

        var x = Math.Clamp((int)MathF.Round(u * (_width - 1)), 0, _width - 1);
        var y = Math.Clamp((int)MathF.Round(v * (_height - 1)), 0, _height - 1);
        var pixel = _pixels[y * _width + x];
        return new Vector3(pixel.R / 255f, pixel.G / 255f, pixel.B / 255f);
    }

    private static void EnsureLoaded()
    {
        if (_pixels is not null)
        {
            return;
        }

        lock (Sync)
        {
            if (_pixels is not null)
            {
                return;
            }

            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "textures", "colormap", "grass.png");
            if (!File.Exists(path))
            {
                return;
            }

            using var image = Image.Load<Rgba32>(path);
            _width = image.Width;
            _height = image.Height;
            _pixels = new Rgba32[_width * _height];
            image.CopyPixelDataTo(_pixels);
        }
    }
}
