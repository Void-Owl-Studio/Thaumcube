using System.Numerics;
using VoxelGame.World.Blocks;
using VoxelGame.World.Chunks;
using VoxelGame.World.Generation;

namespace VoxelGame.World;

public sealed class VoxelWorld
{
    private readonly WorldGenerator _generator;
    private readonly ChunkManager _chunks;

    public BlockRegistry Blocks { get; } = new();
    public int LoadedChunkCount => _chunks.LoadedChunkCount;
    public int VisibleMeshCount => _chunks.VisibleMeshCount;

    public VoxelWorld(int seed, int renderDistance)
    {
        _generator = new WorldGenerator(seed);
        _chunks = new ChunkManager(_generator, renderDistance);
    }

    public void LoadAround(Vector3 position) => _chunks.LoadAround(position);

    public BlockType GetBlock(int x, int y, int z) => _chunks.GetBlock(x, y, z);

    public void SetBlock(int x, int y, int z, BlockType type) => _chunks.SetBlock(x, y, z, type);

    public bool IsSolid(int x, int y, int z) => Blocks.IsSolid(GetBlock(x, y, z));

    public bool IsTransparent(int x, int y, int z)
    {
        if (y < 0)
        {
            return false;
        }

        if (y >= Chunk.SizeY)
        {
            return true;
        }

        return Blocks.IsTransparent(GetBlock(x, y, z));
    }

    public Vector3 FindSpawnPosition(int worldX, int worldZ)
    {
        var height = _generator.GetTerrainHeight(worldX, worldZ);
        return new Vector3(worldX + 0.5f, Math.Min(height + 3, Chunk.SizeY - 4), worldZ + 0.5f);
    }

    public Vector3 GetGrassTint(int worldX, int worldY, int worldZ) => _generator.GetGrassTint(worldX, worldY, worldZ);

    public void RebuildDirtyMeshes() => _chunks.RebuildDirtyMeshes(this);

    public IEnumerable<ChunkRenderMesh> GetVisibleMeshes() => _chunks.VisibleMeshes;
}
