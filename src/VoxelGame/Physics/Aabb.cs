using System.Numerics;

namespace VoxelGame.Physics;

public readonly record struct Aabb(Vector3 Min, Vector3 Max)
{
    public static Aabb FromCenterHeight(Vector3 feetPosition, Vector3 size)
    {
        var half = new Vector3(size.X * 0.5f, 0, size.Z * 0.5f);
        return new Aabb(
            new Vector3(feetPosition.X - half.X, feetPosition.Y, feetPosition.Z - half.Z),
            new Vector3(feetPosition.X + half.X, feetPosition.Y + size.Y, feetPosition.Z + half.Z));
    }

    public Aabb Offset(Vector3 delta) => new(Min + delta, Max + delta);

    public bool IntersectsBlock(int x, int y, int z)
    {
        return Max.X > x && Min.X < x + 1 &&
               Max.Y > y && Min.Y < y + 1 &&
               Max.Z > z && Min.Z < z + 1;
    }
}
