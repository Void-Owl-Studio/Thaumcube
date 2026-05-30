using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using VoxelGame.Core;
using VoxelGame.Player;
using VoxelGame.World.Chunks;

namespace VoxelGame.Rendering;

internal static class SkyMeshBuilder
{
    public const uint CloudBlockId = 200;
    public const uint SunBlockId = 201;

    private static readonly Vector3 SunDirection = Vector3.Normalize(new Vector3(-0.42f, 0.78f, -0.46f));

    private static CloudMeshCacheKey? s_cachedCloudKey;
    private static ChunkRenderMesh? s_cachedCloudMesh;
    private static CloudTextureData? s_cachedCloudTexture;

    public static IEnumerable<ChunkRenderMesh> Build(CameraState camera, GameSettings settings, float elapsedSeconds)
    {
        foreach (var cloudMesh in BuildCloudLayers(settings))
        {
            yield return cloudMesh;
        }

        yield return BuildSun(camera.Position, settings);
    }

    private static IEnumerable<ChunkRenderMesh> BuildCloudLayers(GameSettings settings)
    {
        var texturePath = ResolveCloudTexturePath();
        var cloudTexture = GetCloudTextureData(texturePath);
        var width = MathF.Max(1f, settings.CloudPlaneWidthBlocks);
        var depth = MathF.Max(1f, settings.CloudPlaneDepthBlocks);
        var textureScale = MathF.Max(1f, settings.CloudTextureScaleBlocks);
        var pixelWidth = cloudTexture.Width;
        var pixelHeight = cloudTexture.Height;
        var blockSizeX = width / pixelWidth;
        var blockSizeZ = depth / pixelHeight;
        var extrusionHeight = MathF.Max(1f, settings.CloudExtrusionHeightBlocks);
        var threshold = Math.Clamp(settings.CloudBrightnessThreshold, 0f, 1f);
        var baseY = settings.CloudHeightBlocks;
        var topY = baseY + extrusionHeight;
        var cellsX = pixelWidth;
        var cellsZ = pixelHeight;
        var textureStamp = cloudTexture.StampTicks;
        var cacheKey = new CloudMeshCacheKey(
            settings.CloudPlaneWidthBlocks,
            settings.CloudPlaneDepthBlocks,
            settings.CloudHeightBlocks,
            settings.CloudTextureScaleBlocks,
            settings.CloudExtrusionHeightBlocks,
            settings.CloudBlockSize,
            settings.CloudBrightnessThreshold,
            textureStamp);

        if (s_cachedCloudMesh is not null && s_cachedCloudKey == cacheKey)
        {
            yield return s_cachedCloudMesh;

            var transparentCachedMesh = BuildTransparentCacheMesh();
            if (!transparentCachedMesh.IsEmpty)
            {
                yield return transparentCachedMesh;
            }

            yield break;
        }

        var opaqueVertices = new List<VoxelVertex>(4096);
        var opaqueIndices = new List<uint>(8192);
        var transparentVertices = new List<VoxelVertex>(1024);
        var transparentIndices = new List<uint>(2048);
        var tileRepeatRadius = 1;
        var tileMinX = -width * 0.5f;
        var tileMinZ = -depth * 0.5f;

        for (var tileZ = -tileRepeatRadius; tileZ <= tileRepeatRadius; tileZ++)
        {
            for (var tileX = -tileRepeatRadius; tileX <= tileRepeatRadius; tileX++)
            {
                var tileOffsetX = tileX * width;
                var tileOffsetZ = tileZ * depth;

                for (var z = 0; z < cellsZ; z++)
                {
                    for (var x = 0; x < cellsX; x++)
                    {
                        SampleCloudPixel(cloudTexture.Pixels[z * pixelWidth + x], threshold, out var isActive, out var tint, out var alpha);
                        if (!isActive)
                        {
                            continue;
                        }

                        var x0 = tileMinX + tileOffsetX + x * blockSizeX;
                        var x1 = x0 + blockSizeX;
                        var z0 = tileMinZ + tileOffsetZ + z * blockSizeZ;
                        var z1 = z0 + blockSizeZ;
                        var targetVertices = alpha >= 0.995f ? opaqueVertices : transparentVertices;
                        var targetIndices = alpha >= 0.995f ? opaqueIndices : transparentIndices;

                        AddHorizontalFace(targetVertices, targetIndices, x0, x1, topY, z0, z1, textureScale, tint, alpha, topFacing: true, CloudBlockId);
                        AddHorizontalFace(targetVertices, targetIndices, x0, x1, baseY, z0, z1, textureScale, tint * 0.84f, alpha, topFacing: false, CloudBlockId);

                        var leftActive = x > 0 && IsCloudPixelActive(cloudTexture.Pixels[z * pixelWidth + (x - 1)], threshold);
                        var rightActive = x < cellsX - 1 && IsCloudPixelActive(cloudTexture.Pixels[z * pixelWidth + (x + 1)], threshold);
                        var frontActive = z > 0 && IsCloudPixelActive(cloudTexture.Pixels[(z - 1) * pixelWidth + x], threshold);
                        var backActive = z < cellsZ - 1 && IsCloudPixelActive(cloudTexture.Pixels[(z + 1) * pixelWidth + x], threshold);

                        if (!leftActive)
                        {
                            AddVerticalFace(targetVertices, targetIndices,
                            [
                                new Vector3(x0, baseY, z1),
                                new Vector3(x0, baseY, z0),
                                new Vector3(x0, topY, z0),
                                new Vector3(x0, topY, z1)
                            ], -Vector3.UnitX, z0, z1, baseY, topY, textureScale, tint * 0.92f, alpha);
                        }

                        if (!rightActive)
                        {
                            AddVerticalFace(targetVertices, targetIndices,
                            [
                                new Vector3(x1, baseY, z0),
                                new Vector3(x1, baseY, z1),
                                new Vector3(x1, topY, z1),
                                new Vector3(x1, topY, z0)
                            ], Vector3.UnitX, z0, z1, baseY, topY, textureScale, tint * 0.95f, alpha);
                        }

                        if (!frontActive)
                        {
                            AddVerticalFace(targetVertices, targetIndices,
                            [
                                new Vector3(x0, baseY, z0),
                                new Vector3(x1, baseY, z0),
                                new Vector3(x1, topY, z0),
                                new Vector3(x0, topY, z0)
                            ], -Vector3.UnitZ, x0, x1, baseY, topY, textureScale, tint * 0.90f, alpha);
                        }

                        if (!backActive)
                        {
                            AddVerticalFace(targetVertices, targetIndices,
                            [
                                new Vector3(x1, baseY, z1),
                                new Vector3(x0, baseY, z1),
                                new Vector3(x0, topY, z1),
                                new Vector3(x1, topY, z1)
                            ], Vector3.UnitZ, x0, x1, baseY, topY, textureScale, tint * 0.97f, alpha);
                        }
                    }
                }
            }
        }

        s_cachedCloudKey = cacheKey;
        s_cachedCloudMesh = new ChunkRenderMesh(
            new ChunkCoord(int.MinValue, int.MinValue),
            opaqueVertices,
            opaqueIndices,
            "sky:clouds:opaque",
            isTransparent: false,
            alwaysVisible: true);
        yield return s_cachedCloudMesh;

        var transparentMesh = new ChunkRenderMesh(
            new ChunkCoord(int.MinValue + 2, int.MinValue + 2),
            transparentVertices,
            transparentIndices,
            "sky:clouds:transparent",
            isTransparent: true,
            alwaysVisible: true);
        if (!transparentMesh.IsEmpty)
        {
            CacheTransparentMesh(transparentMesh);
            yield return transparentMesh;
        }
        else
        {
            CacheTransparentMesh(ChunkRenderMesh.CreateEmpty(new ChunkCoord(int.MinValue + 2, int.MinValue + 2), "sky:clouds:transparent", isTransparent: true));
        }
    }

    public static Vector2 GetCloudWorldOffset(Vector3 cameraPosition, GameSettings settings, float elapsedSeconds)
    {
        var direction = settings.CloudDirection.LengthSquared() < 0.0001f
            ? Vector2.UnitX
            : Vector2.Normalize(settings.CloudDirection);
        var drift = new Vector2(
            direction.X * settings.CloudPlaneWidthBlocks,
            direction.Y * settings.CloudPlaneDepthBlocks) * (settings.CloudSpeed * elapsedSeconds);
        return new Vector2(cameraPosition.X, cameraPosition.Z) + drift;
    }

    private static ChunkRenderMesh? s_cachedTransparentCloudMesh;

    private static ChunkRenderMesh BuildTransparentCacheMesh()
    {
        return s_cachedTransparentCloudMesh ?? ChunkRenderMesh.CreateEmpty(new ChunkCoord(int.MinValue + 2, int.MinValue + 2), "sky:clouds:transparent", isTransparent: true);
    }

    private static void CacheTransparentMesh(ChunkRenderMesh mesh)
    {
        s_cachedTransparentCloudMesh = mesh;
    }

    private static bool IsCloudPixelActive(Rgba32 pixel, float threshold)
    {
        var alpha = pixel.A / 255f;
        var color = new Vector3(pixel.R / 255f, pixel.G / 255f, pixel.B / 255f);
        var brightness = (color.X + color.Y + color.Z) / 3f;
        var whiteness = brightness - (MathF.Max(MathF.Abs(color.X - color.Y), MathF.Abs(color.Y - color.Z)) * 0.5f);
        return alpha > 0.01f && whiteness >= threshold;
    }

    private static void SampleCloudPixel(Rgba32 pixel, float threshold, out bool isActive, out Vector3 tint, out float alpha)
    {
        alpha = pixel.A / 255f;
        var color = new Vector3(pixel.R / 255f, pixel.G / 255f, pixel.B / 255f);
        var brightness = (color.X + color.Y + color.Z) / 3f;
        var whiteness = brightness - (MathF.Max(MathF.Abs(color.X - color.Y), MathF.Abs(color.Y - color.Z)) * 0.5f);
        isActive = alpha > 0.01f && whiteness >= threshold;
        if (!isActive)
        {
            tint = Vector3.Zero;
            alpha = 0f;
            return;
        }

        var contrasted = MathF.Pow(Math.Clamp(brightness, 0f, 1f), 0.78f);
        tint = Vector3.Lerp(new Vector3(0.76f, 0.80f, 0.88f), color, 0.55f + contrasted * 0.45f);
        alpha = MathF.Max(0.08f, alpha);
    }

    private static void AddHorizontalFace(
        List<VoxelVertex> vertices,
        List<uint> indices,
        float minX,
        float maxX,
        float y,
        float minZ,
        float maxZ,
        float textureScale,
        Vector3 tint,
        float alpha,
        bool topFacing,
        uint blockId)
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

        var uvMin = new Vector2(minX / textureScale, minZ / textureScale);
        var uvMax = new Vector2(maxX / textureScale, maxZ / textureScale);
        AddCloudQuad(vertices, indices, corners, topFacing ? Vector3.UnitY : -Vector3.UnitY, uvMin, uvMax, tint, alpha, blockId);
    }

    private static void AddVerticalFace(
        List<VoxelVertex> vertices,
        List<uint> indices,
        IReadOnlyList<Vector3> corners,
        Vector3 normal,
        float minAxis,
        float maxAxis,
        float minY,
        float maxY,
        float textureScale,
        Vector3 tint,
        float alpha)
    {
        var uvMin = new Vector2(minAxis / textureScale, minY / textureScale);
        var uvMax = new Vector2(maxAxis / textureScale, maxY / textureScale);
        AddCloudQuad(vertices, indices, corners, normal, uvMin, uvMax, tint, alpha, CloudBlockId);
    }

    private static void AddCloudQuad(
        List<VoxelVertex> vertices,
        List<uint> indices,
        IReadOnlyList<Vector3> corners,
        Vector3 normal,
        Vector2 uvMin,
        Vector2 uvMax,
        Vector3 tint,
        float alpha,
        uint blockId)
    {
        var startIndex = (uint)vertices.Count;
        vertices.Add(new VoxelVertex(corners[0], normal, new Vector2(uvMin.X, uvMin.Y), blockId, tint, alpha));
        vertices.Add(new VoxelVertex(corners[1], normal, new Vector2(uvMax.X, uvMin.Y), blockId, tint, alpha));
        vertices.Add(new VoxelVertex(corners[2], normal, new Vector2(uvMax.X, uvMax.Y), blockId, tint, alpha));
        vertices.Add(new VoxelVertex(corners[3], normal, new Vector2(uvMin.X, uvMax.Y), blockId, tint, alpha));

        indices.Add(startIndex);
        indices.Add(startIndex + 2);
        indices.Add(startIndex + 1);
        indices.Add(startIndex);
        indices.Add(startIndex + 3);
        indices.Add(startIndex + 2);
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

        AddEnvironmentQuad(vertices, indices, corners, toCamera, Vector3.One, EnvironmentTextureAtlas.Sun, SunBlockId);
        return new ChunkRenderMesh(new ChunkCoord(int.MaxValue, int.MaxValue), vertices, indices, "sky:sun", isTransparent: true, alwaysVisible: true);
    }

    private static void AddEnvironmentQuad(
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
        indices.Add(startIndex + 2);
        indices.Add(startIndex + 1);
        indices.Add(startIndex);
        indices.Add(startIndex + 3);
        indices.Add(startIndex + 2);
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

    private static string ResolveCloudTexturePath()
    {
        var primaryPath = Path.Combine(AppContext.BaseDirectory, "Assets", "textures", "environment", "clouds.png");
        if (File.Exists(primaryPath))
        {
            return primaryPath;
        }

        var fallbackPath = Path.Combine(AppContext.BaseDirectory, "assets", "textures", "environment", "clouds.png");
        if (File.Exists(fallbackPath))
        {
            return fallbackPath;
        }

        throw new FileNotFoundException("Cloud texture was not found.", primaryPath);
    }

    private static CloudTextureData GetCloudTextureData(string texturePath)
    {
        var stampTicks = File.GetLastWriteTimeUtc(texturePath).Ticks;
        if (s_cachedCloudTexture is { } cached && cached.StampTicks == stampTicks)
        {
            return cached;
        }

        using var image = Image.Load<Rgba32>(texturePath);
        var pixels = new Rgba32[image.Width * image.Height];
        image.CopyPixelDataTo(pixels);
        var textureData = new CloudTextureData(image.Width, image.Height, pixels, stampTicks);
        s_cachedCloudTexture = textureData;
        return textureData;
    }

    private readonly record struct CloudMeshCacheKey(
        float Width,
        float Depth,
        float Height,
        float TextureScale,
        float ExtrusionHeight,
        float BlockSize,
        float BrightnessThreshold,
        long TextureStampTicks);

    private readonly record struct CloudTextureData(
        int Width,
        int Height,
        Rgba32[] Pixels,
        long StampTicks);
}
