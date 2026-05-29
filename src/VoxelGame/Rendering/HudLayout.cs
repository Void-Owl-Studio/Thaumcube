using System.Numerics;

namespace VoxelGame.Rendering;

public static class HudLayout
{
    public static MainMenuLayout BuildMainMenu(int width, int height)
    {
        return BuildCenteredButtons(width, height, ["SINGLEPLAYER", "SETTINGS", "EXIT"], width / 3, 180);
    }

    public static MainMenuLayout BuildPauseMenu(int width, int height)
    {
        return BuildCenteredButtons(width, height, ["SETTINGS", "EXIT TO MAIN MENU"], Math.Max(250, width / 3), 156);
    }

    public static SettingsLayout BuildSettingsMenu(int width, int height)
    {
        var scale = Math.Clamp(width / 480, 2, 4);
        var panelWidth = Math.Clamp(width / 2, 420, 700);
        var panelX = (width - panelWidth) / 2;
        var renderBounds = new UiRect(panelX, height / 3, panelWidth, 76);
        var sliderBounds = new UiRect(panelX + 20, renderBounds.Y + 38, panelWidth - 40, 18);
        var buttonWidth = Math.Max(140, panelWidth / 3);
        var buttonX = panelX + (panelWidth - buttonWidth) / 2;
        var buttonY = renderBounds.Bottom + 32;

        return new SettingsLayout(
            scale,
            renderBounds,
            sliderBounds,
            new MenuButtonLayout(0, new UiRect(buttonX, buttonY, buttonWidth, 34)));
    }

    public static WorldSelectionLayout BuildWorldSelectionMenu(int width, int height, IReadOnlyList<string> worldNames)
    {
        var scale = Math.Clamp(width / 480, 2, 4);
        var panelWidth = Math.Clamp(width / 2, 420, 760);
        var panelX = (width - panelWidth) / 2;
        var listTop = height / 4;
        var visibleRows = Math.Max(4, Math.Min(8, height / 76));
        var rowHeight = 34;
        var listHeight = visibleRows * rowHeight;
        var listBounds = new UiRect(panelX, listTop, panelWidth, listHeight);

        var worldRows = new List<WorldListItemLayout>(worldNames.Count);
        for (var i = 0; i < worldNames.Count; i++)
        {
            var rowY = listTop + i * rowHeight;
            if (rowY + rowHeight <= listBounds.Bottom)
            {
                worldRows.Add(new WorldListItemLayout(i, new UiRect(panelX + 12, rowY + 6, panelWidth - 24, rowHeight - 8)));
            }
        }

        var buttonWidth = Math.Max(104, (panelWidth - 24) / 4 - 9);
        var buttons = new MenuButtonLayout[4];
        var buttonY = listBounds.Bottom + 20;
        for (var i = 0; i < buttons.Length; i++)
        {
            var x = panelX + 12 + i * (buttonWidth + 8);
            buttons[i] = new MenuButtonLayout(i, new UiRect(x, buttonY, buttonWidth, 34));
        }

        return new WorldSelectionLayout(scale, listBounds, worldRows, buttons);
    }

    public static CreateWorldLayout BuildCreateWorldMenu(int width, int height)
    {
        var scale = Math.Clamp(width / 480, 2, 4);
        var panelWidth = Math.Clamp(width / 2, 420, 700);
        var panelX = (width - panelWidth) / 2;
        var fieldBounds = new UiRect(panelX, height / 3, panelWidth, 40);
        var buttonWidth = Math.Max(140, panelWidth / 3);
        var buttonGap = 14;
        var buttonsWidth = buttonWidth * 2 + buttonGap;
        var buttonStartX = panelX + (panelWidth - buttonsWidth) / 2;
        var buttonY = fieldBounds.Bottom + 32;

        return new CreateWorldLayout(
            scale,
            fieldBounds,
            [
                new MenuButtonLayout(0, new UiRect(buttonStartX, buttonY, buttonWidth, 34)),
                new MenuButtonLayout(1, new UiRect(buttonStartX + buttonWidth + buttonGap, buttonY, buttonWidth, 34))
            ]);
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

    public static int? HitTestMainMenu(Vector2 mousePosition, int width, int height)
    {
        var layout = BuildMainMenu(width, height);
        return HitTestButtons(mousePosition, layout.Items);
    }

    public static int? HitTestPauseMenu(Vector2 mousePosition, int width, int height)
    {
        var layout = BuildPauseMenu(width, height);
        return HitTestButtons(mousePosition, layout.Items);
    }

    public static int? HitTestWorldSelectionWorld(Vector2 mousePosition, int width, int height, IReadOnlyList<string> worldNames)
    {
        var layout = BuildWorldSelectionMenu(width, height, worldNames);
        foreach (var item in layout.WorldRows)
        {
            if (item.Bounds.Contains(mousePosition))
            {
                return item.Index;
            }
        }

        return null;
    }

    public static int? HitTestWorldSelectionAction(Vector2 mousePosition, int width, int height, IReadOnlyList<string> worldNames)
    {
        var layout = BuildWorldSelectionMenu(width, height, worldNames);
        return HitTestButtons(mousePosition, layout.Buttons);
    }

    public static int? HitTestCreateWorldAction(Vector2 mousePosition, int width, int height)
    {
        var layout = BuildCreateWorldMenu(width, height);
        return HitTestButtons(mousePosition, layout.Buttons);
    }

    public static int? HitTestSettingsAction(Vector2 mousePosition, int width, int height)
    {
        var layout = BuildSettingsMenu(width, height);
        return layout.BackButton.Bounds.Contains(mousePosition) ? layout.BackButton.Index : null;
    }

    public static bool HitTestSettingsSlider(Vector2 mousePosition, int width, int height)
    {
        var layout = BuildSettingsMenu(width, height);
        return layout.SliderBounds.Contains(mousePosition);
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

    public static int TextWidth(string text, int scale)
    {
        var width = 0;
        foreach (var character in text)
        {
            width += character == ' ' ? 4 * scale : 6 * scale;
        }

        return Math.Max(0, width - scale);
    }

    private static MainMenuLayout BuildCenteredButtons(int width, int height, string[] options, int minBoxWidth, int topOffset)
    {
        var menuScale = Math.Clamp(width / 430, 2, 4);
        var y = height / 2 - topOffset / 2;
        var items = new MenuButtonLayout[options.Length];

        for (var i = 0; i < items.Length; i++)
        {
            var textWidth = TextWidth(options[i], menuScale);
            var boxWidth = Math.Max(textWidth + 56, minBoxWidth);
            var boxX = (width - boxWidth) / 2;
            var boxY = y + i * 58;
            items[i] = new MenuButtonLayout(i, new UiRect(boxX, boxY, boxWidth, 32));
        }

        return new MainMenuLayout(menuScale, items);
    }

    private static int? HitTestButtons(Vector2 mousePosition, IReadOnlyList<MenuButtonLayout> items)
    {
        foreach (var item in items)
        {
            if (item.Bounds.Contains(mousePosition))
            {
                return item.Index;
            }
        }

        return null;
    }
}

public readonly record struct MainMenuLayout(int Scale, IReadOnlyList<MenuButtonLayout> Items);

public readonly record struct WorldSelectionLayout(
    int Scale,
    UiRect ListBounds,
    IReadOnlyList<WorldListItemLayout> WorldRows,
    IReadOnlyList<MenuButtonLayout> Buttons);

public readonly record struct CreateWorldLayout(
    int Scale,
    UiRect NameFieldBounds,
    IReadOnlyList<MenuButtonLayout> Buttons);

public readonly record struct SettingsLayout(
    int Scale,
    UiRect RenderDistanceBounds,
    UiRect SliderBounds,
    MenuButtonLayout BackButton);

public readonly record struct MenuButtonLayout(int Index, UiRect Bounds);

public readonly record struct WorldListItemLayout(int Index, UiRect Bounds);

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
