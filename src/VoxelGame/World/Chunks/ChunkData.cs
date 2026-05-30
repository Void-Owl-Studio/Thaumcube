using VoxelGame.World.Blocks;

namespace VoxelGame.World.Chunks;

public class ChunkData
{
    public const int SizeX = 16;
    public const int MinY = -64;
    public const int SizeY = 384;
    public const int MaxYExclusive = MinY + SizeY;
    public const int MaxY = MaxYExclusive - 1;
    public const int SizeZ = 16;
    public const int BlockCount = SizeX * SizeY * SizeZ;
    public const int MeshSectionHeight = 64;
    public const int MeshSectionCount = SizeY / MeshSectionHeight;

    private readonly ushort[] _blocks;

    public ChunkCoord Coord { get; }
    public ChunkAura Aura { get; set; }
    public bool HasModifications { get; private set; }

    public ChunkData(ChunkCoord coord)
        : this(coord, new ushort[BlockCount], default)
    {
    }

    public ChunkData(ChunkCoord coord, ushort[] blocks, ChunkAura aura, bool hasModifications = false)
    {
        if (blocks.Length != BlockCount)
        {
            throw new ArgumentException($"Chunk data must contain exactly {BlockCount} blocks.", nameof(blocks));
        }

        Coord = coord;
        _blocks = blocks;
        Aura = aura;
        HasModifications = hasModifications;
    }

    public BlockType GetBlock(int x, int y, int z)
    {
        if (!ContainsLocal(x, y, z))
        {
            return BlockType.Air;
        }

        return (BlockType)_blocks[ToIndex(x, y, z)];
    }

    public bool SetBlock(int x, int y, int z, BlockType type)
    {
        if (!ContainsLocal(x, y, z))
        {
            return false;
        }

        var index = ToIndex(x, y, z);
        if (_blocks[index] == (ushort)type)
        {
            return false;
        }

        _blocks[index] = (ushort)type;
        HasModifications = true;
        return true;
    }

    public void SetGeneratedBlock(int x, int y, int z, BlockType type)
    {
        _blocks[ToIndex(x, y, z)] = (ushort)type;
    }

    public ushort[] CreateBlockSnapshot()
    {
        return (ushort[])_blocks.Clone();
    }

    public void MarkSaved()
    {
        HasModifications = false;
    }

    public ChunkData Clone()
    {
        return new ChunkData(Coord, CreateBlockSnapshot(), Aura, HasModifications);
    }

    public bool IsSectionEmpty(int sectionIndex)
    {
        var minY = MinY + sectionIndex * MeshSectionHeight;
        var maxY = minY + MeshSectionHeight;
        for (var y = minY; y < maxY; y++)
        for (var z = 0; z < SizeZ; z++)
        for (var x = 0; x < SizeX; x++)
        {
            if (GetBlock(x, y, z) != BlockType.Air)
            {
                return false;
            }
        }

        return true;
    }

    public static bool ContainsLocal(int x, int y, int z)
    {
        return x >= 0 && x < SizeX && y >= MinY && y < MaxYExclusive && z >= 0 && z < SizeZ;
    }

    public static int GetSectionIndex(int y)
    {
        return (y - MinY) / MeshSectionHeight;
    }

    public static int ToIndex(int x, int y, int z)
    {
        var localY = y - MinY;
        return x + SizeX * (z + SizeZ * localY);
    }
}
