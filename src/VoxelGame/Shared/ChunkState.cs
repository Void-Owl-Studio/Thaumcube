namespace VoxelGame.Shared;

public enum ServerChunkState
{
    Unloaded,
    Loading,
    Active
}

public enum ClientChunkState
{
    Missing,
    Meshing,
    Rendered
}
