using System.Numerics;
using Silk.NET.Vulkan;
using VoxelGame.World.Blocks;
using VoxelGame.World.Chunks;

namespace VoxelGame.Rendering.Vulkan;

internal unsafe sealed class VulkanImmediatePreview
{
    private readonly Vk _vk;

    public VulkanImmediatePreview(Vk vk)
    {
        _vk = vk;
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
        if (scene.Hud.ShowMainMenu)
        {
            DrawMainMenu(commandBuffer, extent, scene, width, height);
            return;
        }

        var pulse = scene.Hud.SelectedBlock is BlockType.ArcaneCrystal or BlockType.MagicOre ? 0.18f : 0.0f;
        DrawCrosshair(commandBuffer, extent, width, height, new Rgba(0.72f + pulse, 0.94f, 0.88f, 1f));
        DrawHotbar(commandBuffer, extent, scene, width, height);
        DrawHeldItem(commandBuffer, extent, scene.Hud.SelectedBlock, width, height);
    }

    private void DrawMainMenu(CommandBuffer commandBuffer, Extent2D extent, RenderScene scene, int width, int height)
    {
        DrawRect(commandBuffer, extent, 0, 0, width, height, new Rgba(0.025f, 0.018f, 0.035f, 0.92f));

        var scale = Math.Clamp(width / 320, 3, 5);
        var title = "VOXELGAME";
        DrawText(commandBuffer, extent, title, (width - TextWidth(title, scale)) / 2, height / 5, scale, new Rgba(0.62f, 0.96f, 0.82f, 1f));

        var options = HudLayout.BuildMainMenuOptions(scene.Hud.MainMenuRenderDistance);
        var layout = HudLayout.BuildMainMenu(width, height, scene.Hud.MainMenuRenderDistance);
        var menuScale = layout.Scale;

        for (var i = 0; i < options.Length; i++)
        {
            var selected = i == scene.Hud.MainMenuSelectedIndex;
            var text = options[i];
            var textWidth = TextWidth(text, menuScale);
            var bounds = layout.Items[i].Bounds;
            var boxWidth = bounds.Width;
            var boxX = bounds.X;
            var boxY = bounds.Y;
            var frame = selected ? new Rgba(0.56f, 0.95f, 0.78f, 1f) : new Rgba(0.16f, 0.13f, 0.18f, 1f);
            DrawRect(commandBuffer, extent, boxX - 3, boxY - 3, boxWidth + 6, 38, frame);
            DrawRect(commandBuffer, extent, boxX, boxY, boxWidth, 32, new Rgba(0.035f, 0.030f, 0.040f, 1f));
            DrawText(commandBuffer, extent, text, (width - textWidth) / 2, boxY + 8, menuScale, selected ? new Rgba(0.90f, 1f, 0.86f, 1f) : new Rgba(0.66f, 0.66f, 0.70f, 1f));
        }
    }

    private void DrawProjectedVoxelMeshes(CommandBuffer commandBuffer, Extent2D extent, RenderScene scene, int width, int height)
    {
        var forward = Vector3.Normalize(scene.Camera.Forward);
        var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        var up = Vector3.Normalize(Vector3.Cross(forward, right));
        var focalLength = (height * 0.5f) / MathF.Tan(70f * MathF.PI / 360f);
        var faces = new List<ProjectedFace>(2048);

        foreach (var mesh in scene.ChunkMeshes)
        {
            ProjectMeshFaces(scene.Camera.Position, forward, right, up, focalLength, width, height, mesh, faces);
        }

        const int maxFaces = 3600;
        var ordered = faces
            .OrderBy(face => face.Depth)
            .Take(maxFaces)
            .OrderByDescending(face => face.Depth);

        foreach (var face in ordered)
        {
            DrawRect(commandBuffer, extent, face.X, face.Y, face.Width, face.Height, face.Color);
        }
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
        var slot = layout.SlotSize;
        var y = layout.Y;

        for (var i = 0; i < Hotbar.SlotCount; i++)
        {
            var bounds = layout.GetSlotRect(i);
            var x = bounds.X;
            var selected = i == scene.Hud.SelectedSlot;
            var frame = selected ? new Rgba(0.55f, 0.95f, 0.82f, 1f) : new Rgba(0.12f, 0.10f, 0.13f, 1f);
            DrawRect(commandBuffer, extent, x - 2, y - 2, slot + 4, slot + 4, frame);
            DrawRect(commandBuffer, extent, x, y, slot, slot, new Rgba(0.025f, 0.021f, 0.028f, 1f));
            var hotbarSlot = scene.Hotbar.Slots[i];
            if (!hotbarSlot.IsEmpty)
            {
                DrawRect(commandBuffer, extent, x + slot / 4, y + slot / 4, slot / 2, slot / 2, BlockColor(hotbarSlot.Block));
                DrawStackPips(commandBuffer, extent, x, y, slot, hotbarSlot.Count);
            }
        }
    }

    private void DrawStackPips(CommandBuffer commandBuffer, Extent2D extent, int x, int y, int slot, int count)
    {
        var pipCount = Math.Clamp((count + 15) / 16, 1, 4);
        var pipSize = Math.Max(2, slot / 11);
        var startX = x + slot - pipCount * (pipSize + 2) - 3;
        var startY = y + slot - pipSize - 4;

        for (var i = 0; i < pipCount; i++)
        {
            DrawRect(commandBuffer, extent, startX + i * (pipSize + 2), startY, pipSize, pipSize, new Rgba(0.86f, 0.92f, 0.80f, 1f));
        }
    }

    private void DrawHeldItem(CommandBuffer commandBuffer, Extent2D extent, BlockType block, int width, int height)
    {
        if (block == BlockType.Air)
        {
            return;
        }

        var size = Math.Clamp(width / 11, 72, 128);
        var x = width - size - width / 9;
        var y = height - size - height / 8;
        var color = BlockColor(block);

        DrawRect(commandBuffer, extent, x + size / 7, y + size / 7, size, size, new Rgba(0.015f, 0.012f, 0.018f, 1f));
        DrawRect(commandBuffer, extent, x, y, size, size, color);
        DrawRect(commandBuffer, extent, x + size / 6, y + size / 6, size * 2 / 3, size * 2 / 3, new Rgba(color.R * 1.18f, color.G * 1.18f, color.B * 1.18f, 1f));
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

    private readonly record struct Rgba(float R, float G, float B, float A);
    private readonly record struct ProjectedFace(int X, int Y, int Width, int Height, float Depth, Rgba Color);
}
