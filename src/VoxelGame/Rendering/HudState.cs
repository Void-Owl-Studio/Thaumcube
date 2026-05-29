using System.Numerics;
using VoxelGame.World.Blocks;

namespace VoxelGame.Rendering;

public sealed class HudState
{
    public string MainMenuLoadWorldLabel { get; set; } = "LOAD WORLD";
    public string MainMenuNewWorldLabel { get; set; } = "NEW WORLD";
    public int Fps { get; set; }
    public Vector3 PlayerPosition { get; set; }
    public int LoadedChunks { get; set; }
    public int VisibleChunkMeshes { get; set; }
    public int SelectedSlot { get; set; }
    public BlockType SelectedBlock { get; set; }
    public BlockType TargetedBlock { get; set; }
    public float BreakProgress { get; set; }
    public bool ShowMainMenu { get; set; }
    public int MainMenuSelectedIndex { get; set; }
    public int MainMenuRenderDistance { get; set; }
}
