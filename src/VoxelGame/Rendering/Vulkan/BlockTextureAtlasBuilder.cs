using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using VoxelGame.World.Blocks;

namespace VoxelGame.Rendering.Vulkan;

internal sealed class BlockTextureAtlasBuilder
{
    private readonly string _rootDirectory;

    public BlockTextureAtlasBuilder(string rootDirectory)
    {
        _rootDirectory = rootDirectory;
    }

    public AtlasImage Build()
    {
        var atlasRows = (BlockTextureAtlas.TextureCount + BlockTextureAtlas.TilesPerRow - 1) / BlockTextureAtlas.TilesPerRow;
        var atlasWidth = BlockTextureAtlas.TilesPerRow * BlockTextureAtlas.TileSize;
        var atlasHeight = atlasRows * BlockTextureAtlas.TileSize;

        using var atlas = new Image<Rgba32>(atlasWidth, atlasHeight);

        foreach (var entry in GetEntries())
        {
            using var source = Image.Load<Rgba32>(Path.Combine(_rootDirectory, entry.FileName));
            source.Mutate(context => context.Resize(BlockTextureAtlas.TileSize, BlockTextureAtlas.TileSize));

            var tileX = (entry.Index % BlockTextureAtlas.TilesPerRow) * BlockTextureAtlas.TileSize;
            var tileY = (entry.Index / BlockTextureAtlas.TilesPerRow) * BlockTextureAtlas.TileSize;
            atlas.Mutate(context => context.DrawImage(source, new Point(tileX, tileY), 1f));
        }

        var pixels = new byte[atlasWidth * atlasHeight * 4];
        atlas.CopyPixelDataTo(pixels);
        return new AtlasImage(atlasWidth, atlasHeight, pixels);
    }

    private static IReadOnlyList<AtlasEntry> GetEntries()
    {
        var entries = new List<AtlasEntry>
        {
            new(BlockTextureAtlas.GrassTop, "block/grass_block_top.png"),
            new(BlockTextureAtlas.GrassSide, "block/grass_block_side.png"),
            new(BlockTextureAtlas.Dirt, "block/dirt.png"),
            new(BlockTextureAtlas.Stone, "block/stone.png"),
            new(BlockTextureAtlas.Sand, "block/sand.png"),
            new(BlockTextureAtlas.Water, "block/water_still.png"),
            new(BlockTextureAtlas.CorruptedGrass, "block/sculk.png"),
            new(BlockTextureAtlas.ArcaneCrystal, "block/amethyst_block.png"),
            new(BlockTextureAtlas.MagicOre, "block/diamond_ore.png")
        };

        for (var i = 0; i < 10; i++)
        {
            entries.Add(new AtlasEntry(BlockTextureAtlas.DestroyStage(i), $"block/destroy_stage_{i}.png"));
        }

        return entries;
    }

    internal readonly record struct AtlasImage(int Width, int Height, byte[] Pixels);

    private readonly record struct AtlasEntry(int Index, string FileName);
}
