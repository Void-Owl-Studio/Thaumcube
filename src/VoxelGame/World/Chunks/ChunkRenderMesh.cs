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

    public VoxelVertex(Vector3 position, Vector3 normal, Vector2 uv, uint blockId)
    {
        Position = position;
        Normal = normal;
        Uv = uv;
        BlockId = blockId;
    }
}

public sealed class ChunkRenderMesh
{
    public ChunkCoord Coord { get; }
    public string Key { get; }
    public VoxelVertex[] Vertices { get; }
    public uint[] Indices { get; }

    public ChunkRenderMesh(ChunkCoord coord, List<VoxelVertex> vertices, List<uint> indices, string? key = null)
    {
        Coord = coord;
        Key = key ?? $"chunk:{coord.X},{coord.Z}";
        Vertices = vertices.ToArray();
        Indices = indices.ToArray();
    }

    public bool IsEmpty => Indices.Length == 0;
}
