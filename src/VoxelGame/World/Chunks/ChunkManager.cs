using System.Numerics;
using VoxelGame.World.Blocks;
using VoxelGame.World.Generation;

namespace VoxelGame.World.Chunks;

public sealed class ChunkManager
{
    private readonly Dictionary<ChunkCoord, Chunk> _chunks = new();
    private readonly WorldGenerator _generator;
    private readonly ChunkMeshBuilder _meshBuilder = new();
    private readonly int _renderDistance;

    public ChunkManager(WorldGenerator generator, int renderDistance)
    {
        _generator = generator;
        _renderDistance = Math.Max(1, renderDistance);
    }

    public int LoadedChunkCount => _chunks.Count;
    public int VisibleMeshCount => _chunks.Values.Count(chunk => chunk.Mesh is { IsEmpty: false });

    public IEnumerable<ChunkRenderMesh> VisibleMeshes => _chunks.Values
        .Select(chunk => chunk.Mesh)
        .Where(mesh => mesh is { IsEmpty: false })!;

    public void LoadAround(Vector3 position)
    {
        var center = ToChunkCoord((int)MathF.Floor(position.X), (int)MathF.Floor(position.Z));
        var needed = new HashSet<ChunkCoord>();

        for (var z = -_renderDistance; z <= _renderDistance; z++)
        for (var x = -_renderDistance; x <= _renderDistance; x++)
        {
            var coord = new ChunkCoord(center.X + x, center.Z + z);
            needed.Add(coord);
            EnsureLoaded(coord);
        }

        foreach (var coord in _chunks.Keys.ToArray())
        {
            if (!needed.Contains(coord))
            {
                _chunks.Remove(coord);
            }
        }
    }

    public Chunk? GetChunk(ChunkCoord coord) => _chunks.GetValueOrDefault(coord);

    public BlockType GetBlock(int worldX, int y, int worldZ)
    {
        if (y < 0 || y >= Chunk.SizeY)
        {
            return BlockType.Air;
        }

        var coord = ToChunkCoord(worldX, worldZ);
        if (!_chunks.TryGetValue(coord, out var chunk))
        {
            return BlockType.Air;
        }

        var local = ToLocal(worldX, worldZ);
        return chunk.GetBlock(local.X, y, local.Z);
    }

    public void SetBlock(int worldX, int y, int worldZ, BlockType type)
    {
        if (y < 0 || y >= Chunk.SizeY)
        {
            return;
        }

        var coord = ToChunkCoord(worldX, worldZ);
        var chunk = EnsureLoaded(coord);
        var local = ToLocal(worldX, worldZ);
        chunk.SetBlock(local.X, y, local.Z, type);

        if (local.X == 0) MarkDirty(new ChunkCoord(coord.X - 1, coord.Z));
        if (local.X == Chunk.SizeX - 1) MarkDirty(new ChunkCoord(coord.X + 1, coord.Z));
        if (local.Z == 0) MarkDirty(new ChunkCoord(coord.X, coord.Z - 1));
        if (local.Z == Chunk.SizeZ - 1) MarkDirty(new ChunkCoord(coord.X, coord.Z + 1));
    }

    public void RebuildDirtyMeshes(VoxelWorld world)
    {
        foreach (var chunk in _chunks.Values)
        {
            if (chunk.IsDirty)
            {
                chunk.SetMesh(_meshBuilder.Build(world, chunk));
            }
        }
    }

    public static ChunkCoord ToChunkCoord(int worldX, int worldZ)
    {
        return new ChunkCoord(FloorDiv(worldX, Chunk.SizeX), FloorDiv(worldZ, Chunk.SizeZ));
    }

    public static (int X, int Z) ToLocal(int worldX, int worldZ)
    {
        return (FloorMod(worldX, Chunk.SizeX), FloorMod(worldZ, Chunk.SizeZ));
    }

    private Chunk EnsureLoaded(ChunkCoord coord)
    {
        if (_chunks.TryGetValue(coord, out var existing))
        {
            return existing;
        }

        var chunk = new Chunk(coord);
        _generator.Generate(chunk);
        _chunks.Add(coord, chunk);
        return chunk;
    }

    private void MarkDirty(ChunkCoord coord)
    {
        if (_chunks.TryGetValue(coord, out var chunk))
        {
            chunk.MarkDirty();
        }
    }

    private static int FloorDiv(int value, int divisor)
    {
        var result = value / divisor;
        var remainder = value % divisor;
        return remainder != 0 && (remainder < 0) != (divisor < 0) ? result - 1 : result;
    }

    private static int FloorMod(int value, int divisor)
    {
        var result = value % divisor;
        return result < 0 ? result + divisor : result;
    }
}
