using VoxelGame.World.Chunks;
using VoxelGame.World.Generation;
using VoxelGame.World.Storage;

namespace VoxelGame.Server;

public sealed class ChunkGenerationSystem
{
    private readonly WorldGenerator _generator;
    private readonly WorldSaveStore? _saveStore;

    public ChunkGenerationSystem(WorldGenerator generator, WorldSaveStore? saveStore)
    {
        _generator = generator;
        _saveStore = saveStore;
    }

    public ChunkData LoadOrGenerate(ChunkCoord coord)
    {
        if (_saveStore is not null && _saveStore.TryLoadChunk(coord, out var persistedSnapshot))
        {
            return new ChunkData(coord, persistedSnapshot.Blocks, persistedSnapshot.Aura);
        }

        var chunk = new ChunkData(coord);
        _generator.Generate(chunk);
        return chunk;
    }
}
