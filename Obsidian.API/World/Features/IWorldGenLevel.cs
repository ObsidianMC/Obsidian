using Obsidian.API.Registry.Codecs.Biomes;

namespace Obsidian.API.World.Features;

/// <summary>
/// Synchronous block access used while generating a chunk, like vanilla's WorldGenLevel.
/// </summary>
/// <remarks>
/// During feature placement this covers the chunk being decorated and its 8 neighbors; writes outside the
/// writable area are ignored and <see cref="SetBlock"/> returns <c>false</c>.
/// </remarks>
public interface IWorldGenLevel
{
    /// <summary>
    /// World seed.
    /// </summary>
    public long Seed { get; }

    public int MinY { get; }

    public int Height { get; }

    public int SeaLevel { get; }

    public IBlock GetBlock(Vector position);

    /// <summary>
    /// Sets a block, updating heightmaps. Returns <c>false</c> when the position can't be written.
    /// </summary>
    public bool SetBlock(Vector position, IBlock block);

    /// <summary>
    /// Whether <paramref name="position"/> is inside the area this level may write to.
    /// </summary>
    public bool EnsureCanWrite(Vector position);

    /// <summary>
    /// First free Y above the highest block matching the heightmap at (<paramref name="x"/>, <paramref name="z"/>).
    /// </summary>
    public int GetHeight(HeightmapType type, int x, int z);

    public BiomeCodec GetBiome(Vector position);

    /// <summary>
    /// Stores a block entity (chests, spawners...) created by a feature.
    /// </summary>
    public void SetBlockEntity(Vector position, IBlockEntity blockEntity);

    /// <summary>
    /// Gets the block entity at <paramref name="position"/>, or <c>null</c> when there's none.
    /// </summary>
    public IBlockEntity? GetBlockEntity(Vector position);

    /// <summary>
    /// Adds an entity to the chunk at its position; it spawns once that chunk is complete.
    /// </summary>
    public void AddEntity(GeneratedEntity entity);

    /// <summary>
    /// Schedules a fluid update at <paramref name="position"/> for when generation completes, like vanilla's
    /// <c>scheduleTick</c> with a fluid (springs, geode cracks).
    /// </summary>
    public void ScheduleFluidTick(Vector position);

    public bool IsOutsideBuildHeight(int y) => y < this.MinY || y >= this.MinY + this.Height;
}
