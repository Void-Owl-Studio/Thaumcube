using VoxelGame.World.Blocks;

namespace VoxelGame.World.Chunks;

public sealed class Chunk
{
    public const int SizeX = 16;
    public const int SizeY = 128;
    public const int SizeZ = 16;
    public const int BlockCount = SizeX * SizeY * SizeZ;

    private readonly ushort[] _blocks = new ushort[BlockCount];

    public ChunkCoord Coord { get; }
    public bool IsDirty { get; private set; } = true;
    public ChunkRenderMesh? Mesh { get; private set; }
    public ChunkAura Aura { get; set; }

    public Chunk(ChunkCoord coord)
    {
        Coord = coord;
    }

    public BlockType GetBlock(int x, int y, int z)
    {
        if (!ContainsLocal(x, y, z))
        {
            return BlockType.Air;
        }

        return (BlockType)_blocks[ToIndex(x, y, z)];
    }

    public void SetBlock(int x, int y, int z, BlockType type)
    {
        if (!ContainsLocal(x, y, z))
        {
            return;
        }

        _blocks[ToIndex(x, y, z)] = (ushort)type;
        MarkDirty();
    }

    public void SetGeneratedBlock(int x, int y, int z, BlockType type)
    {
        _blocks[ToIndex(x, y, z)] = (ushort)type;
    }

    public void SetMesh(ChunkRenderMesh mesh)
    {
        Mesh = mesh;
        IsDirty = false;
    }

    public void MarkDirty() => IsDirty = true;

    public static bool ContainsLocal(int x, int y, int z)
    {
        return x >= 0 && x < SizeX && y >= 0 && y < SizeY && z >= 0 && z < SizeZ;
    }

    public static int ToIndex(int x, int y, int z)
    {
        return x + SizeX * (z + SizeZ * y);
    }
}
