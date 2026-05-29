using System.Numerics;
using VoxelGame.Core;
using VoxelGame.Player;
using VoxelGame.World.Blocks;
using VoxelGame.World.Chunks;

namespace VoxelGame.Rendering;

internal static class SkyMeshBuilder
{
    public const uint CloudBlockId = 200;
    public const uint SunBlockId = 201;

    private static readonly Vector3 SunDirection = Vector3.Normalize(new Vector3(-0.42f, 0.78f, -0.46f));

    public static IEnumerable<ChunkRenderMesh> Build(CameraState camera, GameSettings settings)
    {
        yield return BuildCloudLayer(camera.Position, settings);
        yield return BuildSun(camera.Position, settings);
    }

    private static ChunkRenderMesh BuildCloudLayer(Vector3 cameraPosition, GameSettings settings)
    {
        var vertices = new List<VoxelVertex>();
        var indices = new List<uint>();

        var tileSize = MathF.Max(64f, settings.CloudTileSizeBlocks);
        var radius = Math.Max(0, settings.CloudTileRadius);
        var planeSize = tileSize * Math.Max(1, radius * 2 + 1);
        var centerX = cameraPosition.X;
        var centerZ = cameraPosition.Z;
        var y = settings.CloudHeightBlocks;
        var thickness = MathF.Max(2f, settings.CloudVolumeThicknessBlocks);
        var uvScale = MathF.Max(8f, settings.CloudTextureScaleBlocks);
        AddCloudLayeredPlane(vertices, indices, centerX, y, centerZ, planeSize, thickness, uvScale);

        return new ChunkRenderMesh(new ChunkCoord(int.MinValue, int.MinValue), vertices, indices, "sky:clouds", isTransparent: true);
    }

    private static void AddCloudLayeredPlane(
        List<VoxelVertex> vertices,
        List<uint> indices,
        float centerX,
        float topY,
        float centerZ,
        float planeSize,
        float thickness,
        float uvScale)
    {
        var half = planeSize * 0.5f;
        var layerStep = thickness / 3f;
        var layerInset = MathF.Max(6f, planeSize * 0.028f);
        var baseMinX = centerX - half;
        var baseMaxX = centerX + half;
        var baseMinZ = centerZ - half;
        var baseMaxZ = centerZ + half;

        for (var layer = 0; layer < 3; layer++)
        {
            var inset = layerInset * layer;
            var y = topY - layerStep * layer;
            var minX = baseMinX + inset;
            var maxX = baseMaxX - inset;
            var minZ = baseMinZ + inset;
            var maxZ = baseMaxZ - inset;
            var tint = Vector3.One * (1f - layer * 0.06f);

            AddHorizontalPlane(vertices, indices, minX, maxX, y, minZ, maxZ, uvScale, tint, topFacing: true);
            AddHorizontalPlane(vertices, indices, minX, maxX, y - layerStep * 0.55f, minZ, maxZ, uvScale, tint * 0.92f, topFacing: false);
        }

        var bottomY = topY - thickness;
        AddSideBand(vertices, indices, baseMinX, baseMaxX, topY - layerStep * 0.1f, bottomY, baseMinZ, baseMaxZ, uvScale);
    }

    private static ChunkRenderMesh BuildSun(Vector3 cameraPosition, GameSettings settings)
    {
        var vertices = new List<VoxelVertex>();
        var indices = new List<uint>();

        var center = cameraPosition + SunDirection * settings.SunDistanceBlocks;
        var halfSize = settings.SunSizeBlocks * 0.5f;
        var toCamera = Vector3.Normalize(cameraPosition - center);
        var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, toCamera));
        if (right.LengthSquared() < 0.0001f)
        {
            right = Vector3.UnitX;
        }

        var up = Vector3.Normalize(Vector3.Cross(toCamera, right));
        var corners = new[]
        {
            center - right * halfSize - up * halfSize,
            center + right * halfSize - up * halfSize,
            center + right * halfSize + up * halfSize,
            center - right * halfSize + up * halfSize
        };

        AddQuad(vertices, indices, corners, toCamera, Vector3.One, EnvironmentTextureAtlas.Sun, SunBlockId);
        return new ChunkRenderMesh(new ChunkCoord(int.MaxValue, int.MaxValue), vertices, indices, "sky:sun", isTransparent: true);
    }

    private static void AddHorizontalQuad(
        List<VoxelVertex> vertices,
        List<uint> indices,
        float minX,
        float y,
        float minZ,
        float size,
        Vector3 tint,
        int textureIndex,
        uint blockId,
        bool bothSides)
    {
        var topCorners = new[]
        {
            new Vector3(minX, y, minZ),
            new Vector3(minX + size, y, minZ),
            new Vector3(minX + size, y, minZ + size),
            new Vector3(minX, y, minZ + size)
        };

        AddQuad(vertices, indices, topCorners, Vector3.UnitY, tint, textureIndex, blockId);

        if (!bothSides)
        {
            return;
        }

        var bottomCorners = new[]
        {
            new Vector3(minX, y, minZ + size),
            new Vector3(minX + size, y, minZ + size),
            new Vector3(minX + size, y, minZ),
            new Vector3(minX, y, minZ)
        };

        AddQuad(vertices, indices, bottomCorners, -Vector3.UnitY, tint, textureIndex, blockId);
    }

    private static void AddHorizontalPlane(
        List<VoxelVertex> vertices,
        List<uint> indices,
        float minX,
        float maxX,
        float y,
        float minZ,
        float maxZ,
        float uvScale,
        Vector3 tint,
        bool topFacing)
    {
        var corners = topFacing
            ? new[]
            {
                new Vector3(minX, y, minZ),
                new Vector3(maxX, y, minZ),
                new Vector3(maxX, y, maxZ),
                new Vector3(minX, y, maxZ)
            }
            : new[]
            {
                new Vector3(minX, y, maxZ),
                new Vector3(maxX, y, maxZ),
                new Vector3(maxX, y, minZ),
                new Vector3(minX, y, minZ)
            };

        AddQuad(
            vertices,
            indices,
            corners,
            topFacing ? Vector3.UnitY : -Vector3.UnitY,
            tint,
            EnvironmentTextureAtlas.Clouds,
            CloudBlockId,
            Vector2.Zero,
            new Vector2((maxX - minX) / uvScale, (maxZ - minZ) / uvScale));
    }

    private static void AddSideBand(
        List<VoxelVertex> vertices,
        List<uint> indices,
        float minX,
        float maxX,
        float topY,
        float bottomY,
        float minZ,
        float maxZ,
        float uvScale)
    {
        AddQuad(vertices, indices,
        [
            new Vector3(minX, bottomY, minZ),
            new Vector3(maxX, bottomY, minZ),
            new Vector3(maxX, topY, minZ),
            new Vector3(minX, topY, minZ)
        ], -Vector3.UnitZ, Vector3.One * 0.94f, EnvironmentTextureAtlas.Clouds, CloudBlockId, Vector2.Zero, new Vector2((maxX - minX) / uvScale, (topY - bottomY) / uvScale));

        AddQuad(vertices, indices,
        [
            new Vector3(maxX, bottomY, maxZ),
            new Vector3(minX, bottomY, maxZ),
            new Vector3(minX, topY, maxZ),
            new Vector3(maxX, topY, maxZ)
        ], Vector3.UnitZ, Vector3.One, EnvironmentTextureAtlas.Clouds, CloudBlockId, Vector2.Zero, new Vector2((maxX - minX) / uvScale, (topY - bottomY) / uvScale));

        AddQuad(vertices, indices,
        [
            new Vector3(minX, bottomY, maxZ),
            new Vector3(minX, bottomY, minZ),
            new Vector3(minX, topY, minZ),
            new Vector3(minX, topY, maxZ)
        ], -Vector3.UnitX, Vector3.One * 0.92f, EnvironmentTextureAtlas.Clouds, CloudBlockId, Vector2.Zero, new Vector2((maxZ - minZ) / uvScale, (topY - bottomY) / uvScale));

        AddQuad(vertices, indices,
        [
            new Vector3(maxX, bottomY, minZ),
            new Vector3(maxX, bottomY, maxZ),
            new Vector3(maxX, topY, maxZ),
            new Vector3(maxX, topY, minZ)
        ], Vector3.UnitX, Vector3.One * 0.97f, EnvironmentTextureAtlas.Clouds, CloudBlockId, Vector2.Zero, new Vector2((maxZ - minZ) / uvScale, (topY - bottomY) / uvScale));
    }

    private static void AddQuad(
        List<VoxelVertex> vertices,
        List<uint> indices,
        IReadOnlyList<Vector3> corners,
        Vector3 normal,
        Vector3 tint,
        int textureIndex,
        uint blockId)
    {
        AddQuad(vertices, indices, corners, normal, tint, textureIndex, blockId, Vector2.Zero, Vector2.One);
    }

    private static void AddQuad(
        List<VoxelVertex> vertices,
        List<uint> indices,
        IReadOnlyList<Vector3> corners,
        Vector3 normal,
        Vector3 tint,
        int textureIndex,
        uint blockId,
        Vector2 uvMin,
        Vector2 uvMax)
    {
        var startIndex = (uint)vertices.Count;
        vertices.Add(new VoxelVertex(corners[0], normal, AtlasUv(new Vector2(uvMin.X, uvMin.Y), textureIndex), blockId, tint));
        vertices.Add(new VoxelVertex(corners[1], normal, AtlasUv(new Vector2(uvMax.X, uvMin.Y), textureIndex), blockId, tint));
        vertices.Add(new VoxelVertex(corners[2], normal, AtlasUv(new Vector2(uvMax.X, uvMax.Y), textureIndex), blockId, tint));
        vertices.Add(new VoxelVertex(corners[3], normal, AtlasUv(new Vector2(uvMin.X, uvMax.Y), textureIndex), blockId, tint));

        indices.Add(startIndex);
        indices.Add(startIndex + 1);
        indices.Add(startIndex + 2);
        indices.Add(startIndex);
        indices.Add(startIndex + 2);
        indices.Add(startIndex + 3);
    }

    private static Vector2 AtlasUv(Vector2 localUv, int textureIndex)
    {
        var tileX = textureIndex % EnvironmentTextureAtlas.TilesPerRow;
        var tileY = textureIndex / EnvironmentTextureAtlas.TilesPerRow;
        var atlasRows = (EnvironmentTextureAtlas.TextureCount + EnvironmentTextureAtlas.TilesPerRow - 1) / EnvironmentTextureAtlas.TilesPerRow;
        var atlasWidth = EnvironmentTextureAtlas.TilesPerRow * EnvironmentTextureAtlas.TileSize;
        var atlasHeight = atlasRows * EnvironmentTextureAtlas.TileSize;
        var insetX = 0.5f / atlasWidth;
        var insetY = 0.5f / atlasHeight;
        var tileWidth = 1f / EnvironmentTextureAtlas.TilesPerRow;
        var tileHeight = 1f / atlasRows;

        return new Vector2(
            tileX * tileWidth + insetX + localUv.X * (tileWidth - insetX * 2f),
            tileY * tileHeight + insetY + localUv.Y * (tileHeight - insetY * 2f));
    }
}
