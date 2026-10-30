using Obsidian.API.World.Generator.RandomSources;
using System.Collections;

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
    private readonly BitArray bits = new(256 * (maxY - minY + 1));

    public int MinY { get; } = minY;

    public int MaxY { get; } = maxY;

    public bool Get(int x, int y, int z) => this.bits[(x * 16 + z) * this.height + y - this.MinY];

    public void Set(int x, int y, int z) => this.bits[(x * 16 + z) * this.height + y - this.MinY] = true;
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
