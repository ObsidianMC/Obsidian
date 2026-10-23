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
    private const int FocusWidth = 6;

    private readonly long zoomSeed;
    private readonly IBiomeSource biomeSource;
    private readonly int minQuartY;
    private readonly int maxQuartY;
    private readonly Dictionary<(int, int, int), BiomeCodec>? cache;

    // While a chunk is in focus (see FocusOn), the quart biomes its block lookups reach, kept by position, which is much
    // cheaper than hashing a key per lookup: the chunk's 4 by 4 quart columns and the ring around them, over the full height.
    private BiomeCodec?[]? focus;
    private int focusMinQuartX;
    private int focusMinQuartZ;

    // Jitter only depends on the seed and the corner, so a thread's biome managers share one table (see GetJitter).
    [ThreadStatic]
    private static JitterTable? jitterTable;

    /// <param name="biomeSource">Source of the stored (quart) biomes.</param>
    /// <param name="seed">World seed.</param>
    /// <param name="minY">Lowest block Y of the level; quart lookups are clamped to the level height like chunk storage.</param>
    /// <param name="height">Level height in blocks.</param>
    /// <param name="cacheNoiseBiomes">Whether to remember the source's biomes; sources that are cheap to ask don't need it.</param>
    /// <param name="capacity">How many quart biomes to make room for in the cache up front.</param>
    public BiomeManager(IBiomeSource biomeSource, long seed, int minY, int height, bool cacheNoiseBiomes = true, int capacity = 0)
    {
        this.biomeSource = biomeSource;
        this.zoomSeed = ObfuscateSeed(seed);
        this.minQuartY = minY >> 2;
        this.maxQuartY = this.minQuartY + (height >> 2) - 1;
        this.cache = cacheNoiseBiomes ? new(capacity) : null;
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
    /// Forgets the remembered biomes, for when the source's biomes change.
    /// </summary>
    public void ClearCache()
    {
        this.cache?.Clear();
        if (this.focus is not null)
            Array.Clear(this.focus);
    }

    /// <summary>
    /// Forgets the remembered biomes, then remembers those that block lookups in chunk (<paramref name="chunkX"/>,
    /// <paramref name="chunkZ"/>) reach by position. The source must answer the chunk's own quart biomes cheaply, as a
    /// chunk's stored biomes do: lookups read them even for corners they don't pick (see TryGetSharedCornerBiome).
    /// </summary>
    public void FocusOn(int chunkX, int chunkZ)
    {
        this.ClearCache();
        this.focus ??= new BiomeCodec?[FocusWidth * FocusWidth * this.QuartHeight];

        // A block's lookup starts 2 blocks lower (see GetBiome), so its corners reach one quart before the chunk and one
        // after it.
        this.focusMinQuartX = (chunkX << 2) - 1;
        this.focusMinQuartZ = (chunkZ << 2) - 1;
    }

    private int QuartHeight => this.maxQuartY - this.minQuartY + 1;

    /// <param name="quartY">A quart Y within the level.</param>
    private int FocusIndex(int focusX, int quartY, int focusZ) => (focusX * FocusWidth + focusZ) * this.QuartHeight + quartY - this.minQuartY;

    /// <summary>
    /// Gets the stored biome for quart coordinates, clamping Y to the level like chunk biome storage does.
    /// </summary>
    public BiomeCodec GetNoiseBiome(int quartX, int quartY, int quartZ)
    {
        var clampedY = Math.Clamp(quartY, this.minQuartY, this.maxQuartY);
        if (this.focus is not null)
        {
            var focusX = quartX - this.focusMinQuartX;
            var focusZ = quartZ - this.focusMinQuartZ;
            if ((uint)focusX < FocusWidth && (uint)focusZ < FocusWidth)
                return this.focus[this.FocusIndex(focusX, clampedY, focusZ)] ??= this.biomeSource.GetNoiseBiome(quartX, clampedY, quartZ);
        }

        var key = (quartX, clampedY, quartZ);
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

        if (this.TryGetSharedCornerBiome(quartX, quartY, quartZ) is BiomeCodec shared)
            return shared;

        var fractionX = (offsetX & 3) / 4.0;
        var fractionY = (offsetY & 3) / 4.0;
        var fractionZ = (offsetZ & 3) / 4.0;

        var closest = 0;
        var closestDistance = double.PositiveInfinity;

        var jitters = jitterTable ??= new JitterTable();
        if (jitters.Seed != this.zoomSeed)
            jitters.Reset(this.zoomSeed);

        for (var corner = 0; corner < 8; corner++)
        {
            var lowX = (corner & 4) == 0;
            var lowY = (corner & 2) == 0;
            var lowZ = (corner & 1) == 0;

            var jitter = GetJitter(jitters, lowX ? quartX : quartX + 1, lowY ? quartY : quartY + 1, lowZ ? quartZ : quartZ + 1);
            var distance = Square((lowZ ? fractionZ : fractionZ - 1.0) + jitter.Z) + Square((lowY ? fractionY : fractionY - 1.0) + jitter.Y)
                + Square((lowX ? fractionX : fractionX - 1.0) + jitter.X);

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

    /// <summary>
    /// The biome of the 8 corners a block's lookup picks from, when they're all the same: then it's the answer whichever
    /// corner the jitter picks. Corners in the focused chunk are read as needed; those outside it are only compared when
    /// remembered, so the source isn't asked for corners outside the chunk that the lookup wouldn't pick.
    /// </summary>
    private BiomeCodec? TryGetSharedCornerBiome(int quartX, int quartY, int quartZ)
    {
        if (this.focus is null)
            return null;

        var focusX = quartX - this.focusMinQuartX;
        var focusZ = quartZ - this.focusMinQuartZ;
        if ((uint)focusX >= FocusWidth - 1 || (uint)focusZ >= FocusWidth - 1)
            return null;

        var lowY = Math.Clamp(quartY, this.minQuartY, this.maxQuartY);
        var highY = Math.Clamp(quartY + 1, this.minQuartY, this.maxQuartY);
        var shared = this.GetFocusedCornerBiome(focusX, lowY, focusZ);
        if (shared is null)
            return null;

        for (var corner = 1; corner < 8; corner++)
        {
            var biome = this.GetFocusedCornerBiome(focusX + (corner >> 2), (corner & 2) == 0 ? lowY : highY, focusZ + (corner & 1));
            if (!ReferenceEquals(biome, shared))
                return null;
        }

        return shared;
    }

    /// <summary>
    /// A corner's biome if it's remembered or in the focused chunk, otherwise <c>null</c>.
    /// </summary>
    private BiomeCodec? GetFocusedCornerBiome(int focusX, int quartY, int focusZ)
    {
        ref var biome = ref this.focus![this.FocusIndex(focusX, quartY, focusZ)];
        if (biome is null && focusX is >= 1 and <= 4 && focusZ is >= 1 and <= 4)
            biome = this.biomeSource.GetNoiseBiome(this.focusMinQuartX + focusX, quartY, this.focusMinQuartZ + focusZ);

        return biome;
    }

    /// <summary>
    /// The seeded jitter of a quart corner (vanilla's <c>getFiddledDistance</c> without the distance), kept for the
    /// recently used corners: a block measures 8 corners, and a corner serves the 64 blocks around it.
    /// </summary>
    private static (double X, double Y, double Z) GetJitter(JitterTable table, int x, int y, int z)
    {
        ref var entry = ref table.Entries[(x & 1) | (z & 1) << 1 | (y & 127) << 2];
        if (entry.X != x || entry.Y != y || entry.Z != z || !entry.Set)
        {
            var zoomSeed = table.Seed;
            var hash = zoomSeed;
            hash = NextLcg(hash, x);
            hash = NextLcg(hash, y);
            hash = NextLcg(hash, z);
            hash = NextLcg(hash, x);
            hash = NextLcg(hash, y);
            hash = NextLcg(hash, z);
            var fiddleX = Fiddle(hash);
            hash = NextLcg(hash, zoomSeed);
            var fiddleY = Fiddle(hash);
            hash = NextLcg(hash, zoomSeed);
            var fiddleZ = Fiddle(hash);

            entry = new CornerJitter(x, y, z, fiddleX, fiddleY, fiddleZ, true);
        }

        return (entry.FiddleX, entry.FiddleY, entry.FiddleZ);
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

    private readonly record struct CornerJitter(int X, int Y, int Z, double FiddleX, double FiddleY, double FiddleZ, bool Set);

    /// <summary>
    /// The jitter of recently used corners for one obfuscated seed.
    /// </summary>
    private sealed class JitterTable
    {
        public long Seed { get; private set; }

        // Two corners along X and Z by 128 along Y: the corners a column of blocks measures.
        public CornerJitter[] Entries { get; } = new CornerJitter[512];

        public JitterTable() => this.Reset(0L);

        public void Reset(long seed)
        {
            this.Seed = seed;
            Array.Clear(this.Entries);
        }
    }
}
