using System.Numerics;
using System.Runtime.InteropServices;
using VoxelGame.World.Blocks;

namespace VoxelGame.World.Chunks;

[StructLayout(LayoutKind.Sequential)]
public readonly struct VoxelVertex
{
    public readonly Vector3 Position;
    public readonly Vector3 Normal;
    public readonly Vector2 Uv;
    public readonly uint BlockId;
    public readonly Vector3 Tint;
    public readonly float Alpha;

    public VoxelVertex(Vector3 position, Vector3 normal, Vector2 uv, uint blockId, Vector3 tint, float alpha = 1f)
    {
        Position = position;
        Normal = normal;
        Uv = uv;
        BlockId = blockId;
        Tint = tint;
        Alpha = alpha;
    }
}

public sealed class ChunkRenderMesh
{
    public ChunkCoord Coord { get; }
    public string Key { get; }
    public VoxelVertex[] Vertices { get; }
    public uint[] Indices { get; }
    public bool IsTransparent { get; }
    public bool AlwaysVisible { get; }
    public Vector3 BoundsMin { get; }
    public Vector3 BoundsMax { get; }
    public float BoundingRadius { get; }
    public Vector3 SortCenter { get; }

    public ChunkRenderMesh(ChunkCoord coord, List<VoxelVertex> vertices, List<uint> indices, string? key = null, bool isTransparent = false, bool alwaysVisible = false)
        : this(coord, vertices.ToArray(), indices.ToArray(), key, isTransparent, alwaysVisible)
    {
    }

    private ChunkRenderMesh(ChunkCoord coord, VoxelVertex[] vertices, uint[] indices, string? key, bool isTransparent, bool alwaysVisible)
    {
        Coord = coord;
        Key = key ?? $"chunk:{coord.X},{coord.Z}";
        Vertices = vertices;
        Indices = indices;
        IsTransparent = isTransparent;
        AlwaysVisible = alwaysVisible;
        SortCenter = CalculateSortCenter(Vertices);
        (BoundsMin, BoundsMax) = CalculateBounds(Vertices);
        BoundingRadius = CalculateBoundingRadius(SortCenter, Vertices);
    }

    public bool IsEmpty => Indices.Length == 0;

    public static ChunkRenderMesh CreateEmpty(ChunkCoord coord, string key, bool isTransparent = false)
    {
        return new ChunkRenderMesh(coord, Array.Empty<VoxelVertex>(), Array.Empty<uint>(), key, isTransparent, alwaysVisible: false);
    }

    private static Vector3 CalculateSortCenter(ReadOnlySpan<VoxelVertex> vertices)
    {
        if (vertices.IsEmpty)
        {
            return Vector3.Zero;
        }

        var sum = Vector3.Zero;
        for (var i = 0; i < vertices.Length; i++)
        {
            sum += vertices[i].Position;
        }

        return sum / vertices.Length;
    }

    private static (Vector3 Min, Vector3 Max) CalculateBounds(ReadOnlySpan<VoxelVertex> vertices)
    {
        if (vertices.IsEmpty)
        {
            return (Vector3.Zero, Vector3.Zero);
        }

        var min = vertices[0].Position;
        var max = vertices[0].Position;
        for (var i = 1; i < vertices.Length; i++)
        {
            min = Vector3.Min(min, vertices[i].Position);
            max = Vector3.Max(max, vertices[i].Position);
        }

        return (min, max);
    }

    private static float CalculateBoundingRadius(Vector3 center, ReadOnlySpan<VoxelVertex> vertices)
    {
        var radiusSquared = 0f;
        for (var i = 0; i < vertices.Length; i++)
        {
            radiusSquared = MathF.Max(radiusSquared, Vector3.DistanceSquared(center, vertices[i].Position));
        }

        return MathF.Sqrt(radiusSquared);
    }
}

public readonly record struct ChunkSectionMeshes(ChunkRenderMesh OpaqueMesh, ChunkRenderMesh TransparentMesh);
