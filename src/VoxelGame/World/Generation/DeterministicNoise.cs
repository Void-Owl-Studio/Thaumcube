namespace VoxelGame.World.Generation;

public sealed class DeterministicNoise
{
    private readonly int _seed;

    public DeterministicNoise(int seed)
    {
        _seed = seed;
    }

    public float Value2D(float x, float z, float frequency)
    {
        x *= frequency;
        z *= frequency;

        var x0 = FastFloor(x);
        var z0 = FastFloor(z);
        var tx = Smooth(x - x0);
        var tz = Smooth(z - z0);

        var a = Hash01(x0, z0, 0);
        var b = Hash01(x0 + 1, z0, 0);
        var c = Hash01(x0, z0 + 1, 0);
        var d = Hash01(x0 + 1, z0 + 1, 0);

        return Lerp(Lerp(a, b, tx), Lerp(c, d, tx), tz);
    }

    public float Value3D(float x, float y, float z, float frequency)
    {
        x *= frequency;
        y *= frequency;
        z *= frequency;

        var x0 = FastFloor(x);
        var y0 = FastFloor(y);
        var z0 = FastFloor(z);
        var tx = Smooth(x - x0);
        var ty = Smooth(y - y0);
        var tz = Smooth(z - z0);

        var x00 = Lerp(Hash01(x0, y0, z0), Hash01(x0 + 1, y0, z0), tx);
        var x10 = Lerp(Hash01(x0, y0 + 1, z0), Hash01(x0 + 1, y0 + 1, z0), tx);
        var x01 = Lerp(Hash01(x0, y0, z0 + 1), Hash01(x0 + 1, y0, z0 + 1), tx);
        var x11 = Lerp(Hash01(x0, y0 + 1, z0 + 1), Hash01(x0 + 1, y0 + 1, z0 + 1), tx);

        return Lerp(Lerp(x00, x10, ty), Lerp(x01, x11, ty), tz);
    }

    public float Fractal2D(float x, float z, float frequency, int octaves)
    {
        var amplitude = 1f;
        var total = 0f;
        var normalization = 0f;

        for (var i = 0; i < octaves; i++)
        {
            total += Value2D(x, z, frequency) * amplitude;
            normalization += amplitude;
            frequency *= 2f;
            amplitude *= 0.5f;
        }

        return total / normalization;
    }

    private float Hash01(int x, int y, int z)
    {
        unchecked
        {
            var h = _seed;
            h ^= x * 374761393;
            h ^= y * 668265263;
            h ^= z * 1442695041;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0x00FFFFFF) / 16777215f;
        }
    }

    private static int FastFloor(float value) => value >= 0 ? (int)value : (int)value - 1;
    private static float Smooth(float t) => t * t * (3f - 2f * t);
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
