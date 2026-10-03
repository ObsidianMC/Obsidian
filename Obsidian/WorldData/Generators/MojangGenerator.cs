using Obsidian.API.World;
using Obsidian.Nbt;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Mojang.Structures;
using Obsidian.WorldData.Lighting;
using System.Threading;


namespace Obsidian.WorldData.Generators;

/// <summary>
/// Vanilla's overworld generator.
/// </summary>
internal class MojangGenerator : ILevelGenerator, IStructureStartStorage
{
    // Chunk locks are striped so their number stays bounded; stripes are always taken in ascending order.
    private const int LockStripeCount = 4096;

    public virtual string Id => "minecraft:mojang_generator";

    /// <summary>
    /// The dimension this generator builds; levels using it must have the dimension's build range.
    /// </summary>
    protected virtual MojangDimension Dimension => MojangDimension.Overworld;

    private ChunkBuilder builder;

    /// <summary>
    /// The builder generating the level's chunks, available once <see cref="Init"/> ran.
    /// </summary>
    internal ChunkBuilder Builder => this.builder;
    private ILevel world;

    private readonly SemaphoreSlim[] chunkLocks = [.. Enumerable.Range(0, LockStripeCount).Select(_ => new SemaphoreSlim(1, 1))];

    // Carving and decorating steps in flight, so requests that need the same step share it instead of queueing on its locks.
    private readonly ConcurrentDictionary<(int X, int Z, ChunkGenStage Stage), Lazy<Task>> steps = [];

    public ValueTask<IChunk> GenerateChunkAsync(int cx, int cz, IChunk? chunk = null, ChunkGenStage stage = ChunkGenStage.full) =>
        this.GenerateChunkAsync(cx, cz, chunk, stage, decorateInOrder: false);

    /// <param name="decorateInOrder">Whether the chunk's neighbors are decorated one after another in a fixed order, so the
    /// blocks they place where their features meet are the same every time (the spawn search), rather than concurrently.</param>
    private async ValueTask<IChunk> GenerateChunkAsync(int cx, int cz, IChunk? chunk, ChunkGenStage stage, bool decorateInOrder)
    {
        chunk ??= new Chunk(cx, cz, this.Dimension.MinY, this.Dimension.Height);

        // Sanity checks
        if (chunk.IsGenerated)
            return chunk;

        // Features of neighboring chunks may have created and written into the stored instance already.
        chunk = await this.world.GetChunkAsync(cx, cz, scheduleGeneration: false) ?? chunk;

        if (stage < ChunkGenStage.features)
        {
            using (await this.LockAsync(cx, cz, 0))
                this.GenerateUpToCarvers(chunk, stage);

            return chunk;
        }

        // Like vanilla, a chunk is only complete once its neighbors are decorated too, since their features can reach into
        // it. The decorations run concurrently; those whose areas overlap wait on each other's locks.
        var radius = ChunkGenStage.features < stage ? 1 : 0;
        var decorations = new List<Task>((2 * radius + 1) * (2 * radius + 1));
        for (var dx = -radius; dx <= radius; dx++)
        {
            for (var dz = -radius; dz <= radius; dz++)
            {
                if (decorateInOrder)
                    await this.DecorateAsync(cx + dx, cz + dz);
                else
                    decorations.Add(this.DecorateAsync(cx + dx, cz + dz));
            }
        }

        await Task.WhenAll(decorations);

        if (ChunkGenStage.initialize_light <= stage && chunk.ChunkStatus < ChunkGenStage.initialize_light)
            await this.FinishBlocksAsync(chunk);

        if (ChunkGenStage.light <= stage && chunk.ChunkStatus < ChunkGenStage.light)
            await this.LightAsync(chunk);

        if (ChunkGenStage.spawn <= stage && chunk.ChunkStatus < ChunkGenStage.spawn)
        {
            // TODO: Implement spawn point calculation
            chunk.SetChunkStatus(ChunkGenStage.spawn);
        }

        if (stage < ChunkGenStage.full)
            return chunk;

        // Completing the chunk happens under its lock, so concurrent requests do it once.
        using (await this.LockAsync(cx, cz, 0))
        {
            if (chunk.ChunkStatus >= ChunkGenStage.full)
                return chunk;

            chunk.SetChunkStatus(ChunkGenStage.full);
        }

        if (this.world is AbstractLevel level)
        {
            level.QueueChunkPopulation(chunk);

            // Like vanilla when a proto chunk becomes a level chunk; the level spawns the entities generation placed on its
            // own thread. They stay pending in the chunk until then, so a save in between keeps them.
            if (chunk is Chunk { PendingEntities.Count: > 0 } complete)
                level.QueueEntitySpawn(complete);

            // Its fluid ticks (from features, and from post-processing it and its neighbors) start counting down.
            level.Fluids.Track(chunk);
        }

        return chunk;
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
            var chunk = await this.GenerateChunkAsync(spawnChunkX + dx, spawnChunkZ + dz, null, ChunkGenStage.full, decorateInOrder: true);
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
            this.builder.Generate3DTerrain(chunk);
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
            this.builder.ApplyCarvers(chunk);
            chunk.SetChunkStatus(ChunkGenStage.carvers);
        }
    }

    /// <summary>
    /// Places the features of a chunk (trees, ores, etc.), carving its neighbors first since features write into them.
    /// </summary>
    private async Task DecorateAsync(int cx, int cz)
    {
        // A status never goes back, so a decorated chunk can be skipped without its locks; the check under them decides.
        if ((await this.GetChunkAsync(cx, cz)).ChunkStatus >= ChunkGenStage.features)
            return;

        // On the thread pool, so a caller starting several decorations doesn't run them one after another.
        await this.RunStepAsync(cx, cz, ChunkGenStage.features, () => Task.Run(() => this.DecorateStepAsync(cx, cz)));
    }

    private async Task DecorateStepAsync(int cx, int cz)
    {
        // Carve the area first, each chunk under its own lock and in parallel, so other jobs aren't blocked on the whole area
        // during terrain generation.
        var carving = new Task[9];
        for (var i = 0; i < carving.Length; i++)
            carving[i] = this.CarveAsync(cx + i / 3 - 1, cz + i % 3 - 1);

        await Task.WhenAll(carving);
        await this.LoadStructureStartsAsync(cx, cz);

        using (await this.LockAsync(cx, cz, 1))
        {
            var chunk = await this.GetChunkAsync(cx, cz);
            if (chunk.ChunkStatus >= ChunkGenStage.features)
                return;

            var area = new Dictionary<(int X, int Z), IChunk>();
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dz = -1; dz <= 1; dz++)
                    area[(cx + dx, cz + dz)] = dx == 0 && dz == 0 ? chunk : await this.GetChunkAsync(cx + dx, cz + dz);
            }

            this.builder.Decorate(area, cx, cz);
            chunk.SetChunkStatus(ChunkGenStage.features);
        }

        // The pieces placed may have changed, and a start chunk may have been unloaded meanwhile: loading them again keeps
        // each start's current state in a chunk that will be saved.
        await this.LoadStructureStartsAsync(cx, cz);
    }

    /// <summary>
    /// Generates a chunk up to its carvers, on the thread pool, unless that's done.
    /// </summary>
    private async Task CarveAsync(int cx, int cz)
    {
        if ((await this.GetChunkAsync(cx, cz)).ChunkStatus >= ChunkGenStage.carvers)
            return;

        await this.RunStepAsync(cx, cz, ChunkGenStage.carvers, () => Task.Run(async () =>
        {
            using var chunkLock = await this.LockAsync(cx, cz, 0);
            this.GenerateUpToCarvers(await this.GetChunkAsync(cx, cz), ChunkGenStage.carvers);
        }));
    }

    /// <summary>
    /// Runs a chunk's step once for every request made while it runs; requests after it ended find the chunk's status.
    /// </summary>
    private Task RunStepAsync(int cx, int cz, ChunkGenStage stage, Func<Task> step)
    {
        var key = (cx, cz, stage);
        return this.steps.GetOrAdd(key, newKey => new Lazy<Task>(async () =>
        {
            try
            {
                await step();
            }
            finally
            {
                this.steps.TryRemove(key, out _);
            }
        })).Value;
    }

    /// <summary>
    /// Loads the start chunk of every structure start reaching a chunk, restoring each start's saved state the first time,
    /// so pieces placed before a restart aren't placed again. Like vanilla, each start lives in its start chunk: start
    /// chunks are loaded, or created at the structure starts status, so they save the state again.
    /// </summary>
    /// <remarks>
    /// Done without generation locks, since it may read chunks from disk. Every decoration does this before placing
    /// pieces, and a start is only restored once, so that happens before any of its pieces are placed in this session.
    /// </remarks>
    private async ValueTask LoadStructureStartsAsync(int cx, int cz)
    {
        if (this.builder.Structures is not StructureManager structures)
            return;

        foreach (var start in structures.GetStartsReaching(cx, cz))
        {
            var startChunk = await this.world.GetChunkAsync(start.ChunkX, start.ChunkZ, scheduleGeneration: false) as Chunk;
            StructureManager.RestoreStart(start, startChunk?.StructureStarts);
        }
    }

    // StructureManager.LoadStartChunk: structure searches run synchronously (explorer maps are made while loot is generated),
    // like vanilla's, which also wait for start chunks on the server thread. Getting a chunk takes no generation locks.
    private NbtCompound? LoadStartChunk(int chunkX, int chunkZ) =>
        (this.world.GetChunkAsync(chunkX, chunkZ, scheduleGeneration: false).AsTask().GetAwaiter().GetResult() as Chunk)?.StructureStarts;

    public NbtCompound? SaveStructureStarts(int chunkX, int chunkZ, NbtCompound? loaded) =>
        this.builder.Structures is StructureManager structures ? structures.SaveStarts(chunkX, chunkZ, loaded) : loaded;

    /// <summary>
    /// Every feature that can reach the chunk has run: post-processes its marked blocks and fluids (which reads and may
    /// change its neighbors), then stores its final heightmaps. Its sky light sources are found when it's lit.
    /// </summary>
    private async ValueTask FinishBlocksAsync(IChunk chunk)
    {
        using var locks = await this.LockAsync(chunk.X, chunk.Z, 1);
        if (chunk.ChunkStatus >= ChunkGenStage.initialize_light)
            return;

        var area = new Dictionary<(int X, int Z), IChunk>();
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dz = -1; dz <= 1; dz++)
                area[(chunk.X + dx, chunk.Z + dz)] = dx == 0 && dz == 0 ? chunk : await this.GetChunkAsync(chunk.X + dx, chunk.Z + dz);
        }

        // Post-processing ticks fluids, which may flow into complete neighbors that the level is ticking.
        if (this.world is AbstractLevel level)
            level.Fluids.RunExclusive(() => this.builder.PostProcess(area, chunk.X, chunk.Z, level.Fluids.Rules));
        else
            this.builder.PostProcess(area, chunk.X, chunk.Z);

        this.builder.UpdateFinalHeightmaps(chunk);
        chunk.SetChunkStatus(ChunkGenStage.initialize_light);
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

        // Light spreads into lit neighbors, and complete ones may already be on clients.
        if (this.world is AbstractLevel level)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dz = -1; dz <= 1; dz++)
                {
                    var neighbor = await this.world.GetChunkAsync(chunk.X + dx, chunk.Z + dz, scheduleGeneration: false);
                    if (neighbor is not null && (dx != 0 || dz != 0) && neighbor.IsGenerated)
                        level.SendLightUpdate(neighbor);
                }
            }
        }
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

    public async ValueTask<IDisposable?> LockChunkAsync(int x, int z) => await this.LockAsync(x, z, 0);

    public void Init(ILevel world)
    {
        this.world = world;
        this.builder = new ChunkBuilder(this.Dimension, RandomState.ParseSeed(world.Seed));

        if (this.builder.Structures is StructureManager structures)
            structures.LoadStartChunk = this.LoadStartChunk;
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
