using System.Numerics;
using VoxelGame.World.Blocks;

namespace VoxelGame.Rendering.Sprites;

public readonly record struct WorldSprite(
    Vector3 Position,
    float Size,
    BlockType Block,
    int TextureIndex,
    Vector2 UvMin,
    Vector2 UvMax,
    Vector3 Tint,
    float Alpha = 1f);
