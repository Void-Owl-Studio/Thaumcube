namespace VoxelGame.World.Blocks;

public static class BlockTextureAtlas
{
    public const int TileSize = 16;
    public const int TilePadding = 1;
    public const int TilesPerRow = 4;

    public const int GrassTop = 0;
    public const int GrassSide = 1;
    public const int Dirt = 2;
    public const int Stone = 3;
    public const int Sand = 4;
    public const int Water = 5;
    public const int CorruptedGrass = 6;
    public const int ArcaneCrystal = 7;
    public const int MagicOre = 8;
    public const int Snow = 9;
    public const int OakLogSide = 10;
    public const int OakLogTop = 11;
    public const int OakLeaves = 12;
    public const int DestroyStageStart = 13;
    public const int WaterEdge = 23;
    public const int PlayerWhite = 24;
    public const int TextureCount = 25;
    public const int PlayerSkinWidth = 64;
    public const int PlayerSkinHeight = 64;

    public const int PaddedTileSize = TileSize + TilePadding * 2;

    public static int DestroyStage(int stage) => DestroyStageStart + Math.Clamp(stage, 0, 9);

    public static int GetAtlasRows() => (TextureCount + TilesPerRow - 1) / TilesPerRow;

    public static int GetBlockAtlasWidth() => TilesPerRow * PaddedTileSize;

    public static int GetBlockAtlasHeight() => GetAtlasRows() * PaddedTileSize;

    public static int GetAtlasWidth() => Math.Max(GetBlockAtlasWidth(), PlayerSkinWidth);

    public static int GetAtlasHeight() => GetBlockAtlasHeight() + PlayerSkinHeight;
}
