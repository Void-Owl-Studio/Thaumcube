namespace VoxelGame.World.Generation;

public sealed class NoiseGenerator
{
    private readonly byte[] _permutations = new byte[512];
    private readonly int _seed;

    public NoiseGenerator(int seed)
    {
        _seed = seed;

        var permutation = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var random = new Random(seed);

        for (var i = permutation.Length - 1; i > 0; i--)
        {
            var swapIndex = random.Next(i + 1);
            (permutation[i], permutation[swapIndex]) = (permutation[swapIndex], permutation[i]);
        }

        for (var i = 0; i < _permutations.Length; i++)
        {
            _permutations[i] = permutation[i & 255];
        }
    }

    public float Perlin2D(float x, float z)
    {
        var floorX = FastFloor(x);
        var floorZ = FastFloor(z);
        var xi = floorX & 255;
        var zi = floorZ & 255;
        var xf = x - floorX;
        var zf = z - floorZ;

        var u = Fade(xf);
        var v = Fade(zf);

        var aa = _permutations[_permutations[xi] + zi];
        var ab = _permutations[_permutations[xi] + zi + 1];
        var ba = _permutations[_permutations[xi + 1] + zi];
        var bb = _permutations[_permutations[xi + 1] + zi + 1];

        var x1 = Lerp(Grad2D(aa, xf, zf), Grad2D(ba, xf - 1f, zf), u);
        var x2 = Lerp(Grad2D(ab, xf, zf - 1f), Grad2D(bb, xf - 1f, zf - 1f), u);
        return Lerp(x1, x2, v);
    }

    public float Perlin3D(float x, float y, float z)
    {
        var floorX = FastFloor(x);
        var floorY = FastFloor(y);
        var floorZ = FastFloor(z);
        var xi = floorX & 255;
        var yi = floorY & 255;
        var zi = floorZ & 255;
        var xf = x - floorX;
        var yf = y - floorY;
        var zf = z - floorZ;

        var u = Fade(xf);
        var v = Fade(yf);
        var w = Fade(zf);

        var aaa = _permutations[_permutations[_permutations[xi] + yi] + zi];
        var aba = _permutations[_permutations[_permutations[xi] + yi + 1] + zi];
        var aab = _permutations[_permutations[_permutations[xi] + yi] + zi + 1];
        var abb = _permutations[_permutations[_permutations[xi] + yi + 1] + zi + 1];
        var baa = _permutations[_permutations[_permutations[xi + 1] + yi] + zi];
        var bba = _permutations[_permutations[_permutations[xi + 1] + yi + 1] + zi];
        var bab = _permutations[_permutations[_permutations[xi + 1] + yi] + zi + 1];
        var bbb = _permutations[_permutations[_permutations[xi + 1] + yi + 1] + zi + 1];

        var x1 = Lerp(Grad3D(aaa, xf, yf, zf), Grad3D(baa, xf - 1f, yf, zf), u);
        var x2 = Lerp(Grad3D(aba, xf, yf - 1f, zf), Grad3D(bba, xf - 1f, yf - 1f, zf), u);
        var y1 = Lerp(x1, x2, v);

        var x3 = Lerp(Grad3D(aab, xf, yf, zf - 1f), Grad3D(bab, xf - 1f, yf, zf - 1f), u);
        var x4 = Lerp(Grad3D(abb, xf, yf - 1f, zf - 1f), Grad3D(bbb, xf - 1f, yf - 1f, zf - 1f), u);
        var y2 = Lerp(x3, x4, v);

        return Lerp(y1, y2, w);
    }

    public float Fractal2D(float x, float z, NoiseLayerSettings settings)
    {
        var frequency = 1f / MathF.Max(1f, settings.Scale);
        var amplitude = 1f;
        var sum = 0f;
        var normalization = 0f;

        for (var octave = 0; octave < Math.Max(1, settings.Octaves); octave++)
        {
            var sampleX = (x + settings.OffsetX) * frequency;
            var sampleZ = (z + settings.OffsetZ) * frequency;
            sum += Perlin2D(sampleX, sampleZ) * amplitude;
            normalization += amplitude;
            amplitude *= settings.Persistence;
            frequency *= settings.Lacunarity;
        }

        return normalization <= 0f ? 0f : (sum / normalization) * settings.HeightMultiplier;
    }

    public float Fractal3D(float x, float y, float z, float scale, int octaves, float persistence, float lacunarity)
    {
        var frequency = 1f / MathF.Max(1f, scale);
        var amplitude = 1f;
        var sum = 0f;
        var normalization = 0f;

        for (var octave = 0; octave < Math.Max(1, octaves); octave++)
        {
            sum += Perlin3D(x * frequency, y * frequency, z * frequency) * amplitude;
            normalization += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return normalization <= 0f ? 0f : sum / normalization;
    }

    public float Fractal2DNormalized(float x, float z, NoiseLayerSettings settings) => ToUnitRange(Fractal2D(x, z, settings));

    public float Billow2DNormalized(float x, float z, NoiseLayerSettings settings) => ToUnitRange(MathF.Abs(Fractal2D(x, z, settings)) * 2f - 1f);

    public float Billow3DNormalized(float x, float y, float z, float scale, int octaves, float persistence, float lacunarity)
    {
        return ToUnitRange(MathF.Abs(Fractal3D(x, y, z, scale, octaves, persistence, lacunarity)) * 2f - 1f);
    }

    public float Ridged2DNormalized(float x, float z, NoiseLayerSettings settings) => Clamp01(1f - MathF.Abs(Fractal2D(x, z, settings)));

    public float Ridged3DNormalized(float x, float y, float z, float scale, int octaves, float persistence, float lacunarity)
    {
        return Clamp01(1f - MathF.Abs(Fractal3D(x, y, z, scale, octaves, persistence, lacunarity)));
    }

    public float Hash01(int x, int y = 0, int z = 0)
    {
        unchecked
        {
            var hash = _seed;
            hash ^= x * 374761393;
            hash = (hash << 13) ^ hash;
            hash ^= y * 668265263;
            hash = (hash << 7) ^ hash;
            hash ^= z * 1442695041;
            hash = (hash ^ (hash >> 15)) * 1274126177;
            hash ^= hash >> 16;
            return (hash & 0x00FFFFFF) / 16777215f;
        }
    }

    public float HashSigned(int x, int y = 0, int z = 0) => Hash01(x, y, z) * 2f - 1f;

    private static int FastFloor(float value) => value >= 0f ? (int)value : (int)value - 1;

    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    private static float Grad2D(int hash, float x, float z)
    {
        return (hash & 3) switch
        {
            0 => x + z,
            1 => -x + z,
            2 => x - z,
            _ => -x - z
        };
    }

    private static float Grad3D(int hash, float x, float y, float z)
    {
        var h = hash & 15;
        var u = h < 8 ? x : y;
        var v = h < 4 ? y : h is 12 or 14 ? x : z;
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    private static float ToUnitRange(float value) => Clamp01(value * 0.5f + 0.5f);
    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
}
