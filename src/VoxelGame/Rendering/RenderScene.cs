using VoxelGame.Player;
using VoxelGame.World.Chunks;

namespace VoxelGame.Rendering;

public sealed class RenderScene
{
    public IReadOnlyList<ChunkRenderMesh> ChunkMeshes { get; }
    public CameraState Camera { get; }
    public Hotbar Hotbar { get; }
    public HudState Hud { get; }
    public int RenderDistanceChunks { get; }
    public float ElapsedSeconds { get; }

    public RenderScene(IEnumerable<ChunkRenderMesh> chunkMeshes, CameraState camera, Hotbar hotbar, HudState hud, int renderDistanceChunks, float elapsedSeconds)
    {
        ChunkMeshes = chunkMeshes.ToArray();
        Camera = camera;
        Hotbar = hotbar;
        Hud = hud;
        RenderDistanceChunks = renderDistanceChunks;
        ElapsedSeconds = elapsedSeconds;
    }
}
