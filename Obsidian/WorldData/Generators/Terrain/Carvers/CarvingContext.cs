using Obsidian.API.World.Generator.RandomSources;
using System.Numerics;

namespace Obsidian.WorldData.Generators.Terrain.Carvers;

internal sealed class CarvingContext(TerrainContext terrain)
{
    public IChunk Chunk => terrain.Chunk;

    public int MinY => terrain.Volume.MinBlockY;

    public int Height => terrain.Volume.SizeY;

    public CarvingMask Mask { get; } = new(
        terrain.Volume.MinBlockY + 1,
        terrain.Volume.MinBlockY + terrain.Volume.SizeY - 8);

    public CarvingBuffers Buffers { get; } = new(terrain.Volume.SizeY);
}

/// <summary>Temporary, x-major columns of carving bits, discarded when terrain finishes.</summary>
internal sealed class CarvingMask(int minY, int maxY)
{
    private readonly int height = maxY - minY + 1;
    private readonly ulong[] words = new ulong[(256 * (maxY - minY + 1) + 63) >> 6];

    public int MinY { get; } = minY;

    public int MaxY { get; } = maxY;

    public bool Get(int x, int y, int z)
    {
        var bit = this.Bit(x, y, z);
        return (this.words[bit >> 6] >> (bit & 63) & 1) != 0;
    }

    public void Set(int x, int y, int z)
    {
        var bit = this.Bit(x, y, z);
        this.words[bit >> 6] |= 1UL << (bit & 63);
    }

    /// <summary>The lowest carved y of the column from <paramref name="fromY"/> up, or MaxY + 1.</summary>
    public int NextCarved(int x, int z, int fromY) => this.Next(x, z, fromY, carved: true);

    /// <summary>The lowest uncarved y of the column from <paramref name="fromY"/> up, or MaxY + 1.</summary>
    public int NextUncarved(int x, int z, int fromY) => this.Next(x, z, fromY, carved: false);

    // A column's bits are consecutive, so a run is found a word at a time.
    private int Next(int x, int z, int fromY, bool carved)
    {
        var end = this.Bit(x, this.MaxY, z) + 1;
        var bit = this.Bit(x, fromY, z);

        while (bit < end)
        {
            var word = this.words[bit >> 6];

            if (!carved)
                word = ~word;

            word >>= bit & 63;

            if (word != 0)
            {
                var found = Math.Min(bit + BitOperations.TrailingZeroCount(word), end);
                return found - end + this.MaxY + 1;
            }

            bit = (bit | 63) + 1;
        }

        return this.MaxY + 1;
    }

    private int Bit(int x, int y, int z) => (x * 16 + z) * this.height + y - this.MinY;
}

internal sealed class CarvingBuffers(int height)
{
    private readonly List<LegacyRandomSource> randoms = [];

    public float[] WidthFactors { get; } = new float[height];

    public LegacyRandomSource TunnelRandom(int depth, long seed)
    {
        while (this.randoms.Count <= depth)
        {
            this.randoms.Add(new LegacyRandomSource(0L));
        }

        var random = this.randoms[depth];
        random.SetSeed(seed);

        return random;
    }
}
