using System.Numerics;

namespace VoxelGame.World.Blocks;

public sealed record BlockDefinition(
    BlockType Id,
    string Name,
    bool Solid,
    bool Transparent,
    bool Emissive,
    bool Placeable,
    int SideTextureIndex,
    int TopTextureIndex,
    int BottomTextureIndex,
    Vector3 Tint,
    MagicalProperties Magic);
