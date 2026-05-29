using System.Text.Json;

namespace VoxelGame.World.Storage;

public static class WorldCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static IReadOnlyList<WorldMetadata> ListWorlds(string worldsRootPath)
    {
        Directory.CreateDirectory(worldsRootPath);

        var worlds = new List<WorldMetadata>();
        foreach (var directoryPath in Directory.EnumerateDirectories(worldsRootPath))
        {
            var metadataPath = Path.Combine(directoryPath, "world.json");
            if (!File.Exists(metadataPath))
            {
                continue;
            }

            try
            {
                var json = File.ReadAllText(metadataPath);
                var metadata = JsonSerializer.Deserialize<WorldMetadata>(json, JsonOptions);
                if (metadata is not null)
                {
                    worlds.Add(metadata);
                }
            }
            catch
            {
                // Ignore malformed world folders to keep menu navigation resilient.
            }
        }

        return worlds
            .OrderByDescending(world => world.LastPlayedUtc)
            .ThenBy(world => world.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string CreateNewWorldName()
    {
        return $"WORLD{DateTime.UtcNow:yyyyMMddHHmmss}";
    }

    public static void DeleteWorld(string worldsRootPath, string worldName)
    {
        Directory.CreateDirectory(worldsRootPath);

        var rootFullPath = Path.GetFullPath(worldsRootPath);
        var worldFullPath = Path.GetFullPath(Path.Combine(worldsRootPath, worldName));
        var relativePath = Path.GetRelativePath(rootFullPath, worldFullPath);
        if (relativePath.StartsWith("..", StringComparison.OrdinalIgnoreCase) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException($"Refusing to delete world path outside the worlds root: '{worldFullPath}'.");
        }

        if (Directory.Exists(worldFullPath))
        {
            Directory.Delete(worldFullPath, true);
        }
    }
}
