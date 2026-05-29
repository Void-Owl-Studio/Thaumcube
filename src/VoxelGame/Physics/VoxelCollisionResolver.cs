using System.Numerics;
using VoxelGame.World;

namespace VoxelGame.Physics;

public readonly record struct CollisionMoveResult(Vector3 Delta, Vector3 Velocity, bool Grounded);

public static class VoxelCollisionResolver
{
    private const float MaxStepDistance = 0.45f;

    public static CollisionMoveResult Move(VoxelWorld world, Aabb body, Vector3 velocity, float dt)
    {
        var actualDelta = Vector3.Zero;
        var grounded = false;
        var totalDelta = velocity * dt;
        var maxDistance = Math.Max(Math.Abs(totalDelta.X), Math.Max(Math.Abs(totalDelta.Y), Math.Abs(totalDelta.Z)));
        var steps = Math.Max(1, (int)MathF.Ceiling(maxDistance / MaxStepDistance));
        var stepDt = dt / steps;

        for (var step = 0; step < steps; step++)
        {
            var stepDelta = velocity * stepDt;
            ResolveAxis(world, ref body, ref velocity, ref actualDelta, new Vector3(stepDelta.X, 0, 0), Axis.X, ref grounded);
            ResolveAxis(world, ref body, ref velocity, ref actualDelta, new Vector3(0, stepDelta.Y, 0), Axis.Y, ref grounded);
            ResolveAxis(world, ref body, ref velocity, ref actualDelta, new Vector3(0, 0, stepDelta.Z), Axis.Z, ref grounded);
        }

        return new CollisionMoveResult(actualDelta, velocity, grounded);
    }

    private static void ResolveAxis(
        VoxelWorld world,
        ref Aabb body,
        ref Vector3 velocity,
        ref Vector3 actualDelta,
        Vector3 axisDelta,
        Axis axis,
        ref bool grounded)
    {
        if (axisDelta == Vector3.Zero)
        {
            return;
        }

        var moved = body.Offset(axisDelta);
        if (!Collides(world, moved))
        {
            body = moved;
            actualDelta += axisDelta;
            return;
        }

        if (axis == Axis.Y && axisDelta.Y < 0)
        {
            grounded = true;
        }

        velocity = axis switch
        {
            Axis.X => new Vector3(0, velocity.Y, velocity.Z),
            Axis.Y => new Vector3(velocity.X, 0, velocity.Z),
            Axis.Z => new Vector3(velocity.X, velocity.Y, 0),
            _ => velocity
        };
    }

    private static bool Collides(VoxelWorld world, Aabb body)
    {
        var minX = (int)MathF.Floor(body.Min.X);
        var minY = (int)MathF.Floor(body.Min.Y);
        var minZ = (int)MathF.Floor(body.Min.Z);
        var maxX = (int)MathF.Floor(body.Max.X);
        var maxY = (int)MathF.Floor(body.Max.Y);
        var maxZ = (int)MathF.Floor(body.Max.Z);

        for (var y = minY; y <= maxY; y++)
        for (var z = minZ; z <= maxZ; z++)
        for (var x = minX; x <= maxX; x++)
        {
            if (world.IsSolid(x, y, z) && body.IntersectsBlock(x, y, z))
            {
                return true;
            }
        }

        return false;
    }

    private enum Axis
    {
        X,
        Y,
        Z
    }
}
