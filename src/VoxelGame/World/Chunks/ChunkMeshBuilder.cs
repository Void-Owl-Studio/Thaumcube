using System.Numerics;
using VoxelGame.World;
using VoxelGame.World.Blocks;

namespace VoxelGame.World.Chunks;

public sealed class ChunkMeshBuilder
{
    public const uint BreakOverlayBlockId = 100;

    private static readonly Face[] Faces =
    [
        new(
            new Vector3(1, 0, 0),
            new[] { new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(1, 0, 1) },
            new[] { new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1) }),
        new(
            new Vector3(-1, 0, 0),
            new[] { new Vector3(0, 0, 1), new Vector3(0, 1, 1), new Vector3(0, 1, 0), new Vector3(0, 0, 0) },
            new[] { new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1) }),
        new(
            new Vector3(0, 1, 0),
            new[] { new Vector3(0, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 1, 0), new Vector3(0, 1, 0) },
            new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) }),
        new(
            new Vector3(0, -1, 0),
            new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1), new Vector3(0, 0, 1) },
            new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) }),
        new(
            new Vector3(0, 0, 1),
            new[] { new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1), new Vector3(0, 0, 1) },
            new[] { new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1) }),
        new(
            new Vector3(0, 0, -1),
            new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 0, 0) },
            new[] { new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1) })
    ];

    public ChunkRenderMesh BuildSection(VoxelWorld world, Chunk chunk, int sectionIndex)
    {
        var vertices = new List<VoxelVertex>(2048);
        var indices = new List<uint>(4096);
        var baseX = chunk.Coord.X * Chunk.SizeX;
        var baseZ = chunk.Coord.Z * Chunk.SizeZ;
        var minY = sectionIndex * Chunk.MeshSectionHeight;
        var maxY = minY + Chunk.MeshSectionHeight;

        for (var y = minY; y < maxY; y++)
        for (var z = 0; z < Chunk.SizeZ; z++)
        for (var x = 0; x < Chunk.SizeX; x++)
        {
            var block = chunk.GetBlock(x, y, z);
            if (block == BlockType.Air)
            {
                continue;
            }

            foreach (var face in Faces)
            {
                var neighborX = baseX + x + (int)face.Normal.X;
                var neighborY = y + (int)face.Normal.Y;
                var neighborZ = baseZ + z + (int)face.Normal.Z;

                if (!world.IsTransparent(neighborX, neighborY, neighborZ))
                {
                    continue;
                }

                var textureIndex = world.Blocks.GetFaceTextureIndex(block, face.Normal);
                var tint = ResolveTint(world, block, face.Normal, baseX + x, y, baseZ + z);
                AddFace(vertices, indices, new Vector3(baseX + x, y, baseZ + z), face, textureIndex, block, tint);
            }
        }

        return new ChunkRenderMesh(chunk.Coord, vertices, indices, $"chunk:{chunk.Coord.X},{chunk.Coord.Z}:section:{sectionIndex}");
    }

    private static void AddFace(List<VoxelVertex> vertices, List<uint> indices, Vector3 origin, Face face, int textureIndex, BlockType block, Vector3 tint)
    {
        var start = (uint)vertices.Count;
        for (var i = 0; i < 4; i++)
        {
            vertices.Add(new VoxelVertex(origin + face.Corners[i], face.Normal, AtlasUv(face.Uvs[i], textureIndex), (uint)block, tint));
        }

        indices.Add(start);
        indices.Add(start + 1);
        indices.Add(start + 2);
        indices.Add(start);
        indices.Add(start + 2);
        indices.Add(start + 3);
    }

    public static ChunkRenderMesh BuildDroppedBlockMesh(string key, Vector3 center, float size, float rotationRadians, BlockType block, BlockRegistry blocks)
    {
        var vertices = new List<VoxelVertex>(24);
        var indices = new List<uint>(36);
        var cos = MathF.Cos(rotationRadians);
        var sin = MathF.Sin(rotationRadians);

        foreach (var face in Faces)
        {
            var textureIndex = blocks.GetFaceTextureIndex(block, face.Normal);
            var start = (uint)vertices.Count;

            for (var i = 0; i < 4; i++)
            {
                var local = (face.Corners[i] - new Vector3(0.5f)) * size;
                var rotated = new Vector3(
                    local.X * cos - local.Z * sin,
                    local.Y,
                    local.X * sin + local.Z * cos);
                var normal = new Vector3(
                    face.Normal.X * cos - face.Normal.Z * sin,
                    face.Normal.Y,
                    face.Normal.X * sin + face.Normal.Z * cos);

                vertices.Add(new VoxelVertex(center + rotated, Vector3.Normalize(normal), AtlasUv(face.Uvs[i], textureIndex), (uint)block, ResolveItemTint(block, face.Normal)));
            }

            AddQuadIndices(indices, start);
        }

        return new ChunkRenderMesh(new ChunkCoord(int.MinValue, int.MinValue), vertices, indices, key);
    }

    public static ChunkRenderMesh BuildBreakOverlayMesh(BlockPosition blockPosition, BlockPosition faceNormal, int stage)
    {
        var normal = new Vector3(faceNormal.X, faceNormal.Y, faceNormal.Z);
        var face = Faces.First(candidate => candidate.Normal == normal);
        var origin = new Vector3(blockPosition.X, blockPosition.Y, blockPosition.Z);
        var vertices = new List<VoxelVertex>(4);
        var indices = new List<uint>(6);
        var textureIndex = BlockTextureAtlas.DestroyStage(stage);
        var offset = normal * 0.003f;

        for (var i = 0; i < 4; i++)
        {
            vertices.Add(new VoxelVertex(origin + face.Corners[i] + offset, normal, AtlasUv(face.Uvs[i], textureIndex), BreakOverlayBlockId, Vector3.One));
        }

        AddQuadIndices(indices, 0);
        return new ChunkRenderMesh(new ChunkCoord(int.MinValue + 1, int.MinValue + 1), vertices, indices, "break-overlay");
    }

    private static void AddQuadIndices(List<uint> indices, uint start)
    {
        indices.Add(start);
        indices.Add(start + 1);
        indices.Add(start + 2);
        indices.Add(start);
        indices.Add(start + 2);
        indices.Add(start + 3);
    }

    private static Vector2 AtlasUv(Vector2 localUv, int textureIndex)
    {
        var tileX = textureIndex % BlockTextureAtlas.TilesPerRow;
        var tileY = textureIndex / BlockTextureAtlas.TilesPerRow;
        var atlasRows = (BlockTextureAtlas.TextureCount + BlockTextureAtlas.TilesPerRow - 1) / BlockTextureAtlas.TilesPerRow;
        var atlasWidth = BlockTextureAtlas.TilesPerRow * BlockTextureAtlas.TileSize;
        var atlasHeight = atlasRows * BlockTextureAtlas.TileSize;
        var insetX = 0.5f / atlasWidth;
        var insetY = 0.5f / atlasHeight;
        var tileWidth = 1f / BlockTextureAtlas.TilesPerRow;
        var tileHeight = 1f / atlasRows;
        return new Vector2(
            tileX * tileWidth + insetX + localUv.X * (tileWidth - insetX * 2f),
            tileY * tileHeight + insetY + localUv.Y * (tileHeight - insetY * 2f));
    }

    private static Vector3 ResolveTint(VoxelWorld world, BlockType block, Vector3 faceNormal, int worldX, int worldY, int worldZ)
    {
        if (block != BlockType.Grass)
        {
            return Vector3.One;
        }

        if (faceNormal.Y < -0.5f)
        {
            return Vector3.One;
        }

        var grassTint = world.GetGrassTint(worldX, worldY, worldZ);
        return faceNormal.Y > 0.5f ? grassTint : Vector3.Lerp(Vector3.One, grassTint, 0.55f);
    }

    private static Vector3 ResolveItemTint(BlockType block, Vector3 faceNormal)
    {
        if (block != BlockType.Grass)
        {
            return Vector3.One;
        }

        if (faceNormal.Y < -0.5f)
        {
            return Vector3.One;
        }

        var grassTint = new Vector3(0.48f, 0.76f, 0.30f);
        return faceNormal.Y > 0.5f ? grassTint : Vector3.Lerp(Vector3.One, grassTint, 0.55f);
    }

    private readonly record struct Face(Vector3 Normal, Vector3[] Corners, Vector2[] Uvs);
}
