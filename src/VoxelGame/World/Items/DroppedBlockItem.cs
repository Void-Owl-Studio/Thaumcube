using System.Numerics;
using VoxelGame.World.Blocks;

namespace VoxelGame.World.Items;

public sealed class DroppedBlockItem
{
    public int Id { get; }
    public BlockType Block { get; }
    public Vector3 Position { get; set; }
    public Vector3 Velocity { get; set; }
    public float RotationRadians { get; set; }

    public DroppedBlockItem(int id, BlockType block, Vector3 position, Vector3 velocity)
    {
        Id = id;
        Block = block;
        Position = position;
        Velocity = velocity;
    }
}
