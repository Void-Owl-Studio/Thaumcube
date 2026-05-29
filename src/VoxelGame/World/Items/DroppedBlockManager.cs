using System.Numerics;
using VoxelGame.Rendering;
using VoxelGame.World.Blocks;
using VoxelGame.World.Chunks;

namespace VoxelGame.World.Items;

public sealed class DroppedBlockManager
{
    private readonly List<DroppedBlockItem> _items = new();
    private readonly VoxelWorld _world;
    private int _nextId;

    public DroppedBlockManager(VoxelWorld world)
    {
        _world = world;
    }

    public void Spawn(BlockType block, BlockPosition position, Vector3 impulse)
    {
        if (block == BlockType.Air)
        {
            return;
        }

        var center = new Vector3(position.X + 0.5f, position.Y + 0.45f, position.Z + 0.5f);
        _items.Add(new DroppedBlockItem(_nextId++, block, center, impulse));
    }

    public void Update(float dt, Vector3 playerPosition, Hotbar hotbar)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var item = _items[i];
            item.RotationRadians += dt * 2.7f;
            item.Velocity += new Vector3(0, -12f * dt, 0);

            var next = item.Position + item.Velocity * dt;
            if (next.Y < 0.25f || _world.IsSolid((int)MathF.Floor(next.X), (int)MathF.Floor(next.Y - 0.18f), (int)MathF.Floor(next.Z)))
            {
                next.Y = MathF.Floor(item.Position.Y) + 0.28f;
                item.Velocity = new Vector3(item.Velocity.X * 0.72f, 0f, item.Velocity.Z * 0.72f);
            }

            item.Position = next;

            if (Vector3.DistanceSquared(playerPosition + new Vector3(0, 0.8f, 0), item.Position) < 1.35f * 1.35f && hotbar.TryAdd(item.Block))
            {
                _items.RemoveAt(i);
            }
        }
    }

    public IEnumerable<ChunkRenderMesh> BuildRenderMeshes()
    {
        foreach (var item in _items)
        {
            yield return ChunkMeshBuilder.BuildDroppedBlockMesh(
                $"drop:{item.Id}",
                item.Position + new Vector3(0, MathF.Sin(item.RotationRadians * 2f) * 0.06f, 0),
                0.36f,
                item.RotationRadians,
                item.Block,
                _world.Blocks);
        }
    }
}
