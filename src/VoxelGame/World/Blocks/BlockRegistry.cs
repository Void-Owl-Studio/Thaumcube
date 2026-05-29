using System.Numerics;

namespace VoxelGame.World.Blocks;

public sealed class BlockRegistry
{
    private readonly Dictionary<BlockType, BlockDefinition> _definitions = new();

    public BlockRegistry()
    {
        Register(new BlockDefinition(BlockType.Air, "Air", false, true, false, 0, 0, 0, Vector3.Zero, new MagicalProperties(0, 0, 1, "None")));
        Register(new BlockDefinition(BlockType.Grass, "Grass", true, false, false, BlockTextureAtlas.GrassSide, BlockTextureAtlas.GrassTop, BlockTextureAtlas.Dirt, new Vector3(0.28f, 0.56f, 0.22f), new MagicalProperties(0.05f, 0, 0.95f, "Verdant")));
        Register(new BlockDefinition(BlockType.Dirt, "Dirt", true, false, false, BlockTextureAtlas.Dirt, BlockTextureAtlas.Dirt, BlockTextureAtlas.Dirt, new Vector3(0.42f, 0.28f, 0.16f), new MagicalProperties(0.02f, 0, 1, "Earth")));
        Register(new BlockDefinition(BlockType.Stone, "Stone", true, false, false, BlockTextureAtlas.Stone, BlockTextureAtlas.Stone, BlockTextureAtlas.Stone, new Vector3(0.45f, 0.46f, 0.48f), new MagicalProperties(0.04f, 0, 1, "Deep")));
        Register(new BlockDefinition(BlockType.Sand, "Sand", true, false, false, BlockTextureAtlas.Sand, BlockTextureAtlas.Sand, BlockTextureAtlas.Sand, new Vector3(0.72f, 0.65f, 0.42f), new MagicalProperties(0.01f, 0, 1, "Dust")));
        Register(new BlockDefinition(BlockType.Water, "Water", false, true, false, BlockTextureAtlas.WaterEdge, BlockTextureAtlas.Water, BlockTextureAtlas.Water, new Vector3(0.16f, 0.32f, 0.58f), new MagicalProperties(0.10f, 0, 0.9f, "Flow")));
        Register(new BlockDefinition(BlockType.CorruptedGrass, "Corrupted Grass", true, false, false, BlockTextureAtlas.CorruptedGrass, BlockTextureAtlas.CorruptedGrass, BlockTextureAtlas.CorruptedGrass, new Vector3(0.18f, 0.08f, 0.22f), new MagicalProperties(0.35f, 0.8f, 0.35f, "Blight")));
        Register(new BlockDefinition(BlockType.ArcaneCrystal, "Arcane Crystal", true, false, true, BlockTextureAtlas.ArcaneCrystal, BlockTextureAtlas.ArcaneCrystal, BlockTextureAtlas.ArcaneCrystal, new Vector3(0.18f, 0.82f, 0.74f), new MagicalProperties(1.0f, 0.05f, 0.65f, "Crystal")));
        Register(new BlockDefinition(BlockType.MagicOre, "Magic Ore", true, false, true, BlockTextureAtlas.MagicOre, BlockTextureAtlas.MagicOre, BlockTextureAtlas.MagicOre, new Vector3(0.26f, 0.18f, 0.55f), new MagicalProperties(0.75f, 0.18f, 0.5f, "Vein")));
        Register(new BlockDefinition(BlockType.Snow, "Snow", true, false, false, BlockTextureAtlas.Snow, BlockTextureAtlas.Snow, BlockTextureAtlas.Snow, new Vector3(0.92f, 0.94f, 0.98f), new MagicalProperties(0.01f, 0, 1f, "Frost")));
        Register(new BlockDefinition(BlockType.OakLog, "Oak Log", true, false, false, BlockTextureAtlas.OakLogSide, BlockTextureAtlas.OakLogTop, BlockTextureAtlas.OakLogTop, new Vector3(0.70f, 0.58f, 0.36f), new MagicalProperties(0.03f, 0, 1f, "Timber")));
        Register(new BlockDefinition(BlockType.OakLeaves, "Oak Leaves", true, true, false, BlockTextureAtlas.OakLeaves, BlockTextureAtlas.OakLeaves, BlockTextureAtlas.OakLeaves, new Vector3(0.42f, 0.66f, 0.24f), new MagicalProperties(0.06f, 0, 0.95f, "Canopy")));
    }

    public BlockDefinition this[BlockType type] => _definitions[type];

    public bool IsSolid(BlockType type) => _definitions[type].Solid;

    public bool IsTransparent(BlockType type) => _definitions[type].Transparent;

    public int GetFaceTextureIndex(BlockType type, Vector3 faceNormal)
    {
        var definition = _definitions[type];
        if (faceNormal.Y > 0.5f)
        {
            return definition.TopTextureIndex;
        }

        if (faceNormal.Y < -0.5f)
        {
            return definition.BottomTextureIndex;
        }

        return definition.SideTextureIndex;
    }

    private void Register(BlockDefinition definition) => _definitions[definition.Id] = definition;
}
