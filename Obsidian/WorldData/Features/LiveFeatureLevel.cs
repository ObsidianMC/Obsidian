using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features;

/// <summary>
/// A running level as features see it, for features placed outside world generation, like a sapling's tree: reads
/// and writes go to the level's loaded chunks, and <see cref="Commit"/> shows the players what changed.
/// </summary>
/// <remarks>
/// Positions in chunks that aren't loaded read as air and can't be written, as are positions outside the build range.
/// Use it on the level's tick, which no other write to its chunks races. Light isn't worked out again, as for blocks
/// players place.
/// </remarks>
internal sealed class LiveFeatureLevel(AbstractLevel level) : IWorldGenLevel
{
    // The block each written position had before its first write, in the order they were first written.
    private readonly Dictionary<Vector, IBlock> originals = [];

    public long Seed => level.LevelData.RandomSeed;

    public int MinY => level.MinY;

    public int Height => level.Height;

    public int SeaLevel => level.Generator is MojangGenerator generator ? generator.SeaLevel : 63;

    public IRandomSource Random => field ??= new XoroshiroRandomSource(System.Random.Shared.NextInt64());

    /// <summary>
    /// The level's logger, for features that fail.
    /// </summary>
    public Microsoft.Extensions.Logging.ILogger Logger => level.Logger;

    /// <summary>
    /// The generation context features placed here take their heights from: the level's build range.
    /// </summary>
    public WorldGenerationContext Generation => new(level.MinY, level.Height);

    public IBlock GetBlock(Vector position)
    {
        if (level.IsOutsideBuildHeight(position.Y))
            return BlocksRegistry.VoidAir;

        return ChunkAt(position)?.GetBlock(position) ?? BlocksRegistry.Air;
    }

    public bool SetBlock(Vector position, IBlock block)
    {
        if (!this.EnsureCanWrite(position))
            return false;

        var chunk = ChunkAt(position)!;
        this.originals.TryAdd(position, chunk.GetBlock(position));

        chunk.SetBlock(position, block);
        DataBlockEntity.ApplyBlockChange(chunk, position, block);

        return true;
    }

    public bool EnsureCanWrite(Vector position) =>
        !level.IsOutsideBuildHeight(position.Y) && ChunkAt(position) is not null;

    public int GetHeight(HeightmapType type, int x, int z)
    {
        if (ChunkAt(new Vector(x, 0, z)) is not IChunk chunk)
            return this.MinY;

        for (var y = this.MinY + this.Height - 1; y >= this.MinY; y--)
        {
            if (MatchesHeightmap(type, chunk.GetBlock(x, y, z)))
                return y + 1;
        }

        return this.MinY;
    }

    public BiomeCodec GetBiome(Vector position) =>
        ChunkAt(position)?.GetBiome(position) ?? CodecRegistry.Biomes.Plains;

    public void SetBlockEntity(Vector position, IBlockEntity blockEntity) =>
        ChunkAt(position)?.SetBlockEntity(position.X, position.Y, position.Z, blockEntity);

    public IBlockEntity? GetBlockEntity(Vector position) => level.IsOutsideBuildHeight(position.Y)
        ? null
        : ChunkAt(position)?.GetBlockEntity(position.X, position.Y, position.Z);

    public void AddEntity(GeneratedEntity entity)
    {
        // Trees and the plants bone meal grows add no entities: a bee nest's bees live in its block entity.
    }

    public void MarkForPostProcessing(Vector position)
    {
        // A running level has nothing to post-process: features' own shape updates already ran.
    }

    public void ScheduleFluidTick(Vector position) => level.Fluids.OnBlockChanged(position, this.GetBlock(position));

    /// <summary>
    /// Sends the players the blocks that changed since the level was made, and lets the level react to them (mob
    /// eggs, fluids). A position written back to what it was isn't sent.
    /// </summary>
    public void Commit()
    {
        foreach (var (position, original) in this.originals)
        {
            var block = this.GetBlock(position);
            if (block.Equals(original))
                continue;

            level.BroadcastBlockChange(block, position);
            level.TrackMobEgg(position, block);
        }

        this.originals.Clear();
    }

    private IChunk? ChunkAt(Vector position) => level.GetLoadedChunk(position.X >> 4, position.Z >> 4);

    // Vanilla's heightmap predicates, as the legacy generator's tree level approximates them with block physics.
    private static bool MatchesHeightmap(HeightmapType type, IBlock block) => type switch
    {
        HeightmapType.WorldSurface or HeightmapType.WorldSurfaceWG => !block.IsAir,
        HeightmapType.OceanFloor or HeightmapType.OceanFloorWG => block.BlocksMotion(),
        HeightmapType.MotionBlocking => block.BlocksMotion() || block.HasFluid(),
        _ => (block.BlocksMotion() || block.HasFluid()) && !TagsRegistry.Block.Leaves.Entries.Contains(block.RegistryId)
    };
}
