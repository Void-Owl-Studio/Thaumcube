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

        var tileSize = settings.CloudTileSizeBlocks;
        var radius = Math.Max(0, settings.CloudTileRadius);
        var baseTileX = MathF.Floor(cameraPosition.X / tileSize);
        var baseTileZ = MathF.Floor(cameraPosition.Z / tileSize);
        var y = settings.CloudHeightBlocks;

        for (var dz = -radius; dz <= radius; dz++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                var minX = (baseTileX + dx) * tileSize;
                var minZ = (baseTileZ + dz) * tileSize;
                AddHorizontalQuad(
                    vertices,
                    indices,
                    minX,
                    y,
                    minZ,
                    tileSize,
                    Vector3.One,
                    EnvironmentTextureAtlas.Clouds,
                    CloudBlockId,
                    bothSides: true);
            }
        }

        return new ChunkRenderMesh(new ChunkCoord(int.MinValue, int.MinValue), vertices, indices, "sky:clouds");
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
        return new ChunkRenderMesh(new ChunkCoord(int.MaxValue, int.MaxValue), vertices, indices, "sky:sun");
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

    private static void AddQuad(
        List<VoxelVertex> vertices,
        List<uint> indices,
        IReadOnlyList<Vector3> corners,
        Vector3 normal,
        Vector3 tint,
        int textureIndex,
        uint blockId)
    {
        var startIndex = (uint)vertices.Count;
        vertices.Add(new VoxelVertex(corners[0], normal, AtlasUv(new Vector2(0f, 0f), textureIndex), blockId, tint));
        vertices.Add(new VoxelVertex(corners[1], normal, AtlasUv(new Vector2(1f, 0f), textureIndex), blockId, tint));
        vertices.Add(new VoxelVertex(corners[2], normal, AtlasUv(new Vector2(1f, 1f), textureIndex), blockId, tint));
        vertices.Add(new VoxelVertex(corners[3], normal, AtlasUv(new Vector2(0f, 1f), textureIndex), blockId, tint));

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
