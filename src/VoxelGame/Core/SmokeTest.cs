using VoxelGame.World;
using VoxelGame.World.Blocks;
using VoxelGame.World.Chunks;
using VoxelGame.World.Storage;
using VoxelGame.Input;
using VoxelGame.Player;

namespace VoxelGame.Core;

public static class SmokeTest
{
    public static int Run()
    {
        var tempWorldsRoot = Path.Combine(Path.GetTempPath(), $"voxelgame-smoke-{Guid.NewGuid():N}");

        try
        {
            var saveStore = WorldSaveStore.CreateNew(tempWorldsRoot, "SMOKEWORLD", 734_241);
            var world = new VoxelWorld(seed: saveStore.Metadata.Seed, renderDistance: 1, saveStore);
            var spawn = world.FindSpawnPosition(0, 0);
            world.LoadAround(spawn, immediate: true);
            world.RebuildDirtyMeshes(immediate: true);

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

            var editedY = (int)MathF.Floor(spawn.Y) - 2;
            world.SetBlock(0, editedY, 0, BlockType.MagicOre);
            world.RebuildDirtyMeshes(immediate: true);

            world.LoadAround(spawn + new System.Numerics.Vector3(Chunk.SizeX * 4, 0, 0), immediate: true);
            world.LoadAround(spawn, immediate: true);
            world.RebuildDirtyMeshes(immediate: true);

            var restored = world.GetBlock(0, editedY, 0);
            if (restored != BlockType.MagicOre)
            {
                Console.Error.WriteLine($"Expected edited block to persist after chunk unload/reload, got {restored}.");
                return 1;
            }

            var savedPlayer = PlayerSaveData.FromState(spawn + new System.Numerics.Vector3(3, 0, 2), 135f, -18f);
            world.SaveWorldState();
            saveStore.SavePlayer(savedPlayer);

            var reopenedStore = WorldSaveStore.Open(tempWorldsRoot, "SMOKEWORLD");
            var reopenedWorld = new VoxelWorld(reopenedStore.Metadata.Seed, 1, reopenedStore);
            reopenedWorld.LoadAround(spawn, immediate: true);
            reopenedWorld.RebuildDirtyMeshes(immediate: true);

            var reopenedBlock = reopenedWorld.GetBlock(0, editedY, 0);
            if (reopenedBlock != BlockType.MagicOre)
            {
                Console.Error.WriteLine($"Expected disk-saved block to reload as MagicOre, got {reopenedBlock}.");
                return 1;
            }

            var reopenedPlayer = reopenedStore.LoadPlayer();
            if (reopenedPlayer is null || reopenedPlayer.Position != savedPlayer.Position || reopenedPlayer.YawDegrees != savedPlayer.YawDegrees || reopenedPlayer.PitchDegrees != savedPlayer.PitchDegrees)
            {
                Console.Error.WriteLine("Expected saved player state to reload from disk.");
                return 1;
            }

            var resumedSpawn = reopenedWorld.EnsureSafeSpawnPosition(reopenedPlayer.Position);
            reopenedWorld.LoadAround(resumedSpawn, immediate: true);
            var resumedPlayer = new FirstPersonPlayer(reopenedWorld, 0f);
            resumedPlayer.SpawnAt(resumedSpawn, reopenedPlayer.YawDegrees, reopenedPlayer.PitchDegrees);
            resumedPlayer.Update(1.25f, new InputManager());

            if (resumedPlayer.Position.Y < resumedSpawn.Y - 0.05f)
            {
                Console.Error.WriteLine($"Expected resumed player to stay above terrain on a long frame, but fell from {resumedSpawn.Y:0.00} to {resumedPlayer.Position.Y:0.00}.");
                return 1;
            }

            var playerMeshes = resumedPlayer.BuildRenderMeshes().ToArray();
            if (playerMeshes.Length == 0 || playerMeshes[0].Vertices.Length == 0 || playerMeshes[0].Indices.Length == 0)
            {
                Console.Error.WriteLine("Expected player model asset to build a visible render mesh.");
                return 1;
            }

            Console.WriteLine($"Smoke OK: spawn={spawn}, chunks={world.LoadedChunkCount}, meshes={world.VisibleMeshCount}, ground={ground}, world={saveStore.Name}");
            return 0;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempWorldsRoot))
                {
                    Directory.Delete(tempWorldsRoot, true);
                }
            }
            catch
            {
                // Ignore cleanup failure in smoke mode.
            }
        }
    }
}
