using Obsidian.API.BlockEntities;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Nbt;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Mojang;

namespace Obsidian.WorldData;

/// <summary>
/// Evaluates placement against private block/entity copies. A missing FULL chunk suspends the plan before any
/// live writes; after loading that chunk the caller starts over against the current world and the same random.
/// The template's actual entry list and conditional waterlogging queries determine demand, never its size.
/// </summary>
internal sealed class JigsawPlacementPlan(AbstractLevel level, IRandomSource random) : IWorldGenLevel
{
    internal sealed class ChunkNeeded(Vector position) : Exception
    {
        internal Vector Position { get; } = position;
    }

    // A biome query may need only BIOMES, rather than FULL. Generating its terrain would exceed vanilla's demand.
    internal sealed class UnsupportedAccess : Exception;

    private readonly Dictionary<Vector, IBlock> blocks = [];
    private readonly Dictionary<Vector, IBlockEntity?> entities = [];
    private readonly LiveFeatureLevel reads = new(level);

    public long Seed => level.LevelData.RandomSeed;

    public int MinY => level.MinY;

    public int Height => level.Height;

    public int SeaLevel => this.reads.SeaLevel;

    public IRandomSource Random => random;

    public IBlock GetBlock(Vector position)
    {
        if (!this.EnsureCanWrite(position))
            return BlocksRegistry.VoidAir;

        this.RequireChunk(position);
        return this.blocks.GetValueOrDefault(position) ?? level.BlockUpdates.GetBlock(position);
    }

    public bool EnsureCanWrite(Vector position) => !level.IsOutsideBuildHeight(position.Y)
        && position.X is >= -30000000 and < 30000000 && position.Z is >= -30000000 and < 30000000;

    public bool SetBlock(Vector position, IBlock block)
    {
        if (!this.EnsureCanWrite(position))
            return false;

        var previous = this.GetBlock(position);
        if (previous.IsSameState(block))
            return false;

        this.blocks[position] = block;
        if (previous.RegistryId != block.RegistryId)
        {
            var type = block.BlockEntityType();
            this.entities[position] = type is null
                ? null : BlockEntityNbt.Create(type, position, pluginInstances: false);
        }

        return true;
    }

    public IBlockEntity? GetBlockEntity(Vector position)
    {
        if (!this.EnsureCanWrite(position))
            return null;

        this.RequireChunk(position);
        if (!this.entities.TryGetValue(position, out var entity))
        {
            // Loading template NBT must never mutate the resident entity during planning.
            if (level.GetResidentBlockEntity(position) is BlockEntity resident
                && BlockEntityNbt.Save(resident) is NbtCompound tag)
            {
                entity = BlockEntityNbt.Load(tag, resident.Id, position, pluginInstances: false);
            }

            this.entities[position] = entity;
        }

        return entity;
    }

    public void SetBlockEntity(Vector position, IBlockEntity entity)
    {
        this.RequireChunk(position);
        this.entities[position] = entity;
    }

    public int GetHeight(HeightmapType type, int x, int z)
    {
        this.RequireChunk(new Vector(x, this.MinY, z));
        for (var y = this.MinY + this.Height - 1; y >= this.MinY; y--)
        {
            if (WorldgenHeightmaps.Matches(type, this.GetBlock(new Vector(x, y, z))))
                return y + 1;
        }

        return this.MinY;
    }

    public BiomeCodec GetBiome(Vector position)
    {
        if (level.GetLoadedChunk(position.X >> 4, position.Z >> 4) is not { IsGenerated: true })
            throw new UnsupportedAccess();

        return this.reads.GetBiome(position);
    }

    public void AddEntity(GeneratedEntity entity)
    {
        // Entity spawning and all block/removal callbacks belong to the live commit only.
    }

    public void MarkForPostProcessing(Vector position)
    {
    }

    public void ScheduleFluidTick(Vector position)
    {
    }

    private void RequireChunk(Vector position)
    {
        if (level.GetLoadedChunk(position.X >> 4, position.Z >> 4) is not { IsGenerated: true })
            throw new ChunkNeeded(position);
    }
}
