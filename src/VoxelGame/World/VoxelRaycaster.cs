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

            var normal = ResolveFaceNormal(previous, current, direction);

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

    private static BlockPosition ResolveFaceNormal(BlockPosition previous, BlockPosition current, Vector3 direction)
    {
        var deltaX = previous.X - current.X;
        var deltaY = previous.Y - current.Y;
        var deltaZ = previous.Z - current.Z;

        if (deltaX != 0 && deltaY == 0 && deltaZ == 0)
        {
            return new BlockPosition(Math.Sign(deltaX), 0, 0);
        }

        if (deltaY != 0 && deltaX == 0 && deltaZ == 0)
        {
            return new BlockPosition(0, Math.Sign(deltaY), 0);
        }

        if (deltaZ != 0 && deltaX == 0 && deltaY == 0)
        {
            return new BlockPosition(0, 0, Math.Sign(deltaZ));
        }

        var absX = MathF.Abs(direction.X);
        var absY = MathF.Abs(direction.Y);
        var absZ = MathF.Abs(direction.Z);

        if (absX >= absY && absX >= absZ)
        {
            return new BlockPosition(direction.X >= 0f ? -1 : 1, 0, 0);
        }

        if (absY >= absZ)
        {
            return new BlockPosition(0, direction.Y >= 0f ? -1 : 1, 0);
        }

        return new BlockPosition(0, 0, direction.Z >= 0f ? -1 : 1);
    }
}
