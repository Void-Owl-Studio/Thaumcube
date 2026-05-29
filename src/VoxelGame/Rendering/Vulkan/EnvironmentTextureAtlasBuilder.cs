using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace VoxelGame.Rendering.Vulkan;

internal sealed class EnvironmentTextureAtlasBuilder
{
    private readonly string _rootDirectory;

    public EnvironmentTextureAtlasBuilder(string rootDirectory)
    {
        _rootDirectory = rootDirectory;
    }

    public AtlasImage Build()
    {
        var atlasRows = (EnvironmentTextureAtlas.TextureCount + EnvironmentTextureAtlas.TilesPerRow - 1) / EnvironmentTextureAtlas.TilesPerRow;
        var atlasWidth = EnvironmentTextureAtlas.TilesPerRow * EnvironmentTextureAtlas.TileSize;
        var atlasHeight = atlasRows * EnvironmentTextureAtlas.TileSize;

        using var atlas = new Image<Rgba32>(atlasWidth, atlasHeight);

        foreach (var entry in GetEntries())
        {
            using var source = Image.Load<Rgba32>(Path.Combine(_rootDirectory, entry.FileName));
            source.Mutate(context => context.Resize(new ResizeOptions
            {
                Size = new Size(EnvironmentTextureAtlas.TileSize, EnvironmentTextureAtlas.TileSize),
                Sampler = KnownResamplers.NearestNeighbor
            }));

            var tileX = (entry.Index % EnvironmentTextureAtlas.TilesPerRow) * EnvironmentTextureAtlas.TileSize;
            var tileY = (entry.Index / EnvironmentTextureAtlas.TilesPerRow) * EnvironmentTextureAtlas.TileSize;
            atlas.Mutate(context => context.DrawImage(source, new Point(tileX, tileY), 1f));
        }

        var pixels = new byte[atlasWidth * atlasHeight * 4];
        atlas.CopyPixelDataTo(pixels);
        return new AtlasImage(atlasWidth, atlasHeight, pixels);
    }

    private static IReadOnlyList<AtlasEntry> GetEntries()
    {
        return
        [
            new AtlasEntry(EnvironmentTextureAtlas.Clouds, "environment/clouds.png"),
            new AtlasEntry(EnvironmentTextureAtlas.Sun, "environment/sun.png")
        ];
    }

    internal readonly record struct AtlasImage(int Width, int Height, byte[] Pixels);

    private readonly record struct AtlasEntry(int Index, string FileName);
}
