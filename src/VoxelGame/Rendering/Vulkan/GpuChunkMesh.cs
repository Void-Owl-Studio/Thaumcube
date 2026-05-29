using Silk.NET.Vulkan;
using VoxelGame.World.Chunks;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace VoxelGame.Rendering.Vulkan;

internal sealed class GpuChunkMesh
{
    public ChunkCoord Coord { get; }
    public string Key { get; }
    public ChunkRenderMesh SourceMesh { get; }
    public Buffer VertexBuffer { get; }
    public DeviceMemory VertexMemory { get; }
    public Buffer IndexBuffer { get; }
    public DeviceMemory IndexMemory { get; }
    public uint IndexCount { get; }

    public GpuChunkMesh(
        ChunkCoord coord,
        ChunkRenderMesh sourceMesh,
        Buffer vertexBuffer,
        DeviceMemory vertexMemory,
        Buffer indexBuffer,
        DeviceMemory indexMemory,
        uint indexCount)
    {
        Coord = coord;
        Key = sourceMesh.Key;
        SourceMesh = sourceMesh;
        VertexBuffer = vertexBuffer;
        VertexMemory = vertexMemory;
        IndexBuffer = indexBuffer;
        IndexMemory = indexMemory;
        IndexCount = indexCount;
    }
}
