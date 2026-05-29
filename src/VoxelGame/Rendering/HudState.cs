using System.Numerics;
using VoxelGame.World.Blocks;

namespace VoxelGame.Rendering;

public sealed class HudState
{
    public int Fps { get; set; }
    public Vector3 PlayerPosition { get; set; }
    public int LoadedChunks { get; set; }
    public int VisibleChunkMeshes { get; set; }
    public int SelectedSlot { get; set; }
    public BlockType SelectedBlock { get; set; }
    public BlockType TargetedBlock { get; set; }
    public float BreakProgress { get; set; }
    public bool ShowMainMenu { get; set; }
    public MenuScreen MenuScreen { get; set; }
    public int MainMenuSelectedIndex { get; set; }
    public int MenuSelectedWorldIndex { get; set; } = -1;
    public int MenuSelectedActionIndex { get; set; }
    public string MenuTitle { get; set; } = "VOXELGAME";
    public string MenuSubtitle { get; set; } = string.Empty;
    public string MenuStatusText { get; set; } = string.Empty;
    public string CreateWorldName { get; set; } = string.Empty;
    public IReadOnlyList<string> MenuWorldNames { get; set; } = Array.Empty<string>();
    public int MenuRenderDistance { get; set; }
}
