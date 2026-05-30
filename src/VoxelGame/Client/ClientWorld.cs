using System.Numerics;
using VoxelGame.Server;
using VoxelGame.Shared;
using VoxelGame.Shared.Messages;
using VoxelGame.Shared.Transport;
using VoxelGame.World;
using VoxelGame.World.Blocks;
using VoxelGame.World.Chunks;
using VoxelGame.World.Generation;

namespace VoxelGame.Client;

public sealed class ClientWorld : IBlockWorld
{
    private readonly IMessageBus _bus;
    private readonly ServerWorld _serverWorld;
    private readonly ChunkStreamingOptions _streamingOptions;
    private readonly Dictionary<ChunkCoord, ChunkData> _chunks = new();
    private readonly ClientChunkRenderer _chunkRenderer = new();
    private readonly object _chunkLock = new();

    public ClientWorld(IMessageBus bus, ServerWorld serverWorld, ChunkStreamingOptions streamingOptions)
    {
        _bus = bus;
        _serverWorld = serverWorld;
        _streamingOptions = streamingOptions;
    }

    public BlockRegistry Blocks { get; } = new();
    public int LoadedChunkCount => _chunks.Count;
    public int VisibleMeshCount => _chunkRenderer.VisibleMeshCount;

    public void SendPlayerPosition(Vector3 position, bool immediate = false)
    {
        _bus.SendToServer(new PlayerPositionMessage(position, immediate));
    }

    public void Tick()
    {
        while (_bus.TryReceiveForClient(out var message))
        {
            switch (message)
            {
                case ChunkDataMessage chunkData:
                    lock (_chunkLock)
                    {
                        _chunks[chunkData.Coord] = chunkData.Chunk;
                    }

                    _chunkRenderer.SetChunk(chunkData.Chunk);
                    break;
                case BlockUpdateMessage blockUpdate:
                    ApplyBlockUpdate(blockUpdate);
                    break;
                case UnloadChunkMessage unload:
                    lock (_chunkLock)
                    {
                        _chunks.Remove(unload.Coord);
                    }

                    _chunkRenderer.QueueUnload(unload.Coord);
                    break;
            }
        }

        _chunkRenderer.Tick(this, _streamingOptions.MaxMeshUploadsPerFrame, _streamingOptions.MaxUnloadsPerFrame);
    }

    public void DrainMeshing(Action<int, int>? progress = null)
    {
        Tick();
        _chunkRenderer.Drain(this, progress);
    }

    public void SetBlock(int x, int y, int z, BlockType type)
    {
        _bus.SendToServer(new BlockUpdateMessage(x, y, z, type));
        _serverWorld.Tick();
        Tick();
    }

    public BlockType GetBlock(int x, int y, int z)
    {
        if (y < ChunkData.MinY || y >= ChunkData.MaxYExclusive)
        {
            return BlockType.Air;
        }

        var coord = ServerChunkManager.ToChunkCoord(x, z);
        lock (_chunkLock)
        {
            if (!_chunks.TryGetValue(coord, out var chunk))
            {
                return BlockType.Air;
            }

            var local = ServerChunkManager.ToLocal(x, z);
            return chunk.GetBlock(local.X, y, local.Z);
        }
    }

    public IEnumerable<ChunkRenderMesh> GetVisibleMeshes() => _chunkRenderer.VisibleMeshes;

    public Vector3 GetGrassTint(int worldX, int worldY, int worldZ) => _serverWorld.GetGrassTint(worldX, worldY, worldZ);

    public Vector3 GetFoliageTint(int worldX, int worldY, int worldZ) => _serverWorld.GetFoliageTint(worldX, worldY, worldZ);

    public BiomeType GetBiome(int worldX, int worldZ) => _serverWorld.GetBiome(worldX, worldZ);

    public WorldColumnSample GetColumnSample(int worldX, int worldZ) => _serverWorld.GetColumnSample(worldX, worldZ);

    public float GetTemperature(int worldX, int worldZ) => _serverWorld.GetTemperature(worldX, worldZ);

    public float GetCaveValue(int worldX, int worldY, int worldZ) => _serverWorld.GetCaveValue(worldX, worldY, worldZ);

    public WorldBlockSample GetBlockSample(int worldX, int worldY, int worldZ) => _serverWorld.GetBlockSample(worldX, worldY, worldZ);

    private void ApplyBlockUpdate(BlockUpdateMessage update)
    {
        if (update.Y < ChunkData.MinY || update.Y >= ChunkData.MaxYExclusive)
        {
            return;
        }

        var coord = ServerChunkManager.ToChunkCoord(update.X, update.Z);
        lock (_chunkLock)
        {
            if (!_chunks.TryGetValue(coord, out var chunk))
            {
                return;
            }

            var local = ServerChunkManager.ToLocal(update.X, update.Z);
            if (chunk.SetBlock(local.X, update.Y, local.Z, update.Block))
            {
                _chunkRenderer.MarkBlockDirty(coord, update.Y, includeHorizontalNeighbors: true, local);
            }
        }
    }
}
