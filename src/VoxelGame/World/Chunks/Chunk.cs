using VoxelGame.World.Blocks;

namespace VoxelGame.World.Chunks;

public sealed class Chunk
{
    public const int SizeX = 16;
    public const int SizeY = 128;
    public const int SizeZ = 16;
    public const int BlockCount = SizeX * SizeY * SizeZ;
    public const int MeshSectionHeight = 16;
    public const int MeshSectionCount = SizeY / MeshSectionHeight;

    private readonly ushort[] _blocks = new ushort[BlockCount];
    private readonly bool[] _dirtySections = new bool[MeshSectionCount];
    private readonly ChunkRenderMesh?[] _sectionMeshes = new ChunkRenderMesh?[MeshSectionCount];
    private int _dirtySectionCount = MeshSectionCount;

    public ChunkCoord Coord { get; }
    public bool IsDirty => _dirtySectionCount > 0;
    public ChunkAura Aura { get; set; }

    public Chunk(ChunkCoord coord)
    {
        Coord = coord;

        for (var sectionIndex = 0; sectionIndex < MeshSectionCount; sectionIndex++)
        {
            _dirtySections[sectionIndex] = true;
        }
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

        var sectionIndex = GetSectionIndex(y);
        MarkDirty(sectionIndex);

        var localSectionY = y % MeshSectionHeight;
        if (localSectionY == 0 && sectionIndex > 0)
        {
            MarkDirty(sectionIndex - 1);
        }

        if (localSectionY == MeshSectionHeight - 1 && sectionIndex < MeshSectionCount - 1)
        {
            MarkDirty(sectionIndex + 1);
        }

        return true;
    }

    public void SetGeneratedBlock(int x, int y, int z, BlockType type)
    {
        _blocks[ToIndex(x, y, z)] = (ushort)type;
    }

    public void SetSectionMesh(int sectionIndex, ChunkRenderMesh mesh)
    {
        _sectionMeshes[sectionIndex] = mesh;

        if (_dirtySections[sectionIndex])
        {
            _dirtySections[sectionIndex] = false;
            _dirtySectionCount--;
        }
    }

    public IEnumerable<int> GetDirtySectionIndexes()
    {
        for (var sectionIndex = 0; sectionIndex < MeshSectionCount; sectionIndex++)
        {
            if (_dirtySections[sectionIndex])
            {
                yield return sectionIndex;
            }
        }
    }

    public IEnumerable<ChunkRenderMesh> GetVisibleMeshes()
    {
        for (var sectionIndex = 0; sectionIndex < MeshSectionCount; sectionIndex++)
        {
            var mesh = _sectionMeshes[sectionIndex];
            if (mesh is { IsEmpty: false })
            {
                yield return mesh;
            }
        }
    }

    public int VisibleMeshCount
    {
        get
        {
            var count = 0;
            for (var sectionIndex = 0; sectionIndex < MeshSectionCount; sectionIndex++)
            {
                if (_sectionMeshes[sectionIndex] is { IsEmpty: false })
                {
                    count++;
                }
            }

            return count;
        }
    }

    public void MarkDirty()
    {
        for (var sectionIndex = 0; sectionIndex < MeshSectionCount; sectionIndex++)
        {
            MarkDirty(sectionIndex);
        }
    }

    public void MarkDirty(int sectionIndex)
    {
        if (_dirtySections[sectionIndex])
        {
            return;
        }

        _dirtySections[sectionIndex] = true;
        _dirtySectionCount++;
    }

    public static bool ContainsLocal(int x, int y, int z)
    {
        return x >= 0 && x < SizeX && y >= 0 && y < SizeY && z >= 0 && z < SizeZ;
    }

    public static int GetSectionIndex(int y)
    {
        return y / MeshSectionHeight;
    }

    public static int ToIndex(int x, int y, int z)
    {
        return x + SizeX * (z + SizeZ * y);
    }
}
