using Obsidian.API.World;
using Obsidian.WorldData.Generators.Mojang;
using System.Threading;


namespace Obsidian.WorldData.Generators;

internal class MojangGenerator : ILevelGenerator
{
    // Chunk locks are striped so their number stays bounded; stripes are always taken in ascending order.
    private const int LockStripeCount = 256;

    public string Id => "minecraft:mojang_generator";

    private ChunkBuilder builder;
    private ILevel world;

    private readonly SemaphoreSlim[] chunkLocks = [.. Enumerable.Range(0, LockStripeCount).Select(_ => new SemaphoreSlim(1, 1))];

    // The single instance of every chunk being generated. Features write into neighboring chunks, so a chunk can be
    // touched by several jobs before it's complete, and they must all share one instance.
    private readonly ConcurrentDictionary<(int X, int Z), IChunk> protoChunks = new();

    // Fluids flagged during generation, scheduled once their chunk is complete (vanilla's post-processing).
    private readonly ConcurrentDictionary<(int X, int Z), List<Vector>> pendingFluidUpdates = new();

    public async ValueTask<IChunk> GenerateChunkAsync(int cx, int cz, IChunk? chunk = null, ChunkGenStage stage = ChunkGenStage.full)
    {
        chunk ??= new Chunk(cx, cz);

        // Sanity checks
        if (chunk.IsGenerated)
            return chunk;

        chunk = this.protoChunks.GetOrAdd((cx, cz), chunk);

        using (await this.LockAsync(cx, cz, 0))
            this.GenerateUpToCarvers(chunk, stage);

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
            // TODO: Implement light initialization
            chunk.SetChunkStatus(ChunkGenStage.initialize_light);
        }

        if (ChunkGenStage.light <= stage && chunk.ChunkStatus < ChunkGenStage.light)
        {
            Lighting.InitialFillSkyLight(chunk);
            await Lighting.LightFromNeighbors(chunk, this.world);
            chunk.SetChunkStatus(ChunkGenStage.light);
            await Lighting.LightToNeighbors(chunk, this.world);
        }

        if (ChunkGenStage.spawn <= stage && chunk.ChunkStatus < ChunkGenStage.spawn)
        {
            // TODO: Implement spawn point calculation
            chunk.SetChunkStatus(ChunkGenStage.spawn);
        }

        chunk.SetChunkStatus(ChunkGenStage.full);
        this.protoChunks.TryRemove((cx, cz), out _);
        await this.ScheduleFluidUpdatesAsync(chunk);
        return chunk;
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
                this.GenerateUpToCarvers(await this.GetProtoChunkAsync(cx + dx, cz + dz), ChunkGenStage.carvers);
            }
        }

        using var locks = await this.LockAsync(cx, cz, 1);

        var chunk = await this.GetProtoChunkAsync(cx, cz);
        if (chunk.ChunkStatus >= ChunkGenStage.features)
            return;

        var area = new Dictionary<(int X, int Z), IChunk>();
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dz = -1; dz <= 1; dz++)
                area[(cx + dx, cz + dz)] = dx == 0 && dz == 0 ? chunk : await this.GetProtoChunkAsync(cx + dx, cz + dz);
        }

        this.builder.Decorate(area, cx, cz);
        chunk.SetChunkStatus(ChunkGenStage.features);
    }

    private async ValueTask<IChunk> GetProtoChunkAsync(int cx, int cz)
    {
        if (this.protoChunks.TryGetValue((cx, cz), out var chunk))
            return chunk;

        chunk = await this.world.GetChunkAsync(cx, cz, scheduleGeneration: false)
            ?? throw new InvalidOperationException($"Chunk ({cx}, {cz}) couldn't be loaded.");

        return chunk.IsGenerated ? chunk : this.protoChunks.GetOrAdd((cx, cz), chunk);
    }

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

    private List<Vector> GetPendingFluidUpdates(IChunk chunk) => this.pendingFluidUpdates.GetOrAdd((chunk.X, chunk.Z), _ => []);

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
        this.builder = new ChunkBuilder(RandomState.ParseSeed(world.Seed));
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
