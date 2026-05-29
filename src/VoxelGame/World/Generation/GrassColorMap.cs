using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace VoxelGame.World.Generation;

public static class GrassColorMap
{
    private static readonly object Sync = new();
    private static Rgba32[]? _pixels;
    private static int _width;
    private static int _height;

    public static Vector3 Sample(float temperature, float humidity)
    {
        EnsureLoaded();
        if (_pixels is null || _width <= 0 || _height <= 0)
        {
            return new Vector3(0.28f, 0.56f, 0.22f);
        }

        temperature = Math.Clamp(temperature, 0f, 1f);
        humidity = Math.Clamp(humidity, 0f, 1f) * temperature;
        var x = Math.Clamp((int)MathF.Round((1f - temperature) * (_width - 1)), 0, _width - 1);
        var y = Math.Clamp((int)MathF.Round((1f - humidity) * (_height - 1)), 0, _height - 1);
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
