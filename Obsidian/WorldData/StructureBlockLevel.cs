using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.Redstone;

namespace Obsidian.WorldData;

/// <summary>
/// Places a template into a live level with the structure editor's update flags. Use only under the tick lock,
/// after loading its chunks. Unlike a worldgen view, placed entities spawn in the live level.
/// </summary>
internal sealed class StructureBlockLevel(AbstractLevel level, bool strict) : IWorldGenLevel
{
    private readonly LiveFeatureLevel reads = new(level);

    public long Seed => level.LevelData.RandomSeed;

    public int MinY => level.MinY;

    public int Height => level.Height;

    public int SeaLevel => this.reads.SeaLevel;

    public IRandomSource Random => this.reads.Random;

    public IBlock GetBlock(Vector position) => level.BlockUpdates.GetBlock(position);

    public bool EnsureCanWrite(Vector position) => !level.IsOutsideBuildHeight(position.Y)
        && level.GetLoadedChunk(position.X >> 4, position.Z >> 4) is { IsGenerated: true };

    public bool SetBlock(Vector position, IBlock block)
    {
        if (!this.EnsureCanWrite(position))
            return false;

        var flags = BlockUpdateFlags.Clients;
        if (strict)
        {
            flags |= BlockUpdateFlags.KnownShape | BlockUpdateFlags.SuppressDrops
                | BlockUpdateFlags.SkipBlockEntitySideEffects | BlockUpdateFlags.SkipOnPlace;
        }

        return level.BlockUpdates.SetBlock(position, block, flags);
    }

    internal void PrepareBlockEntity(Vector position)
    {
        // Vanilla uses 820 for the temporary barrier: invisible, known shape, no drops, no block entity
        // removal side effects and no on-place hook. In particular an overwritten chest must not spill its items.
        var flags = BlockUpdateFlags.Invisible | BlockUpdateFlags.KnownShape | BlockUpdateFlags.SuppressDrops
            | BlockUpdateFlags.SkipBlockEntitySideEffects | BlockUpdateFlags.SkipOnPlace;
        level.BlockUpdates.SetBlock(position, BlocksRegistry.Barrier, flags);
    }

    internal void NotifyNeighbors(Vector position) =>
        ((IBlockChangeWorld)level).NotifyNeighborsAsync(position).GetAwaiter().GetResult();

    public int GetHeight(HeightmapType type, int x, int z) => this.reads.GetHeight(type, x, z);

    public BiomeCodec GetBiome(Vector position) => this.reads.GetBiome(position);

    public IBlockEntity? GetBlockEntity(Vector position) => level.GetResidentBlockEntity(position);

    public void SetBlockEntity(Vector position, IBlockEntity entity) =>
        ((IRedstoneLevel)level).SetBlockEntityAt(position, entity);

    public void AddEntity(GeneratedEntity entity)
    {
        var tag = EntityNbt.ToNbt(entity);
        tag.Remove("UUID");
        if (EntityNbt.Load(tag, level) is Entity spawned)
            level.TrySpawnEntity(spawned, hostOwned: true);
    }

    public void MarkForPostProcessing(Vector position)
    {
    }

    public void ScheduleFluidTick(Vector position) => level.Fluids.OnBlockChanged(position, this.GetBlock(position));
}
