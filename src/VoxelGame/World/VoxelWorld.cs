using System.Numerics;
using VoxelGame.World.Blocks;
using VoxelGame.World.Chunks;
using VoxelGame.World.Generation;
using VoxelGame.World.Storage;

namespace VoxelGame.World;

public sealed class VoxelWorld
{
    private readonly WorldGenerator _generator;
    private readonly ChunkManager _chunks;
    private readonly WorldSaveStore? _saveStore;

    public BlockRegistry Blocks { get; } = new();
    public int Seed => _generator.Seed;
    public string? Name => _saveStore?.Name;
    public int RenderDistanceChunks => _chunks.RenderDistance;
    public int LoadedChunkCount => _chunks.LoadedChunkCount;
    public int VisibleMeshCount => _chunks.VisibleMeshCount;

    public VoxelWorld(int seed, int renderDistance, WorldSaveStore? saveStore = null)
    {
        _generator = new WorldGenerator(seed);
        _saveStore = saveStore;
        _chunks = new ChunkManager(_generator, renderDistance, saveStore);
    }

    public void LoadAround(Vector3 position, bool immediate = false) => _chunks.LoadAround(position, immediate);

    public BlockType GetBlock(int x, int y, int z) => _chunks.GetBlock(x, y, z);

    public void SetBlock(int x, int y, int z, BlockType type) => _chunks.SetBlock(x, y, z, type);

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
        if (TryGetDrySpawnColumn(worldX, worldZ, out var spawnX, out var spawnZ, out var surfaceHeight))
        {
            var roughSpawn = new Vector3(spawnX + 0.5f, Math.Clamp(surfaceHeight + 2, Chunk.MinY + 2, Chunk.MaxY - 4), spawnZ + 0.5f);
            LoadAround(roughSpawn, immediate: true);
            return ResolveLoadedSurfaceSpawn(spawnX, spawnZ, surfaceHeight);
        }

        var fallback = _generator.GetColumnSample(worldX, worldZ);
        var fallbackSpawn = new Vector3(worldX + 0.5f, Math.Clamp(fallback.SurfaceHeight + 2, Chunk.MinY + 2, Chunk.MaxY - 4), worldZ + 0.5f);
        LoadAround(fallbackSpawn, immediate: true);
        return ResolveLoadedSurfaceSpawn(worldX, worldZ, fallback.SurfaceHeight);
    }

    public Vector3 EnsureSafeSpawnPosition(Vector3 desiredPosition)
    {
        LoadAround(desiredPosition, immediate: true);
        var worldX = (int)MathF.Floor(desiredPosition.X);
        var worldZ = (int)MathF.Floor(desiredPosition.Z);
        var estimatedSurfaceHeight = Math.Clamp((int)MathF.Floor(desiredPosition.Y), Chunk.MinY + 1, Chunk.MaxY - 3);
        return ResolveLoadedSurfaceSpawn(worldX, worldZ, estimatedSurfaceHeight);
    }

    public Vector3 GetGrassTint(int worldX, int worldY, int worldZ) => _generator.GetGrassTint(worldX, worldY, worldZ);

    public Vector3 GetFoliageTint(int worldX, int worldY, int worldZ) => _generator.GetFoliageTint(worldX, worldY, worldZ);

    public WorldColumnSample GetColumnSample(int worldX, int worldZ) => _generator.GetColumnSample(worldX, worldZ);

    public BiomeType GetBiome(int worldX, int worldZ) => _generator.GetBiome(worldX, worldZ);

    public float GetTemperature(int worldX, int worldZ) => _generator.GetTemperature(worldX, worldZ);

    public float GetCaveValue(int worldX, int worldY, int worldZ) => _generator.GetCaveValue(worldX, worldY, worldZ);

    public WorldBlockSample GetBlockSample(int worldX, int worldY, int worldZ) => _generator.GetBlockSample(worldX, worldY, worldZ);

    public void RebuildDirtyMeshes(bool immediate = false) => _chunks.RebuildDirtyMeshes(this, immediate);

    public IEnumerable<ChunkRenderMesh> GetVisibleMeshes() => _chunks.VisibleMeshes;

    public void SaveWorldState()
    {
        _saveStore?.Touch();
        _chunks.SaveLoadedModifiedChunks();
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
        surfaceHeight = _generator.GetColumnSample(originX, originZ).SurfaceHeight;
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
        var column = _generator.GetColumnSample(worldX, worldZ);
        surfaceHeight = column.SurfaceHeight;
        return !column.HasSurfaceWater && column.Biome is not BiomeType.Ocean and not BiomeType.River and not BiomeType.Lake;
    }

    private Vector3 ResolveLoadedSurfaceSpawn(int worldX, int worldZ, int estimatedSurfaceHeight)
    {
        var startY = Math.Clamp(Math.Max(estimatedSurfaceHeight + 8, Chunk.MaxY - 2), Chunk.MinY + 2, Chunk.MaxY - 2);
        var emptySpaceAbove = 0;

        for (var y = startY; y >= Chunk.MinY + 1; y--)
        {
            var block = GetBlock(worldX, y, worldZ);
            if (IsSpawnPassable(block))
            {
                emptySpaceAbove++;
                continue;
            }

            if (Blocks.IsSolid(block) && emptySpaceAbove >= 2)
            {
                return new Vector3(worldX + 0.5f, Math.Clamp(y + 1, Chunk.MinY + 1, Chunk.MaxY - 3), worldZ + 0.5f);
            }

            emptySpaceAbove = 0;
        }

        return new Vector3(worldX + 0.5f, Math.Clamp(estimatedSurfaceHeight + 1, Chunk.MinY + 1, Chunk.MaxY - 3), worldZ + 0.5f);
    }

    private static bool IsSpawnPassable(BlockType block)
    {
        return block is BlockType.Air;
    }
}
