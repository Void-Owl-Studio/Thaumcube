using System.Numerics;
using VoxelGame.World.Blocks;
using VoxelGame.World.Generation;
using VoxelGame.World.Storage;

namespace VoxelGame.World.Chunks;

public sealed class ChunkManager
{
    private const int MaxChunkLoadsPerFrame = 1;
    private const int MaxChunkUnloadsPerFrame = 1;
    private const int MaxMeshSectionsPerFrame = 2;

    private readonly Dictionary<ChunkCoord, Chunk> _chunks = new();
    private readonly HashSet<ChunkCoord> _dirtyChunks = [];
    private readonly Dictionary<ChunkCoord, ChunkSnapshot> _savedChunks = new();
    private readonly HashSet<ChunkCoord> _neededChunks = [];
    private readonly HashSet<ChunkCoord> _pendingChunkUnloadSet = [];
    private readonly WorldGenerator _generator;
    private readonly WorldSaveStore? _saveStore;
    private readonly ChunkMeshBuilder _meshBuilder = new();
    private readonly int _renderDistance;
    private readonly Queue<ChunkCoord> _pendingChunkLoads = new();
    private readonly Queue<ChunkCoord> _pendingChunkUnloads = new();
    private ChunkCoord? _loadedCenter;

    public ChunkManager(WorldGenerator generator, int renderDistance, WorldSaveStore? saveStore = null)
    {
        _generator = generator;
        _renderDistance = Math.Max(1, renderDistance);
        _saveStore = saveStore;
    }

    public int RenderDistance => _renderDistance;
    public int LoadedChunkCount => _chunks.Count;
    public int VisibleMeshCount => _chunks.Values.Sum(chunk => chunk.VisibleMeshCount);

    public IEnumerable<ChunkRenderMesh> VisibleMeshes => _chunks.Values.SelectMany(chunk => chunk.GetVisibleMeshes());

    public void LoadAround(Vector3 position, bool immediate = false)
    {
        var center = ToChunkCoord((int)MathF.Floor(position.X), (int)MathF.Floor(position.Z));
        if (_loadedCenter is not { } loadedCenter || loadedCenter != center)
        {
            RebuildLoadQueue(center);
            _loadedCenter = center;
        }

        ProcessPendingChunkLoads(immediate ? int.MaxValue : MaxChunkLoadsPerFrame);
        ProcessPendingChunkUnloads(immediate ? int.MaxValue : MaxChunkUnloadsPerFrame);
    }

    public Chunk? GetChunk(ChunkCoord coord) => _chunks.GetValueOrDefault(coord);

    public BlockType GetBlock(int worldX, int y, int worldZ)
    {
        if (y < Chunk.MinY || y >= Chunk.MaxYExclusive)
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
        if (y < Chunk.MinY || y >= Chunk.MaxYExclusive)
        {
            return;
        }

        var coord = ToChunkCoord(worldX, worldZ);
        var chunk = EnsureLoaded(coord);
        var local = ToLocal(worldX, worldZ);
        if (!chunk.SetBlock(local.X, y, local.Z, type))
        {
            return;
        }

        _dirtyChunks.Add(coord);

        var sectionIndex = Chunk.GetSectionIndex(y);

        if (local.X == 0) MarkDirty(new ChunkCoord(coord.X - 1, coord.Z), sectionIndex);
        if (local.X == Chunk.SizeX - 1) MarkDirty(new ChunkCoord(coord.X + 1, coord.Z), sectionIndex);
        if (local.Z == 0) MarkDirty(new ChunkCoord(coord.X, coord.Z - 1), sectionIndex);
        if (local.Z == Chunk.SizeZ - 1) MarkDirty(new ChunkCoord(coord.X, coord.Z + 1), sectionIndex);
    }

    public void RebuildDirtyMeshes(VoxelWorld world, bool immediate = false)
    {
        if (_dirtyChunks.Count == 0)
        {
            return;
        }

        var remainingSections = immediate ? int.MaxValue : MaxMeshSectionsPerFrame;
        foreach (var coord in _dirtyChunks.ToArray())
        {
            if (remainingSections <= 0)
            {
                break;
            }

            if (!_chunks.TryGetValue(coord, out var chunk))
            {
                _dirtyChunks.Remove(coord);
                continue;
            }

            foreach (var sectionIndex in chunk.GetDirtySectionIndexes().ToArray())
            {
                if (remainingSections <= 0)
                {
                    break;
                }

                if (chunk.IsSectionEmpty(sectionIndex))
                {
                    chunk.SetSectionMeshes(sectionIndex, CreateEmptySectionMeshes(chunk, sectionIndex));
                }
                else
                {
                    chunk.SetSectionMeshes(sectionIndex, _meshBuilder.BuildSection(world, chunk, sectionIndex));
                }

                remainingSections--;
            }

            if (!chunk.IsDirty)
            {
                _dirtyChunks.Remove(coord);
            }
        }
    }

    public void SaveLoadedModifiedChunks()
    {
        foreach (var chunk in _chunks.Values)
        {
            SaveChunkState(chunk);
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
        if (_savedChunks.TryGetValue(coord, out var cachedSnapshot))
        {
            chunk.Aura = cachedSnapshot.Aura;
            chunk.RestoreBlockSnapshot(cachedSnapshot.Blocks);
        }
        else if (_saveStore is not null && _saveStore.TryLoadChunk(coord, out var persistedSnapshot))
        {
            chunk.Aura = persistedSnapshot.Aura;
            chunk.RestoreBlockSnapshot(persistedSnapshot.Blocks);
            _savedChunks[coord] = new ChunkSnapshot(persistedSnapshot.Blocks, persistedSnapshot.Aura);
        }
        else
        {
            _generator.Generate(chunk);
        }

        _chunks.Add(coord, chunk);
        if (chunk.IsDirty)
        {
            _dirtyChunks.Add(coord);
        }

        return chunk;
    }

    private void SaveChunkState(Chunk chunk)
    {
        if (!chunk.HasModifications)
        {
            return;
        }

        var snapshot = new ChunkSnapshot(chunk.CreateBlockSnapshot(), chunk.Aura);
        _savedChunks[chunk.Coord] = snapshot;
        _saveStore?.SaveChunk(chunk.Coord, new ChunkSnapshotData(snapshot.Blocks, snapshot.Aura));
    }

    private void MarkDirty(ChunkCoord coord, int sectionIndex)
    {
        if (_chunks.TryGetValue(coord, out var chunk))
        {
            chunk.MarkDirty(sectionIndex);
            _dirtyChunks.Add(coord);
        }
    }

    private static ChunkSectionMeshes CreateEmptySectionMeshes(Chunk chunk, int sectionIndex)
    {
        return new ChunkSectionMeshes(
            ChunkRenderMesh.CreateEmpty(chunk.Coord, $"chunk:{chunk.Coord.X},{chunk.Coord.Z}:section:{sectionIndex}:opaque"),
            ChunkRenderMesh.CreateEmpty(chunk.Coord, $"chunk:{chunk.Coord.X},{chunk.Coord.Z}:section:{sectionIndex}:transparent", isTransparent: true));
    }

    private void RebuildLoadQueue(ChunkCoord center)
    {
        var loadQueue = new List<(ChunkCoord Coord, int DistanceSquared)>();
        var maxDistanceSquared = (_renderDistance + 0.5f) * (_renderDistance + 0.5f);
        _neededChunks.Clear();

        for (var z = -_renderDistance; z <= _renderDistance; z++)
        for (var x = -_renderDistance; x <= _renderDistance; x++)
        {
            var distanceSquared = x * x + z * z;
            if (distanceSquared > maxDistanceSquared)
            {
                continue;
            }

            var coord = new ChunkCoord(center.X + x, center.Z + z);
            _neededChunks.Add(coord);
            if (!_chunks.ContainsKey(coord))
            {
                loadQueue.Add((coord, distanceSquared));
            }
        }

        foreach (var coord in _chunks.Keys)
        {
            if (!_neededChunks.Contains(coord) && _pendingChunkUnloadSet.Add(coord))
            {
                _pendingChunkUnloads.Enqueue(coord);
            }
        }

        loadQueue.Sort((left, right) => left.DistanceSquared.CompareTo(right.DistanceSquared));
        _pendingChunkLoads.Clear();
        foreach (var (coord, _) in loadQueue)
        {
            _pendingChunkLoads.Enqueue(coord);
        }
    }

    private void ProcessPendingChunkLoads(int chunkBudget)
    {
        while (chunkBudget > 0 && _pendingChunkLoads.Count > 0)
        {
            var coord = _pendingChunkLoads.Dequeue();
            if (_neededChunks.Count == 0 || _neededChunks.Contains(coord))
            {
                EnsureLoaded(coord);
                chunkBudget--;
            }
        }
    }

    private void ProcessPendingChunkUnloads(int chunkBudget)
    {
        while (chunkBudget > 0 && _pendingChunkUnloads.Count > 0)
        {
            var coord = _pendingChunkUnloads.Dequeue();
            _pendingChunkUnloadSet.Remove(coord);
            if (_neededChunks.Contains(coord) || !_chunks.TryGetValue(coord, out var chunk))
            {
                continue;
            }

            SaveChunkState(chunk);
            _chunks.Remove(coord);
            _dirtyChunks.Remove(coord);
            chunkBudget--;
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

    private sealed record ChunkSnapshot(ushort[] Blocks, ChunkAura Aura);
}
