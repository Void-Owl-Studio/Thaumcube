using System.Numerics;

namespace VoxelGame.Rendering;

public static class HudLayout
{
    public static MainMenuLayout BuildMainMenu(int width, int height)
    {
        var scale = Math.Clamp(width / 520, 2, 4);
        var previewWidth = Math.Clamp(width / 5, 180, 250);
        var previewHeight = Math.Clamp(height / 2, 260, 360);
        var gap = Math.Clamp(width / 28, 24, 56);
        var buttonWidth = Math.Clamp(width / 4, 290, 430);
        var buttonHeight = Math.Clamp(height / 20, 34, 42);
        var buttonSpacing = buttonHeight + Math.Clamp(height / 48, 12, 18);
        var contentWidth = previewWidth + gap + buttonWidth;
        var startX = Math.Max(28, (width - contentWidth) / 2);
        var previewY = Math.Max(height / 3, 206);
        var buttonsX = startX + previewWidth + gap;
        var totalButtonHeight = buttonSpacing * 5 + buttonHeight;
        var buttonsY = previewY + Math.Max(4, (previewHeight - totalButtonHeight) / 2);
        var options = new[] { "SINGLEPLAYER", "SETTINGS", "BODY NORMAL", "LOAD SKIN", "APPLY SKIN", "EXIT" };
        var items = new MenuButtonLayout[options.Length];

        for (var i = 0; i < items.Length; i++)
        {
            var textWidth = TextWidth(options[i], scale);
            var boxWidth = Math.Max(textWidth + 56, buttonWidth);
            items[i] = new MenuButtonLayout(i, new UiRect(buttonsX, buttonsY + i * buttonSpacing, boxWidth, buttonHeight));
        }

        return new MainMenuLayout(scale, items, new UiRect(startX, previewY, previewWidth, previewHeight));
    }

    public static MainMenuLayout BuildPauseMenu(int width, int height)
    {
        return BuildCenteredButtons(width, height, ["SETTINGS", "EXIT TO MAIN MENU"], Math.Max(320, width / 3), 116);
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

    public static InventoryLayout BuildInventory(int width, int height)
    {
        const int mainSlotCount = 27;
        const int columns = 9;
        var rows = (int)Math.Ceiling(mainSlotCount / (float)columns);
        var slotSize = Math.Clamp(Math.Min(width / 24, height / 12), 28, 42);
        var gap = Math.Max(4, slotSize / 8);
        var padding = Math.Max(16, slotSize / 2);
        var titleHeight = 24;
        var headerGap = 12;
        var mainWidth = columns * slotSize + (columns - 1) * gap;
        var hotbarWidth = Hotbar.SlotCount * slotSize + (Hotbar.SlotCount - 1) * gap;
        var contentWidth = Math.Max(mainWidth, hotbarWidth);
        var panelWidth = contentWidth + padding * 2;
        var panelHeight = titleHeight + headerGap + rows * slotSize + (rows - 1) * gap + padding + slotSize + 22;
        var panelX = (width - panelWidth) / 2;
        var panelY = Math.Max(28, (height - panelHeight) / 2);

        var mainStartX = panelX + padding + (contentWidth - mainWidth) / 2;
        var mainStartY = panelY + padding + titleHeight + headerGap;
        var mainSlots = new UiRect[mainSlotCount];
        for (var i = 0; i < mainSlotCount; i++)
        {
            var row = i / columns;
            var column = i % columns;
            mainSlots[i] = new UiRect(
                mainStartX + column * (slotSize + gap),
                mainStartY + row * (slotSize + gap),
                slotSize,
                slotSize);
        }

        var hotbarStartX = panelX + padding + (contentWidth - hotbarWidth) / 2;
        var hotbarY = mainStartY + rows * (slotSize + gap) - gap + padding;
        var hotbarSlots = new UiRect[Hotbar.SlotCount];
        for (var i = 0; i < Hotbar.SlotCount; i++)
        {
            hotbarSlots[i] = new UiRect(
                hotbarStartX + i * (slotSize + gap),
                hotbarY,
                slotSize,
                slotSize);
        }

        return new InventoryLayout(
            slotSize,
            gap,
            new UiRect(panelX, panelY, panelWidth, panelHeight),
            new UiRect(panelX + padding, panelY + padding, contentWidth, titleHeight),
            mainSlots,
            hotbarSlots);
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

    public static InventoryHitResult HitTestInventory(Vector2 mousePosition, int width, int height)
    {
        var layout = BuildInventory(width, height);
        for (var i = 0; i < layout.MainSlots.Count; i++)
        {
            if (layout.MainSlots[i].Contains(mousePosition))
            {
                return new InventoryHitResult(InventoryArea.Main, i);
            }
        }

        for (var i = 0; i < layout.HotbarSlots.Count; i++)
        {
            if (layout.HotbarSlots[i].Contains(mousePosition))
            {
                return new InventoryHitResult(InventoryArea.Hotbar, i);
            }
        }

        return new InventoryHitResult(InventoryArea.None, -1);
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

        return new MainMenuLayout(menuScale, items, default);
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

public readonly record struct MainMenuLayout(int Scale, IReadOnlyList<MenuButtonLayout> Items, UiRect PlayerPreviewBounds);

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

public readonly record struct InventoryLayout(
    int SlotSize,
    int Gap,
    UiRect PanelBounds,
    UiRect TitleBounds,
    IReadOnlyList<UiRect> MainSlots,
    IReadOnlyList<UiRect> HotbarSlots);

public enum InventoryArea
{
    None,
    Main,
    Hotbar
}

public readonly record struct InventoryHitResult(InventoryArea Area, int Index);

public readonly record struct UiRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public bool Contains(Vector2 point)
    {
        return point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;
    }
}
