using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using VoxelGame.Player;
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
        var atlasWidth = BlockTextureAtlas.GetAtlasWidth();
        var atlasHeight = BlockTextureAtlas.GetAtlasHeight();

        using var atlas = new Image<Rgba32>(atlasWidth, atlasHeight);

        foreach (var entry in GetEntries())
        {
            using var source = string.IsNullOrEmpty(entry.FileName)
                ? new Image<Rgba32>(BlockTextureAtlas.TileSize, BlockTextureAtlas.TileSize, Color.White)
                : Image.Load<Rgba32>(Path.Combine(_rootDirectory, entry.FileName));
            source.Mutate(context => context.Resize(new ResizeOptions
            {
                Size = new Size(BlockTextureAtlas.TileSize, BlockTextureAtlas.TileSize),
                Sampler = KnownResamplers.NearestNeighbor
            }));

            var tileX = (entry.Index % BlockTextureAtlas.TilesPerRow) * BlockTextureAtlas.PaddedTileSize;
            var tileY = (entry.Index / BlockTextureAtlas.TilesPerRow) * BlockTextureAtlas.PaddedTileSize;
            CopyTileWithPadding(atlas, source, tileX, tileY);
        }

        using (var playerSkin = LoadPlayerSkin())
        {
            CopyImage(atlas, playerSkin, 0, BlockTextureAtlas.GetBlockAtlasHeight());
        }

        var pixels = new byte[atlasWidth * atlasHeight * 4];
        atlas.CopyPixelDataTo(pixels);
        return new AtlasImage(atlasWidth, atlasHeight, pixels);
    }

    internal static IReadOnlyList<AtlasEntry> GetEntries()
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
            new(BlockTextureAtlas.MagicOre, "block/diamond_ore.png"),
            new(BlockTextureAtlas.Snow, "block/snow.png"),
            new(BlockTextureAtlas.OakLogSide, "block/oak_log.png"),
            new(BlockTextureAtlas.OakLogTop, "block/oak_log_top.png"),
            new(BlockTextureAtlas.OakLeaves, "block/oak_leaves.png"),
            new(BlockTextureAtlas.WaterEdge, "block/water_flow.png")
        };

        for (var i = 0; i < 10; i++)
        {
            entries.Add(new AtlasEntry(BlockTextureAtlas.DestroyStage(i), $"block/destroy_stage_{i}.png"));
        }

        entries.Add(new AtlasEntry(BlockTextureAtlas.PlayerWhite, string.Empty));

        return entries;
    }

    private static void CopyTileWithPadding(Image<Rgba32> atlas, Image<Rgba32> source, int tileX, int tileY)
    {
        var padding = BlockTextureAtlas.TilePadding;
        var tileSize = BlockTextureAtlas.TileSize;
        var paddedTileSize = BlockTextureAtlas.PaddedTileSize;

        for (var y = 0; y < tileSize; y++)
        {
            for (var x = 0; x < tileSize; x++)
            {
                atlas[tileX + padding + x, tileY + padding + y] = source[x, y];
            }
        }

        for (var y = 0; y < tileSize; y++)
        {
            var leftPixel = source[0, y];
            var rightPixel = source[tileSize - 1, y];
            for (var pad = 0; pad < padding; pad++)
            {
                atlas[tileX + pad, tileY + padding + y] = leftPixel;
                atlas[tileX + padding + tileSize + pad, tileY + padding + y] = rightPixel;
            }
        }

        for (var pad = 0; pad < padding; pad++)
        {
            for (var x = 0; x < paddedTileSize; x++)
            {
                atlas[tileX + x, tileY + pad] = atlas[tileX + x, tileY + padding];
                atlas[tileX + x, tileY + padding + tileSize + pad] = atlas[tileX + x, tileY + padding + tileSize - 1];
            }
        }
    }

    private Image<Rgba32> LoadPlayerSkin()
    {
        var appliedSkin = PlayerSkinStore.LoadAppliedSkin();
        if (appliedSkin is not null)
        {
            return appliedSkin;
        }

        var modelPath = Path.Combine(AppContext.BaseDirectory, "Assets", "models", "playermodel.gltf");
        if (!File.Exists(modelPath))
        {
            modelPath = Path.Combine(AppContext.BaseDirectory, "assets", "models", "playermodel.gltf");
        }

        var file = File.ReadAllText(modelPath);
        var marker = "\"uri\":\"data:image/png;base64,";
        var markerIndex = file.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            throw new InvalidOperationException("Player skin image was not found in playermodel.gltf.");
        }

        var startIndex = markerIndex + marker.Length;
        var endIndex = file.IndexOf('"', startIndex);
        var base64 = file[startIndex..endIndex];
        return Image.Load<Rgba32>(Convert.FromBase64String(base64));
    }

    private static void CopyImage(Image<Rgba32> atlas, Image<Rgba32> source, int offsetX, int offsetY)
    {
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                atlas[offsetX + x, offsetY + y] = source[x, y];
            }
        }
    }

    internal readonly record struct AtlasImage(int Width, int Height, byte[] Pixels);

    internal readonly record struct AtlasEntry(int Index, string FileName);
}
