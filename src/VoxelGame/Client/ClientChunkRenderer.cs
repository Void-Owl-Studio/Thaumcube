using VoxelGame.Shared;
using VoxelGame.World;
using VoxelGame.World.Chunks;

namespace VoxelGame.Client;

public sealed class ClientChunkRenderer
{
    private readonly MeshBuilder _meshBuilder = new();
    private readonly Dictionary<ChunkCoord, ClientChunkRenderState> _chunks = new();
    private readonly Queue<ChunkCoord> _dirtyRemeshQueue = new();
    private readonly Dictionary<(ChunkCoord Coord, int Section), Task<SectionMeshResult>> _meshingTasks = new();
    private readonly Queue<SectionMeshResult> _pendingMeshUploads = new();
    private readonly Queue<ChunkCoord> _pendingUnloads = new();
    private readonly HashSet<ChunkCoord> _queuedDirtyChunks = [];

    public int VisibleMeshCount => _chunks.Values.Sum(chunk => chunk.VisibleMeshCount);

    public IEnumerable<ChunkRenderMesh> VisibleMeshes => _chunks.Values.SelectMany(chunk => chunk.GetVisibleMeshes());

    public void SetChunk(ChunkData chunk)
    {
        if (!_chunks.TryGetValue(chunk.Coord, out var state))
        {
            state = new ClientChunkRenderState(chunk);
            _chunks.Add(chunk.Coord, state);
        }
        else
        {
            state.Chunk = chunk;
        }

        state.State = ClientChunkState.Missing;
        state.MarkAllDirty();
        QueueDirty(chunk.Coord);
    }

    public void MarkBlockDirty(ChunkCoord coord, int y, bool includeHorizontalNeighbors, (int X, int Z) local)
    {
        MarkSectionDirty(coord, y);

        if (!includeHorizontalNeighbors)
        {
            return;
        }

        if (local.X == 0) MarkSectionDirty(new ChunkCoord(coord.X - 1, coord.Z), y);
        if (local.X == ChunkData.SizeX - 1) MarkSectionDirty(new ChunkCoord(coord.X + 1, coord.Z), y);
        if (local.Z == 0) MarkSectionDirty(new ChunkCoord(coord.X, coord.Z - 1), y);
        if (local.Z == ChunkData.SizeZ - 1) MarkSectionDirty(new ChunkCoord(coord.X, coord.Z + 1), y);
    }

    public void QueueUnload(ChunkCoord coord)
    {
        _pendingUnloads.Enqueue(coord);
    }

    public void Tick(ClientWorld world, int maxMeshUploadsPerFrame, int maxUnloadsPerFrame)
    {
        ProcessCompletedMeshing();
        StartQueuedMeshing(world);
        ProcessMeshUploads(Math.Max(0, maxMeshUploadsPerFrame));
        ProcessUnloads(Math.Max(0, maxUnloadsPerFrame));
    }

    public void Drain(ClientWorld world, Action<int, int>? progress = null)
    {
        var totalSections = GetPendingSectionWorkCount();
        if (totalSections <= 0)
        {
            progress?.Invoke(1, 1);
            return;
        }

        while (_dirtyRemeshQueue.Count > 0 || _meshingTasks.Count > 0 || _pendingMeshUploads.Count > 0 || _pendingUnloads.Count > 0)
        {
            ProcessCompletedMeshing();
            StartQueuedMeshing(world);

            if (_meshingTasks.Count > 0)
            {
                Task.WaitAll(_meshingTasks.Values.ToArray());
            }

            ProcessCompletedMeshing();
            ProcessMeshUploads(int.MaxValue);
            ProcessUnloads(int.MaxValue);
            progress?.Invoke(Math.Clamp(totalSections - GetPendingSectionWorkCount(), 0, totalSections), totalSections);
        }

        progress?.Invoke(totalSections, totalSections);
    }

    private int GetPendingSectionWorkCount()
    {
        return _meshingTasks.Count + _pendingMeshUploads.Count + _chunks.Values.Sum(chunk => chunk.CountDirtySections());
    }

    private void MarkSectionDirty(ChunkCoord coord, int y)
    {
        if (!_chunks.TryGetValue(coord, out var state))
        {
            return;
        }

        var sectionIndex = ChunkData.GetSectionIndex(y);
        state.MarkDirty(sectionIndex);

        var localSectionY = (y - ChunkData.MinY) % ChunkData.MeshSectionHeight;
        if (localSectionY == 0 && sectionIndex > 0)
        {
            state.MarkDirty(sectionIndex - 1);
        }

        if (localSectionY == ChunkData.MeshSectionHeight - 1 && sectionIndex < ChunkData.MeshSectionCount - 1)
        {
            state.MarkDirty(sectionIndex + 1);
        }

        QueueDirty(coord);
    }

    private void QueueDirty(ChunkCoord coord)
    {
        if (_queuedDirtyChunks.Add(coord))
        {
            _dirtyRemeshQueue.Enqueue(coord);
        }
    }

    private void StartQueuedMeshing(ClientWorld world)
    {
        while (_dirtyRemeshQueue.Count > 0)
        {
            var coord = _dirtyRemeshQueue.Dequeue();
            _queuedDirtyChunks.Remove(coord);
            if (!_chunks.TryGetValue(coord, out var state))
            {
                continue;
            }

            foreach (var sectionIndex in state.TakeDirtySections())
            {
                var key = (coord, sectionIndex);
                if (_meshingTasks.ContainsKey(key))
                {
                    state.MarkDirty(sectionIndex);
                    continue;
                }

                var chunk = state.Chunk.Clone();
                state.State = ClientChunkState.Meshing;
                _meshingTasks[key] = Task.Run(() => new SectionMeshResult(coord, sectionIndex, _meshBuilder.BuildSection(world, chunk, sectionIndex)));
            }
        }
    }

    private void ProcessCompletedMeshing()
    {
        foreach (var (key, task) in _meshingTasks.ToArray())
        {
            if (!task.IsCompleted)
            {
                continue;
            }

            _meshingTasks.Remove(key);
            _pendingMeshUploads.Enqueue(task.GetAwaiter().GetResult());
        }
    }

    private void ProcessMeshUploads(int uploadBudget)
    {
        while (uploadBudget > 0 && _pendingMeshUploads.Count > 0)
        {
            var result = _pendingMeshUploads.Dequeue();
            if (_chunks.TryGetValue(result.Coord, out var state))
            {
                state.SetSectionMeshes(result.SectionIndex, result.Meshes);
                state.State = ClientChunkState.Rendered;
                uploadBudget--;
            }
        }
    }

    private void ProcessUnloads(int unloadBudget)
    {
        while (unloadBudget > 0 && _pendingUnloads.Count > 0)
        {
            var coord = _pendingUnloads.Dequeue();
            _chunks.Remove(coord);
            unloadBudget--;
        }
    }

    private sealed class ClientChunkRenderState
    {
        private readonly bool[] _dirtySections = new bool[ChunkData.MeshSectionCount];
        private readonly ChunkRenderMesh?[] _opaqueSectionMeshes = new ChunkRenderMesh?[ChunkData.MeshSectionCount];
        private readonly ChunkRenderMesh?[] _transparentSectionMeshes = new ChunkRenderMesh?[ChunkData.MeshSectionCount];

        public ClientChunkRenderState(ChunkData chunk)
        {
            Chunk = chunk;
        }

        public ChunkData Chunk { get; set; }
        public ClientChunkState State { get; set; } = ClientChunkState.Missing;

        public int VisibleMeshCount
        {
            get
            {
                var count = 0;
                for (var sectionIndex = 0; sectionIndex < ChunkData.MeshSectionCount; sectionIndex++)
                {
                    if (_opaqueSectionMeshes[sectionIndex] is { IsEmpty: false }) count++;
                    if (_transparentSectionMeshes[sectionIndex] is { IsEmpty: false }) count++;
                }

                return count;
            }
        }

        public void MarkAllDirty()
        {
            Array.Fill(_dirtySections, true);
        }

        public void MarkDirty(int sectionIndex)
        {
            _dirtySections[sectionIndex] = true;
        }

        public IEnumerable<int> TakeDirtySections()
        {
            for (var sectionIndex = 0; sectionIndex < _dirtySections.Length; sectionIndex++)
            {
                if (!_dirtySections[sectionIndex])
                {
                    continue;
                }

                _dirtySections[sectionIndex] = false;
                yield return sectionIndex;
            }
        }

        public void SetSectionMeshes(int sectionIndex, ChunkSectionMeshes meshes)
        {
            _opaqueSectionMeshes[sectionIndex] = meshes.OpaqueMesh;
            _transparentSectionMeshes[sectionIndex] = meshes.TransparentMesh;
        }

        public int CountDirtySections()
        {
            var count = 0;
            for (var sectionIndex = 0; sectionIndex < _dirtySections.Length; sectionIndex++)
            {
                if (_dirtySections[sectionIndex])
                {
                    count++;
                }
            }

            return count;
        }

        public IEnumerable<ChunkRenderMesh> GetVisibleMeshes()
        {
            for (var sectionIndex = 0; sectionIndex < ChunkData.MeshSectionCount; sectionIndex++)
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
    }

    private readonly record struct SectionMeshResult(ChunkCoord Coord, int SectionIndex, ChunkSectionMeshes Meshes);
}
