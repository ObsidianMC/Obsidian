using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.ChunkData;

namespace Obsidian.WorldData.Generators.Mojang.Features;

/// <summary>
/// The chunks around a chunk being decorated, mirroring vanilla's WorldGenRegion for the features step.
/// </summary>
/// <remarks>
/// Writes are limited to the 3x3 chunks around the center. The <c>*_WG</c> heightmaps keep their post-carver values,
/// while the final heightmaps (<c>OCEAN_FLOOR</c>, <c>WORLD_SURFACE</c>, <c>MOTION_BLOCKING</c>,
/// <c>MOTION_BLOCKING_NO_LEAVES</c>) follow every write, like vanilla chunks at the carvers status.
/// Reads outside the given chunks see an empty chunk, like vanilla's chunks that are only at the structure starts status.
/// Not thread-safe: a region is used by one decoration at a time.
/// </remarks>
internal sealed class WorldGenRegion : IWorldGenLevel
{
    private const int WriteRadius = 1;
    private const int AreaWidth = 2 * WriteRadius + 1;

    // What a write needs to know about a block (see WriteInfo): its heightmap mask, and whether it has a block entity.
    private const int HeightmapMask = 0xF;
    private const int BlockEntityBit = 1 << 4;

    private static readonly byte[] writeInfos = CreateWriteInfos();

    private readonly IReadOnlyDictionary<(int X, int Z), IChunk> chunks;

    // The chunks writes may reach (null where the region has none) and their final heightmaps, indexed by AreaIndex.
    private readonly IChunk?[] area = new IChunk?[AreaWidth * AreaWidth];
    private readonly FinalHeightmaps?[] areaHeightmaps = new FinalHeightmaps?[AreaWidth * AreaWidth];

    // The sections of the writable chunks, indexed by AreaIndex * sectionCount + section index, so block reads and writes
    // skip the chunk; null for chunks of other types or build ranges, which are read through the chunk.
    private readonly ChunkSection?[] areaSections;
    private readonly int sectionCount;

    // Heights computed from the blocks of a chunk when first needed, for the chunks outside the writable area and the
    // world generation heightmaps a chunk no longer has. They don't follow writes.
    private readonly Dictionary<(int X, int Z), FinalHeightmaps> snapshots = [];

    private readonly bool trackHeightmaps;
    private readonly BiomeManager biomeManager;
    private readonly int centerX;
    private readonly int centerZ;

    public long Seed { get; }

    public int MinY { get; }

    public int Height { get; }

    public int SeaLevel { get; }

    public IRandomSource Random { get; }

    /// <param name="chunks">The center chunk and (at least) its 8 neighbors, keyed by chunk coordinates.</param>
    /// <param name="centerX">X of the chunk being decorated.</param>
    /// <param name="centerZ">Z of the chunk being decorated.</param>
    /// <param name="biomeSource">Source for biomes of chunks outside <paramref name="chunks"/>.</param>
    /// <param name="random">The region's own random (see <see cref="IWorldGenLevel.Random"/>).</param>
    /// <param name="trackHeightmaps">
    /// Whether the writable chunks keep the final heightmaps the region computes for them (see
    /// <see cref="Chunk.FinalHeightmaps"/>), so later regions don't compute them again. Only for chunks that nothing but
    /// generation writes to until their final heightmaps are stored, like the chunks around a chunk being decorated.
    /// </param>
    public WorldGenRegion(IReadOnlyDictionary<(int X, int Z), IChunk> chunks, int centerX, int centerZ, long seed,
        int minY, int height, int seaLevel, IBiomeSource biomeSource, IRandomSource random, bool trackHeightmaps)
    {
        this.Random = random;
        this.chunks = chunks;
        this.centerX = centerX;
        this.centerZ = centerZ;
        this.Seed = seed;
        this.MinY = minY;
        this.Height = height;
        this.SeaLevel = seaLevel;
        this.trackHeightmaps = trackHeightmaps;
        this.biomeManager = new BiomeManager(new RegionBiomeSource(this, biomeSource), seed, minY, height, cacheNoiseBiomes: false);

        this.sectionCount = height >> 4;
        this.areaSections = new ChunkSection?[AreaWidth * AreaWidth * this.sectionCount];

        for (var dx = -WriteRadius; dx <= WriteRadius; dx++)
        {
            for (var dz = -WriteRadius; dz <= WriteRadius; dz++)
            {
                var index = this.AreaIndex(centerX + dx, centerZ + dz);
                var chunk = chunks.GetValueOrDefault((centerX + dx, centerZ + dz));
                this.area[index] = chunk;

                if (chunk is Chunk generated && generated.MinY == minY && generated.Sections.Length == this.sectionCount)
                {
                    for (var section = 0; section < this.sectionCount; section++)
                        this.areaSections[index * this.sectionCount + section] = generated.Sections[section] as ChunkSection;
                }
            }
        }
    }

    public IBlock GetBlock(Vector position)
    {
        if (this.IsOutsideBuildHeight(position.Y))
            return BlocksRegistry.VoidAir;

        var index = this.AreaIndex(position.X >> 4, position.Z >> 4);
        if (index >= 0)
        {
            var section = this.areaSections[index * this.sectionCount + ((position.Y - this.MinY) >> 4)];
            if (section is not null)
                return section.GetBlock(position.X & 15, position.Y & 15, position.Z & 15);
        }

        var chunk = this.FindChunk(position.X >> 4, position.Z >> 4);
        return chunk is not null ? chunk.GetBlock(position.X, position.Y, position.Z) : BlocksRegistry.Air;
    }

    public bool IsOutsideBuildHeight(int y) => y < this.MinY || y >= this.MinY + this.Height;

    public bool EnsureCanWrite(Vector position) => this.AreaIndex(position.X >> 4, position.Z >> 4) >= 0;

    public bool SetBlock(Vector position, IBlock block)
    {
        var index = this.AreaIndex(position.X >> 4, position.Z >> 4);
        if (index < 0)
            return false;

        // Like a proto chunk, writes outside the build height are ignored.
        if (this.IsOutsideBuildHeight(position.Y))
            return true;

        var chunk = this.GetAreaChunk(index, position);

        // ProtoChunk.setBlockState primes missing heightmaps after the write; priming before it and updating gives the same
        // heights.
        var heightmaps = this.GetFinalHeightmaps(index, chunk);

        var section = this.areaSections[index * this.sectionCount + ((position.Y - this.MinY) >> 4)];
        if (section is not null)
            section.SetBlock(position.X & 15, position.Y & 15, position.Z & 15, block);
        else
            chunk.SetBlock(position.X, position.Y, position.Z, block);

        var stateId = block.GetHashCode();
        var info = (uint)stateId < (uint)writeInfos.Length ? writeInfos[stateId] : WriteInfo(block);
        heightmaps.Update(position.X, position.Y, position.Z, info & HeightmapMask);

        // Like DataBlockEntity.ApplyBlockChange, which blocks without a block entity don't need.
        if ((info & BlockEntityBit) != 0)
            DataBlockEntity.ApplyBlockChange(chunk, position, block);
        else
            chunk.RemoveBlockEntity(position.X, position.Y, position.Z);

        return true;
    }

    public int GetHeight(HeightmapType type, int x, int z)
    {
        var chunkX = x >> 4;
        var chunkZ = z >> 4;
        var index = this.AreaIndex(chunkX, chunkZ);
        var chunk = index >= 0 ? this.area[index] : this.chunks.GetValueOrDefault((chunkX, chunkZ));
        if (chunk is null)
            return this.MinY;

        if (type is HeightmapType.WorldSurfaceWG or HeightmapType.OceanFloorWG)
        {
            return chunk.Heightmaps.TryGetValue(type, out var heightmap)
                ? heightmap.GetHeight(x & 15, z & 15)
                : this.GetSnapshot(chunk, chunkX, chunkZ).GetHeight(type, x, z);
        }

        var heightmaps = index >= 0 ? this.GetFinalHeightmaps(index, chunk) : this.GetSnapshot(chunk, chunkX, chunkZ);
        return heightmaps.GetHeight(type, x, z);
    }

    public BiomeCodec GetBiome(Vector position) => this.biomeManager.GetBiome(position.X, position.Y, position.Z);

    public void SetBlockEntity(Vector position, IBlockEntity blockEntity)
    {
        var index = this.AreaIndex(position.X >> 4, position.Z >> 4);
        if (index >= 0 && !this.IsOutsideBuildHeight(position.Y))
            this.GetAreaChunk(index, position).SetBlockEntity(position.X, position.Y, position.Z, blockEntity);
    }

    public IBlockEntity? GetBlockEntity(Vector position)
    {
        if (this.IsOutsideBuildHeight(position.Y))
            return null;

        return this.FindChunk(position.X >> 4, position.Z >> 4)?.GetBlockEntity(position.X, position.Y, position.Z);
    }

    public void AddEntity(GeneratedEntity entity)
    {
        // Like vanilla's addFreshEntity, any chunk of the region can take entities, not only writable ones.
        var chunkX = (int)Math.Floor(entity.Position.X) >> 4;
        var chunkZ = (int)Math.Floor(entity.Position.Z) >> 4;
        if (this.FindChunk(chunkX, chunkZ) is Chunk generated)
            generated.PendingEntities.Add(entity);
    }

    /// <remarks>
    /// The tick is for the fluid at <paramref name="position"/> and goes into its chunk's tick list. Like vanilla's proto
    /// chunk ticks, it has no delay: it runs on the first level tick once the chunk is complete.
    /// </remarks>
    public void ScheduleFluidTick(Vector position)
    {
        var fluid = this.GetBlock(position).GetFluid();
        if (fluid != FluidKind.Empty)
            this.ScheduleFluidTick(position, fluid, 0);
    }

    /// <summary>
    /// Schedules a tick of <paramref name="fluid"/> in the tick list of the position's chunk, if the region holds it.
    /// </summary>
    internal void ScheduleFluidTick(Vector position, FluidKind fluid, int delay)
    {
        if (this.FindChunk(position.X >> 4, position.Z >> 4) is Chunk generated)
            generated.FluidTicks.Schedule(position, fluid, delay);
    }

    public void MarkForPostProcessing(Vector position)
    {
        if (this.FindChunk(position.X >> 4, position.Z >> 4) is Chunk generated)
            generated.PostProcessing.Add(position);
    }

    private static byte[] CreateWriteInfos()
    {
        var infos = new byte[BlocksRegistry.StateToNumeric.Length];
        for (var stateId = 0; stateId < infos.Length; stateId++)
            infos[stateId] = (byte)WriteInfo(BlocksRegistry.Get(stateId));

        return infos;
    }

    private static int WriteInfo(IBlock block) =>
        WorldgenHeightmaps.Mask(block) | (block.HasBlockEntity() ? BlockEntityBit : 0);

    // The index of a chunk of the writable area, or -1 for chunks outside it.
    private int AreaIndex(int chunkX, int chunkZ)
    {
        var dx = chunkX - this.centerX + WriteRadius;
        var dz = chunkZ - this.centerZ + WriteRadius;
        return (uint)dx < AreaWidth && (uint)dz < AreaWidth ? dx * AreaWidth + dz : -1;
    }

    private IChunk? FindChunk(int chunkX, int chunkZ)
    {
        var index = this.AreaIndex(chunkX, chunkZ);
        return index >= 0 ? this.area[index] : this.chunks.GetValueOrDefault((chunkX, chunkZ));
    }

    private IChunk GetAreaChunk(int index, Vector position) =>
        this.area[index] ?? throw new InvalidOperationException($"Chunk ({position.X >> 4}, {position.Z >> 4}) is outside the generation region.");

    /// <summary>
    /// The final heightmaps of a writable chunk, following the region's writes: the ones the chunk keeps, or else computed
    /// from its blocks on first use.
    /// </summary>
    private FinalHeightmaps GetFinalHeightmaps(int index, IChunk chunk)
    {
        var heightmaps = this.areaHeightmaps[index];
        if (heightmaps is not null)
            return heightmaps;

        var generated = chunk as Chunk;
        heightmaps = generated?.FinalHeightmaps;
        if (heightmaps is null)
        {
            heightmaps = new FinalHeightmaps(chunk, this.MinY, this.Height);
            if (this.trackHeightmaps && generated is not null)
                generated.FinalHeightmaps = heightmaps;
        }

        return this.areaHeightmaps[index] = heightmaps;
    }

    private FinalHeightmaps GetSnapshot(IChunk chunk, int chunkX, int chunkZ)
    {
        if (!this.snapshots.TryGetValue((chunkX, chunkZ), out var heightmaps))
            this.snapshots[(chunkX, chunkZ)] = heightmaps = new FinalHeightmaps(chunk, this.MinY, this.Height);

        return heightmaps;
    }

    /// <summary>
    /// Reads stored biomes of region chunks and samples the biome source elsewhere, like WorldGenRegion.getNoiseBiome.
    /// </summary>
    /// <remarks>
    /// Only sampled biomes are cached: stored ones are cheaper to read again.
    /// </remarks>
    private sealed class RegionBiomeSource : IBiomeSource
    {
        private readonly WorldGenRegion region;
        private readonly IBiomeSource fallback;
        private Dictionary<(int X, int Y, int Z), BiomeCodec>? sampled;

        public RegionBiomeSource(WorldGenRegion region, IBiomeSource fallback)
        {
            this.region = region;
            this.fallback = fallback;
        }

        public BiomeCodec GetNoiseBiome(int quartX, int quartY, int quartZ)
        {
            var chunk = this.region.FindChunk(quartX >> 2, quartZ >> 2);
            if (chunk is not null)
                return chunk.GetBiome((quartX & 3) << 2, quartY << 2, (quartZ & 3) << 2);

            this.sampled ??= [];
            if (!this.sampled.TryGetValue((quartX, quartY, quartZ), out var biome))
                this.sampled[(quartX, quartY, quartZ)] = biome = this.fallback.GetNoiseBiome(quartX, quartY, quartZ);

            return biome;
        }
    }
}
