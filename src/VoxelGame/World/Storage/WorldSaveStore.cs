using System.Text.Json;
using VoxelGame.World.Chunks;

namespace VoxelGame.World.Storage;

public sealed class WorldSaveStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _worldPath;
    private readonly string _chunksPath;
    private readonly string _worldMetadataPath;
    private readonly string _playerPath;

    public string Name => Metadata.Name;
    public string WorldsRootPath { get; }
    public WorldMetadata Metadata { get; private set; }

    private WorldSaveStore(string worldsRootPath, string worldPath, WorldMetadata metadata)
    {
        WorldsRootPath = worldsRootPath;
        _worldPath = worldPath;
        _chunksPath = Path.Combine(worldPath, "chunks");
        _worldMetadataPath = Path.Combine(worldPath, "world.json");
        _playerPath = Path.Combine(worldPath, "player.json");
        Metadata = metadata;
    }

    public static WorldSaveStore CreateNew(string worldsRootPath, string worldName, int seed)
    {
        Directory.CreateDirectory(worldsRootPath);

        var worldPath = Path.Combine(worldsRootPath, worldName);
        if (Directory.Exists(worldPath))
        {
            throw new InvalidOperationException($"World '{worldName}' already exists.");
        }

        Directory.CreateDirectory(worldPath);
        Directory.CreateDirectory(Path.Combine(worldPath, "chunks"));

        var now = DateTime.UtcNow;
        var metadata = new WorldMetadata
        {
            Name = worldName,
            Seed = seed,
            CreatedUtc = now,
            LastPlayedUtc = now
        };

        var store = new WorldSaveStore(worldsRootPath, worldPath, metadata);
        store.SaveMetadata();
        return store;
    }

    public static WorldSaveStore Open(string worldsRootPath, string worldName)
    {
        var worldPath = Path.Combine(worldsRootPath, worldName);
        var metadataPath = Path.Combine(worldPath, "world.json");
        if (!File.Exists(metadataPath))
        {
            throw new FileNotFoundException($"World metadata was not found for world '{worldName}'.", metadataPath);
        }

        var metadata = JsonSerializer.Deserialize<WorldMetadata>(File.ReadAllText(metadataPath), JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize world metadata for '{worldName}'.");

        Directory.CreateDirectory(Path.Combine(worldPath, "chunks"));
        return new WorldSaveStore(worldsRootPath, worldPath, metadata);
    }

    public void Touch()
    {
        Metadata = Metadata with { LastPlayedUtc = DateTime.UtcNow };
        SaveMetadata();
    }

    public void SaveMetadata()
    {
        Directory.CreateDirectory(_worldPath);
        Directory.CreateDirectory(_chunksPath);
        File.WriteAllText(_worldMetadataPath, JsonSerializer.Serialize(Metadata, JsonOptions));
    }

    public void SavePlayer(PlayerSaveData player)
    {
        Directory.CreateDirectory(_worldPath);
        File.WriteAllText(_playerPath, JsonSerializer.Serialize(player, JsonOptions));
    }

    public PlayerSaveData? LoadPlayer()
    {
        if (!File.Exists(_playerPath))
        {
            return null;
        }

        var json = File.ReadAllText(_playerPath);
        return JsonSerializer.Deserialize<PlayerSaveData>(json, JsonOptions);
    }

    public void SaveChunk(ChunkCoord coord, ChunkSnapshotData snapshot)
    {
        Directory.CreateDirectory(_chunksPath);

        using var stream = File.Create(GetChunkPath(coord));
        using var writer = new BinaryWriter(stream);
        writer.Write(snapshot.Aura.Density);
        writer.Write(snapshot.Aura.Corruption);
        writer.Write(snapshot.Aura.Stability);
        writer.Write(snapshot.Aura.Type);

        var blocks = snapshot.Blocks;
        writer.Write(blocks.Length);
        for (var i = 0; i < blocks.Length; i++)
        {
            writer.Write(blocks[i]);
        }
    }

    public bool TryLoadChunk(ChunkCoord coord, out ChunkSnapshotData snapshot)
    {
        var path = GetChunkPath(coord);
        if (!File.Exists(path))
        {
            snapshot = default;
            return false;
        }

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        var aura = new ChunkAura(
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadString());

        var blockCount = reader.ReadInt32();
        if (blockCount != Chunk.BlockCount)
        {
            throw new InvalidDataException($"Chunk file '{path}' contains {blockCount} blocks instead of {Chunk.BlockCount}.");
        }

        var blocks = new ushort[blockCount];
        for (var i = 0; i < blockCount; i++)
        {
            blocks[i] = reader.ReadUInt16();
        }

        snapshot = new ChunkSnapshotData(blocks, aura);
        return true;
    }

    private string GetChunkPath(ChunkCoord coord)
    {
        return Path.Combine(_chunksPath, $"chunk_{coord.X}_{coord.Z}.bin");
    }
}
