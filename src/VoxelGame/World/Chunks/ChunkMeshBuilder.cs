using System.Numerics;
using VoxelGame.World;
using VoxelGame.World.Blocks;
using VoxelGame.World.Generation;

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
                var blockWorldX = baseX + x;
                var blockWorldY = y;
                var blockWorldZ = baseZ + z;
                var tint = ResolveTint(world, block, face.Normal, blockWorldX, blockWorldY, blockWorldZ);
                AddFace(world, vertices, indices, new Vector3(blockWorldX, blockWorldY, blockWorldZ), face, textureIndex, block, tint);
            }
        }

        return new ChunkRenderMesh(chunk.Coord, vertices, indices, $"chunk:{chunk.Coord.X},{chunk.Coord.Z}:section:{sectionIndex}");
    }

    private static void AddFace(VoxelWorld world, List<VoxelVertex> vertices, List<uint> indices, Vector3 origin, Face face, int textureIndex, BlockType block, Vector3 tint)
    {
        var start = (uint)vertices.Count;
        Span<float> aoValues = stackalloc float[4];

        for (var i = 0; i < 4; i++)
        {
            var ao = SampleAmbientOcclusion(world, origin, face.Normal, face.Corners[i]);
            aoValues[i] = ao;
            vertices.Add(new VoxelVertex(
                origin + face.Corners[i],
                face.Normal,
                AtlasUv(face.Uvs[i], textureIndex),
                (uint)block,
                tint * ao));
        }

        AddQuadIndices(indices, start, aoValues[0], aoValues[1], aoValues[2], aoValues[3]);
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

    private static void AddQuadIndices(List<uint> indices, uint start, float ao0, float ao1, float ao2, float ao3)
    {
        if (ao0 + ao2 < ao1 + ao3)
        {
            indices.Add(start);
            indices.Add(start + 1);
            indices.Add(start + 3);
            indices.Add(start + 1);
            indices.Add(start + 2);
            indices.Add(start + 3);
            return;
        }

        AddQuadIndices(indices, start);
    }

    private static Vector2 AtlasUv(Vector2 localUv, int textureIndex)
    {
        var tileX = textureIndex % BlockTextureAtlas.TilesPerRow;
        var tileY = textureIndex / BlockTextureAtlas.TilesPerRow;
        var atlasWidth = (float)BlockTextureAtlas.GetAtlasWidth();
        var atlasHeight = (float)BlockTextureAtlas.GetAtlasHeight();
        var paddedTileSize = BlockTextureAtlas.PaddedTileSize;
        var tilePixelX = tileX * paddedTileSize + BlockTextureAtlas.TilePadding;
        var tilePixelY = tileY * paddedTileSize + BlockTextureAtlas.TilePadding;
        return new Vector2(
            (tilePixelX + localUv.X * BlockTextureAtlas.TileSize) / atlasWidth,
            (tilePixelY + localUv.Y * BlockTextureAtlas.TileSize) / atlasHeight);
    }

    private static Vector3 ResolveTint(VoxelWorld world, BlockType block, Vector3 faceNormal, int worldX, int worldY, int worldZ)
    {
        if (block != BlockType.Grass && block != BlockType.CorruptedGrass)
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
        if (block != BlockType.Grass && block != BlockType.CorruptedGrass)
        {
            return Vector3.One;
        }

        if (faceNormal.Y < -0.5f)
        {
            return Vector3.One;
        }

        var grassTint = block == BlockType.CorruptedGrass
            ? GrassColorMap.Sample(0.5f, 0.5f, 1f)
            : new Vector3(0.48f, 0.76f, 0.30f);
        return faceNormal.Y > 0.5f ? grassTint : Vector3.Lerp(Vector3.One, grassTint, 0.55f);
    }

    private static float SampleAmbientOcclusion(VoxelWorld world, Vector3 origin, Vector3 normal, Vector3 corner)
    {
        var blockX = (int)origin.X;
        var blockY = (int)origin.Y;
        var blockZ = (int)origin.Z;

        GetFaceAxes(normal, out var axisA, out var axisB);
        var signA = AxisCornerSign(corner, axisA);
        var signB = AxisCornerSign(corner, axisB);

        var offsetA = AxisVector(axisA) * signA;
        var offsetB = AxisVector(axisB) * signB;

        var side1Solid = IsOccluding(world, blockX, blockY, blockZ, normal + offsetA);
        var side2Solid = IsOccluding(world, blockX, blockY, blockZ, normal + offsetB);
        var cornerSolid = IsOccluding(world, blockX, blockY, blockZ, normal + offsetA + offsetB);

        var occlusion = side1Solid && side2Solid
            ? 3
            : (side1Solid ? 1 : 0) + (side2Solid ? 1 : 0) + (cornerSolid ? 1 : 0);

        return occlusion switch
        {
            0 => 1.00f,
            1 => 0.76f,
            2 => 0.56f,
            _ => 0.42f
        };
    }

    private static bool IsOccluding(VoxelWorld world, int blockX, int blockY, int blockZ, Vector3 offset)
    {
        var sampleX = blockX + (int)offset.X;
        var sampleY = blockY + (int)offset.Y;
        var sampleZ = blockZ + (int)offset.Z;
        return !world.IsTransparent(sampleX, sampleY, sampleZ);
    }

    private static void GetFaceAxes(Vector3 normal, out int axisA, out int axisB)
    {
        if (MathF.Abs(normal.X) > 0.5f)
        {
            axisA = 1;
            axisB = 2;
            return;
        }

        if (MathF.Abs(normal.Y) > 0.5f)
        {
            axisA = 0;
            axisB = 2;
            return;
        }

        axisA = 0;
        axisB = 1;
    }

    private static int AxisCornerSign(Vector3 corner, int axis)
    {
        return axis switch
        {
            0 => corner.X > 0.5f ? 1 : -1,
            1 => corner.Y > 0.5f ? 1 : -1,
            _ => corner.Z > 0.5f ? 1 : -1
        };
    }

    private static Vector3 AxisVector(int axis)
    {
        return axis switch
        {
            0 => Vector3.UnitX,
            1 => Vector3.UnitY,
            _ => Vector3.UnitZ
        };
    }

    private readonly record struct Face(Vector3 Normal, Vector3[] Corners, Vector2[] Uvs);
}
