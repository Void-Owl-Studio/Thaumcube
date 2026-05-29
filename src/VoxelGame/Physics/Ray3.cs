using System.Numerics;

namespace VoxelGame.Physics;

public readonly record struct Ray3(Vector3 Origin, Vector3 Direction);
