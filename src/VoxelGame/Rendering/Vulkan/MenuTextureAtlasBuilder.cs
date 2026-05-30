using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Vector2 = System.Numerics.Vector2;

namespace VoxelGame.Rendering.Vulkan;

internal sealed class MenuTextureAtlasBuilder
{
    private readonly string _rootDirectory;

    public MenuTextureAtlasBuilder(string rootDirectory)
    {
        _rootDirectory = rootDirectory;
    }

    public MenuAtlasImage Build()
    {
        using var background = Image.Load<Rgba32>(Path.Combine(_rootDirectory, "menu", "menubg.png"));
        using var logo = Image.Load<Rgba32>(Path.Combine(_rootDirectory, "menu", "logo.png"));
        ApplyTransparencyFixup(logo);

        var atlasWidth = Math.Max(background.Width, logo.Width);
        var atlasHeight = background.Height + logo.Height;

        using var atlas = new Image<Rgba32>(atlasWidth, atlasHeight);
        atlas.Mutate(context =>
        {
            context.DrawImage(background, new Point(0, 0), 1f);
            context.DrawImage(logo, new Point((atlasWidth - logo.Width) / 2, background.Height), 1f);
        });

        var pixels = new byte[atlasWidth * atlasHeight * 4];
        atlas.CopyPixelDataTo(pixels);

        var backgroundUvMin = Vector2.Zero;
        var backgroundUvMax = new Vector2(background.Width / (float)atlasWidth, background.Height / (float)atlasHeight);

        var logoOffsetX = (atlasWidth - logo.Width) / 2f;
        var logoOffsetY = background.Height;
        var logoUvMin = new Vector2(logoOffsetX / atlasWidth, logoOffsetY / atlasHeight);
        var logoUvMax = new Vector2((logoOffsetX + logo.Width) / atlasWidth, (logoOffsetY + logo.Height) / atlasHeight);

        return new MenuAtlasImage(
            atlasWidth,
            atlasHeight,
            pixels,
            background.Width / (float)background.Height,
            logo.Width / (float)logo.Height,
            backgroundUvMin,
            backgroundUvMax,
            logoUvMin,
            logoUvMax);
    }

    private static void ApplyTransparencyFixup(Image<Rgba32> image)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    ref var pixel = ref row[x];
                    if (pixel.A <= 4)
                    {
                        pixel = new Rgba32(0, 0, 0, 0);
                    }
                }
            }
        });
    }

    internal readonly record struct MenuAtlasImage(
        int Width,
        int Height,
        byte[] Pixels,
        float BackgroundAspect,
        float LogoAspect,
        Vector2 BackgroundUvMin,
        Vector2 BackgroundUvMax,
        Vector2 LogoUvMin,
        Vector2 LogoUvMax);
}
