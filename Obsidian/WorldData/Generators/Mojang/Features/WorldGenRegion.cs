using Obsidian.API.Registry.Codecs.Biomes;

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

    private static readonly HeightmapType[] finalHeightmaps =
        [HeightmapType.OceanFloor, HeightmapType.WorldSurface, HeightmapType.MotionBlocking, HeightmapType.MotionBlockingNoLeaves];

    private readonly Dictionary<(int X, int Z), IChunk> chunks;
    private readonly Dictionary<(int X, int Z, HeightmapType Type), int[]> heights = [];
    private readonly BiomeManager biomeManager;
    private readonly Action<Vector>? scheduleFluidTick;
    private readonly int centerX;
    private readonly int centerZ;

    public long Seed { get; }

    public int MinY { get; }

    public int Height { get; }

    public int SeaLevel { get; }

    /// <param name="chunks">The center chunk and (at least) its 8 neighbors, keyed by chunk coordinates.</param>
    /// <param name="centerX">X of the chunk being decorated.</param>
    /// <param name="centerZ">Z of the chunk being decorated.</param>
    /// <param name="biomeSource">Source for biomes of chunks outside <paramref name="chunks"/>.</param>
    /// <param name="scheduleFluidTick">Receives the fluid updates features schedule; they're ignored when null.</param>
    public WorldGenRegion(IReadOnlyDictionary<(int X, int Z), IChunk> chunks, int centerX, int centerZ, long seed,
        int minY, int height, int seaLevel, IBiomeSource biomeSource, Action<Vector>? scheduleFluidTick = null)
    {
        this.scheduleFluidTick = scheduleFluidTick;
        this.chunks = new Dictionary<(int X, int Z), IChunk>(chunks);
        this.centerX = centerX;
        this.centerZ = centerZ;
        this.Seed = seed;
        this.MinY = minY;
        this.Height = height;
        this.SeaLevel = seaLevel;
        this.biomeManager = new BiomeManager(new RegionBiomeSource(this, biomeSource), seed, minY, height);
    }

    public IBlock GetBlock(Vector position)
    {
        if (this.IsOutsideBuildHeight(position.Y))
            return BlocksRegistry.VoidAir;

        return this.chunks.TryGetValue((position.X >> 4, position.Z >> 4), out var chunk)
            ? chunk.GetBlock(position.X, position.Y, position.Z)
            : BlocksRegistry.Air;
    }

    public bool IsOutsideBuildHeight(int y) => y < this.MinY || y >= this.MinY + this.Height;

    public bool EnsureCanWrite(Vector position) =>
        Math.Abs((position.X >> 4) - this.centerX) <= WriteRadius && Math.Abs((position.Z >> 4) - this.centerZ) <= WriteRadius;

    public bool SetBlock(Vector position, IBlock block)
    {
        if (!this.EnsureCanWrite(position))
            return false;

        // Like a proto chunk, writes outside the build height are ignored.
        if (this.IsOutsideBuildHeight(position.Y))
            return true;

        var chunkX = position.X >> 4;
        var chunkZ = position.Z >> 4;
        var chunk = this.GetChunk(chunkX, chunkZ);

        // Prime the final heightmaps from the blocks before the change, like ProtoChunk.setBlockState.
        foreach (var type in finalHeightmaps)
            this.GetHeights(chunk, chunkX, chunkZ, type);

        chunk.SetBlock(position.X, position.Y, position.Z, block);

        foreach (var type in finalHeightmaps)
            this.UpdateHeight(chunk, chunkX, chunkZ, type, position, block);

        DataBlockEntity.ApplyBlockChange(chunk, position, block);
        return true;
    }

    public int GetHeight(HeightmapType type, int x, int z)
    {
        var chunkX = x >> 4;
        var chunkZ = z >> 4;
        if (!this.chunks.TryGetValue((chunkX, chunkZ), out var chunk))
            return this.MinY;

        if (type is HeightmapType.WorldSurfaceWG or HeightmapType.OceanFloorWG && chunk.Heightmaps.TryGetValue(type, out var heightmap))
            return heightmap.GetHeight(x & 15, z & 15);

        return this.GetHeights(chunk, chunkX, chunkZ, type)[(z & 15) * 16 + (x & 15)];
    }

    public BiomeCodec GetBiome(Vector position) => this.biomeManager.GetBiome(position.X, position.Y, position.Z);

    public void SetBlockEntity(Vector position, IBlockEntity blockEntity)
    {
        if (this.EnsureCanWrite(position) && !this.IsOutsideBuildHeight(position.Y))
            this.GetChunk(position.X >> 4, position.Z >> 4).SetBlockEntity(position.X, position.Y, position.Z, blockEntity);
    }

    public IBlockEntity? GetBlockEntity(Vector position) =>
        !this.IsOutsideBuildHeight(position.Y) && this.chunks.TryGetValue((position.X >> 4, position.Z >> 4), out var chunk)
            ? chunk.GetBlockEntity(position.X, position.Y, position.Z)
            : null;

    public void AddEntity(GeneratedEntity entity)
    {
        // Like vanilla's addFreshEntity, any chunk of the region can take entities, not only writable ones.
        var chunkX = (int)Math.Floor(entity.Position.X) >> 4;
        var chunkZ = (int)Math.Floor(entity.Position.Z) >> 4;
        if (this.chunks.TryGetValue((chunkX, chunkZ), out var chunk) && chunk is Chunk generated)
            generated.PendingEntities.Add(entity);
    }

    public void ScheduleFluidTick(Vector position)
    {
        if (this.EnsureCanWrite(position))
            this.scheduleFluidTick?.Invoke(position);
    }

    private IChunk GetChunk(int chunkX, int chunkZ) =>
        this.chunks.TryGetValue((chunkX, chunkZ), out var chunk)
            ? chunk
            : throw new InvalidOperationException($"Chunk ({chunkX}, {chunkZ}) is outside the generation region.");

    /// <summary>
    /// Heights (first free Y) for a chunk, computed from its blocks on first use.
    /// </summary>
    private int[] GetHeights(IChunk chunk, int chunkX, int chunkZ, HeightmapType type)
    {
        if (this.heights.TryGetValue((chunkX, chunkZ, type), out var values))
            return values;

        values = new int[256];
        for (var localZ = 0; localZ < 16; localZ++)
        {
            for (var localX = 0; localX < 16; localX++)
            {
                var y = this.MinY + this.Height - 1;
                while (y >= this.MinY && !WorldgenHeightmaps.Matches(type, chunk.GetBlock(localX, y, localZ)))
                    y--;

                values[localZ * 16 + localX] = y + 1;
            }
        }

        this.heights[(chunkX, chunkZ, type)] = values;
        return values;
    }

    /// <summary>
    /// Updates a height after a block change, like vanilla's Heightmap.update.
    /// </summary>
    private void UpdateHeight(IChunk chunk, int chunkX, int chunkZ, HeightmapType type, Vector position, IBlock block)
    {
        var values = this.heights[(chunkX, chunkZ, type)];
        var localX = position.X & 15;
        var localZ = position.Z & 15;
        var column = localZ * 16 + localX;
        var height = values[column];

        if (position.Y <= height - 2)
            return;

        if (WorldgenHeightmaps.Matches(type, block))
        {
            if (position.Y >= height)
                values[column] = position.Y + 1;

            return;
        }

        if (height - 1 != position.Y)
            return;

        var y = position.Y - 1;
        while (y >= this.MinY && !WorldgenHeightmaps.Matches(type, chunk.GetBlock(localX, y, localZ)))
            y--;

        values[column] = y + 1;
    }

    /// <summary>
    /// Reads stored biomes of region chunks and samples the biome source elsewhere, like WorldGenRegion.getNoiseBiome.
    /// </summary>
    private sealed class RegionBiomeSource : IBiomeSource
    {
        private readonly WorldGenRegion region;
        private readonly IBiomeSource fallback;

        public RegionBiomeSource(WorldGenRegion region, IBiomeSource fallback)
        {
            this.region = region;
            this.fallback = fallback;
        }

        public BiomeCodec GetNoiseBiome(int quartX, int quartY, int quartZ) =>
            this.region.chunks.TryGetValue((quartX >> 2, quartZ >> 2), out var chunk)
                ? chunk.GetBiome((quartX & 3) << 2, quartY << 2, (quartZ & 3) << 2)
                : this.fallback.GetNoiseBiome(quartX, quartY, quartZ);
    }
}
