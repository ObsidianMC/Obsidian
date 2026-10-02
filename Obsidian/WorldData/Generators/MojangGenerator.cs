using Obsidian.API.World;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Lighting;
using System.Threading;


namespace Obsidian.WorldData.Generators;

/// <summary>
/// Vanilla's overworld generator.
/// </summary>
internal class MojangGenerator : ILevelGenerator
{
    // Chunk locks are striped so their number stays bounded; stripes are always taken in ascending order.
    private const int LockStripeCount = 256;

    public virtual string Id => "minecraft:mojang_generator";

    /// <summary>
    /// The dimension this generator builds; levels using it must have the dimension's build range.
    /// </summary>
    protected virtual MojangDimension Dimension => MojangDimension.Overworld;

    private ChunkBuilder builder;
    private ILevel world;

    private readonly SemaphoreSlim[] chunkLocks = [.. Enumerable.Range(0, LockStripeCount).Select(_ => new SemaphoreSlim(1, 1))];

    // Fluids flagged during generation, scheduled once their chunk is complete (vanilla's post-processing).
    private readonly ConcurrentDictionary<(int X, int Z), List<Vector>> pendingFluidUpdates = new();

    public async ValueTask<IChunk> GenerateChunkAsync(int cx, int cz, IChunk? chunk = null, ChunkGenStage stage = ChunkGenStage.full)
    {
        chunk ??= new Chunk(cx, cz, this.Dimension.MinY, this.Dimension.Height);

        // Sanity checks
        if (chunk.IsGenerated)
            return chunk;

        using (await this.LockAsync(cx, cz, 0))
        {
            // Features of neighboring chunks may have created and written into the stored instance already.
            chunk = await this.world.GetChunkAsync(cx, cz, scheduleGeneration: false) ?? chunk;
            this.GenerateUpToCarvers(chunk, stage);
        }

        if (ChunkGenStage.features <= stage)
        {
            // Like vanilla, a chunk is only complete once its neighbors are decorated too, since their features can
            // reach into it.
            var radius = ChunkGenStage.features < stage ? 1 : 0;
            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dz = -radius; dz <= radius; dz++)
                    await this.DecorateAsync(cx + dx, cz + dz);
            }
        }

        if (ChunkGenStage.initialize_light <= stage && chunk.ChunkStatus < ChunkGenStage.initialize_light)
        {
            // Every feature that can reach this chunk has run, so its heightmaps are final. Its sky light sources are
            // found when it's lit.
            this.builder.UpdateFinalHeightmaps(chunk);
            chunk.SetChunkStatus(ChunkGenStage.initialize_light);
        }

        if (ChunkGenStage.light <= stage && chunk.ChunkStatus < ChunkGenStage.light)
            await this.LightAsync(chunk);

        if (ChunkGenStage.spawn <= stage && chunk.ChunkStatus < ChunkGenStage.spawn)
        {
            // TODO: Implement spawn point calculation
            chunk.SetChunkStatus(ChunkGenStage.spawn);
        }

        if (stage < ChunkGenStage.full)
            return chunk;

        chunk.SetChunkStatus(ChunkGenStage.full);
        await this.ScheduleFluidUpdatesAsync(chunk);
        this.SpawnPendingEntities(chunk);
        return chunk;
    }

    /// <summary>
    /// Spawns the entities generation placed in a chunk that just became complete, like vanilla when a proto chunk
    /// becomes a level chunk.
    /// </summary>
    /// <remarks>
    /// Only the type, position and rotation are applied; Obsidian's entities don't read vanilla's other saved fields.
    /// </remarks>
    private void SpawnPendingEntities(IChunk chunk)
    {
        if (chunk is not Chunk generated || generated.PendingEntities.Count == 0)
            return;

        foreach (var pending in generated.PendingEntities)
        {
            if (!TryGetEntityType(pending.Type, out var type))
                continue;

            var entity = this.world.SpawnEntity(pending.Position, type);
            if (entity is null)
                continue;

            entity.Yaw = pending.Yaw;
            entity.Pitch = pending.Pitch;
        }

        generated.PendingEntities.Clear();
    }

    /// <summary>
    /// Maps a vanilla entity type id (e.g. <c>minecraft:end_crystal</c>) to Obsidian's <see cref="EntityType"/>.
    /// </summary>
    private static bool TryGetEntityType(string id, out EntityType type)
    {
        var name = id[(id.IndexOf(':') + 1)..].Replace("_", string.Empty);
        return Enum.TryParse(name, ignoreCase: true, out type);
    }

    /// <summary>
    /// Vanilla's initial spawn: the climate spawn chunk at the default spawn height, moved onto the first standable block
    /// found in the chunks spiraling out from it.
    /// </summary>
    public virtual async ValueTask<VectorF?> FindSpawnPointAsync()
    {
        var (x, z) = this.builder.FindClimateSpawn();
        var spawnChunkX = x >> 4;
        var spawnChunkZ = z >> 4;
        var spawn = new Vector((spawnChunkX << 4) + 8, SpawnFinder.DefaultSpawnHeight, (spawnChunkZ << 4) + 8);

        foreach (var (dx, dz) in SpawnFinder.SpiralOffsets())
        {
            var chunk = await this.GenerateChunkAsync(spawnChunkX + dx, spawnChunkZ + dz);
            var found = SpawnFinder.FindSpawnInChunk(chunk, hasCeiling: false);
            if (found is not null)
            {
                spawn = found.Value;
                break;
            }
        }

        return new VectorF(spawn.X + 0.5f, spawn.Y, spawn.Z + 0.5f);
    }

    /// <summary>
    /// Runs the stages up to and including carvers that <paramref name="stage"/> asks for.
    /// The caller must hold the chunk's lock.
    /// </summary>
    private void GenerateUpToCarvers(IChunk chunk, ChunkGenStage stage)
    {
        chunk.SetChunkStatus(chunk.ChunkStatus == ChunkGenStage.empty ? ChunkGenStage.structure_references : chunk.ChunkStatus);

        if (ChunkGenStage.structure_starts <= stage && chunk.ChunkStatus < ChunkGenStage.structure_starts)
        {
            // TODO: Implement structure starts
            chunk.SetChunkStatus(ChunkGenStage.structure_starts);
        }

        if (ChunkGenStage.structure_references <= stage && chunk.ChunkStatus < ChunkGenStage.structure_references)
        {
            // TODO: Implement structure references
            chunk.SetChunkStatus(ChunkGenStage.structure_references);
        }

        if (ChunkGenStage.biomes <= stage && chunk.ChunkStatus < ChunkGenStage.biomes)
        {
            // Use multi-noise biome selection based on climate parameters
            this.builder.PopulateBiomes(chunk);
            chunk.SetChunkStatus(ChunkGenStage.biomes);
        }

        if (ChunkGenStage.noise <= stage && chunk.ChunkStatus < ChunkGenStage.noise)
        {
            // Generate terrain using 3D density sampling with aquifer support
            this.builder.Generate3DTerrain(chunk, this.GetPendingFluidUpdates(chunk));
            chunk.SetChunkStatus(ChunkGenStage.noise);
        }

        if (ChunkGenStage.surface <= stage && chunk.ChunkStatus < ChunkGenStage.surface)
        {
            // Apply surface rules to replace stone with grass, dirt, sand, etc.
            this.builder.ApplySurfaceRules(chunk);
            chunk.SetChunkStatus(ChunkGenStage.surface);
        }

        if (ChunkGenStage.carvers <= stage && chunk.ChunkStatus < ChunkGenStage.carvers)
        {
            // Carve classic caves and ravines
            this.builder.ApplyCarvers(chunk, this.GetPendingFluidUpdates(chunk));
            chunk.SetChunkStatus(ChunkGenStage.carvers);
        }
    }

    /// <summary>
    /// Places the features of a chunk (trees, ores, etc.), carving its neighbors first since features write into them.
    /// </summary>
    private async ValueTask DecorateAsync(int cx, int cz)
    {
        // Carve the area one chunk lock at a time first, so other jobs aren't blocked on the whole area during terrain generation.
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dz = -1; dz <= 1; dz++)
            {
                using var chunkLock = await this.LockAsync(cx + dx, cz + dz, 0);
                this.GenerateUpToCarvers(await this.GetChunkAsync(cx + dx, cz + dz), ChunkGenStage.carvers);
            }
        }

        using var locks = await this.LockAsync(cx, cz, 1);

        var chunk = await this.GetChunkAsync(cx, cz);
        if (chunk.ChunkStatus >= ChunkGenStage.features)
            return;

        var area = new Dictionary<(int X, int Z), IChunk>();
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dz = -1; dz <= 1; dz++)
                area[(cx + dx, cz + dz)] = dx == 0 && dz == 0 ? chunk : await this.GetChunkAsync(cx + dx, cz + dz);
        }

        // The area's chunks are locked, so their pending lists can be written.
        this.builder.Decorate(area, cx, cz, position => this.GetPendingFluidUpdates(position.X >> 4, position.Z >> 4).Add(position));
        chunk.SetChunkStatus(ChunkGenStage.features);
    }

    /// <summary>
    /// Lights a chunk whose blocks are final, spreading light between it and its lit neighbors, whose locks it holds.
    /// </summary>
    private async ValueTask LightAsync(IChunk chunk)
    {
        using var locks = await this.LockAsync(chunk.X, chunk.Z, 1);
        if (chunk.ChunkStatus >= ChunkGenStage.light)
            return;

        await LightEngine.LightChunkAsync(chunk, this.world, this.Dimension.HasSkyLight);
        chunk.SetChunkStatus(ChunkGenStage.light);
    }

    /// <summary>
    /// The stored chunk at (<paramref name="cx"/>, <paramref name="cz"/>), created if it doesn't exist yet.
    /// The caller must hold the chunk's lock.
    /// </summary>
    private async ValueTask<IChunk> GetChunkAsync(int cx, int cz) =>
        await this.world.GetChunkAsync(cx, cz, scheduleGeneration: false)
            ?? throw new InvalidOperationException($"Chunk ({cx}, {cz}) couldn't be loaded.");

    /// <summary>
    /// Locks every chunk within <paramref name="radius"/> chunks of (<paramref name="cx"/>, <paramref name="cz"/>).
    /// </summary>
    private async ValueTask<ChunkLocks> LockAsync(int cx, int cz, int radius)
    {
        var stripes = new SortedSet<int>();
        for (var dx = -radius; dx <= radius; dx++)
        {
            for (var dz = -radius; dz <= radius; dz++)
                stripes.Add((int)((uint)HashCode.Combine(cx + dx, cz + dz) % LockStripeCount));
        }

        foreach (var stripe in stripes)
            await this.chunkLocks[stripe].WaitAsync();

        return new ChunkLocks(this.chunkLocks, stripes);
    }

    private List<Vector> GetPendingFluidUpdates(IChunk chunk) => this.GetPendingFluidUpdates(chunk.X, chunk.Z);

    private List<Vector> GetPendingFluidUpdates(int cx, int cz) => this.pendingFluidUpdates.GetOrAdd((cx, cz), _ => []);

    private async ValueTask ScheduleFluidUpdatesAsync(IChunk chunk)
    {
        if (!this.pendingFluidUpdates.TryRemove((chunk.X, chunk.Z), out var positions))
            return;

        foreach (var position in positions)
        {
            var block = chunk.GetBlock(position);
            if (block.IsLiquid)
                await this.world.ScheduleBlockUpdateAsync(new BlockUpdate(this.world, position, block));
        }
    }

    public void Init(ILevel world)
    {
        this.world = world;
        this.builder = new ChunkBuilder(this.Dimension, RandomState.ParseSeed(world.Seed));
    }

    private readonly struct ChunkLocks(SemaphoreSlim[] locks, SortedSet<int> stripes) : IDisposable
    {
        public void Dispose()
        {
            foreach (var stripe in stripes)
                locks[stripe].Release();
        }
    }
}

/// <summary>
/// Vanilla's nether generator, used by the <c>minecraft:the_nether</c> dimension.
/// </summary>
internal sealed class MojangNetherGenerator : MojangGenerator
{
    public override string Id => "minecraft:the_nether";

    protected override MojangDimension Dimension => MojangDimension.Nether;

    // Vanilla only searches a spawn in the overworld.
    public override ValueTask<VectorF?> FindSpawnPointAsync() => ValueTask.FromResult<VectorF?>(null);
}

/// <summary>
/// Vanilla's end generator, used by the <c>minecraft:the_end</c> dimension.
/// </summary>
internal sealed class MojangEndGenerator : MojangGenerator
{
    public override string Id => "minecraft:the_end";

    protected override MojangDimension Dimension => MojangDimension.End;

    // Vanilla's ServerLevel.END_SPAWN_POINT, on the obsidian platform.
    public override ValueTask<VectorF?> FindSpawnPointAsync() => ValueTask.FromResult<VectorF?>(new VectorF(100.5f, 50, 0.5f));
}
