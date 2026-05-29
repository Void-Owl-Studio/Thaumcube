namespace VoxelGame.World.Blocks;

public static class BlockTextureAtlas
{
    public const int TileSize = 16;
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
    public const int DestroyStageStart = 9;
    public const int TextureCount = 19;

    public static int DestroyStage(int stage) => DestroyStageStart + Math.Clamp(stage, 0, 9);
}
