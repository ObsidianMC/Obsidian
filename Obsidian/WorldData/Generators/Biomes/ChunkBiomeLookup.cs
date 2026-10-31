using Obsidian.WorldData.Generators.Mojang;
using System.Threading;

namespace Obsidian.WorldData.Generators.Biomes;

/// <summary>
/// The block biomes of one chunk's terrain status, the same as <see cref="BiomeTerrain.BiomeAt"/> gives asked block by
/// block: vanilla's zoomed lookup over fresh climate searches. One instance serves one chunk on one thread.
/// </summary>
/// <remarks>
/// Material rules ask for a biome at nearly every block, about 33,000 times a chunk, mostly in the same few quarts.
/// So a quart's climate and the part of its search that doesn't depend on history (see
/// <see cref="BiomeParameterTree{T}.Prepare"/>) are worked out once per chunk. Each lookup still settles its search
/// against the thread's previous result, since vanilla's climate search keeps the previous result on an exact tie:
/// the results, and the previous result later searches start from, stay those of a search per block.
/// </remarks>
internal sealed class ChunkBiomeLookup
{
    // The chunk's 4 quart columns along each axis and one on each side: a block's lookup starts 2 blocks lower (see
    // BiomeManager.GetBiome), so its corners reach one quart before the chunk and one after it.
    private const int Width = 6;

    // A thread's prepared quarts, which each lookup marks with its stamp so that a new chunk needn't clear them.
    [ThreadStatic]
    private static Entry[]? entries;

    // A thread's quart columns, with the climate fields that are the same at every y, stamped like the entries.
    [ThreadStatic]
    private static ColumnEntry[]? columns;

    private static long lastStamp;

    private readonly ClimateSource source;
    private readonly BiomeParameterTree<string>? tree;
    private readonly long zoomSeed;
    private readonly long stamp;
    private readonly int minQuartX;
    private readonly int minQuartY;
    private readonly int minQuartZ;
    private readonly int quartHeight;

    // The cell the last lookup measured, and the entry of a corner whose first leaf (or End biome) all 8 corners
    // share, or -1. Blocks in a column come one cell after another, and most cells lie inside one biome.
    private bool hasCell;
    private int cellX;
    private int cellY;
    private int cellZ;
    private int sharedCorner = -1;

    // The search prepared last, from whose leaf the next one starts.
    private BiomeParameterTree<string>.Prepared lastPrepared;

    /// <param name="zoomSeed">The obfuscated seed (<see cref="BiomeManager.ObfuscateSeed"/>).</param>
    public ChunkBiomeLookup(ClimateSource source, long zoomSeed, IChunk chunk)
    {
        this.source = source;
        this.tree = source.Tree;
        this.zoomSeed = zoomSeed;
        this.stamp = Interlocked.Increment(ref lastStamp);
        this.minQuartX = (chunk.X << 2) - 1;
        this.minQuartZ = (chunk.Z << 2) - 1;

        // The quarts the chunk's blocks reach; fresh searches aren't clamped to the build height.
        this.minQuartY = (chunk.MinY - 2) >> 2;
        var maxQuartY = ((chunk.MinY + chunk.Height - 3) >> 2) + 1;
        this.quartHeight = maxQuartY - this.minQuartY + 1;
    }

    /// <summary>The biome at a block of the chunk.</summary>
    public string BiomeAt(int x, int y, int z)
    {
        var table = this.Table();
        var cellX = (x - 2) >> 2;
        var cellY = (y - 2) >> 2;
        var cellZ = (z - 2) >> 2;

        if (!this.hasCell || this.cellX != cellX || this.cellY != cellY || this.cellZ != cellZ)
        {
            this.sharedCorner = this.FindSharedCorner(table, cellX, cellY, cellZ);
            this.cellX = cellX;
            this.cellY = cellY;
            this.cellZ = cellZ;
            this.hasCell = true;
        }

        if (this.sharedCorner >= 0 && this.TryShared(in table[this.sharedCorner]) is string shared)
        {
            return shared;
        }

        var quart = BiomeManager.QuartAtBlock(this.zoomSeed, x, y, z);
        var index = this.Index(quart.X, quart.Y, quart.Z);

        if (index < 0)
        {
            return this.source.GetNoiseBiome(quart.X, quart.Y, quart.Z).Name;
        }

        ref var entry = ref this.Prepared(table, index, quart.X, quart.Y, quart.Z);

        return this.tree is null ? entry.Biome! : this.tree.Search(entry.Search);
    }

    /// <summary>
    /// The shared corner's biome when it's the answer whichever corner the jitter picks: all 8 corners have the same
    /// first leaf and it's the thread's previous result, which each corner's search then returns. In the End, all 8
    /// corners have the same biome.
    /// </summary>
    private string? TryShared(in Entry corner)
    {
        if (corner.Stamp != this.stamp)
        {
            // Another chunk's lookup on this thread replaced the corner.
            this.hasCell = false;
            return null;
        }

        if (this.tree is null)
        {
            return corner.Biome;
        }

        return this.tree.IsPrevious(corner.Search) ? this.tree.Search(corner.Search) : null;
    }

    private int FindSharedCorner(Entry[] table, int cellX, int cellY, int cellZ)
    {
        var shared = -1;
        object? sharedValue = null;

        for (var corner = 0; corner < 8; corner++)
        {
            var quartX = cellX + (corner >> 2);
            var quartY = cellY + ((corner >> 1) & 1);
            var quartZ = cellZ + (corner & 1);
            var index = this.Index(quartX, quartY, quartZ);

            if (index < 0)
            {
                return -1;
            }

            ref var entry = ref this.Prepared(table, index, quartX, quartY, quartZ);
            var value = this.tree is null ? entry.Biome : entry.Search.First;

            if (shared < 0)
            {
                shared = index;
                sharedValue = value;
            }
            else if (!ReferenceEquals(value, sharedValue))
            {
                return -1;
            }
        }

        return shared;
    }

    private Entry[] Table()
    {
        var size = Width * Width * this.quartHeight;
        var table = entries;

        if (table is null || table.Length < size)
        {
            entries = table = new Entry[size];
        }

        return table;
    }

    /// <returns>The quart's entry in the table, or -1 for a quart the chunk's blocks don't reach.</returns>
    private int Index(int quartX, int quartY, int quartZ)
    {
        var localX = quartX - this.minQuartX;
        var localY = quartY - this.minQuartY;
        var localZ = quartZ - this.minQuartZ;
        var inside = (uint)localX < Width && (uint)localY < this.quartHeight && (uint)localZ < Width;

        return inside ? (localX * Width + localZ) * this.quartHeight + localY : -1;
    }

    private ref Entry Prepared(Entry[] table, int index, int quartX, int quartY, int quartZ)
    {
        ref var entry = ref table[index];

        if (entry.Stamp == this.stamp)
        {
            return ref entry;
        }

        if (this.tree is null)
        {
            var biome = this.source.GetNoiseBiome(quartX, quartY, quartZ).Name;
            entry = new Entry(this.stamp, biome, default);
        }
        else
        {
            var column = this.ColumnClimate(quartX, quartZ);
            var target = this.source.Sample(quartX, quartY, quartZ, in column);
            this.lastPrepared = this.tree.Prepare(target, in this.lastPrepared);
            entry = new Entry(this.stamp, null, this.lastPrepared);
        }

        return ref entry;
    }

    private TargetPoint ColumnClimate(int quartX, int quartZ)
    {
        var table = columns ??= new ColumnEntry[Width * Width];
        ref var column = ref table[(quartX - this.minQuartX) * Width + quartZ - this.minQuartZ];

        if (column.Stamp != this.stamp)
        {
            column = new ColumnEntry(this.stamp, this.source.SampleColumn(quartX, quartZ));
        }

        return column.Climate;
    }

    private readonly record struct ColumnEntry(long Stamp, TargetPoint Climate);

    /// <param name="Biome">The End's biome, which takes no search.</param>
    /// <param name="Search">The prepared search elsewhere.</param>
    private readonly record struct Entry(long Stamp, string? Biome, BiomeParameterTree<string>.Prepared Search);
}
