namespace VoxelGame.World.Chunks;

public readonly record struct ChunkSnapshotData(ushort[] Blocks, ChunkAura Aura);
