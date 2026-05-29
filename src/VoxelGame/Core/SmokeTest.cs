using VoxelGame.World;
using VoxelGame.World.Blocks;

namespace VoxelGame.Core;

public static class SmokeTest
{
    public static int Run()
    {
        var world = new VoxelWorld(seed: 734_241, renderDistance: 1);
        var spawn = world.FindSpawnPosition(0, 0);
        world.LoadAround(spawn);
        world.RebuildDirtyMeshes();

        if (world.LoadedChunkCount != 9)
        {
            Console.Error.WriteLine($"Expected 9 loaded chunks, got {world.LoadedChunkCount}.");
            return 1;
        }

        if (world.VisibleMeshCount == 0)
        {
            Console.Error.WriteLine("Expected at least one generated chunk mesh.");
            return 1;
        }

        var ground = world.GetBlock(0, (int)MathF.Floor(spawn.Y) - 3, 0);
        if (ground == BlockType.Air)
        {
            Console.Error.WriteLine("Expected solid terrain below spawn.");
            return 1;
        }

        Console.WriteLine($"Smoke OK: spawn={spawn}, chunks={world.LoadedChunkCount}, meshes={world.VisibleMeshCount}, ground={ground}");
        return 0;
    }
}
