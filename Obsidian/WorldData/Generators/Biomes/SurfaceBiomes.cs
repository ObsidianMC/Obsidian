using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.WorldData.Generators.Mojang;

namespace Obsidian.WorldData.Generators.Biomes;

/// <summary>
/// The block biomes of one chunk's surface: vanilla's zoomed lookup over the biomes stored in the chunk and the ring of
/// quarts around it, read once into a table.
/// </summary>
/// <remarks>
/// <para>
/// A block's lookup picks one of the eight quart corners around it by seeded jitter, with the corners' y clamped to
/// the chunk's storage. The pick is a pure function of the position, so reading the reachable quarts up front gives
/// the same biome as asking the source for each block. When all eight corners hold the same biome, it is the answer
/// whichever corner the jitter picks, so such cells are answered from the table without measuring the jitter.
/// </para>
/// <para>
/// The material rules' biome conditions also use <see cref="Names"/>: a condition naming none of the biomes the chunk
/// can reach is false at every block, without a lookup.
/// </para>
/// </remarks>
internal sealed class SurfaceBiomes
{
    // A block's lookup starts 2 blocks lower, so its corners reach one quart before the chunk and one after it.
    private const int QuartWidth = 6;
    private const int CellWidth = QuartWidth - 1;

    private readonly long zoomSeed;
    private readonly int minQuartX;
    private readonly int minQuartZ;
    private readonly int minQuartY;
    private readonly int quartHeight;

    // By (x * QuartWidth + z) * quartHeight + y from the minimum quarts.
    private readonly BiomeCodec[] quarts;

    // The biome all eight corners of a cell share, or null, by (x * CellWidth + z) * cellHeight + y. A cell's y runs
    // from one below the lowest quart to the highest: lower cells clamp both corners to the lowest quart, higher
    // ones to the highest.
    private readonly BiomeCodec?[] cells;
    private readonly int cellHeight;

    /// <param name="source">The stored quart biomes, with y within the chunk's storage.</param>
    /// <param name="zoomSeed">The obfuscated seed (<see cref="BiomeManager.ObfuscateSeed"/>).</param>
    public SurfaceBiomes(IBiomeSource source, long zoomSeed, int chunkX, int chunkZ, int minY, int height)
    {
        this.zoomSeed = zoomSeed;
        this.minQuartX = (chunkX << 2) - 1;
        this.minQuartZ = (chunkZ << 2) - 1;
        this.minQuartY = minY >> 2;
        this.quartHeight = height >> 2;
        this.cellHeight = this.quartHeight + 1;
        this.quarts = new BiomeCodec[QuartWidth * QuartWidth * this.quartHeight];
        this.cells = new BiomeCodec?[CellWidth * CellWidth * this.cellHeight];

        var names = new List<string>();

        for (var x = 0; x < QuartWidth; x++)
        {
            for (var z = 0; z < QuartWidth; z++)
            {
                for (var y = 0; y < this.quartHeight; y++)
                {
                    var biome = source.GetNoiseBiome(this.minQuartX + x, this.minQuartY + y, this.minQuartZ + z);
                    this.quarts[this.QuartIndex(x, y, z)] = biome;

                    if (!names.Contains(biome.Name))
                        names.Add(biome.Name);
                }
            }
        }

        this.Names = names;
        this.FillCells();
    }

    /// <summary>The distinct biomes the chunk's block lookups can return.</summary>
    public IReadOnlyList<string> Names { get; }

    private int QuartIndex(int x, int y, int z) => (x * QuartWidth + z) * this.quartHeight + y;

    private void FillCells()
    {
        for (var x = 0; x < CellWidth; x++)
        {
            for (var z = 0; z < CellWidth; z++)
            {
                for (var cellY = 0; cellY < this.cellHeight; cellY++)
                {
                    var lowY = Math.Max(cellY - 1, 0);
                    var highY = Math.Min(cellY, this.quartHeight - 1);
                    this.cells[(x * CellWidth + z) * this.cellHeight + cellY] = this.SharedBiome(x, lowY, highY, z);
                }
            }
        }
    }

    private BiomeCodec? SharedBiome(int x, int lowY, int highY, int z)
    {
        var shared = this.quarts[this.QuartIndex(x, lowY, z)];

        for (var corner = 1; corner < 8; corner++)
        {
            var cornerX = x + (corner >> 2);
            var cornerY = (corner & 2) == 0 ? lowY : highY;
            var cornerZ = z + (corner & 1);

            if (!ReferenceEquals(this.quarts[this.QuartIndex(cornerX, cornerY, cornerZ)], shared))
                return null;
        }

        return shared;
    }

    /// <summary>
    /// The biome at a block of the chunk, or <c>null</c> for a block outside it, which the table doesn't reach.
    /// </summary>
    public BiomeCodec? Get(int x, int y, int z)
    {
        var cellX = ((x - 2) >> 2) - this.minQuartX;
        var cellZ = ((z - 2) >> 2) - this.minQuartZ;

        if ((uint)cellX >= CellWidth || (uint)cellZ >= CellWidth)
            return null;

        var cellY = Math.Clamp(((y - 2) >> 2) - this.minQuartY + 1, 0, this.cellHeight - 1);

        if (this.cells[(cellX * CellWidth + cellZ) * this.cellHeight + cellY] is BiomeCodec shared)
            return shared;

        var quart = BiomeManager.QuartAtBlock(this.zoomSeed, x, y, z);
        var quartY = Math.Clamp(quart.Y - this.minQuartY, 0, this.quartHeight - 1);

        return this.quarts[this.QuartIndex(quart.X - this.minQuartX, quartY, quart.Z - this.minQuartZ)];
    }
}
