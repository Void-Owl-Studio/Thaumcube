using VoxelGame.World.Blocks;

namespace VoxelGame.World.Chunks;

public sealed class Chunk
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

    private readonly ushort[] _blocks = new ushort[BlockCount];
    private readonly bool[] _dirtySections = new bool[MeshSectionCount];
    private readonly ChunkRenderMesh?[] _opaqueSectionMeshes = new ChunkRenderMesh?[MeshSectionCount];
    private readonly ChunkRenderMesh?[] _transparentSectionMeshes = new ChunkRenderMesh?[MeshSectionCount];
    private readonly int[] _nonAirBlockCounts = new int[MeshSectionCount];
    private int _dirtySectionCount = MeshSectionCount;

    public ChunkCoord Coord { get; }
    public bool IsDirty => _dirtySectionCount > 0;
    public bool HasModifications { get; private set; }
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

        var previousBlock = (BlockType)_blocks[index];
        _blocks[index] = (ushort)type;
        AdjustSectionBlockCount(y, previousBlock, type);
        HasModifications = true;

        var sectionIndex = GetSectionIndex(y);
        MarkDirty(sectionIndex);

        var localSectionY = (y - MinY) % MeshSectionHeight;
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
        var index = ToIndex(x, y, z);
        var previousBlock = (BlockType)_blocks[index];
        _blocks[index] = (ushort)type;
        AdjustSectionBlockCount(y, previousBlock, type);
    }

    public ushort[] CreateBlockSnapshot()
    {
        return (ushort[])_blocks.Clone();
    }

    public void RestoreBlockSnapshot(ReadOnlySpan<ushort> blocks, bool markModified = false)
    {
        if (blocks.Length != BlockCount)
        {
            throw new ArgumentException($"Chunk snapshot size must be exactly {BlockCount} blocks.", nameof(blocks));
        }

        blocks.CopyTo(_blocks);
        Array.Clear(_nonAirBlockCounts);
        for (var y = MinY; y < MaxYExclusive; y++)
        {
            var sectionIndex = GetSectionIndex(y);
            for (var z = 0; z < SizeZ; z++)
            for (var x = 0; x < SizeX; x++)
            {
                if ((BlockType)_blocks[ToIndex(x, y, z)] != BlockType.Air)
                {
                    _nonAirBlockCounts[sectionIndex]++;
                }
            }
        }

        HasModifications = markModified;
        MarkDirty();
    }

    public void SetSectionMeshes(int sectionIndex, ChunkSectionMeshes meshes)
    {
        _opaqueSectionMeshes[sectionIndex] = meshes.OpaqueMesh;
        _transparentSectionMeshes[sectionIndex] = meshes.TransparentMesh;

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
            var opaqueMesh = _opaqueSectionMeshes[sectionIndex];
            if (opaqueMesh is { IsEmpty: false })
            {
                yield return opaqueMesh;
            }

            var transparentMesh = _transparentSectionMeshes[sectionIndex];
            if (transparentMesh is { IsEmpty: false })
            {
                yield return transparentMesh;
            }
        }
    }

    public bool IsSectionEmpty(int sectionIndex) => _nonAirBlockCounts[sectionIndex] == 0;

    public int VisibleMeshCount
    {
        get
        {
            var count = 0;
            for (var sectionIndex = 0; sectionIndex < MeshSectionCount; sectionIndex++)
            {
                if (_opaqueSectionMeshes[sectionIndex] is { IsEmpty: false })
                {
                    count++;
                }

                if (_transparentSectionMeshes[sectionIndex] is { IsEmpty: false })
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

    private void AdjustSectionBlockCount(int y, BlockType previousBlock, BlockType nextBlock)
    {
        if (previousBlock == nextBlock)
        {
            return;
        }

        var sectionIndex = GetSectionIndex(y);
        if (previousBlock == BlockType.Air)
        {
            _nonAirBlockCounts[sectionIndex]++;
        }
        else if (nextBlock == BlockType.Air)
        {
            _nonAirBlockCounts[sectionIndex]--;
        }
    }
}
