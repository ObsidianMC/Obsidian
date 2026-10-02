using Obsidian.API.Registry.Codecs.Biomes;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Block-level biome lookup. Biomes are stored per 4x4x4 cell; this picks one of the eight surrounding cells
/// with seeded jitter so biome borders aren't grid aligned, like vanilla's BiomeManager.
/// </summary>
internal sealed class BiomeManager
{
    private readonly long zoomSeed;
    private readonly IBiomeSource biomeSource;
    private readonly int minQuartY;
    private readonly int maxQuartY;
    private readonly Dictionary<(int, int, int), BiomeCodec>? cache;

    /// <param name="biomeSource">Source of the stored (quart) biomes.</param>
    /// <param name="seed">World seed.</param>
    /// <param name="minY">Lowest block Y of the level; quart lookups are clamped to the level height like chunk storage.</param>
    /// <param name="height">Level height in blocks.</param>
    /// <param name="cacheNoiseBiomes">Whether to remember the source's biomes; sources that are cheap to ask don't need it.</param>
    public BiomeManager(IBiomeSource biomeSource, long seed, int minY, int height, bool cacheNoiseBiomes = true)
    {
        this.biomeSource = biomeSource;
        this.zoomSeed = ObfuscateSeed(seed);
        this.minQuartY = minY >> 2;
        this.maxQuartY = this.minQuartY + (height >> 2) - 1;
        this.cache = cacheNoiseBiomes ? [] : null;
    }

    /// <summary>
    /// Hashes the world seed so the biome jitter doesn't reveal it (SHA-256, little-endian like Guava's hashLong).
    /// </summary>
    public static long ObfuscateSeed(long seed)
    {
        Span<byte> input = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(input, seed);

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(input, hash);

        return BinaryPrimitives.ReadInt64LittleEndian(hash);
    }

    /// <summary>
    /// Gets the stored biome for quart coordinates, clamping Y to the level like chunk biome storage does.
    /// </summary>
    public BiomeCodec GetNoiseBiome(int quartX, int quartY, int quartZ)
    {
        var key = (quartX, Math.Clamp(quartY, this.minQuartY, this.maxQuartY), quartZ);
        if (this.cache is null)
            return this.biomeSource.GetNoiseBiome(key.quartX, key.Item2, key.quartZ);

        if (!this.cache.TryGetValue(key, out var biome))
        {
            biome = this.biomeSource.GetNoiseBiome(key.quartX, key.Item2, key.quartZ);
            this.cache[key] = biome;
        }

        return biome;
    }

    /// <summary>
    /// Gets the biome at a block position.
    /// </summary>
    public BiomeCodec GetBiome(int x, int y, int z)
    {
        var offsetX = x - 2;
        var offsetY = y - 2;
        var offsetZ = z - 2;
        var quartX = offsetX >> 2;
        var quartY = offsetY >> 2;
        var quartZ = offsetZ >> 2;
        var fractionX = (offsetX & 3) / 4.0;
        var fractionY = (offsetY & 3) / 4.0;
        var fractionZ = (offsetZ & 3) / 4.0;

        var closest = 0;
        var closestDistance = double.PositiveInfinity;

        for (var corner = 0; corner < 8; corner++)
        {
            var lowX = (corner & 4) == 0;
            var lowY = (corner & 2) == 0;
            var lowZ = (corner & 1) == 0;

            var distance = FiddledDistance(this.zoomSeed,
                lowX ? quartX : quartX + 1, lowY ? quartY : quartY + 1, lowZ ? quartZ : quartZ + 1,
                lowX ? fractionX : fractionX - 1.0, lowY ? fractionY : fractionY - 1.0, lowZ ? fractionZ : fractionZ - 1.0);

            if (closestDistance > distance)
            {
                closest = corner;
                closestDistance = distance;
            }
        }

        return this.GetNoiseBiome(
            (closest & 4) == 0 ? quartX : quartX + 1,
            (closest & 2) == 0 ? quartY : quartY + 1,
            (closest & 1) == 0 ? quartZ : quartZ + 1);
    }

    private static double FiddledDistance(long seed, int x, int y, int z, double fractionX, double fractionY, double fractionZ)
    {
        var hash = seed;
        hash = NextLcg(hash, x);
        hash = NextLcg(hash, y);
        hash = NextLcg(hash, z);
        hash = NextLcg(hash, x);
        hash = NextLcg(hash, y);
        hash = NextLcg(hash, z);
        var fiddleX = Fiddle(hash);
        hash = NextLcg(hash, seed);
        var fiddleY = Fiddle(hash);
        hash = NextLcg(hash, seed);
        var fiddleZ = Fiddle(hash);

        return Square(fractionZ + fiddleZ) + Square(fractionY + fiddleY) + Square(fractionX + fiddleX);
    }

    private static double Fiddle(long hash)
    {
        var value = (double)(int)MathMod(hash >> 24, 1024L) / 1024.0;
        return (value - 0.5) * 0.9;
    }

    private static long NextLcg(long value, long increment) =>
        unchecked(value * (value * 6364136223846793005L + 1442695040888963407L) + increment);

    private static long MathMod(long value, long modulus) => ((value % modulus) + modulus) % modulus;

    private static double Square(double value) => value * value;
}
