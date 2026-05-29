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
}
