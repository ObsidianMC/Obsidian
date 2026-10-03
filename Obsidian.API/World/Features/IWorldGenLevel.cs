using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator.RandomSources;

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

    /// <summary>
    /// The level's own random, like vanilla's <c>WorldGenRegion.getRandom</c>: seeded from the world seed and the chunk
    /// being decorated, and separate from the random of the feature or structure being placed.
    /// </summary>
    public IRandomSource Random { get; }

    public IBlock GetBlock(Vector position);

    /// <summary>
    /// The state id of the block at <paramref name="position"/>, like <see cref="GetBlock"/>; levels that store state ids
    /// read it without resolving the block.
    /// </summary>
    public int GetStateId(Vector position) => this.GetBlock(position).GetHashCode();

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
    /// Marks a block to be checked against its neighbors once its chunk is complete (fence connections, torch support...),
    /// like vanilla's <c>markPosForPostprocessing</c>.
    /// </summary>
    public void MarkForPostProcessing(Vector position);

    /// <summary>
    /// Schedules a fluid update at <paramref name="position"/> for when generation completes, like vanilla's
    /// <c>scheduleTick</c> with a fluid (springs, geode cracks).
    /// </summary>
    public void ScheduleFluidTick(Vector position);

    public bool IsOutsideBuildHeight(int y) => y < this.MinY || y >= this.MinY + this.Height;
}
