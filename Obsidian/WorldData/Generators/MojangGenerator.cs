using Microsoft.Extensions.Logging;
using Obsidian.API.World;
using Obsidian.Nbt;
using Obsidian.Plugins;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Mojang.Structures;
using Obsidian.WorldData.Lighting;
using Obsidian.WorldData.Structures;
using System.Threading;


namespace Obsidian.WorldData.Generators;

/// <summary>
/// Vanilla's overworld generator.
/// </summary>
internal partial class MojangGenerator : ILevelGenerator, IStructureStartStorage
{
    // Chunk locks are striped so their number stays bounded; stripes are always taken in ascending order.
    private const int LockStripeCount = 4096;

    /// <summary>
    /// Vanilla's overworld generator.
    /// </summary>
    public MojangGenerator()
        : this(MojangDimension.Overworld)
    {
    }

    /// <summary>
    /// A generator of <paramref name="dimension"/>, which levels using it must have the build range of.
    /// </summary>
    protected internal MojangGenerator(MojangDimension dimension)
    {
        this.dimension = dimension;
    }

    public virtual string Id => "minecraft:mojang_generator";

    private MojangDimension dimension;

    /// <summary>
    /// The dimension this generator builds; levels using it must have the dimension's build range. <see cref="Resume"/>
    /// replaces it with one rebuilt from the same level stem.
    /// </summary>
    internal MojangDimension Dimension => Volatile.Read(ref this.dimension);

    /// <summary>
    /// Whether <see cref="Init"/> took the dimension for the level (see <see cref="PluginDimensions.Take"/>): false
    /// when it went before, when the level can't generate and has to be left out, or before <see cref="Init"/>.
    /// Once taken, the level must use <see cref="Dimension"/>'s dimension type.
    /// </summary>
    internal bool IsTaken { get; private set; }

    private ImmutableArray<PluginContainer> failedBy = [];

    /// <summary>
    /// The plugins whose code failing stopped the generator (see <see cref="Contain"/>), or none. It resumes once
    /// they're no longer loaded: reloaded, or gone.
    /// </summary>
    internal ImmutableArray<PluginContainer> FailedBy => this.failedBy;

    private ChunkBuilder? builder;

    /// <summary>
    /// The builder generating the level's chunks, available once <see cref="Init"/> ran, and null once
    /// <see cref="Stop"/> did.
    /// </summary>
    internal ChunkBuilder? Builder => Volatile.Read(ref this.builder);

    // The builder's structure starts, kept once it's dropped so the state of the pieces placed is still saved.
    private StructureStartCache? startCache;

    /// <summary>
    /// The noise settings' sea level, which mobs use.
    /// </summary>
    internal int SeaLevel => this.Dimension.SeaLevel;

    /// <summary>
    /// The biomes' features, read before each chunk is decorated; <see cref="Registries.PluginFeatures.Current"/> when
    /// null. Tests set them.
    /// </summary>
    internal Func<Registries.WorldgenFeatureSet>? Features { get; init; }

    // The builder, for the steps of generation.
    private ChunkBuilder Running => this.Builder
        ?? throw new GeneratorStoppedException($"{this.Dimension.Identifier} stopped generating.");

    internal Vector? FindDolphinTreasure(Vector position)
    {
        if (this.Builder is not { Structures: StructureManager manager } builder)
            return null;

        var targets = manager.StructuresPerStep.SelectMany(step => step).Where(structure =>
            structure.Identifier is "minecraft:buried_treasure" or "minecraft:shipwreck" or "minecraft:ocean_ruin_cold" or "minecraft:ocean_ruin_warm").ToArray();

        try
        {
            using var calls = builder.EnterGenerator();
            return new StructureLocator(manager).FindNearest(targets, position, 16, false);
        }
        catch (Exception ex) when (this.Contain(ex))
        {
            return null;
        }
    }

    private ILevel world;

    // Set by the first plugin failure contained, which is the one logged.
    private int failed;

    private readonly SemaphoreSlim[] chunkLocks = [.. Enumerable.Range(0, LockStripeCount).Select(_ => new SemaphoreSlim(1, 1))];

    // Carving and decorating steps in flight, so requests that need the same step share it instead of queueing on its locks.
    private readonly ConcurrentDictionary<(int X, int Z, ChunkGenStage Stage), Lazy<Task>> steps = [];

    /// <remarks>
    /// Once the generator stopped (see <see cref="Stop"/> and <see cref="Contain"/>), a chunk not generated yet is
    /// returned as it is: its level keeps it, and generates it once the generator resumes.
    /// </remarks>
    public async ValueTask<IChunk> GenerateChunkAsync(int cx, int cz, IChunk? chunk = null,
        ChunkGenStage stage = ChunkGenStage.full)
    {
        chunk ??= new Chunk(cx, cz, this.Dimension.MinY, this.Dimension.Height);

        try
        {
            return await this.GenerateChunkAsync(cx, cz, chunk, stage, decorateInOrder: false);
        }
        catch (Exception ex) when (this.Contain(ex))
        {
            return chunk;
        }
    }

    /// <summary>
    /// Contains a failure of plugins' code, which would otherwise reach the level and end chunk management, or the
    /// server: it's logged with the plugins' names, and the generator stops like it does when they unload (see
    /// <see cref="Stop"/>). The dimension isn't released, as its plugins are still loaded, and vanilla's are shared.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No step leaves a partial result that replaying it would build on: a step before decoration that fails
    /// rewinds its chunk (see <see cref="GenerateUpToCarvers"/>), which generates again once it resumes, while a
    /// decoration that fails counts as done (see <see cref="Decorate"/>).
    /// </para>
    /// <para>
    /// A failure is plugins' when it's raised in a step of a dimension whose noise settings or biome source are made
    /// of their types (<see cref="MojangDimension.Plugins"/>), or placing a feature made of their types: their
    /// density functions run all through the noise, so where the failure was raised can't tell whose it is.
    /// Failures generating with Obsidian's types alone aren't contained.
    /// </para>
    /// </remarks>
    /// <returns>Whether the caller goes on as if the generator had stopped: the failure was plugins', or the
    /// generator had stopped already.</returns>
    internal bool Contain(Exception exception)
    {
        if (exception is GeneratorStoppedException)
            return true;

        var plugins = this.FailedPlugins(exception);
        if (plugins.IsEmpty)
            return false;

        Volatile.Write(ref this.builder, null);
        ImmutableInterlocked.Update(ref this.failedBy, failed => failed.Union(plugins).ToImmutableArray());

        // Steps running alongside may fail too, once the first did.
        if (Interlocked.Exchange(ref this.failed, 1) == 0 && this.world is AbstractLevel level)
        {
            var names = string.Join(", ", plugins.Select(plugin => plugin.Info.Name));
            Log.PluginFailed(level.Logger, exception, this.Dimension.Identifier, names);
        }

        return true;
    }

    // The plugins whose code a failure is attributed to (see Contain), or none.
    private ImmutableArray<PluginContainer> FailedPlugins(Exception exception)
    {
        if (exception is OutOfMemoryException)
            return [];

        for (var inner = exception; inner is not null; inner = inner.InnerException)
        {
            if (inner is PluginGenerationException failure)
                return failure.Plugins;
        }

        return this.Dimension.Plugins;
    }

    /// <summary>
    /// Stops generating, because a plugin the dimension generates with unloads: chunks already generated still load,
    /// and ones being generated finish, but no new step starts. The builder and the dimension's generator parts are
    /// dropped, so that they don't keep the plugin's code loaded; the structure starts stay, and are still saved.
    /// </summary>
    internal void Stop()
    {
        Volatile.Write(ref this.builder, null);

        var dimension = this.Dimension;
        if (!dimension.IsReleased)
            dimension.Release();
    }

    /// <summary>
    /// Generates again once the generator stopped, from the next step on, with <paramref name="dimension"/>: one
    /// rebuilt from the same level stem with the plugins loaded now, or the same one once the plugins that failed are
    /// gone. The structure starts and chunk locks stay, so chunks generated before are kept as they are, and ones
    /// partway through go on from their last finished step.
    /// </summary>
    /// <returns>Whether it generates again: building the generator runs plugins' code, which may fail (see
    /// <see cref="Contain"/>).</returns>
    internal bool Resume(MojangDimension dimension)
    {
        if (this.Builder is not null)
            return true;

        Volatile.Write(ref this.dimension, dimension);
        this.failedBy = [];
        Interlocked.Exchange(ref this.failed, 0);

        return this.Start();
    }

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
    /// found in the chunks spiraling out from it. None once the generator stopped.
    /// </summary>
    public virtual async ValueTask<VectorD?> FindSpawnPointAsync()
    {
        try
        {
            return await this.SearchSpawnAsync();
        }
        catch (Exception ex) when (this.Contain(ex))
        {
            return null;
        }
    }

    private async ValueTask<VectorD?> SearchSpawnAsync()
    {
        var (x, z) = this.Running.FindClimateSpawn();
        var spawnChunkX = x >> 4;
        var spawnChunkZ = z >> 4;
        var spawn = new Vector((spawnChunkX << 4) + 8, SpawnFinder.DefaultSpawnHeight, (spawnChunkZ << 4) + 8);

        foreach (var (dx, dz) in SpawnFinder.SpiralOffsets())
        {
            var chunk = await this.GenerateChunkAsync(spawnChunkX + dx, spawnChunkZ + dz, null, ChunkGenStage.full, decorateInOrder: true);
            var found = SpawnFinder.FindSpawnInChunk(chunk, this.Dimension.DimensionType.Element.HasCeiling);
            if (found is not null)
            {
                spawn = found.Value;
                break;
            }
        }

        return new VectorD(spawn.X + 0.5, spawn.Y, spawn.Z + 0.5);
    }

    /// <summary>
    /// Runs the stages up to and including carvers that <paramref name="stage"/> asks for.
    /// The caller must hold the chunk's lock.
    /// </summary>
    /// <remarks>
    /// A step that fails partway rewinds the chunk to before its biomes (see <see cref="Chunk.RewindTerrain"/>), so
    /// its steps replay from scratch: they only write into the chunk, but they read what they wrote (the surface
    /// replaces the noise's stone, carvers carve what the surface left) and add post-processing marks, so replaying
    /// one over its partial result could differ.
    /// </remarks>
    private void GenerateUpToCarvers(IChunk chunk, ChunkGenStage stage)
    {
        var builder = this.Running;

        try
        {
            RunStepsUpToCarvers(builder, chunk, stage);
        }
        catch (Exception ex) when (ex is not GeneratorStoppedException && chunk is Chunk generated)
        {
            // A stopped generator refuses a step before it writes anything, which needs no rewinding.
            generated.RewindTerrain();
            throw;
        }
    }

    private static void RunStepsUpToCarvers(ChunkBuilder builder, IChunk chunk, ChunkGenStage stage)
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
            builder.PopulateBiomes(chunk);
            chunk.SetChunkStatus(ChunkGenStage.biomes);
        }

        if (ChunkGenStage.noise <= stage && chunk.ChunkStatus < ChunkGenStage.noise)
        {
            // Generate terrain using 3D density sampling with aquifer support
            builder.Generate3DTerrain(chunk);
            chunk.SetChunkStatus(ChunkGenStage.noise);
        }

        if (ChunkGenStage.surface <= stage && chunk.ChunkStatus < ChunkGenStage.surface)
        {
            // Apply surface rules to replace stone with grass, dirt, sand, etc.
            builder.ApplySurfaceRules(chunk);
            chunk.SetChunkStatus(ChunkGenStage.surface);
        }

        if (ChunkGenStage.carvers <= stage && chunk.ChunkStatus < ChunkGenStage.carvers)
        {
            // Carve classic caves and ravines
            builder.ApplyCarvers(chunk);
            chunk.SetChunkStatus(ChunkGenStage.carvers);
        }
    }

    /// <summary>
    /// Generates the chunks from (<paramref name="minX"/>, <paramref name="minZ"/>) up to (<paramref name="maxX"/>,
    /// <paramref name="maxZ"/>), exclusive, up to their light, along with the neighbors they need, staged so that steps
    /// running at once never wait on each other's locks. Completing the chunks afterwards only marks them complete.
    /// </summary>
    /// <remarks>
    /// Every chunk is carved in parallel first. Decorating, post-processing and lighting then each run in 9 waves, one per
    /// class of a 3 by 3 colouring of chunk positions: chunks of a wave are 3 apart, so the areas they lock never overlap.
    /// Completing chunks one by one in order instead keeps most concurrent jobs waiting on their neighbors' locks. Light
    /// doesn't depend on the order chunks are lit in, and decorations meeting at borders already ran in any order.
    /// Cancelling stops new chunks from starting; steps already running finish.
    /// </remarks>
    internal async Task PrepareAreaAsync(int minX, int minZ, int maxX, int maxZ, CancellationToken cancellationToken = default)
    {
        // Completing a chunk decorates its neighbors, and decorating a chunk carves its neighbors.
        await Parallel.ForEachAsync(Area(minX - 2, minZ - 2, maxX + 2, maxZ + 2), cancellationToken,
            async (position, _) => await this.CarveAsync(position.X, position.Z));

        await InWavesAsync(Area(minX - 1, minZ - 1, maxX + 1, maxZ + 1), position => this.DecorateAsync(position.X, position.Z));
        await InWavesAsync(Area(minX, minZ, maxX, maxZ), async position => await this.FinishBlocksAsync(await this.GetChunkAsync(position.X, position.Z)));
        await InWavesAsync(Area(minX, minZ, maxX, maxZ), async position => await this.LightAsync(await this.GetChunkAsync(position.X, position.Z)));

        async Task InWavesAsync(IEnumerable<(int X, int Z)> positions, Func<(int X, int Z), Task> step)
        {
            var all = positions.ToList();
            for (var wave = 0; wave < 9; wave++)
            {
                await Parallel.ForEachAsync(all.Where(position => Mod3(position.X) * 3 + Mod3(position.Z) == wave), cancellationToken,
                    async (position, _) => await step(position));
            }
        }

        static int Mod3(int value) => (value % 3 + 3) % 3;

        static IEnumerable<(int X, int Z)> Area(int minX, int minZ, int maxX, int maxZ)
        {
            for (var x = minX; x < maxX; x++)
            {
                for (var z = minZ; z < maxZ; z++)
                    yield return (x, z);
            }
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

        // Found once, as the generator may have stopped by the time the pieces placed are kept.
        var starts = this.FindStartsReaching(cx, cz);
        await this.LoadStartChunksAsync(starts);

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

            this.Decorate(area, cx, cz);
            chunk.SetChunkStatus(ChunkGenStage.features);
        }

        // The pieces placed may have changed, and a start chunk may have been unloaded meanwhile: loading them again keeps
        // each start's current state in a chunk that will be saved.
        await this.LoadStartChunksAsync(starts);
    }

    /// <summary>
    /// Places a chunk's features. When plugins' code fails partway, the generator stops (see <see cref="Contain"/>)
    /// and the chunk counts as decorated without the features left: those placed wrote blocks, entities and ticks,
    /// into its neighbors too, which decorating it again would duplicate.
    /// </summary>
    private void Decorate(Dictionary<(int X, int Z), IChunk> area, int cx, int cz)
    {
        try
        {
            this.Running.Decorate(area, cx, cz);
        }
        catch (Exception ex) when (ex is not GeneratorStoppedException && this.Contain(ex))
        {
            // A stopped generator refuses before any feature is placed, so the chunk is decorated once it resumes.
            if (this.world is AbstractLevel level)
                Log.DecorationCut(level.Logger, cx, cz, this.Dimension.Identifier);
        }
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

    // The structure starts reaching a chunk, which decorating it places. Finding them checks biomes and the terrain,
    // which runs the generator's plugins' code.
    private List<StructureStart> FindStartsReaching(int cx, int cz)
    {
        var builder = this.Running;
        if (builder.Structures is not StructureManager structures)
            return [];

        using (builder.EnterGenerator())
            return [.. structures.GetStartsReaching(cx, cz)];
    }

    /// <summary>
    /// Loads the start chunk of every start, restoring each start's saved state the first time, so pieces placed
    /// before a restart aren't placed again. Like vanilla, each start lives in its start chunk: start chunks are
    /// loaded, or created at the structure starts status, so they save the state again.
    /// </summary>
    /// <remarks>
    /// Done without generation locks, since it may read chunks from disk. Every decoration does this before placing
    /// pieces, and a start is only restored once, so that happens before any of its pieces are placed in this session.
    /// </remarks>
    private async ValueTask LoadStartChunksAsync(List<StructureStart> starts)
    {
        foreach (var start in starts)
        {
            var startChunk = await this.world.GetChunkAsync(start.ChunkX, start.ChunkZ, scheduleGeneration: false) as Chunk;
            StructureManager.RestoreStart(start, startChunk?.StructureStarts);
        }
    }

    // StructureManager.LoadStartChunk: structure searches run synchronously (explorer maps are made while loot is generated),
    // like vanilla's, which also wait for start chunks on the server thread. Getting a chunk takes no generation locks.
    private NbtCompound? LoadStartChunk(int chunkX, int chunkZ) =>
        (this.world.GetChunkAsync(chunkX, chunkZ, scheduleGeneration: false).AsTask().GetAwaiter().GetResult() as Chunk)?.StructureStarts;

    internal bool IsInsideFortress(Vector position)
    {
        if (this.Builder is not { Structures: StructureManager manager } builder)
            return false;
        var fortress = manager.StructuresPerStep.SelectMany(step => step).FirstOrDefault(structure => structure.Identifier == "minecraft:fortress");
        if (fortress is null)
            return false;

        try
        {
            using var calls = builder.EnterGenerator();
            return manager.GetReferencingStarts(position.X >> 4, position.Z >> 4, fortress).Any(start =>
                start.Pieces.Any(piece => piece.BoundingBox.IsInside(position)) ||
                start.BoundingBox.IsInside(position) && this.world is AbstractLevel level &&
                level.GetLoadedChunk(position.X >> 4, position.Z >> 4)
                    ?.GetBlock(position.X, position.Y - 1, position.Z).RegistryId == BlockIds.NetherBricks);
        }
        catch (Exception ex) when (this.Contain(ex))
        {
            return false;
        }
    }

    /// <remarks>
    /// The starts are saved after the generator stopped too, with the pieces steps placed until then.
    /// </remarks>
    public NbtCompound? SaveStructureStarts(int chunkX, int chunkZ, NbtCompound? loaded) =>
        this.startCache is StructureStartCache cache
            ? cache.Save(chunkX, chunkZ, loaded)
            : loaded;

    /// <summary>
    /// Every feature that can reach the chunk has run: post-processes its marked blocks and fluids (which reads and may
    /// change its neighbors), then stores its final heightmaps. Its sky light sources are found when it's lit.
    /// </summary>
    private async ValueTask FinishBlocksAsync(IChunk chunk)
    {
        using var locks = await this.LockAsync(chunk.X, chunk.Z, 1);
        if (chunk.ChunkStatus >= ChunkGenStage.initialize_light)
            return;

        // Read once: the generator stopping between the calls would leave the chunk post-processed but unfinished.
        var builder = this.Running;
        var area = new Dictionary<(int X, int Z), IChunk>();
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dz = -1; dz <= 1; dz++)
                area[(chunk.X + dx, chunk.Z + dz)] = dx == 0 && dz == 0 ? chunk : await this.GetChunkAsync(chunk.X + dx, chunk.Z + dz);
        }

        // Post-processing ticks fluids, which may flow into complete neighbors that the level is ticking.
        if (this.world is AbstractLevel level)
            level.Fluids.RunExclusive(() => builder.PostProcess(area, chunk.X, chunk.Z, level.Fluids.Rules));
        else
            builder.PostProcess(area, chunk.X, chunk.Z);

        builder.UpdateFinalHeightmaps(chunk);
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

        // A dimension generates as data packs and plugins define it when its level starts, and keeps that. One that
        // went since its level's creator read it can't generate: the creator leaves the level out (see IsTaken).
        if (PluginDimensions.Take(this.Dimension) is not MojangDimension taken)
            return;

        Volatile.Write(ref this.dimension, taken);
        this.IsTaken = true;

        this.Start();
    }

    // Builds the builder the steps run with, keeping the structure starts of the one before it, if any. Binding the
    // noise runs plugins' code, which may fail: the generator then stays stopped.
    private bool Start()
    {
        ChunkBuilder builder;

        try
        {
            builder = new ChunkBuilder(
                this.Dimension,
                RandomState.ParseSeed(this.world.Seed),
                features: this.Features,
                startCache: this.startCache);
        }
        catch (Exception ex) when (this.Contain(ex))
        {
            return false;
        }

        if (builder.Structures is StructureManager structures)
        {
            structures.LoadStartChunk = this.LoadStartChunk;
            this.startCache = structures.StartCache;
        }

        Volatile.Write(ref this.builder, builder);

        return true;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error,
            Message = "{Level} stopped generating new chunks until {Plugins} reload or the server restarts: "
                + "generating with them failed")]
        public static partial void PluginFailed(ILogger logger, Exception exception, string level, string plugins);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Chunk ({X}, {Z}) of {Level} is kept decorated without the features its failure left out")]
        public static partial void DecorationCut(ILogger logger, int x, int z, string level);
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
internal sealed class MojangNetherGenerator() : MojangGenerator(MojangDimension.Nether)
{
    public override string Id => "minecraft:the_nether";

    // Vanilla only searches a spawn in the overworld.
    public override ValueTask<VectorD?> FindSpawnPointAsync() => ValueTask.FromResult<VectorD?>(null);
}

/// <summary>
/// Vanilla's end generator, used by the <c>minecraft:the_end</c> dimension.
/// </summary>
internal sealed class MojangEndGenerator() : MojangGenerator(MojangDimension.End)
{
    public override string Id => "minecraft:the_end";

    // Vanilla's ServerLevel.END_SPAWN_POINT, on the obsidian platform.
    public override ValueTask<VectorD?> FindSpawnPointAsync() => ValueTask.FromResult<VectorD?>(new VectorD(100.5, 50, 0.5));
}

/// <summary>
/// The generator of a plugin's dimension, built from its level stem file
/// (see <see cref="Registries.PluginDimensions"/>).
/// </summary>
/// <remarks>
/// Its spawn is searched like the overworld's, with the dimension type's ceiling, so players sent there without a
/// position land on the ground; vanilla doesn't search spawns outside the overworld.
/// </remarks>
internal sealed class LevelStemGenerator(MojangDimension dimension) : MojangGenerator(dimension)
{
    public override string Id => this.Dimension.Identifier;
}
