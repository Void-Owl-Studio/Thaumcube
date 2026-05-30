using System.Numerics;
using VoxelGame.Shared;
using VoxelGame.Shared.Messages;
using VoxelGame.Shared.Transport;
using VoxelGame.World.Blocks;
using VoxelGame.World.Chunks;
using VoxelGame.World.Storage;

namespace VoxelGame.Server;

public sealed class ServerChunkManager
{
    private readonly Dictionary<ChunkCoord, ChunkData> _chunks = new();
    private readonly Dictionary<ChunkCoord, ServerChunkState> _states = new();
    private readonly Dictionary<ChunkCoord, Task<ChunkData>> _loadingTasks = new();
    private readonly HashSet<ChunkCoord> _neededChunks = [];
    private readonly HashSet<ChunkCoord> _queuedLoads = [];
    private readonly Queue<ChunkCoord> _pendingChunkLoads = new();
    private readonly Queue<ChunkCoord> _pendingChunkUnloads = new();
    private readonly ChunkGenerationSystem _generation;
    private readonly WorldSaveStore? _saveStore;
    private readonly int _renderDistance;
    private ChunkCoord? _loadedCenter;

    public ServerChunkManager(ChunkGenerationSystem generation, int renderDistance, WorldSaveStore? saveStore)
    {
        _generation = generation;
        _renderDistance = Math.Max(1, renderDistance);
        _saveStore = saveStore;
    }

    public int RenderDistance => _renderDistance;
    public int ActiveChunkCount => _chunks.Count;

    public void HandlePlayerPosition(Vector3 position, IMessageBus bus, ChunkStreamingOptions options, bool immediate, Action<int, int>? progress = null)
    {
        var center = ToChunkCoord((int)MathF.Floor(position.X), (int)MathF.Floor(position.Z));
        if (_loadedCenter is not { } loadedCenter || loadedCenter != center)
        {
            RebuildLoadQueue(center);
            _loadedCenter = center;
        }

        Tick(bus, options, immediate, progress);
    }

    public void Tick(IMessageBus bus, ChunkStreamingOptions options, bool immediate = false, Action<int, int>? progress = null)
    {
        if (immediate)
        {
            ProcessImmediate(bus, progress);
            return;
        }

        ProcessCompletedLoads(bus);
        StartPendingLoads(Math.Max(1, options.MaxChunkLoadsPerTick));
        ProcessCompletedLoads(bus);
        ProcessPendingUnloads(bus, Math.Max(0, options.MaxUnloadsPerFrame));
    }

    public ChunkData? GetChunk(ChunkCoord coord)
    {
        return _chunks.GetValueOrDefault(coord);
    }

    public BlockType GetBlock(int worldX, int y, int worldZ)
    {
        if (y < ChunkData.MinY || y >= ChunkData.MaxYExclusive)
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

    public void SetBlock(int worldX, int y, int worldZ, BlockType type, IMessageBus bus)
    {
        if (y < ChunkData.MinY || y >= ChunkData.MaxYExclusive)
        {
            return;
        }

        var coord = ToChunkCoord(worldX, worldZ);
        if (!_chunks.TryGetValue(coord, out var chunk))
        {
            chunk = _generation.LoadOrGenerate(coord);
            _chunks[coord] = chunk;
            _states[coord] = ServerChunkState.Active;
            bus.SendToClient(new ChunkDataMessage(coord, chunk.Clone()));
        }

        var local = ToLocal(worldX, worldZ);
        if (!chunk.SetBlock(local.X, y, local.Z, type))
        {
            return;
        }

        bus.SendToClient(new BlockUpdateMessage(worldX, y, worldZ, type));
    }

    public void SaveLoadedModifiedChunks(Action<int, int>? progress = null)
    {
        var modifiedChunks = _chunks.Values.Where(chunk => chunk.HasModifications).ToArray();
        if (modifiedChunks.Length == 0)
        {
            progress?.Invoke(1, 1);
            return;
        }

        for (var i = 0; i < modifiedChunks.Length; i++)
        {
            SaveChunkState(modifiedChunks[i]);
            progress?.Invoke(i + 1, modifiedChunks.Length);
        }
    }

    public static ChunkCoord ToChunkCoord(int worldX, int worldZ)
    {
        return new ChunkCoord(FloorDiv(worldX, ChunkData.SizeX), FloorDiv(worldZ, ChunkData.SizeZ));
    }

    public static (int X, int Z) ToLocal(int worldX, int worldZ)
    {
        return (FloorMod(worldX, ChunkData.SizeX), FloorMod(worldZ, ChunkData.SizeZ));
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
            if (!_chunks.ContainsKey(coord) && !_loadingTasks.ContainsKey(coord) && _queuedLoads.Add(coord))
            {
                loadQueue.Add((coord, distanceSquared));
            }
        }

        foreach (var coord in _chunks.Keys.ToArray())
        {
            if (!_neededChunks.Contains(coord))
            {
                _pendingChunkUnloads.Enqueue(coord);
            }
        }

        loadQueue.Sort((left, right) => left.DistanceSquared.CompareTo(right.DistanceSquared));
        foreach (var (coord, _) in loadQueue)
        {
            _pendingChunkLoads.Enqueue(coord);
        }
    }

    private void StartPendingLoads(int chunkBudget)
    {
        while (chunkBudget > 0 && _pendingChunkLoads.Count > 0)
        {
            var coord = _pendingChunkLoads.Dequeue();
            _queuedLoads.Remove(coord);
            if (!_neededChunks.Contains(coord) || _chunks.ContainsKey(coord) || _loadingTasks.ContainsKey(coord))
            {
                continue;
            }

            _states[coord] = ServerChunkState.Loading;
            _loadingTasks[coord] = Task.Run(() => _generation.LoadOrGenerate(coord));
            chunkBudget--;
        }
    }

    private void ProcessCompletedLoads(IMessageBus bus)
    {
        foreach (var (coord, task) in _loadingTasks.ToArray())
        {
            if (!task.IsCompleted)
            {
                continue;
            }

            _loadingTasks.Remove(coord);
            if (!_neededChunks.Contains(coord))
            {
                _states[coord] = ServerChunkState.Unloaded;
                continue;
            }

            var chunk = task.GetAwaiter().GetResult();
            _chunks[coord] = chunk;
            _states[coord] = ServerChunkState.Active;
            bus.SendToClient(new ChunkDataMessage(coord, chunk.Clone()));
        }
    }

    private void ProcessPendingUnloads(IMessageBus bus, int unloadBudget)
    {
        while (unloadBudget > 0 && _pendingChunkUnloads.Count > 0)
        {
            var coord = _pendingChunkUnloads.Dequeue();
            if (_neededChunks.Contains(coord) || !_chunks.TryGetValue(coord, out var chunk))
            {
                continue;
            }

            SaveChunkState(chunk);
            _chunks.Remove(coord);
            _states[coord] = ServerChunkState.Unloaded;
            bus.SendToClient(new UnloadChunkMessage(coord));
            unloadBudget--;
        }
    }

    private void ProcessImmediate(IMessageBus bus, Action<int, int>? progress = null)
    {
        var totalLoads = _pendingChunkLoads.Count;
        var completedLoads = 0;
        if (totalLoads == 0)
        {
            progress?.Invoke(1, 1);
        }

        while (_pendingChunkLoads.Count > 0)
        {
            var coord = _pendingChunkLoads.Dequeue();
            _queuedLoads.Remove(coord);
            if (!_neededChunks.Contains(coord) || _chunks.ContainsKey(coord))
            {
                continue;
            }

            var chunk = _generation.LoadOrGenerate(coord);
            _chunks[coord] = chunk;
            _states[coord] = ServerChunkState.Active;
            bus.SendToClient(new ChunkDataMessage(coord, chunk.Clone()));
            completedLoads++;
            progress?.Invoke(completedLoads, totalLoads);
        }

        ProcessPendingUnloads(bus, int.MaxValue);
    }

    private void SaveChunkState(ChunkData chunk)
    {
        if (!chunk.HasModifications)
        {
            return;
        }

        _saveStore?.SaveChunk(chunk.Coord, new ChunkSnapshotData(chunk.CreateBlockSnapshot(), chunk.Aura));
        chunk.MarkSaved();
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
