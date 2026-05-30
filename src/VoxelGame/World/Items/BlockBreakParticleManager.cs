using System.Numerics;
using VoxelGame.Rendering.Sprites;
using VoxelGame.World.Blocks;
using VoxelGame.World.Chunks;

namespace VoxelGame.World.Items;

public sealed class BlockBreakParticleManager
{
    private const int ParticlesPerBreak = 14;
    private const float ParticleLifetimeSeconds = 0.5f;
    private readonly List<BlockBreakParticle> _particles = [];
    private readonly BlockRegistry _blocks;
    private int _nextId;

    public BlockBreakParticleManager(VoxelWorld world)
    {
        _blocks = world.Blocks;
    }

    public void Spawn(BlockType block, BlockPosition position)
    {
        if (block == BlockType.Air)
        {
            return;
        }

        var random = Random.Shared;
        var blockMin = new Vector3(position.X, position.Y, position.Z);

        for (var i = 0; i < ParticlesPerBreak; i++)
        {
            var spawnPosition = blockMin + new Vector3(
                0.12f + (float)random.NextDouble() * 0.76f,
                0.12f + (float)random.NextDouble() * 0.76f,
                0.12f + (float)random.NextDouble() * 0.76f);
            var horizontal = new Vector3(
                ((float)random.NextDouble() - 0.5f) * 3.6f,
                0f,
                ((float)random.NextDouble() - 0.5f) * 3.6f);
            var velocity = horizontal + new Vector3(0f, 1.5f + (float)random.NextDouble() * 2.1f, 0f);
            var size = 0.11f + (float)random.NextDouble() * 0.05f;
            var textureWindow = 0.22f + (float)random.NextDouble() * 0.12f;
            var uvMin = new Vector2(
                (float)random.NextDouble() * (1f - textureWindow),
                (float)random.NextDouble() * (1f - textureWindow));
            var uvMax = uvMin + new Vector2(textureWindow);

            _particles.Add(new BlockBreakParticle(
                _nextId++,
                block,
                spawnPosition,
                velocity,
                size,
                uvMin,
                uvMax));
        }
    }

    public void Update(float dt)
    {
        for (var i = _particles.Count - 1; i >= 0; i--)
        {
            var particle = _particles[i];
            particle.AgeSeconds += dt;
            if (particle.AgeSeconds >= ParticleLifetimeSeconds)
            {
                _particles.RemoveAt(i);
                continue;
            }

            particle.Velocity += new Vector3(0f, -10.5f * dt, 0f);
            particle.Velocity *= MathF.Pow(0.9f, dt * 60f);
            particle.Position += particle.Velocity * dt;
        }
    }

    public IEnumerable<WorldSprite> BuildSprites()
    {
        foreach (var particle in _particles)
        {
            var lifetime = Math.Clamp(1f - particle.AgeSeconds / ParticleLifetimeSeconds, 0f, 1f);
            var size = particle.Size * (0.65f + lifetime * 0.35f);
            yield return new WorldSprite(
                particle.Position,
                size,
                particle.Block,
                _blocks.GetFaceTextureIndex(particle.Block, Vector3.UnitY),
                particle.UvMin,
                particle.UvMax,
                ChunkMeshBuilder.ResolveBillboardTint(particle.Block),
                lifetime);
        }
    }

    private sealed class BlockBreakParticle
    {
        public int Id { get; }
        public BlockType Block { get; }
        public Vector2 UvMin { get; }
        public Vector2 UvMax { get; }
        public Vector3 Position { get; set; }
        public Vector3 Velocity { get; set; }
        public float Size { get; }
        public float AgeSeconds { get; set; }

        public BlockBreakParticle(int id, BlockType block, Vector3 position, Vector3 velocity, float size, Vector2 uvMin, Vector2 uvMax)
        {
            Id = id;
            Block = block;
            Position = position;
            Velocity = velocity;
            Size = size;
            UvMin = uvMin;
            UvMax = uvMax;
        }
    }
}
