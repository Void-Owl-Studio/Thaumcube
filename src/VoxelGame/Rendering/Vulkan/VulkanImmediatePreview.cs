using System.Numerics;
using ImageSharpImage = SixLabors.ImageSharp.Image;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Silk.NET.Vulkan;
using VoxelGame.Player;
using VoxelGame.World.Blocks;
using VoxelGame.World.Chunks;

namespace VoxelGame.Rendering.Vulkan;

internal unsafe sealed class VulkanImmediatePreview
{
    private readonly Vk _vk;
    private readonly BlockRegistry _blocks = new();
    private readonly Dictionary<int, TextureTile> _blockTiles;
    private readonly PlayerModel _menuPlayerModel = new();
    private TextureTile _playerSkin;

    public VulkanImmediatePreview(Vk vk)
    {
        _vk = vk;
        _blockTiles = LoadBlockTiles();
        _playerSkin = LoadPlayerSkinTile();
    }

    public void ReloadPlayerSkin()
    {
        _playerSkin = LoadPlayerSkinTile();
    }

    public void Draw(CommandBuffer commandBuffer, Extent2D extent, RenderScene scene)
    {
        var width = (int)extent.Width;
        var height = (int)extent.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        DrawProjectedVoxelMeshes(commandBuffer, extent, scene, width, height);

        DrawHud(commandBuffer, extent, scene, width, height);
    }

    public void DrawHud(CommandBuffer commandBuffer, Extent2D extent, RenderScene scene)
    {
        var width = (int)extent.Width;
        var height = (int)extent.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        DrawHud(commandBuffer, extent, scene, width, height);
    }

    private void DrawHud(CommandBuffer commandBuffer, Extent2D extent, RenderScene scene, int width, int height)
    {
        if (scene.Hud.ShowBusyOverlay)
        {
            DrawBusyOverlay(commandBuffer, extent, scene, width, height);
            return;
        }

        if (scene.Hud.ShowMenu)
        {
            DrawMenu(commandBuffer, extent, scene, width, height);
            return;
        }

        var pulse = scene.Hud.SelectedBlock is BlockType.ArcaneCrystal or BlockType.MagicOre ? 0.18f : 0.0f;
        DrawStatusOverlay(commandBuffer, extent, scene, width);
        if (scene.Hud.ShowInventory)
        {
            DrawInventory(commandBuffer, extent, scene, width, height);
            return;
        }

        DrawCrosshair(commandBuffer, extent, width, height, new Rgba(0.72f + pulse, 0.94f, 0.88f, 1f));
        DrawHotbar(commandBuffer, extent, scene, width, height);
    }

    private void DrawStatusOverlay(CommandBuffer commandBuffer, Extent2D extent, RenderScene scene, int width)
    {
        var biome = string.IsNullOrWhiteSpace(scene.Hud.CurrentBiome) ? "UNKNOWN" : scene.Hud.CurrentBiome.ToUpperInvariant();
        var text = $"FPS {scene.Hud.Fps}  BIOME {biome}";
        var scale = Math.Clamp(width / 640, 1, 2);
        var textWidth = TextWidth(text, scale);
        DrawRect(commandBuffer, extent, 10, 10, textWidth + 12, 7 * scale + 10, new Rgba(0.02f, 0.025f, 0.035f, 0.78f));
        DrawText(commandBuffer, extent, text, 16, 15, scale, new Rgba(0.82f, 0.94f, 0.90f, 1f));
    }

    private void DrawBusyOverlay(CommandBuffer commandBuffer, Extent2D extent, RenderScene scene, int width, int height)
    {
        DrawRect(commandBuffer, extent, 0, 0, width, height, new Rgba(0.025f, 0.018f, 0.035f, 0.94f));

        var panelWidth = Math.Clamp(width / 2, 340, 620);
        var panelHeight = 140;
        var panelX = (width - panelWidth) / 2;
        var panelY = (height - panelHeight) / 2;
        DrawRect(commandBuffer, extent, panelX - 6, panelY - 6, panelWidth + 12, panelHeight + 12, new Rgba(0.11f, 0.14f, 0.16f, 1f));
        DrawRect(commandBuffer, extent, panelX, panelY, panelWidth, panelHeight, new Rgba(0.14f, 0.16f, 0.20f, 1f));

        var title = string.IsNullOrWhiteSpace(scene.Hud.BusyTitle) ? "WORKING" : scene.Hud.BusyTitle;
        DrawText(commandBuffer, extent, title, panelX + 24, panelY + 22, 3, new Rgba(0.62f, 0.96f, 0.82f, 1f));

        if (!string.IsNullOrWhiteSpace(scene.Hud.BusyStatusText))
        {
            DrawText(commandBuffer, extent, scene.Hud.BusyStatusText, panelX + 24, panelY + 58, 2, new Rgba(0.78f, 0.86f, 0.84f, 1f));
        }

        var progressBarX = panelX + 24;
        var progressBarY = panelY + 94;
        var progressBarWidth = panelWidth - 48;
        var progressBarHeight = 18;
        var progress = Math.Clamp(scene.Hud.BusyProgress, 0f, 1f);
        DrawRect(commandBuffer, extent, progressBarX, progressBarY, progressBarWidth, progressBarHeight, new Rgba(0.08f, 0.10f, 0.12f, 1f));
        DrawRect(commandBuffer, extent, progressBarX + 2, progressBarY + 2, Math.Max(0, (int)((progressBarWidth - 4) * progress)), progressBarHeight - 4, new Rgba(0.30f, 0.82f, 0.68f, 1f));

        var percentText = $"{(int)MathF.Round(progress * 100f)}%";
        DrawText(commandBuffer, extent, percentText, panelX + panelWidth - TextWidth(percentText, 2) - 24, panelY + 58, 2, new Rgba(0.88f, 0.90f, 0.84f, 1f));
    }

    private void DrawMenu(CommandBuffer commandBuffer, Extent2D extent, RenderScene scene, int width, int height)
    {
        if (scene.Hud.ShowPauseOverlay)
        {
            DrawPauseBackdrop(commandBuffer, extent, scene, width, height);
        }
        else if (scene.Hud.MenuScreen == MenuScreen.Main)
        {
            DrawMainMenuBackdrop(commandBuffer, extent, width, height);
        }
        else
        {
            DrawRect(commandBuffer, extent, 0, 0, width, height, new Rgba(0.025f, 0.018f, 0.035f, 0.92f));
        }

        if (scene.Hud.MenuScreen != MenuScreen.Main)
        {
            var scale = Math.Clamp(width / 320, 3, 5);
            var title = scene.Hud.MenuTitle;
            DrawText(commandBuffer, extent, title, (width - TextWidth(title, scale)) / 2, height / 5, scale, new Rgba(0.62f, 0.96f, 0.82f, 1f));
            if (!string.IsNullOrWhiteSpace(scene.Hud.MenuSubtitle))
            {
                DrawText(commandBuffer, extent, scene.Hud.MenuSubtitle, (width - TextWidth(scene.Hud.MenuSubtitle, 2)) / 2, height / 5 + 38, 2, new Rgba(0.54f, 0.68f, 0.64f, 1f));
            }
        }

        switch (scene.Hud.MenuScreen)
        {
            case MenuScreen.Main:
                DrawRootMenu(commandBuffer, extent, scene, width, height);
                break;
            case MenuScreen.Pause:
                DrawPauseMenu(commandBuffer, extent, scene, width, height);
                break;
            case MenuScreen.Singleplayer:
                DrawWorldSelectionMenu(commandBuffer, extent, scene, width, height);
                break;
            case MenuScreen.Settings:
                DrawSettingsMenu(commandBuffer, extent, scene, width, height);
                break;
            case MenuScreen.CreateWorld:
                DrawCreateWorldMenu(commandBuffer, extent, scene, width, height);
                break;
        }

        if (!string.IsNullOrWhiteSpace(scene.Hud.MenuStatusText))
        {
            var statusScale = 2;
            DrawText(
                commandBuffer,
                extent,
                scene.Hud.MenuStatusText,
                (width - TextWidth(scene.Hud.MenuStatusText, statusScale)) / 2,
                height - 64,
                statusScale,
                new Rgba(0.88f, 0.82f, 0.64f, 1f));
        }
    }

    private void DrawMainMenuBackdrop(CommandBuffer commandBuffer, Extent2D extent, int width, int height)
    {
    }

    private void DrawPauseBackdrop(CommandBuffer commandBuffer, Extent2D extent, RenderScene scene, int width, int height)
    {
        var faces = CollectProjectedFaces(scene, width, height, 2200);
        var tileSize = Math.Clamp(Math.Min(width, height) / 26, 18, 34);
        var tilesX = (width + tileSize - 1) / tileSize;
        var tilesY = (height + tileSize - 1) / tileSize;
        var samples = new BackdropSample[tilesX * tilesY];
        var fallback = new Rgba(0.16f, 0.20f, 0.27f, 1f);

        foreach (var face in faces)
        {
            var centerX = face.X + face.Width / 2;
            var centerY = face.Y + face.Height / 2;
            if (centerX < 0 || centerX >= width || centerY < 0 || centerY >= height)
            {
                continue;
            }

            var tileX = centerX / tileSize;
            var tileY = centerY / tileSize;
            var tileIndex = tileY * tilesX + tileX;
            var weight = Math.Max(1f, (face.Width * face.Height) / Math.Max(18f, face.Depth * 6f));
            samples[tileIndex].Accumulate(face.Color, weight);
        }

        for (var tileY = 0; tileY < tilesY; tileY++)
        {
            for (var tileX = 0; tileX < tilesX; tileX++)
            {
                var tileIndex = tileY * tilesX + tileX;
                var color = samples[tileIndex].Weight > 0f
                    ? Mix(samples[tileIndex].ToColor(), fallback, 0.58f)
                    : fallback;

                if (((tileX + tileY) & 1) == 0)
                {
                    color = Scale(color, 0.94f);
                }

                var x = tileX * tileSize;
                var y = tileY * tileSize;
                DrawRect(commandBuffer, extent, x, y, Math.Min(tileSize, width - x), Math.Min(tileSize, height - y), color);
            }
        }

        DrawRect(commandBuffer, extent, 0, 0, width, height, new Rgba(0.10f, 0.12f, 0.16f, 1f));

        var panelWidth = Math.Clamp(width / 3, 340, 430);
        var panelHeight = 146;
        var panelX = (width - panelWidth) / 2;
        var panelY = height / 2 - 40;
        DrawRect(commandBuffer, extent, panelX - 10, panelY - 10, panelWidth + 20, panelHeight + 20, new Rgba(0.13f, 0.14f, 0.17f, 1f));
        DrawRect(commandBuffer, extent, panelX - 2, panelY - 2, panelWidth + 4, panelHeight + 4, new Rgba(0.62f, 0.65f, 0.69f, 1f));
        DrawRect(commandBuffer, extent, panelX, panelY, panelWidth, panelHeight, new Rgba(0.24f, 0.25f, 0.28f, 1f));
    }

    private void DrawRootMenu(CommandBuffer commandBuffer, Extent2D extent, RenderScene scene, int width, int height)
    {
        var layout = HudLayout.BuildMainMenu(width, height);
        var menuScale = layout.Scale;
        DrawMainMenuLogo(commandBuffer, extent, scene, width, height);
        DrawPlayerPreviewPanel(commandBuffer, extent, scene, layout.PlayerPreviewBounds);

        var options = new[]
        {
            "SINGLEPLAYER",
            "SETTINGS",
            scene.Hud.MenuPlayerBodyType == PlayerBodyType.Slim ? "BODY SLIM" : "BODY NORMAL",
            "LOAD SKIN",
            "APPLY SKIN",
            "EXIT"
        };
        for (var i = 0; i < options.Length; i++)
        {
            var bounds = layout.Items[i].Bounds;
            DrawMenuButton(commandBuffer, extent, bounds, options[i], menuScale, i == scene.Hud.MainMenuSelectedIndex);
        }

        var skinText = string.IsNullOrWhiteSpace(scene.Hud.MenuSkinText) ? "DEFAULT SKIN" : scene.Hud.MenuSkinText;
        var skinScale = 1;
        var skinX = layout.PlayerPreviewBounds.X + (layout.PlayerPreviewBounds.Width - TextWidth(skinText, skinScale)) / 2;
        DrawText(commandBuffer, extent, skinText, skinX, layout.PlayerPreviewBounds.Bottom - 22, skinScale, scene.Hud.MenuSkinReadyToApply ? new Rgba(0.90f, 0.76f, 0.42f, 1f) : new Rgba(0.62f, 0.56f, 0.74f, 1f));

        var versionText = "ALPHA BUILD";
        var versionScale = 1;
        DrawText(
            commandBuffer,
            extent,
            versionText,
            (width - TextWidth(versionText, versionScale)) / 2,
            height - 36,
            versionScale,
            new Rgba(0.60f, 0.55f, 0.68f, 1f));
    }

    private void DrawMainMenuLogo(CommandBuffer commandBuffer, Extent2D extent, RenderScene scene, int width, int height)
    {
        if (!string.IsNullOrWhiteSpace(scene.Hud.MenuSubtitle))
        {
            var subtitleScale = 2;
            var subtitleY = Math.Max(136, height / 4);
            DrawText(
                commandBuffer,
                extent,
                scene.Hud.MenuSubtitle,
                (width - TextWidth(scene.Hud.MenuSubtitle, subtitleScale)) / 2,
                subtitleY,
                subtitleScale,
                new Rgba(0.82f, 0.78f, 0.64f, 1f));
        }
    }

    private void DrawPlayerPreviewPanel(CommandBuffer commandBuffer, Extent2D extent, RenderScene scene, UiRect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        DrawOutlinedPanel(commandBuffer, extent, bounds, new Rgba(0.74f, 0.56f, 0.28f, 1f), new Rgba(0.055f, 0.030f, 0.065f, 0.96f));
        DrawCornerRunes(commandBuffer, extent, bounds, new Rgba(0.78f, 0.58f, 0.32f, 1f), 8);
        DrawText(commandBuffer, extent, "PLAYER", bounds.X + 14, bounds.Y + 12, 2, new Rgba(0.88f, 0.84f, 0.74f, 1f));

        var modelBounds = new UiRect(bounds.X + 12, bounds.Y + 40, bounds.Width - 24, bounds.Height - 74);
        DrawMagicRing(commandBuffer, extent, new Vector2(modelBounds.X + modelBounds.Width * 0.5f, modelBounds.Bottom - 18), Math.Min(modelBounds.Width / 3, 54), new Rgba(0.58f, 0.22f, 0.82f, 1f));
        DrawPlayerModelPreview(commandBuffer, extent, scene, modelBounds);
    }

    private void DrawPlayerModelPreview(CommandBuffer commandBuffer, Extent2D extent, RenderScene scene, UiRect bounds)
    {
        var cameraPosition = new Vector3(0f, 1.15f, -4.4f);
        var target = new Vector3(0f, 0.92f, 0f);
        var forward = Vector3.Normalize(target - cameraPosition);
        var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        var up = Vector3.Normalize(Vector3.Cross(forward, right));
        var center = new Vector2(bounds.X + bounds.Width * 0.5f, bounds.Y + bounds.Height * 0.56f);
        var focalLength = bounds.Height * 1.42f;
        var yaw = scene.ElapsedSeconds * 18f + 28f;
        _menuPlayerModel.BodyType = scene.Hud.MenuPlayerBodyType;
        var meshes = _menuPlayerModel.BuildRenderMeshes(Vector3.Zero, yaw, 0f, hideHead: false);
        var triangles = new List<ProjectedTexturedTriangle>();

        foreach (var mesh in meshes)
        {
            for (var i = 0; i + 2 < mesh.Indices.Length; i += 3)
            {
                var a = mesh.Vertices[(int)mesh.Indices[i]];
                var b = mesh.Vertices[(int)mesh.Indices[i + 1]];
                var c = mesh.Vertices[(int)mesh.Indices[i + 2]];
                var normal = Vector3.Normalize(a.Normal + b.Normal + c.Normal);
                var centerWorld = (a.Position + b.Position + c.Position) / 3f;
                if (Vector3.Dot(normal, cameraPosition - centerWorld) <= 0f)
                {
                    continue;
                }

                if (!ProjectPreviewPoint(a.Position, cameraPosition, forward, right, up, center, focalLength, out var pa) ||
                    !ProjectPreviewPoint(b.Position, cameraPosition, forward, right, up, center, focalLength, out var pb) ||
                    !ProjectPreviewPoint(c.Position, cameraPosition, forward, right, up, center, focalLength, out var pc))
                {
                    continue;
                }

                triangles.Add(new ProjectedTexturedTriangle(
                    new TexturedVertex(pa.Position, PlayerAtlasUvToSkinUv(a.Uv)),
                    new TexturedVertex(pb.Position, PlayerAtlasUvToSkinUv(b.Uv)),
                    new TexturedVertex(pc.Position, PlayerAtlasUvToSkinUv(c.Uv)),
                    (pa.Depth + pb.Depth + pc.Depth) / 3f));
            }
        }

        foreach (var triangle in triangles.OrderByDescending(triangle => triangle.Depth))
        {
            DrawTexturedTriangle(commandBuffer, extent, bounds, _playerSkin, triangle.A, triangle.B, triangle.C);
        }
    }

    private static bool ProjectPreviewPoint(
        Vector3 point,
        Vector3 cameraPosition,
        Vector3 forward,
        Vector3 right,
        Vector3 up,
        Vector2 center,
        float focalLength,
        out ProjectedVertex projected)
    {
        var relative = point - cameraPosition;
        var depth = Vector3.Dot(relative, forward);
        if (depth <= 0.15f)
        {
            projected = default;
            return false;
        }

        var x = Vector3.Dot(relative, right);
        var y = Vector3.Dot(relative, up);
        projected = new ProjectedVertex(
            new Vector2(center.X + x * focalLength / depth, center.Y - y * focalLength / depth),
            depth);
        return true;
    }

    private static Vector2 PlayerAtlasUvToSkinUv(Vector2 uv)
    {
        var atlasWidth = (float)BlockTextureAtlas.GetAtlasWidth();
        var atlasHeight = (float)BlockTextureAtlas.GetAtlasHeight();
        var skinUScale = BlockTextureAtlas.PlayerSkinWidth / atlasWidth;
        var skinVScale = BlockTextureAtlas.PlayerSkinHeight / atlasHeight;
        var skinVOffset = BlockTextureAtlas.GetBlockAtlasHeight() / atlasHeight;
        return new Vector2(
            Math.Clamp(uv.X / skinUScale, 0f, 1f),
            Math.Clamp((uv.Y - skinVOffset) / skinVScale, 0f, 1f));
    }

    private void DrawPauseMenu(CommandBuffer commandBuffer, Extent2D extent, RenderScene scene, int width, int height)
    {
        var options = new[] { "SETTINGS", "EXIT TO MAIN MENU" };
        var layout = HudLayout.BuildPauseMenu(width, height);
        var menuScale = layout.Scale;

        for (var i = 0; i < options.Length; i++)
        {
            var bounds = layout.Items[i].Bounds;
            DrawMenuButton(commandBuffer, extent, bounds, options[i], menuScale, i == scene.Hud.PauseMenuSelectedIndex);
        }
    }

    private void DrawSettingsMenu(CommandBuffer commandBuffer, Extent2D extent, RenderScene scene, int width, int height)
    {
        var layout = HudLayout.BuildSettingsMenu(width, height);
        DrawRect(commandBuffer, extent, layout.RenderDistanceBounds.X - 3, layout.RenderDistanceBounds.Y - 3, layout.RenderDistanceBounds.Width + 6, layout.RenderDistanceBounds.Height + 6, new Rgba(0.16f, 0.13f, 0.18f, 1f));
        DrawRect(commandBuffer, extent, layout.RenderDistanceBounds.X, layout.RenderDistanceBounds.Y, layout.RenderDistanceBounds.Width, layout.RenderDistanceBounds.Height, new Rgba(0.035f, 0.030f, 0.040f, 1f));
        DrawText(commandBuffer, extent, "RENDER DISTANCE", layout.RenderDistanceBounds.X, layout.RenderDistanceBounds.Y - 26, 2, new Rgba(0.70f, 0.78f, 0.76f, 1f));
        var valueText = $"{scene.Hud.MenuRenderDistance} CHUNKS";
        DrawText(commandBuffer, extent, valueText, layout.RenderDistanceBounds.X + 12, layout.RenderDistanceBounds.Y + 10, layout.Scale, new Rgba(0.90f, 1f, 0.86f, 1f));
        DrawSlider(commandBuffer, extent, layout.SliderBounds, scene.Hud.MenuRenderDistance, 1, 12);
        DrawMenuButton(commandBuffer, extent, layout.BackButton.Bounds, "BACK", layout.Scale, scene.Hud.MenuSelectedActionIndex == 0);
    }

    private void DrawSlider(CommandBuffer commandBuffer, Extent2D extent, UiRect bounds, int value, int min, int max)
    {
        DrawRect(commandBuffer, extent, bounds.X, bounds.Y + bounds.Height / 2 - 2, bounds.Width, 4, new Rgba(0.14f, 0.12f, 0.17f, 1f));

        var range = Math.Max(1, max - min);
        var normalized = Math.Clamp((value - min) / (float)range, 0f, 1f);
        var filledWidth = Math.Max(6, (int)MathF.Round(bounds.Width * normalized));
        DrawRect(commandBuffer, extent, bounds.X, bounds.Y + bounds.Height / 2 - 2, filledWidth, 4, new Rgba(0.40f, 0.82f, 0.70f, 1f));

        var knobX = bounds.X + (int)MathF.Round(normalized * bounds.Width);
        DrawRect(commandBuffer, extent, knobX - 5, bounds.Y, 10, bounds.Height, new Rgba(0.90f, 1f, 0.86f, 1f));
    }

    private void DrawWorldSelectionMenu(CommandBuffer commandBuffer, Extent2D extent, RenderScene scene, int width, int height)
    {
        var layout = HudLayout.BuildWorldSelectionMenu(width, height, scene.Hud.MenuWorldNames);
        DrawRect(commandBuffer, extent, layout.ListBounds.X - 4, layout.ListBounds.Y - 4, layout.ListBounds.Width + 8, layout.ListBounds.Height + 8, new Rgba(0.16f, 0.13f, 0.18f, 1f));
        DrawRect(commandBuffer, extent, layout.ListBounds.X, layout.ListBounds.Y, layout.ListBounds.Width, layout.ListBounds.Height, new Rgba(0.030f, 0.026f, 0.036f, 1f));

        if (scene.Hud.MenuWorldNames.Count == 0)
        {
            DrawText(commandBuffer, extent, "NO SAVED WORLDS", (width - TextWidth("NO SAVED WORLDS", 2)) / 2, layout.ListBounds.Y + layout.ListBounds.Height / 2 - 7, 2, new Rgba(0.62f, 0.62f, 0.68f, 1f));
        }
        else
        {
            for (var i = 0; i < layout.WorldRows.Count; i++)
            {
                var row = layout.WorldRows[i];
                var selected = i == scene.Hud.MenuSelectedWorldIndex;
                var text = scene.Hud.MenuWorldNames[i];
                var frame = selected ? new Rgba(0.56f, 0.95f, 0.78f, 1f) : new Rgba(0.12f, 0.10f, 0.14f, 1f);
                DrawRect(commandBuffer, extent, row.Bounds.X - 2, row.Bounds.Y - 2, row.Bounds.Width + 4, row.Bounds.Height + 4, frame);
                DrawRect(commandBuffer, extent, row.Bounds.X, row.Bounds.Y, row.Bounds.Width, row.Bounds.Height, new Rgba(0.045f, 0.040f, 0.052f, 1f));
                var scale = layout.Scale;
                var textWidth = TextWidth(text, scale);
                var drawX = row.Bounds.X + 12;
                if (textWidth > row.Bounds.Width - 24)
                {
                    scale = Math.Max(1, scale - 1);
                }

                DrawText(commandBuffer, extent, text, drawX, row.Bounds.Y + 8, scale, selected ? new Rgba(0.90f, 1f, 0.86f, 1f) : new Rgba(0.72f, 0.72f, 0.76f, 1f));
            }
        }

        var actions = new[] { "CREATE", "LOAD", "DELETE", "BACK" };
        for (var i = 0; i < layout.Buttons.Count; i++)
        {
            DrawMenuButton(commandBuffer, extent, layout.Buttons[i].Bounds, actions[i], layout.Scale, i == scene.Hud.MenuSelectedActionIndex);
        }
    }

    private void DrawCreateWorldMenu(CommandBuffer commandBuffer, Extent2D extent, RenderScene scene, int width, int height)
    {
        var layout = HudLayout.BuildCreateWorldMenu(width, height);
        DrawRect(commandBuffer, extent, layout.NameFieldBounds.X - 3, layout.NameFieldBounds.Y - 3, layout.NameFieldBounds.Width + 6, layout.NameFieldBounds.Height + 6, new Rgba(0.16f, 0.13f, 0.18f, 1f));
        DrawRect(commandBuffer, extent, layout.NameFieldBounds.X, layout.NameFieldBounds.Y, layout.NameFieldBounds.Width, layout.NameFieldBounds.Height, new Rgba(0.035f, 0.030f, 0.040f, 1f));
        DrawText(commandBuffer, extent, "WORLD NAME", layout.NameFieldBounds.X, layout.NameFieldBounds.Y - 26, 2, new Rgba(0.70f, 0.78f, 0.76f, 1f));

        var fieldText = string.IsNullOrWhiteSpace(scene.Hud.CreateWorldName) ? "TYPE NAME..." : scene.Hud.CreateWorldName;
        var fieldColor = string.IsNullOrWhiteSpace(scene.Hud.CreateWorldName)
            ? new Rgba(0.40f, 0.42f, 0.46f, 1f)
            : new Rgba(0.90f, 1f, 0.86f, 1f);
        DrawText(commandBuffer, extent, fieldText, layout.NameFieldBounds.X + 12, layout.NameFieldBounds.Y + 11, layout.Scale, fieldColor);

        var actions = new[] { "CREATE", "BACK" };
        for (var i = 0; i < layout.Buttons.Count; i++)
        {
            DrawMenuButton(commandBuffer, extent, layout.Buttons[i].Bounds, actions[i], layout.Scale, i == scene.Hud.MenuSelectedActionIndex);
        }
    }

    private void DrawMenuButton(CommandBuffer commandBuffer, Extent2D extent, UiRect bounds, string text, int scale, bool selected)
    {
        var frame = selected ? new Rgba(0.94f, 0.78f, 0.42f, 1f) : new Rgba(0.60f, 0.42f, 0.20f, 1f);
        var fill = selected ? new Rgba(0.090f, 0.050f, 0.120f, 0.96f) : new Rgba(0.028f, 0.018f, 0.040f, 0.92f);
        DrawOutlinedPanel(commandBuffer, extent, bounds, frame, fill);
        DrawDiamond(commandBuffer, extent, bounds.X - 10, bounds.Y + bounds.Height / 2, 5, frame);
        DrawDiamond(commandBuffer, extent, bounds.Right + 9, bounds.Y + bounds.Height / 2, 5, frame);
        var textX = bounds.X + (bounds.Width - TextWidth(text, scale)) / 2;
        var textY = bounds.Y + (bounds.Height - 7 * scale) / 2;
        DrawText(commandBuffer, extent, text, textX, textY, scale, selected ? new Rgba(0.96f, 0.93f, 0.84f, 1f) : new Rgba(0.78f, 0.76f, 0.82f, 1f));
    }

    private void DrawProjectedVoxelMeshes(CommandBuffer commandBuffer, Extent2D extent, RenderScene scene, int width, int height)
    {
        var ordered = CollectProjectedFaces(scene, width, height, 3600)
            .OrderBy(face => face.Depth)
            .Take(3600)
            .OrderByDescending(face => face.Depth);

        foreach (var face in ordered)
        {
            DrawRect(commandBuffer, extent, face.X, face.Y, face.Width, face.Height, face.Color);
        }
    }

    private static List<ProjectedFace> CollectProjectedFaces(RenderScene scene, int width, int height, int capacity)
    {
        var forward = Vector3.Normalize(scene.Camera.Forward);
        var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        var up = Vector3.Normalize(Vector3.Cross(forward, right));
        var focalLength = (height * 0.5f) / MathF.Tan(70f * MathF.PI / 360f);
        var faces = new List<ProjectedFace>(capacity);

        foreach (var mesh in scene.ChunkMeshes)
        {
            ProjectMeshFaces(scene.Camera.Position, forward, right, up, focalLength, width, height, mesh, faces);
        }

        return faces;
    }

    private static void ProjectMeshFaces(
        Vector3 cameraPosition,
        Vector3 forward,
        Vector3 right,
        Vector3 up,
        float focalLength,
        int width,
        int height,
        ChunkRenderMesh mesh,
        List<ProjectedFace> faces)
    {
        var vertices = mesh.Vertices;
        for (var i = 0; i + 3 < vertices.Length; i += 4)
        {
            var center = (vertices[i].Position + vertices[i + 1].Position + vertices[i + 2].Position + vertices[i + 3].Position) * 0.25f;
            var toCamera = cameraPosition - center;
            var distance = toCamera.Length();
            if (distance > 96f || Vector3.Dot(vertices[i].Normal, toCamera) <= 0f)
            {
                continue;
            }

            var visible = true;
            var depth = 0f;
            var minX = float.MaxValue;
            var minY = float.MaxValue;
            var maxX = float.MinValue;
            var maxY = float.MinValue;

            for (var corner = 0; corner < 4; corner++)
            {
                if (!ProjectPoint(vertices[i + corner].Position, cameraPosition, forward, right, up, focalLength, width, height, out var projected, out var z))
                {
                    visible = false;
                    break;
                }

                minX = MathF.Min(minX, projected.X);
                minY = MathF.Min(minY, projected.Y);
                maxX = MathF.Max(maxX, projected.X);
                maxY = MathF.Max(maxY, projected.Y);
                depth += z;
            }

            if (!visible)
            {
                continue;
            }

            var rectWidth = (int)MathF.Ceiling(maxX - minX);
            var rectHeight = (int)MathF.Ceiling(maxY - minY);
            if (rectWidth <= 1 || rectHeight <= 1)
            {
                continue;
            }

            faces.Add(new ProjectedFace(
                (int)MathF.Floor(minX),
                (int)MathF.Floor(minY),
                rectWidth,
                rectHeight,
                depth * 0.25f,
                ShadeBlock((BlockType)vertices[i].BlockId, vertices[i].Normal, distance)));
        }
    }

    private static bool ProjectPoint(
        Vector3 point,
        Vector3 cameraPosition,
        Vector3 forward,
        Vector3 right,
        Vector3 up,
        float focalLength,
        int width,
        int height,
        out Vector2 screen,
        out float depth)
    {
        var relative = point - cameraPosition;
        depth = Vector3.Dot(relative, forward);
        if (depth <= 0.15f)
        {
            screen = default;
            return false;
        }

        var x = Vector3.Dot(relative, right);
        var y = Vector3.Dot(relative, up);
        screen = new Vector2(
            width * 0.5f + x * focalLength / depth,
            height * 0.5f - y * focalLength / depth);

        return screen.X > -width && screen.X < width * 2 && screen.Y > -height && screen.Y < height * 2;
    }

    private void DrawCrosshair(CommandBuffer commandBuffer, Extent2D extent, int width, int height, Rgba color)
    {
        var cx = width / 2;
        var cy = height / 2;
        DrawRect(commandBuffer, extent, cx - 12, cy - 1, 9, 2, color);
        DrawRect(commandBuffer, extent, cx + 4, cy - 1, 9, 2, color);
        DrawRect(commandBuffer, extent, cx - 1, cy - 12, 2, 9, color);
        DrawRect(commandBuffer, extent, cx - 1, cy + 4, 2, 9, color);
    }

    private void DrawHotbar(CommandBuffer commandBuffer, Extent2D extent, RenderScene scene, int width, int height)
    {
        var layout = HudLayout.BuildHotbar(width, height);
        for (var i = 0; i < Hotbar.SlotCount; i++)
        {
            DrawInventorySlot(commandBuffer, extent, scene, layout.GetSlotRect(i), scene.Hotbar.Slots[i], i == scene.Hud.SelectedSlot);
        }
    }

    private void DrawInventory(CommandBuffer commandBuffer, Extent2D extent, RenderScene scene, int width, int height)
    {
        var layout = HudLayout.BuildInventory(width, height);
        var panel = layout.PanelBounds;
        DrawRect(commandBuffer, extent, panel.X - 6, panel.Y - 6, panel.Width + 12, panel.Height + 12, new Rgba(0.11f, 0.10f, 0.12f, 0.28f));
        DrawRect(commandBuffer, extent, panel.X - 2, panel.Y - 2, panel.Width + 4, panel.Height + 4, new Rgba(0.78f, 0.78f, 0.82f, 0.72f));
        DrawRect(commandBuffer, extent, panel.X, panel.Y, panel.Width, panel.Height, new Rgba(0.24f, 0.24f, 0.27f, 0.42f));

        DrawText(commandBuffer, extent, "INVENTORY", layout.TitleBounds.X, layout.TitleBounds.Y, 2, new Rgba(0.93f, 0.93f, 0.89f, 1f));

        for (var i = 0; i < layout.MainSlots.Count; i++)
        {
            DrawInventorySlot(commandBuffer, extent, scene, layout.MainSlots[i], scene.Hud.InventorySlots[i], false);
        }

        var hotbarLabel = "HOTBAR";
        var hotbarLabelY = layout.HotbarSlots[0].Y - 18;
        DrawText(commandBuffer, extent, hotbarLabel, layout.HotbarSlots[0].X, hotbarLabelY, 2, new Rgba(0.78f, 0.80f, 0.82f, 1f));

        for (var i = 0; i < layout.HotbarSlots.Count; i++)
        {
            DrawInventorySlot(commandBuffer, extent, scene, layout.HotbarSlots[i], scene.Hotbar.Slots[i], i == scene.Hud.SelectedSlot);
        }

        if (!scene.Hud.CursorSlot.IsEmpty)
        {
            var mouseX = (int)scene.Hud.MousePosition.X - layout.SlotSize / 2;
            var mouseY = (int)scene.Hud.MousePosition.Y - layout.SlotSize / 2;
            DrawInventorySlot(
                commandBuffer,
                extent,
                scene,
                new UiRect(mouseX, mouseY, layout.SlotSize, layout.SlotSize),
                scene.Hud.CursorSlot,
                false);
        }
    }

    private void DrawInventorySlot(CommandBuffer commandBuffer, Extent2D extent, RenderScene scene, UiRect bounds, HotbarSlot hotbarSlot, bool selected)
    {
        var frame = selected ? new Rgba(0.92f, 0.92f, 0.78f, 1f) : new Rgba(0.12f, 0.10f, 0.13f, 1f);
        DrawRect(commandBuffer, extent, bounds.X - 2, bounds.Y - 2, bounds.Width + 4, bounds.Height + 4, frame);
        DrawRect(commandBuffer, extent, bounds.X, bounds.Y, bounds.Width, bounds.Height, new Rgba(0.025f, 0.021f, 0.028f, 1f));
        DrawRect(commandBuffer, extent, bounds.X + 2, bounds.Y + 2, Math.Max(1, bounds.Width - 4), Math.Max(1, bounds.Height - 4), new Rgba(0.12f, 0.12f, 0.13f, 1f));
        if (!hotbarSlot.IsEmpty)
        {
            DrawHotbarBlock(commandBuffer, extent, bounds.X, bounds.Y, bounds.Width, hotbarSlot.Block, selected);
            DrawHotbarCount(commandBuffer, extent, bounds.X, bounds.Y, bounds.Width, hotbarSlot.Count);
        }
    }

    private void DrawHotbarBlock(CommandBuffer commandBuffer, Extent2D extent, int x, int y, int slot, BlockType block, bool selected)
    {
        var clipBounds = new UiRect(x + 2, y + 2, Math.Max(1, slot - 4), Math.Max(1, slot - 4));
        DrawItemOrBlockPreview(commandBuffer, extent, block, clipBounds, slot, -0.74f, 0.60f);
    }

    private void DrawHotbarCount(CommandBuffer commandBuffer, Extent2D extent, int x, int y, int slot, int count)
    {
        var text = count.ToString();
        var scale = count >= 100 ? 1 : 2;
        var textWidth = TextWidth(text, scale);
        var textX = x + slot - textWidth - 4;
        var textY = y + slot - 7 * scale - 3;

        DrawText(commandBuffer, extent, text, textX + 1, textY + 1, scale, new Rgba(0.02f, 0.02f, 0.03f, 1f));
        DrawText(commandBuffer, extent, text, textX, textY, scale, new Rgba(0.96f, 0.96f, 0.92f, 1f));
    }

    private static ProjectedVertex ProjectCubeVertex(Vector3 position, Vector2 center, float scale, float yaw, float pitch)
    {
        var rotated = Vector3.Transform(position, Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateRotationX(pitch));
        var screen = new Vector2(
            center.X + rotated.X * scale,
            center.Y - rotated.Y * scale);

        return new ProjectedVertex(screen, rotated.Z);
    }

    private void DrawTexturedQuad(CommandBuffer commandBuffer, Extent2D extent, UiRect clipBounds, TextureTile texture, ProjectedVertex topLeft, ProjectedVertex topRight, ProjectedVertex bottomRight, ProjectedVertex bottomLeft)
    {
        DrawTexturedQuad(commandBuffer, extent, clipBounds, texture, topLeft, topRight, bottomRight, bottomLeft, new Vector2(0f, 0f), new Vector2(1f, 1f));
    }

    private void DrawTexturedQuad(
        CommandBuffer commandBuffer,
        Extent2D extent,
        UiRect clipBounds,
        TextureTile texture,
        ProjectedVertex topLeft,
        ProjectedVertex topRight,
        ProjectedVertex bottomRight,
        ProjectedVertex bottomLeft,
        Vector2 uvMin,
        Vector2 uvMax)
    {
        DrawTexturedTriangle(
            commandBuffer,
            extent,
            clipBounds,
            texture,
            new TexturedVertex(topLeft.Position, new Vector2(uvMin.X, uvMin.Y)),
            new TexturedVertex(topRight.Position, new Vector2(uvMax.X, uvMin.Y)),
            new TexturedVertex(bottomRight.Position, new Vector2(uvMax.X, uvMax.Y)));

        DrawTexturedTriangle(
            commandBuffer,
            extent,
            clipBounds,
            texture,
            new TexturedVertex(topLeft.Position, new Vector2(uvMin.X, uvMin.Y)),
            new TexturedVertex(bottomRight.Position, new Vector2(uvMax.X, uvMax.Y)),
            new TexturedVertex(bottomLeft.Position, new Vector2(uvMin.X, uvMax.Y)));
    }

    private void DrawTexturedTriangle(CommandBuffer commandBuffer, Extent2D extent, UiRect clipBounds, TextureTile texture, TexturedVertex a, TexturedVertex b, TexturedVertex c)
    {
        var minX = Math.Max(clipBounds.X, (int)MathF.Floor(MathF.Min(a.Position.X, MathF.Min(b.Position.X, c.Position.X))));
        var maxX = Math.Min(clipBounds.X + clipBounds.Width - 1, (int)MathF.Ceiling(MathF.Max(a.Position.X, MathF.Max(b.Position.X, c.Position.X))));
        var minY = Math.Max(clipBounds.Y, (int)MathF.Floor(MathF.Min(a.Position.Y, MathF.Min(b.Position.Y, c.Position.Y))));
        var maxY = Math.Min(clipBounds.Y + clipBounds.Height - 1, (int)MathF.Ceiling(MathF.Max(a.Position.Y, MathF.Max(b.Position.Y, c.Position.Y))));

        var area = Edge(a.Position, b.Position, c.Position);
        if (MathF.Abs(area) < 0.001f || minX > maxX || minY > maxY)
        {
            return;
        }

        for (var py = minY; py <= maxY; py++)
        {
            for (var px = minX; px <= maxX; px++)
            {
                var p = new Vector2(px + 0.5f, py + 0.5f);
                var w0 = Edge(b.Position, c.Position, p);
                var w1 = Edge(c.Position, a.Position, p);
                var w2 = Edge(a.Position, b.Position, p);
                var hasNegative = w0 < 0f || w1 < 0f || w2 < 0f;
                var hasPositive = w0 > 0f || w1 > 0f || w2 > 0f;
                if (hasNegative && hasPositive)
                {
                    continue;
                }

                w0 /= area;
                w1 /= area;
                w2 /= area;

                var uv = a.Uv * w0 + b.Uv * w1 + c.Uv * w2;
                var color = texture.Sample(uv.X, uv.Y);
                if (color.A <= 0.01f)
                {
                    continue;
                }

                DrawRect(commandBuffer, extent, px, py, 1, 1, color);
            }
        }
    }

    private static float Edge(Vector2 a, Vector2 b, Vector2 p)
    {
        return (p.X - a.X) * (b.Y - a.Y) - (p.Y - a.Y) * (b.X - a.X);
    }

    private TextureTile GetBlockTile(int textureIndex)
    {
        return _blockTiles.TryGetValue(textureIndex, out var tile)
            ? tile
            : TextureTile.Solid(new Rgba32(255, 0, 255, 255));
    }

    private static Dictionary<int, TextureTile> LoadBlockTiles()
    {
        var rootDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "textures");
        var tiles = new Dictionary<int, TextureTile>();

        foreach (var entry in BlockTextureAtlasBuilder.GetEntries())
        {
            var path = Path.Combine(rootDirectory, entry.FileName);
            if (!File.Exists(path))
            {
                continue;
            }

            using var source = ImageSharpImage.Load<Rgba32>(path);
            source.Mutate(context => context.Resize(new ResizeOptions
            {
                Size = new SixLabors.ImageSharp.Size(BlockTextureAtlas.TileSize, BlockTextureAtlas.TileSize),
                Sampler = KnownResamplers.NearestNeighbor
            }));

            var pixels = new Rgba32[BlockTextureAtlas.TileSize * BlockTextureAtlas.TileSize];
            source.CopyPixelDataTo(pixels);
            tiles[entry.Index] = new TextureTile(BlockTextureAtlas.TileSize, BlockTextureAtlas.TileSize, pixels);
        }

        return tiles;
    }

    private static TextureTile LoadPlayerSkinTile()
    {
        using var source = PlayerSkinStore.LoadAppliedSkin() ?? LoadEmbeddedPlayerSkin();
        source.Mutate(context => context.Resize(new ResizeOptions
        {
            Size = new SixLabors.ImageSharp.Size(BlockTextureAtlas.PlayerSkinWidth, BlockTextureAtlas.PlayerSkinHeight),
            Sampler = KnownResamplers.NearestNeighbor
        }));

        var pixels = new Rgba32[BlockTextureAtlas.PlayerSkinWidth * BlockTextureAtlas.PlayerSkinHeight];
        source.CopyPixelDataTo(pixels);
        return new TextureTile(BlockTextureAtlas.PlayerSkinWidth, BlockTextureAtlas.PlayerSkinHeight, pixels);
    }

    private static string ResolveAssetPath(string relativePath)
    {
        var preferred = Path.Combine(AppContext.BaseDirectory, "Assets", relativePath);
        if (File.Exists(preferred))
        {
            return preferred;
        }

        return Path.Combine(AppContext.BaseDirectory, "assets", relativePath);
    }

    private static SixLabors.ImageSharp.Image<Rgba32> LoadEmbeddedPlayerSkin()
    {
        var modelPath = Path.Combine(AppContext.BaseDirectory, "Assets", "models", "playermodel.gltf");
        if (!File.Exists(modelPath))
        {
            modelPath = Path.Combine(AppContext.BaseDirectory, "assets", "models", "playermodel.gltf");
        }

        var file = File.ReadAllText(modelPath);
        var marker = "\"uri\":\"data:image/png;base64,";
        var markerIndex = file.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            throw new InvalidOperationException("Player skin image was not found in playermodel.gltf.");
        }

        var startIndex = markerIndex + marker.Length;
        var endIndex = file.IndexOf('"', startIndex);
        return ImageSharpImage.Load<Rgba32>(Convert.FromBase64String(file[startIndex..endIndex]));
    }

    private void DrawHeldBlockViewModel(CommandBuffer commandBuffer, Extent2D extent, RenderScene scene, int width, int height)
    {
        var block = scene.Hud.SelectedBlock;
        if (block == BlockType.Air || scene.Hud.ShowMenu)
        {
            return;
        }

        var size = Math.Clamp(width / 4, 180, 320);
        var clipBounds = new UiRect(0, 0, width, height);
        DrawHeldItemOrBlockPreview(commandBuffer, extent, block, clipBounds, width, height, size);
    }

    private void DrawItemOrBlockPreview(CommandBuffer commandBuffer, Extent2D extent, BlockType block, UiRect clipBounds, int slotSize, float yaw, float pitch)
    {
        if (_blocks.IsPlaceable(block))
        {
            var center = new Vector2(clipBounds.X + clipBounds.Width * 0.50f, clipBounds.Y + clipBounds.Height * 0.58f);
            var scale = Math.Clamp(slotSize * 0.30f, 10f, 16f);
            DrawBlockPreview(commandBuffer, extent, block, clipBounds, center, scale, yaw, pitch);
            return;
        }

        DrawFlatItemPreview(commandBuffer, extent, block, clipBounds, slotSize);
    }

    private void DrawHeldItemOrBlockPreview(CommandBuffer commandBuffer, Extent2D extent, BlockType block, UiRect clipBounds, int width, int height, int size)
    {
        if (_blocks.IsPlaceable(block))
        {
            var center = new Vector2(width * 0.5f, height * 0.58f);
            var scale = size * 0.55f;
            DrawBlockPreview(commandBuffer, extent, block, clipBounds, center, scale, -0.78f, 0.52f);
            return;
        }

        var itemSize = (int)(size * 0.72f);
        var itemBounds = new UiRect(
            (int)(width * 0.5f - itemSize * 0.5f),
            (int)(height * 0.50f - itemSize * 0.5f),
            itemSize,
            itemSize);
        DrawTextureContain(commandBuffer, extent, itemBounds, GetBlockTile(_blocks.GetFaceTextureIndex(block, Vector3.UnitY)));
    }

    private void DrawFlatItemPreview(CommandBuffer commandBuffer, Extent2D extent, BlockType block, UiRect clipBounds, int slotSize)
    {
        var inset = Math.Max(3, slotSize / 10);
        var itemBounds = new UiRect(
            clipBounds.X + inset,
            clipBounds.Y + inset,
            Math.Max(1, clipBounds.Width - inset * 2),
            Math.Max(1, clipBounds.Height - inset * 2));
        DrawTextureContain(commandBuffer, extent, itemBounds, GetBlockTile(_blocks.GetFaceTextureIndex(block, Vector3.UnitY)));
    }

    private void DrawBlockPreview(CommandBuffer commandBuffer, Extent2D extent, BlockType block, UiRect clipBounds, Vector2 center, float scale, float yaw, float pitch)
    {
        var definition = _blocks[block];
        var topTexture = GetBlockTile(definition.TopTextureIndex);
        var frontTexture = GetBlockTile(definition.SideTextureIndex);
        var sideTexture = GetBlockTile(definition.SideTextureIndex);
        var vertices = new[]
        {
            ProjectCubeVertex(new Vector3(-1f, -1f, -1f), center, scale, yaw, pitch),
            ProjectCubeVertex(new Vector3(1f, -1f, -1f), center, scale, yaw, pitch),
            ProjectCubeVertex(new Vector3(1f, 1f, -1f), center, scale, yaw, pitch),
            ProjectCubeVertex(new Vector3(-1f, 1f, -1f), center, scale, yaw, pitch),
            ProjectCubeVertex(new Vector3(-1f, -1f, 1f), center, scale, yaw, pitch),
            ProjectCubeVertex(new Vector3(1f, -1f, 1f), center, scale, yaw, pitch),
            ProjectCubeVertex(new Vector3(1f, 1f, 1f), center, scale, yaw, pitch),
            ProjectCubeVertex(new Vector3(-1f, 1f, 1f), center, scale, yaw, pitch)
        };

        DrawTexturedQuad(commandBuffer, extent, clipBounds, topTexture, vertices[3], vertices[2], vertices[6], vertices[7]);
        DrawTexturedQuad(commandBuffer, extent, clipBounds, sideTexture, vertices[2], vertices[1], vertices[5], vertices[6]);
        DrawTexturedQuad(commandBuffer, extent, clipBounds, frontTexture, vertices[7], vertices[6], vertices[5], vertices[4]);
    }

    private void DrawRect(CommandBuffer commandBuffer, Extent2D extent, int x, int y, int width, int height, Rgba color)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var framebufferWidth = (int)extent.Width;
        var framebufferHeight = (int)extent.Height;
        var clampedX = Math.Clamp(x, 0, framebufferWidth);
        var clampedY = Math.Clamp(y, 0, framebufferHeight);
        var clampedRight = Math.Clamp(x + width, 0, framebufferWidth);
        var clampedBottom = Math.Clamp(y + height, 0, framebufferHeight);
        var clampedWidth = clampedRight - clampedX;
        var clampedHeight = clampedBottom - clampedY;
        if (clampedWidth <= 0 || clampedHeight <= 0)
        {
            return;
        }

        var clearValue = new ClearValue
        {
            Color = new ClearColorValue(color.R, color.G, color.B, color.A)
        };

        var attachment = new ClearAttachment
        {
            AspectMask = ImageAspectFlags.ColorBit,
            ColorAttachment = 0,
            ClearValue = clearValue
        };

        var rect = new ClearRect
        {
            Rect = new Rect2D(new Offset2D(clampedX, clampedY), new Extent2D((uint)clampedWidth, (uint)clampedHeight)),
            BaseArrayLayer = 0,
            LayerCount = 1
        };

        _vk.CmdClearAttachments(commandBuffer, 1, &attachment, 1, &rect);
    }

    private void DrawOutlinedPanel(CommandBuffer commandBuffer, Extent2D extent, UiRect bounds, Rgba borderColor, Rgba fillColor)
    {
        DrawRect(commandBuffer, extent, bounds.X - 4, bounds.Y - 4, bounds.Width + 8, bounds.Height + 8, new Rgba(0.04f, 0.02f, 0.06f, 0.94f));
        DrawRect(commandBuffer, extent, bounds.X - 1, bounds.Y - 1, bounds.Width + 2, bounds.Height + 2, borderColor);
        DrawRect(commandBuffer, extent, bounds.X, bounds.Y, bounds.Width, bounds.Height, fillColor);
        DrawRect(commandBuffer, extent, bounds.X + 1, bounds.Y + 1, bounds.Width - 2, 1, new Rgba(0.20f, 0.12f, 0.24f, 1f));
        DrawRect(commandBuffer, extent, bounds.X + 1, bounds.Bottom - 2, bounds.Width - 2, 1, new Rgba(0.02f, 0.01f, 0.04f, 1f));
    }

    private void DrawTextureContain(CommandBuffer commandBuffer, Extent2D extent, UiRect bounds, TextureTile texture)
    {
        var textureAspect = texture.Width / (float)texture.Height;
        var boundsAspect = bounds.Width / (float)bounds.Height;
        int drawWidth;
        int drawHeight;
        int drawX;
        int drawY;

        if (textureAspect > boundsAspect)
        {
            drawWidth = bounds.Width;
            drawHeight = (int)MathF.Round(drawWidth / textureAspect);
            drawX = bounds.X;
            drawY = bounds.Y + (bounds.Height - drawHeight) / 2;
        }
        else
        {
            drawHeight = bounds.Height;
            drawWidth = (int)MathF.Round(drawHeight * textureAspect);
            drawX = bounds.X + (bounds.Width - drawWidth) / 2;
            drawY = bounds.Y;
        }

        DrawTexturedQuad(
            commandBuffer,
            extent,
            new UiRect(drawX, drawY, drawWidth, drawHeight),
            texture,
            new ProjectedVertex(new Vector2(drawX, drawY), 0f),
            new ProjectedVertex(new Vector2(drawX + drawWidth, drawY), 0f),
            new ProjectedVertex(new Vector2(drawX + drawWidth, drawY + drawHeight), 0f),
            new ProjectedVertex(new Vector2(drawX, drawY + drawHeight), 0f));
    }

    private void DrawTextureCover(CommandBuffer commandBuffer, Extent2D extent, UiRect bounds, TextureTile texture)
    {
        var textureAspect = texture.Width / (float)texture.Height;
        var boundsAspect = bounds.Width / (float)bounds.Height;
        var uvMin = Vector2.Zero;
        var uvMax = Vector2.One;

        if (textureAspect > boundsAspect)
        {
            var visibleWidth = boundsAspect / textureAspect;
            var inset = (1f - visibleWidth) * 0.5f;
            uvMin.X = inset;
            uvMax.X = 1f - inset;
        }
        else
        {
            var visibleHeight = textureAspect / boundsAspect;
            var inset = (1f - visibleHeight) * 0.5f;
            uvMin.Y = inset;
            uvMax.Y = 1f - inset;
        }

        DrawTexturedQuad(
            commandBuffer,
            extent,
            bounds,
            texture,
            new ProjectedVertex(new Vector2(bounds.X, bounds.Y), 0f),
            new ProjectedVertex(new Vector2(bounds.Right, bounds.Y), 0f),
            new ProjectedVertex(new Vector2(bounds.Right, bounds.Bottom), 0f),
            new ProjectedVertex(new Vector2(bounds.X, bounds.Bottom), 0f),
            uvMin,
            uvMax);
    }


    private void DrawDiamond(CommandBuffer commandBuffer, Extent2D extent, int centerX, int centerY, int radius, Rgba color)
    {
        for (var offsetY = -radius; offsetY <= radius; offsetY++)
        {
            var rowRadius = radius - Math.Abs(offsetY);
            DrawRect(commandBuffer, extent, centerX - rowRadius, centerY + offsetY, rowRadius * 2 + 1, 1, color);
        }
    }

    private void DrawCornerRunes(CommandBuffer commandBuffer, Extent2D extent, UiRect bounds, Rgba color, int size)
    {
        DrawDiamond(commandBuffer, extent, bounds.X, bounds.Y, size / 2, color);
        DrawDiamond(commandBuffer, extent, bounds.Right, bounds.Y, size / 2, color);
        DrawDiamond(commandBuffer, extent, bounds.X, bounds.Bottom, size / 2, color);
        DrawDiamond(commandBuffer, extent, bounds.Right, bounds.Bottom, size / 2, color);
    }

    private void DrawMagicRing(CommandBuffer commandBuffer, Extent2D extent, Vector2 center, int radius, Rgba color)
    {
        for (var step = 0; step < 64; step++)
        {
            var angle = step / 64f * MathF.Tau;
            var px = (int)MathF.Round(center.X + MathF.Cos(angle) * radius);
            var py = (int)MathF.Round(center.Y + MathF.Sin(angle) * (radius * 0.34f));
            DrawRect(commandBuffer, extent, px, py, 2, 2, color);
        }
    }

    private void DrawText(CommandBuffer commandBuffer, Extent2D extent, string text, int x, int y, int scale, Rgba color)
    {
        var cursor = x;
        foreach (var character in text.ToUpperInvariant())
        {
            if (character == ' ')
            {
                cursor += 4 * scale;
                continue;
            }

            var glyph = Glyph(character);
            for (var row = 0; row < glyph.Length; row++)
            {
                var bits = glyph[row];
                for (var column = 0; column < 5; column++)
                {
                    if ((bits & (1 << (4 - column))) != 0)
                    {
                        DrawRect(commandBuffer, extent, cursor + column * scale, y + row * scale, scale, scale, color);
                    }
                }
            }

            cursor += 6 * scale;
        }
    }

    private static int TextWidth(string text, int scale)
    {
        var width = 0;
        foreach (var character in text)
        {
            width += character == ' ' ? 4 * scale : 6 * scale;
        }

        return Math.Max(0, width - scale);
    }

    private static byte[] Glyph(char character)
    {
        return character switch
        {
            'A' => [0b01110, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001],
            'B' => [0b11110, 0b10001, 0b10001, 0b11110, 0b10001, 0b10001, 0b11110],
            'C' => [0b01110, 0b10001, 0b10000, 0b10000, 0b10000, 0b10001, 0b01110],
            'D' => [0b11110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b11110],
            'E' => [0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b11111],
            'F' => [0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b10000],
            'G' => [0b01110, 0b10001, 0b10000, 0b10111, 0b10001, 0b10001, 0b01111],
            'H' => [0b10001, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001],
            'I' => [0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b11111],
            'J' => [0b00111, 0b00010, 0b00010, 0b00010, 0b10010, 0b10010, 0b01100],
            'K' => [0b10001, 0b10010, 0b10100, 0b11000, 0b10100, 0b10010, 0b10001],
            'L' => [0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b11111],
            'M' => [0b10001, 0b11011, 0b10101, 0b10101, 0b10001, 0b10001, 0b10001],
            'N' => [0b10001, 0b11001, 0b10101, 0b10011, 0b10001, 0b10001, 0b10001],
            'O' => [0b01110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110],
            'P' => [0b11110, 0b10001, 0b10001, 0b11110, 0b10000, 0b10000, 0b10000],
            'Q' => [0b01110, 0b10001, 0b10001, 0b10001, 0b10101, 0b10010, 0b01101],
            'R' => [0b11110, 0b10001, 0b10001, 0b11110, 0b10100, 0b10010, 0b10001],
            'S' => [0b01111, 0b10000, 0b10000, 0b01110, 0b00001, 0b00001, 0b11110],
            'T' => [0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100],
            'U' => [0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110],
            'V' => [0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01010, 0b00100],
            'W' => [0b10001, 0b10001, 0b10001, 0b10101, 0b10101, 0b10101, 0b01010],
            'X' => [0b10001, 0b10001, 0b01010, 0b00100, 0b01010, 0b10001, 0b10001],
            'Y' => [0b10001, 0b10001, 0b01010, 0b00100, 0b00100, 0b00100, 0b00100],
            'Z' => [0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b10000, 0b11111],
            '0' => [0b01110, 0b10001, 0b10011, 0b10101, 0b11001, 0b10001, 0b01110],
            '1' => [0b00100, 0b01100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110],
            '2' => [0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b01000, 0b11111],
            '3' => [0b11110, 0b00001, 0b00001, 0b01110, 0b00001, 0b00001, 0b11110],
            '4' => [0b00010, 0b00110, 0b01010, 0b10010, 0b11111, 0b00010, 0b00010],
            '5' => [0b11111, 0b10000, 0b10000, 0b11110, 0b00001, 0b00001, 0b11110],
            '6' => [0b01110, 0b10000, 0b10000, 0b11110, 0b10001, 0b10001, 0b01110],
            '7' => [0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b01000, 0b01000],
            '8' => [0b01110, 0b10001, 0b10001, 0b01110, 0b10001, 0b10001, 0b01110],
            '9' => [0b01110, 0b10001, 0b10001, 0b01111, 0b00001, 0b00001, 0b01110],
            '-' => [0b00000, 0b00000, 0b00000, 0b11111, 0b00000, 0b00000, 0b00000],
            '.' => [0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b01100, 0b01100],
            '_' => [0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b11111],
            'А' => [0b01110, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001],
            'Б' => [0b11111, 0b10000, 0b10000, 0b11110, 0b10001, 0b10001, 0b11110],
            'В' => [0b11110, 0b10001, 0b10001, 0b11110, 0b10001, 0b10001, 0b11110],
            'Г' => [0b11111, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000],
            'Д' => [0b00111, 0b01001, 0b01001, 0b01001, 0b11111, 0b10001, 0b10001],
            'Е' => [0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b11111],
            'Ё' => [0b01010, 0b00000, 0b11111, 0b10000, 0b11110, 0b10000, 0b11111],
            'Ж' => [0b10101, 0b10101, 0b01110, 0b00100, 0b01110, 0b10101, 0b10101],
            'З' => [0b01110, 0b10001, 0b00001, 0b00110, 0b00001, 0b10001, 0b01110],
            'И' => [0b10001, 0b11001, 0b10101, 0b10011, 0b10001, 0b10001, 0b10001],
            'Й' => [0b01010, 0b00100, 0b10001, 0b11001, 0b10101, 0b10011, 0b10001],
            'К' => [0b10001, 0b10010, 0b10100, 0b11000, 0b10100, 0b10010, 0b10001],
            'Л' => [0b00111, 0b01001, 0b01001, 0b01001, 0b01001, 0b01001, 0b10001],
            'М' => [0b10001, 0b11011, 0b10101, 0b10101, 0b10001, 0b10001, 0b10001],
            'Н' => [0b10001, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001],
            'О' => [0b01110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110],
            'П' => [0b11111, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001],
            'Р' => [0b11110, 0b10001, 0b10001, 0b11110, 0b10000, 0b10000, 0b10000],
            'С' => [0b01110, 0b10001, 0b10000, 0b10000, 0b10000, 0b10001, 0b01110],
            'Т' => [0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100],
            'У' => [0b10001, 0b10001, 0b10001, 0b01111, 0b00001, 0b10001, 0b01110],
            'Ф' => [0b00100, 0b01110, 0b10101, 0b10101, 0b10101, 0b01110, 0b00100],
            'Х' => [0b10001, 0b10001, 0b01010, 0b00100, 0b01010, 0b10001, 0b10001],
            'Ц' => [0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b11111],
            'Ч' => [0b10001, 0b10001, 0b10001, 0b01111, 0b00001, 0b00001, 0b00001],
            'Ш' => [0b10101, 0b10101, 0b10101, 0b10101, 0b10101, 0b10101, 0b11111],
            'Щ' => [0b10101, 0b10101, 0b10101, 0b10101, 0b10101, 0b10101, 0b11111],
            'Ъ' => [0b11000, 0b01000, 0b01000, 0b01110, 0b01001, 0b01001, 0b01110],
            'Ы' => [0b10001, 0b10001, 0b10001, 0b11101, 0b10011, 0b10011, 0b11101],
            'Ь' => [0b10000, 0b10000, 0b10000, 0b11110, 0b10001, 0b10001, 0b11110],
            'Э' => [0b01110, 0b10001, 0b00001, 0b00111, 0b00001, 0b10001, 0b01110],
            'Ю' => [0b10010, 0b10101, 0b10101, 0b11101, 0b10101, 0b10101, 0b10010],
            'Я' => [0b01111, 0b10001, 0b10001, 0b01111, 0b00101, 0b01001, 0b10001],
            _ => [0b11111, 0b00001, 0b00010, 0b00100, 0b00000, 0b00100, 0b00100]
        };
    }

    private static Rgba BlockColor(BlockType block)
    {
        return block switch
        {
            BlockType.Grass => new Rgba(0.28f, 0.56f, 0.22f, 1f),
            BlockType.Dirt => new Rgba(0.42f, 0.28f, 0.16f, 1f),
            BlockType.Stone => new Rgba(0.45f, 0.46f, 0.48f, 1f),
            BlockType.Sand => new Rgba(0.72f, 0.65f, 0.42f, 1f),
            BlockType.Water => new Rgba(0.16f, 0.32f, 0.58f, 1f),
            BlockType.CorruptedGrass => new Rgba(0.18f, 0.08f, 0.22f, 1f),
            BlockType.ArcaneCrystal => new Rgba(0.18f, 0.82f, 0.74f, 1f),
            BlockType.MagicOre => new Rgba(0.26f, 0.18f, 0.55f, 1f),
            BlockType.Snow => new Rgba(0.92f, 0.94f, 0.98f, 1f),
            _ => new Rgba(0.04f, 0.04f, 0.045f, 1f)
        };
    }

    private static Rgba ShadeBlock(BlockType block, Vector3 normal, float distance)
    {
        var baseColor = BlockColor(block);
        var faceLight = normal.Y > 0.5f ? 1.15f : normal.Y < -0.5f ? 0.45f : 0.72f;
        var fog = Math.Clamp(1f - distance / 110f, 0.35f, 1f);
        var emissive = block is BlockType.ArcaneCrystal or BlockType.MagicOre ? 0.25f : 0f;
        var shade = faceLight * fog + emissive;
        return new Rgba(
            Math.Clamp(baseColor.R * shade, 0f, 1f),
            Math.Clamp(baseColor.G * shade, 0f, 1f),
            Math.Clamp(baseColor.B * shade, 0f, 1f),
            1f);
    }

    private static Rgba Mix(Rgba source, Rgba target, float targetWeight)
    {
        var sourceWeight = 1f - targetWeight;
        return new Rgba(
            source.R * sourceWeight + target.R * targetWeight,
            source.G * sourceWeight + target.G * targetWeight,
            source.B * sourceWeight + target.B * targetWeight,
            1f);
    }

    private static Rgba Scale(Rgba color, float factor)
    {
        return new Rgba(
            Math.Clamp(color.R * factor, 0f, 1f),
            Math.Clamp(color.G * factor, 0f, 1f),
            Math.Clamp(color.B * factor, 0f, 1f),
            color.A);
    }

    private struct BackdropSample
    {
        public float R;
        public float G;
        public float B;
        public float Weight;

        public void Accumulate(Rgba color, float weight)
        {
            R += color.R * weight;
            G += color.G * weight;
            B += color.B * weight;
            Weight += weight;
        }

        public Rgba ToColor()
        {
            if (Weight <= 0f)
            {
                return new Rgba(0f, 0f, 0f, 1f);
            }

            return new Rgba(R / Weight, G / Weight, B / Weight, 1f);
        }
    }

    private readonly record struct Rgba(float R, float G, float B, float A);
    private readonly record struct ProjectedFace(int X, int Y, int Width, int Height, float Depth, Rgba Color);

    private readonly record struct TextureTile(int Width, int Height, Rgba32[] Pixels)
    {
        public static TextureTile Solid(Rgba32 color)
        {
            return new TextureTile(1, 1, [color]);
        }

        public Rgba Sample(float u, float v)
        {
            var sampleX = Math.Clamp((int)MathF.Round(u * (Width - 1)), 0, Width - 1);
            var sampleY = Math.Clamp((int)MathF.Round(v * (Height - 1)), 0, Height - 1);
            var pixel = Pixels[sampleY * Width + sampleX];
            return new Rgba(
                pixel.R / 255f,
                pixel.G / 255f,
                pixel.B / 255f,
                pixel.A / 255f);
        }
    }

    private readonly record struct ProjectedVertex(Vector2 Position, float Depth);
    private readonly record struct TexturedVertex(Vector2 Position, Vector2 Uv);
    private readonly record struct ProjectedTexturedTriangle(TexturedVertex A, TexturedVertex B, TexturedVertex C, float Depth);
}
