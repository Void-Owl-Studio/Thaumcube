using VoxelGame.World.Chunks;

namespace VoxelGame.Shared.Messages;

public sealed record ChunkDataMessage(ChunkCoord Coord, ChunkData Chunk) : GameMessage;
