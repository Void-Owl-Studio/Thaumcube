using System.Numerics;
using VoxelGame.Player;
using VoxelGame.World.Blocks;

namespace VoxelGame.Rendering;

public sealed class HudState
{
    public int Fps { get; set; }
    public Vector3 PlayerPosition { get; set; }
    public int LoadedChunks { get; set; }
    public int VisibleChunkMeshes { get; set; }
    public string CurrentBiome { get; set; } = string.Empty;
    public int SelectedSlot { get; set; }
    public BlockType SelectedBlock { get; set; }
    public BlockType TargetedBlock { get; set; }
    public Vector2 MousePosition { get; set; }
    public float BreakProgress { get; set; }
    public bool ShowMenu { get; set; }
    public bool ShowInventory { get; set; }
    public bool ShowPauseOverlay { get; set; }
    public MenuScreen MenuScreen { get; set; }
    public int MainMenuSelectedIndex { get; set; }
    public int PauseMenuSelectedIndex { get; set; }
    public int MenuSelectedWorldIndex { get; set; } = -1;
    public int MenuSelectedActionIndex { get; set; }
    public string MenuTitle { get; set; } = "VOXELGAME";
    public string MenuSubtitle { get; set; } = string.Empty;
    public string MenuStatusText { get; set; } = string.Empty;
    public string MenuSkinText { get; set; } = string.Empty;
    public bool MenuSkinReadyToApply { get; set; }
    public PlayerBodyType MenuPlayerBodyType { get; set; }
    public string CreateWorldName { get; set; } = string.Empty;
    public IReadOnlyList<string> MenuWorldNames { get; set; } = Array.Empty<string>();
    public IReadOnlyList<HotbarSlot> InventorySlots { get; set; } = Array.Empty<HotbarSlot>();
    public HotbarSlot CursorSlot { get; set; }
    public int MenuRenderDistance { get; set; }
    public bool ShowBusyOverlay { get; set; }
    public string BusyTitle { get; set; } = string.Empty;
    public string BusyStatusText { get; set; } = string.Empty;
    public float BusyProgress { get; set; }
}
