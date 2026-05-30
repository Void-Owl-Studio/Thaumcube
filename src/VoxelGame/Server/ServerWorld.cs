using System.Numerics;
using VoxelGame.Shared;
using VoxelGame.Shared.Messages;
using VoxelGame.Shared.Transport;
using VoxelGame.World.Blocks;
using VoxelGame.World.Chunks;
using VoxelGame.World.Generation;
using VoxelGame.World.Storage;

namespace VoxelGame.Server;

public sealed class ServerWorld
{
    private readonly IMessageBus _bus;
    private readonly ChunkStreamingOptions _streamingOptions;
    private readonly WorldGenerator _generator;
    private readonly ServerChunkManager _chunks;
    private readonly WorldSaveStore? _saveStore;

    public int Seed => _generator.Seed;
    public int RenderDistanceChunks => _chunks.RenderDistance;
    public int LoadedChunkCount => _chunks.ActiveChunkCount;

    public ServerWorld(int seed, int renderDistance, WorldSaveStore? saveStore, IMessageBus bus, ChunkStreamingOptions streamingOptions)
    {
        _bus = bus;
        _streamingOptions = streamingOptions;
        _generator = new WorldGenerator(seed);
        _saveStore = saveStore;
        var generation = new ChunkGenerationSystem(_generator, saveStore);
        _chunks = new ServerChunkManager(generation, renderDistance, saveStore);
    }

    public void Tick()
    {
        while (_bus.TryReceiveForServer(out var message))
        {
            switch (message)
            {
                case PlayerPositionMessage playerPosition:
                    _chunks.HandlePlayerPosition(playerPosition.Position, _bus, _streamingOptions, playerPosition.Immediate);
                    break;
                case BlockUpdateMessage blockUpdate:
                    _chunks.SetBlock(blockUpdate.X, blockUpdate.Y, blockUpdate.Z, blockUpdate.Block, _bus);
                    break;
            }
        }

        _chunks.Tick(_bus, _streamingOptions);
    }

    public void LoadAround(Vector3 position, bool immediate = false, Action<int, int>? progress = null)
    {
        _chunks.HandlePlayerPosition(position, _bus, _streamingOptions, immediate, progress);
    }

    public BlockType GetBlock(int x, int y, int z) => _chunks.GetBlock(x, y, z);

    public void SetBlock(int x, int y, int z, BlockType type) => _chunks.SetBlock(x, y, z, type, _bus);

    public Vector3 GetGrassTint(int worldX, int worldY, int worldZ) => _generator.GetGrassTint(worldX, worldY, worldZ);

    public Vector3 GetFoliageTint(int worldX, int worldY, int worldZ) => _generator.GetFoliageTint(worldX, worldY, worldZ);

    public WorldColumnSample GetColumnSample(int worldX, int worldZ) => _generator.GetColumnSample(worldX, worldZ);

    public BiomeType GetBiome(int worldX, int worldZ) => _generator.GetBiome(worldX, worldZ);

    public float GetTemperature(int worldX, int worldZ) => _generator.GetTemperature(worldX, worldZ);

    public float GetCaveValue(int worldX, int worldY, int worldZ) => _generator.GetCaveValue(worldX, worldY, worldZ);

    public WorldBlockSample GetBlockSample(int worldX, int worldY, int worldZ) => _generator.GetBlockSample(worldX, worldY, worldZ);

    public void SaveWorldState(Action<int, int>? progress = null)
    {
        _saveStore?.Touch();
        _chunks.SaveLoadedModifiedChunks(progress);
    }
}
