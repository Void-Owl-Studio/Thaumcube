using VoxelGame.World.Blocks;

namespace VoxelGame.Shared.Messages;

public sealed record BlockUpdateMessage(int X, int Y, int Z, BlockType Block) : GameMessage;
