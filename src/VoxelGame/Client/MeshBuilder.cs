using VoxelGame.World;
using VoxelGame.World.Chunks;

namespace VoxelGame.Client;

public sealed class MeshBuilder
{
    private readonly ChunkMeshBuilder _chunkMeshBuilder = new();

    public ChunkSectionMeshes BuildSection(IBlockWorld world, ChunkData chunk, int sectionIndex)
    {
        if (chunk.IsSectionEmpty(sectionIndex))
        {
            return new ChunkSectionMeshes(
                ChunkRenderMesh.CreateEmpty(chunk.Coord, BuildKey(chunk.Coord, sectionIndex, "opaque")),
                ChunkRenderMesh.CreateEmpty(chunk.Coord, BuildKey(chunk.Coord, sectionIndex, "transparent"), isTransparent: true));
        }

        return _chunkMeshBuilder.BuildSection(world, chunk, sectionIndex);
    }

    private static string BuildKey(ChunkCoord coord, int sectionIndex, string pass)
    {
        return $"chunk:{coord.X},{coord.Z}:section:{sectionIndex}:{pass}";
    }
}
