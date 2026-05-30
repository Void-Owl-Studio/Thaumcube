using VoxelGame.World.Chunks;

namespace VoxelGame.Shared.Messages;

public sealed record UnloadChunkMessage(ChunkCoord Coord) : GameMessage;
