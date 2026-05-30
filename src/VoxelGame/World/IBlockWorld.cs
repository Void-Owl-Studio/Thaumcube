using System.Numerics;
using VoxelGame.World.Blocks;
using VoxelGame.World.Generation;

namespace VoxelGame.World;

public interface IBlockWorld
{
    BlockRegistry Blocks { get; }
    BlockType GetBlock(int x, int y, int z);
    Vector3 GetGrassTint(int worldX, int worldY, int worldZ);
    Vector3 GetFoliageTint(int worldX, int worldY, int worldZ);
}
