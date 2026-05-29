using System.Numerics;

namespace VoxelGame.Rendering;

public static class HudLayout
{
    public static MainMenuLayout BuildMainMenu(int width, int height, string loadWorldLabel, string newWorldLabel, int renderDistance)
    {
        var menuScale = Math.Clamp(width / 430, 2, 4);
        var options = BuildMainMenuOptions(loadWorldLabel, newWorldLabel, renderDistance);
        var y = height / 2 - 29 * options.Length;
        var items = new MainMenuItemLayout[options.Length];

        for (var i = 0; i < items.Length; i++)
        {
            var textWidth = TextWidth(options[i], menuScale);
            var boxWidth = Math.Max(textWidth + 56, width / 3);
            var boxX = (width - boxWidth) / 2;
            var boxY = y + i * 58;
            items[i] = new MainMenuItemLayout(i, new UiRect(boxX, boxY, boxWidth, 32));
        }

        return new MainMenuLayout(menuScale, items);
    }

    public static HotbarLayout BuildHotbar(int width, int height)
    {
        var slotSize = Math.Clamp(width / 28, 34, 54);
        var gap = Math.Max(3, slotSize / 10);
        var total = slotSize * Hotbar.SlotCount + gap * (Hotbar.SlotCount - 1);
        var startX = (width - total) / 2;
        var y = height - slotSize - Math.Max(16, height / 35);
        return new HotbarLayout(slotSize, gap, startX, y);
    }

    public static int? HitTestMainMenu(Vector2 mousePosition, int width, int height, string loadWorldLabel, string newWorldLabel, int renderDistance)
    {
        var layout = BuildMainMenu(width, height, loadWorldLabel, newWorldLabel, renderDistance);
        foreach (var item in layout.Items)
        {
            if (item.Bounds.Contains(mousePosition))
            {
                return item.Index;
            }
        }

        return null;
    }

    public static string[] BuildMainMenuOptions(string loadWorldLabel, string newWorldLabel, int renderDistance)
    {
        return
        [
            loadWorldLabel,
            newWorldLabel,
            $"RENDER DISTANCE {renderDistance}",
            "EXIT"
        ];
    }

    public static int? HitTestHotbarSlot(Vector2 mousePosition, int width, int height)
    {
        var layout = BuildHotbar(width, height);
        for (var i = 0; i < Hotbar.SlotCount; i++)
        {
            if (layout.GetSlotRect(i).Contains(mousePosition))
            {
                return i;
            }
        }

        return null;
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
}

public readonly record struct MainMenuLayout(int Scale, IReadOnlyList<MainMenuItemLayout> Items);

public readonly record struct MainMenuItemLayout(int Index, UiRect Bounds);

public readonly record struct HotbarLayout(int SlotSize, int Gap, int StartX, int Y)
{
    public UiRect GetSlotRect(int index)
    {
        return new UiRect(StartX + index * (SlotSize + Gap), Y, SlotSize, SlotSize);
    }
}

public readonly record struct UiRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public bool Contains(Vector2 point)
    {
        return point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;
    }
}
