using System.Numerics;
using VoxelGame.Client;
using VoxelGame.Server;
using VoxelGame.Shared;
using VoxelGame.Shared.Transport;
using VoxelGame.World.Blocks;
using VoxelGame.World.Chunks;
using VoxelGame.World.Generation;
using VoxelGame.World.Storage;

namespace VoxelGame.World;

public sealed class VoxelWorld : IBlockWorld
{
    private readonly LocalLoopbackTransport _transport;
    private readonly ServerWorld _server;
    private readonly ClientWorld _client;
    private readonly WorldSaveStore? _saveStore;

    public BlockRegistry Blocks { get; } = new();
    public int Seed => _server.Seed;
    public string? Name => _saveStore?.Name;
    public int RenderDistanceChunks => _server.RenderDistanceChunks;
    public int LoadedChunkCount => _server.LoadedChunkCount;
    public int VisibleMeshCount => _client.VisibleMeshCount;

    public VoxelWorld(int seed, int renderDistance, WorldSaveStore? saveStore = null)
    {
        _saveStore = saveStore;
        _transport = new LocalLoopbackTransport();
        var streamingOptions = new ChunkStreamingOptions();
        _server = new ServerWorld(seed, renderDistance, saveStore, _transport, streamingOptions);
        _client = new ClientWorld(_transport, _server, streamingOptions);
    }

    public void LoadAround(Vector3 position, bool immediate = false)
    {
        ReportPlayerPosition(position, immediate);
    }

    public void LoadAround(Vector3 position, Action<int, int> progress)
    {
        _server.LoadAround(position, immediate: true, progress);
        _client.Tick();
    }

    public void ReportPlayerPosition(Vector3 position, bool immediate = false)
    {
        _client.SendPlayerPosition(position, immediate);
        Tick(immediate);
    }

    public void Tick(bool drainMeshing = false)
    {
        _server.Tick();
        _client.Tick();

        if (!drainMeshing)
        {
            return;
        }

        for (var i = 0; i < ChunkData.MeshSectionCount * Math.Max(1, LoadedChunkCount); i++)
        {
            _server.Tick();
            _client.Tick();
        }

        _client.DrainMeshing();
    }

    public BlockType GetBlock(int x, int y, int z) => _client.GetBlock(x, y, z);

    public void SetBlock(int x, int y, int z, BlockType type) => _client.SetBlock(x, y, z, type);

    public bool IsSolid(int x, int y, int z) => Blocks.IsSolid(GetBlock(x, y, z));

    public bool IsTransparent(int x, int y, int z)
    {
        if (y < Chunk.MinY)
        {
            return false;
        }

        if (y >= Chunk.MaxYExclusive)
        {
            return true;
        }

        return Blocks.IsTransparent(GetBlock(x, y, z));
    }

    public Vector3 FindSpawnPosition(int worldX, int worldZ)
    {
        return FindSpawnPosition(worldX, worldZ, null);
    }

    public Vector3 FindSpawnPosition(int worldX, int worldZ, Action<int, int>? progress)
    {
        if (TryGetDrySpawnColumn(worldX, worldZ, out var spawnX, out var spawnZ, out var surfaceHeight))
        {
            var roughSpawn = new Vector3(spawnX + 0.5f, Math.Clamp(surfaceHeight + 2, ChunkData.MinY + 2, ChunkData.MaxY - 4), spawnZ + 0.5f);
            LoadAround(roughSpawn, progress ?? NoopProgress);
            return ResolveLoadedSurfaceSpawn(spawnX, spawnZ, surfaceHeight);
        }

        var fallback = _server.GetColumnSample(worldX, worldZ);
        var fallbackSpawn = new Vector3(worldX + 0.5f, Math.Clamp(fallback.SurfaceHeight + 2, ChunkData.MinY + 2, ChunkData.MaxY - 4), worldZ + 0.5f);
        LoadAround(fallbackSpawn, progress ?? NoopProgress);
        return ResolveLoadedSurfaceSpawn(worldX, worldZ, fallback.SurfaceHeight);
    }

    public Vector3 EnsureSafeSpawnPosition(Vector3 desiredPosition)
    {
        return EnsureSafeSpawnPosition(desiredPosition, null);
    }

    public Vector3 EnsureSafeSpawnPosition(Vector3 desiredPosition, Action<int, int>? progress)
    {
        LoadAround(desiredPosition, progress ?? NoopProgress);
        var worldX = (int)MathF.Floor(desiredPosition.X);
        var worldZ = (int)MathF.Floor(desiredPosition.Z);
        var estimatedSurfaceHeight = Math.Clamp((int)MathF.Floor(desiredPosition.Y), ChunkData.MinY + 1, ChunkData.MaxY - 3);
        return ResolveLoadedSurfaceSpawn(worldX, worldZ, estimatedSurfaceHeight);
    }

    public Vector3 GetGrassTint(int worldX, int worldY, int worldZ) => _client.GetGrassTint(worldX, worldY, worldZ);

    public Vector3 GetFoliageTint(int worldX, int worldY, int worldZ) => _client.GetFoliageTint(worldX, worldY, worldZ);

    public WorldColumnSample GetColumnSample(int worldX, int worldZ) => _client.GetColumnSample(worldX, worldZ);

    public BiomeType GetBiome(int worldX, int worldZ) => _client.GetBiome(worldX, worldZ);

    public float GetTemperature(int worldX, int worldZ) => _client.GetTemperature(worldX, worldZ);

    public float GetCaveValue(int worldX, int worldY, int worldZ) => _client.GetCaveValue(worldX, worldY, worldZ);

    public WorldBlockSample GetBlockSample(int worldX, int worldY, int worldZ) => _client.GetBlockSample(worldX, worldY, worldZ);

    public void RebuildDirtyMeshes(bool immediate = false) => Tick(drainMeshing: immediate);

    public void RebuildDirtyMeshes(Action<int, int> progress)
    {
        _server.Tick();
        _client.Tick();
        _client.DrainMeshing(progress);
    }

    public IEnumerable<ChunkRenderMesh> GetVisibleMeshes() => _client.GetVisibleMeshes();

    public void SaveWorldState(Action<int, int>? progress = null)
    {
        _server.SaveWorldState(progress);
    }

    private bool TryGetDrySpawnColumn(int originX, int originZ, out int spawnX, out int spawnZ, out int surfaceHeight)
    {
        if (IsDrySpawnColumn(originX, originZ, out surfaceHeight))
        {
            spawnX = originX;
            spawnZ = originZ;
            return true;
        }

        const int maxSearchRadius = 128;
        for (var radius = 1; radius <= maxSearchRadius; radius++)
        {
            for (var offsetX = -radius; offsetX <= radius; offsetX++)
            {
                if (TryCandidate(originX + offsetX, originZ - radius, out spawnX, out spawnZ, out surfaceHeight) ||
                    TryCandidate(originX + offsetX, originZ + radius, out spawnX, out spawnZ, out surfaceHeight))
                {
                    return true;
                }
            }

            for (var offsetZ = -radius + 1; offsetZ <= radius - 1; offsetZ++)
            {
                if (TryCandidate(originX - radius, originZ + offsetZ, out spawnX, out spawnZ, out surfaceHeight) ||
                    TryCandidate(originX + radius, originZ + offsetZ, out spawnX, out spawnZ, out surfaceHeight))
                {
                    return true;
                }
            }
        }

        spawnX = originX;
        spawnZ = originZ;
        surfaceHeight = _server.GetColumnSample(originX, originZ).SurfaceHeight;
        return false;
    }

    private bool TryCandidate(int worldX, int worldZ, out int spawnX, out int spawnZ, out int surfaceHeight)
    {
        if (IsDrySpawnColumn(worldX, worldZ, out surfaceHeight))
        {
            spawnX = worldX;
            spawnZ = worldZ;
            return true;
        }

        spawnX = 0;
        spawnZ = 0;
        return false;
    }

    private bool IsDrySpawnColumn(int worldX, int worldZ, out int surfaceHeight)
    {
        var column = _server.GetColumnSample(worldX, worldZ);
        surfaceHeight = column.SurfaceHeight;
        return !column.HasSurfaceWater && column.Biome is not BiomeType.Ocean and not BiomeType.River and not BiomeType.Lake;
    }

    private Vector3 ResolveLoadedSurfaceSpawn(int worldX, int worldZ, int estimatedSurfaceHeight)
    {
        var startY = Math.Clamp(Math.Max(estimatedSurfaceHeight + 8, ChunkData.MaxY - 2), ChunkData.MinY + 2, ChunkData.MaxY - 2);
        var emptySpaceAbove = 0;

        for (var y = startY; y >= ChunkData.MinY + 1; y--)
        {
            var block = GetBlock(worldX, y, worldZ);
            if (IsSpawnPassable(block))
            {
                emptySpaceAbove++;
                continue;
            }

            if (Blocks.IsSolid(block) && emptySpaceAbove >= 2)
            {
                return new Vector3(worldX + 0.5f, Math.Clamp(y + 1, ChunkData.MinY + 1, ChunkData.MaxY - 3), worldZ + 0.5f);
            }

            emptySpaceAbove = 0;
        }

        return new Vector3(worldX + 0.5f, Math.Clamp(estimatedSurfaceHeight + 1, ChunkData.MinY + 1, ChunkData.MaxY - 3), worldZ + 0.5f);
    }

    private static bool IsSpawnPassable(BlockType block)
    {
        return block is BlockType.Air;
    }

    private static void NoopProgress(int _, int __)
    {
    }
}
