using System.Numerics;
using VoxelGame.Physics;
using VoxelGame.World.Blocks;

namespace VoxelGame.World;

public readonly record struct BlockPosition(int X, int Y, int Z)
{
    public static BlockPosition operator +(BlockPosition a, BlockPosition b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
}

public readonly record struct VoxelRaycastHit(BlockPosition BlockPosition, BlockPosition FaceNormal, BlockType BlockType);

public static class VoxelRaycaster
{
    public static VoxelRaycastHit? Raycast(VoxelWorld world, Ray3 ray, float maxDistance)
    {
        var direction = Vector3.Normalize(ray.Direction);
        var previous = ToBlock(ray.Origin);
        var step = 0.05f;

        for (var distance = 0f; distance <= maxDistance; distance += step)
        {
            var position = ray.Origin + direction * distance;
            var current = ToBlock(position);
            var block = world.GetBlock(current.X, current.Y, current.Z);

            if (block == BlockType.Air || !world.Blocks.IsSolid(block))
            {
                previous = current;
                continue;
            }

            var normal = new BlockPosition(
                previous.X - current.X,
                previous.Y - current.Y,
                previous.Z - current.Z);

            return new VoxelRaycastHit(current, normal, block);
        }

        return null;
    }

    private static BlockPosition ToBlock(Vector3 position)
    {
        return new BlockPosition(
            (int)MathF.Floor(position.X),
            (int)MathF.Floor(position.Y),
            (int)MathF.Floor(position.Z));
    }
}
