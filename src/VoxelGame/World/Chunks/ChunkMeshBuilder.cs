using System.Numerics;
using VoxelGame.World;
using VoxelGame.World.Blocks;
using VoxelGame.World.Generation;

namespace VoxelGame.World.Chunks;

public sealed class ChunkMeshBuilder
{
    public const uint BreakOverlayBlockId = 100;
    private const float WaterSurfaceHeight = 0.92f;

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

    public ChunkSectionMeshes BuildSection(VoxelWorld world, Chunk chunk, int sectionIndex)
    {
        var opaqueVertices = new List<VoxelVertex>(2048);
        var opaqueIndices = new List<uint>(4096);
        var transparentVertices = new List<VoxelVertex>(512);
        var transparentIndices = new List<uint>(1024);
        var baseX = chunk.Coord.X * Chunk.SizeX;
        var baseZ = chunk.Coord.Z * Chunk.SizeZ;
        var minY = Chunk.MinY + sectionIndex * Chunk.MeshSectionHeight;
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

            var blockWorldX = baseX + x;
            var blockWorldY = y;
            var blockWorldZ = baseZ + z;

            foreach (var face in Faces)
            {
                var neighborX = blockWorldX + (int)face.Normal.X;
                var neighborY = blockWorldY + (int)face.Normal.Y;
                var neighborZ = blockWorldZ + (int)face.Normal.Z;
                var neighborBlock = GetBlockForMesh(world, chunk, baseX, baseZ, neighborX, neighborY, neighborZ);
                var isSurfaceWater = block != BlockType.Water ||
                    GetBlockForMesh(world, chunk, baseX, baseZ, blockWorldX, blockWorldY + 1, blockWorldZ) != BlockType.Water;

                if (!ShouldRenderFace(world, block, face.Normal, neighborBlock, isSurfaceWater))
                {
                    continue;
                }

                var textureIndex = world.Blocks.GetFaceTextureIndex(block, face.Normal);
                var tint = ResolveTint(world, block, face.Normal, blockWorldX, blockWorldY, blockWorldZ);

                if (world.Blocks.IsTransparent(block))
                {
                    AddFace(world, chunk, baseX, baseZ, transparentVertices, transparentIndices, new Vector3(blockWorldX, blockWorldY, blockWorldZ), face, textureIndex, block, tint);
                }
                else
                {
                    AddFace(world, chunk, baseX, baseZ, opaqueVertices, opaqueIndices, new Vector3(blockWorldX, blockWorldY, blockWorldZ), face, textureIndex, block, tint);
                }
            }
        }

        return new ChunkSectionMeshes(
            new ChunkRenderMesh(chunk.Coord, opaqueVertices, opaqueIndices, $"chunk:{chunk.Coord.X},{chunk.Coord.Z}:section:{sectionIndex}:opaque"),
            new ChunkRenderMesh(chunk.Coord, transparentVertices, transparentIndices, $"chunk:{chunk.Coord.X},{chunk.Coord.Z}:section:{sectionIndex}:transparent", isTransparent: true));
    }

    private static void AddFace(
        VoxelWorld world,
        Chunk chunk,
        int chunkBaseX,
        int chunkBaseZ,
        List<VoxelVertex> vertices,
        List<uint> indices,
        Vector3 origin,
        Face face,
        int textureIndex,
        BlockType block,
        Vector3 tint)
    {
        var start = (uint)vertices.Count;
        Span<float> aoValues = stackalloc float[4];

        for (var i = 0; i < 4; i++)
        {
            var corner = ResolveFaceCorner(block, face.Corners[i]);
            var ao = block == BlockType.Water ? 1f : SampleAmbientOcclusion(world, chunk, chunkBaseX, chunkBaseZ, origin, face.Normal, face.Corners[i]);
            aoValues[i] = ao;
            vertices.Add(new VoxelVertex(
                origin + corner,
                face.Normal,
                AtlasUv(face.Uvs[i], textureIndex),
                (uint)block,
                tint * ao));
        }

        AddQuadIndices(indices, start, aoValues[0], aoValues[1], aoValues[2], aoValues[3]);
    }

    public static ChunkRenderMesh BuildDroppedBlockMesh(
        string key,
        Vector3 center,
        float size,
        float rotationRadians,
        BlockType block,
        BlockRegistry blocks,
        Vector2? uvMin = null,
        Vector2? uvMax = null)
    {
        var vertices = new List<VoxelVertex>(24);
        var indices = new List<uint>(36);
        var cos = MathF.Cos(rotationRadians);
        var sin = MathF.Sin(rotationRadians);
        var resolvedUvMin = uvMin ?? Vector2.Zero;
        var resolvedUvMax = uvMax ?? Vector2.One;

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

                var faceUv = face.Uvs[i];
                var localUv = new Vector2(
                    resolvedUvMin.X + (resolvedUvMax.X - resolvedUvMin.X) * faceUv.X,
                    resolvedUvMin.Y + (resolvedUvMax.Y - resolvedUvMin.Y) * faceUv.Y);
                vertices.Add(new VoxelVertex(center + rotated, Vector3.Normalize(normal), AtlasUv(localUv, textureIndex), (uint)block, ResolveItemTint(block, face.Normal)));
            }

            AddQuadIndices(indices, start);
        }

        return new ChunkRenderMesh(new ChunkCoord(int.MinValue, int.MinValue), vertices, indices, key, isTransparent: block == BlockType.Water);
    }

    public static ChunkRenderMesh BuildBillboardParticleMesh(
        string key,
        Vector3 center,
        float size,
        BlockType block,
        BlockRegistry blocks,
        Vector3 cameraRight,
        Vector3 cameraUp,
        Vector2 uvMin,
        Vector2 uvMax)
    {
        var vertices = new List<VoxelVertex>(4);
        var indices = new List<uint>(6);
        var halfRight = Vector3.Normalize(cameraRight) * size * 0.5f;
        var halfUp = Vector3.Normalize(cameraUp) * size * 0.5f;
        var faceNormal = Vector3.Normalize(Vector3.Cross(cameraUp, cameraRight));
        var textureIndex = blocks.GetFaceTextureIndex(block, Vector3.UnitZ);

        Span<Vector3> corners =
        [
            center - halfRight - halfUp,
            center - halfRight + halfUp,
            center + halfRight + halfUp,
            center + halfRight - halfUp
        ];

        Span<Vector2> uvs =
        [
            new Vector2(0f, 1f),
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f)
        ];

        for (var i = 0; i < 4; i++)
        {
            var faceUv = uvs[i];
            var localUv = new Vector2(
                uvMin.X + (uvMax.X - uvMin.X) * faceUv.X,
                uvMin.Y + (uvMax.Y - uvMin.Y) * faceUv.Y);
            vertices.Add(new VoxelVertex(corners[i], faceNormal, AtlasUv(localUv, textureIndex), (uint)block, ResolveItemTint(block, Vector3.UnitY)));
        }

        AddQuadIndices(indices, 0);
        return new ChunkRenderMesh(new ChunkCoord(int.MinValue, int.MinValue), vertices, indices, key, isTransparent: true);
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
        return new ChunkRenderMesh(new ChunkCoord(int.MinValue + 1, int.MinValue + 1), vertices, indices, "break-overlay", isTransparent: true);
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
        if (block == BlockType.Water)
        {
            return new Vector3(0.86f, 0.96f, 1.0f);
        }

        if (block == BlockType.OakLeaves)
        {
            return world.GetFoliageTint(worldX, worldY, worldZ);
        }

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
        if (block == BlockType.Water)
        {
            return new Vector3(0.86f, 0.96f, 1.0f);
        }

        if (block == BlockType.OakLeaves)
        {
            return new Vector3(0.54f, 0.78f, 0.30f);
        }

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

    private static float SampleAmbientOcclusion(VoxelWorld world, Chunk chunk, int chunkBaseX, int chunkBaseZ, Vector3 origin, Vector3 normal, Vector3 corner)
    {
        var blockX = (int)origin.X;
        var blockY = (int)origin.Y;
        var blockZ = (int)origin.Z;

        GetFaceAxes(normal, out var axisA, out var axisB);
        var signA = AxisCornerSign(corner, axisA);
        var signB = AxisCornerSign(corner, axisB);

        var offsetA = AxisVector(axisA) * signA;
        var offsetB = AxisVector(axisB) * signB;

        var side1Solid = IsOccluding(world, chunk, chunkBaseX, chunkBaseZ, blockX, blockY, blockZ, normal + offsetA);
        var side2Solid = IsOccluding(world, chunk, chunkBaseX, chunkBaseZ, blockX, blockY, blockZ, normal + offsetB);
        var cornerSolid = IsOccluding(world, chunk, chunkBaseX, chunkBaseZ, blockX, blockY, blockZ, normal + offsetA + offsetB);

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

    private static bool IsOccluding(VoxelWorld world, Chunk chunk, int chunkBaseX, int chunkBaseZ, int blockX, int blockY, int blockZ, Vector3 offset)
    {
        var sampleX = blockX + (int)offset.X;
        var sampleY = blockY + (int)offset.Y;
        var sampleZ = blockZ + (int)offset.Z;
        return !IsTransparentForMesh(world, chunk, chunkBaseX, chunkBaseZ, sampleX, sampleY, sampleZ);
    }

    private static bool ShouldRenderFace(
        VoxelWorld world,
        BlockType block,
        Vector3 faceNormal,
        BlockType neighborBlock,
        bool isSurfaceWater)
    {
        if (block == BlockType.Water)
        {
            return ShouldRenderWaterFace(faceNormal, neighborBlock, isSurfaceWater);
        }

        if (block == neighborBlock)
        {
            return false;
        }

        return world.Blocks.IsTransparent(neighborBlock);
    }

    private static bool ShouldRenderWaterFace(
        Vector3 faceNormal,
        BlockType neighborBlock,
        bool isSurfaceWater)
    {
        if (neighborBlock == BlockType.Water)
        {
            return false;
        }

        if (faceNormal.Y < -0.5f)
        {
            return false;
        }

        if (faceNormal.Y > 0.5f)
        {
            return isSurfaceWater;
        }

        return isSurfaceWater && neighborBlock == BlockType.Air;
    }

    private static Vector3 ResolveFaceCorner(BlockType block, Vector3 corner)
    {
        if (block != BlockType.Water)
        {
            return corner;
        }

        if (corner.Y > 0.5f)
        {
            corner.Y = WaterSurfaceHeight;
        }

        return corner;
    }

    private static BlockType GetBlockForMesh(VoxelWorld world, Chunk chunk, int chunkBaseX, int chunkBaseZ, int worldX, int y, int worldZ)
    {
        if (y < Chunk.MinY || y >= Chunk.MaxYExclusive)
        {
            return BlockType.Air;
        }

        var localX = worldX - chunkBaseX;
        var localZ = worldZ - chunkBaseZ;
        if (localX >= 0 && localX < Chunk.SizeX && localZ >= 0 && localZ < Chunk.SizeZ)
        {
            return chunk.GetBlock(localX, y, localZ);
        }

        return world.GetBlock(worldX, y, worldZ);
    }

    private static bool IsTransparentForMesh(VoxelWorld world, Chunk chunk, int chunkBaseX, int chunkBaseZ, int worldX, int y, int worldZ)
    {
        if (y < Chunk.MinY)
        {
            return false;
        }

        if (y >= Chunk.MaxYExclusive)
        {
            return true;
        }

        return world.Blocks.IsTransparent(GetBlockForMesh(world, chunk, chunkBaseX, chunkBaseZ, worldX, y, worldZ));
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
