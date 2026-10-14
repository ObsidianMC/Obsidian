using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Configuration;
using Obsidian.API.Entities;
using Obsidian.API.Inventory;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.Entities;
using Obsidian.Entities.Factories;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Plugins;
using Obsidian.WorldData.Fluids;
using Obsidian.WorldData.Generators;
using Obsidian.WorldData.Lighting;
using Obsidian.WorldData.Portals;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel : ILevel
{
    private bool generated;

    public LevelData LevelData { get; internal set; } = default!;

    public ConcurrentDictionary<Guid, IPlayer> Players { get; protected set; } = [];

    public ILevelGenerator Generator { get; internal set; }

    public ConcurrentDictionary<long, IRegion> Regions { get; protected set; } = [];

    public ConcurrentQueue<long> ChunksToGen { get; protected set; } = [];

    // The chunks queued or generating, so a chunk is queued once however often it's asked for.
    private readonly ConcurrentDictionary<long, byte> queuedChunks = [];

    // The chunks queued while the level runs, taken from ChunksToGen and started nearest to a player first (see
    // TryTakeNearestJob), so chunks players wait for don't queue behind ones they've moved away from. Locked on itself.
    private readonly List<long> waitingJobs = [];
    private readonly List<(int X, int Z)> playerChunks = [];

    // Generation jobs run in the background, at most one per core: each job fans out over its neighbors' steps itself.
    private readonly SemaphoreSlim generationSlots = new(Environment.ProcessorCount);

    // A failed background generation job, rethrown by the next ManageChunksAsync so failures still reach the server.
    private Exception? generationFailure;

    // The generation jobs finished since the last FlushRegionsAsync started, so disposal knows whether to save again.
    private int jobsSinceFlush;

    // The chunks generation jobs may write, counted per job, which aren't unloaded meanwhile: a job writes up to 2 chunks
    // from its own (its neighbors' decorations reach theirs), and writes to an unloaded chunk would be lost.
    private readonly ConcurrentDictionary<long, int> generationPins = [];

    // Set once DisposeAsync starts. A level can be disposed twice (by the server's shutdown and again by the host's
    // container), and the second call would wait forever on the generation slots the first one keeps.
    private int disposed;

    // The chunks kept loaded around the spawn, filled in once the spawn is known.
    protected readonly long[] spawnChunks;

    public ConcurrentHashSet<long> LoadedChunks { get; protected set; } = [];

    public string Name { get; }
    public string Seed { get; }
    public string FolderPath { get; protected set; } = string.Empty;

    public bool Loaded { get; protected set; }

    public long Time
    {
        get => LevelData.Time;
        set
        {
            LevelData.Time = value;
            this.BroadcastTime();
        }
    }

    public int DayTime
    {
        get => LevelData.DayTime;
        set
        {
            LevelData.DayTime = value;
            this.BroadcastTime();
        }
    }

    public int RegionCount => this.Regions.Count;
    public int ChunksToGenCount => this.ChunksToGen.Count + this.waitingJobs.Count;
    public int LoadedChunkCount => this.Regions.Values.Sum(x => x.LoadedChunkCount);

    public IPacketBroadcaster PacketBroadcaster { get; }
    public IEventDispatcher EventDispatcher { get; }
    public ServerConfiguration Configuration { get; private set; }
    public GameMode DefaultGamemode => LevelData.DefaultGamemode;

    /// <summary>
    /// The level's name, which clients know it by: its dimension's identifier, like <c>minecraft:the_nether</c>.
    /// </summary>
    public string DimensionName { get; protected set; } = string.Empty;

    /// <summary>
    /// The level's dimension type. Vanilla's dimensions are named after theirs; plugins' dimensions may use any.
    /// </summary>
    public DimensionCodec DimensionType { get; private set; } = default!;

    /// <summary>
    /// The dimension's lowest block Y.
    /// </summary>
    public int MinY { get; private set; } = -64;

    /// <summary>
    /// The dimension's build height in blocks.
    /// </summary>
    public int Height { get; private set; } = 384;

    public string LevelDataFilePath { get; protected set; }

    /// <summary>
    /// The folder of the level's chunk region files, inside <see cref="FolderPath"/>: Obsidian's <c>regions</c>, or
    /// vanilla's <c>region</c> in worlds laid out like vanilla saves.
    /// </summary>
    protected virtual string RegionFolderName => "regions";

    /// <summary>
    /// Called while <see cref="GenerateAsync"/> runs, with the chunks generated so far and the chunks to generate.
    /// </summary>
    internal Action<int, int>? GenerationProgress { get; set; }

    protected internal ILogger Logger { get; }

    /// <summary>
    /// The level's fluids and their scheduled ticks.
    /// </summary>
    internal LevelFluids Fluids { get; }

    internal RuntimeLightEngine Lighting { get; }

    internal LevelPortals Portals { get; }

    internal void PinPortalChunk(int x, int z, int delta)
    {
        var key = NumericsHelper.IntsToLong(x, z);
        if (this.generationPins.AddOrUpdate(key, delta, (_, count) => count + delta) == 0)
            this.generationPins.TryRemove(KeyValuePair.Create(key, 0));
    }

    private readonly IDisposable optionsMonitor;
    private readonly Lock regionLock = new();
    private readonly ConcurrentQueue<Func<ValueTask>> entityActions = new();
    private MobSpawner? mobSpawner;
    private readonly SemaphoreSlim simulationGate = new(1, 1);

    internal void EnqueueEntityAction(Func<ValueTask> action) => entityActions.Enqueue(action);

    internal void QueueChunkPopulation(IChunk chunk) => EnqueueEntityAction(() =>
    {
        if (ReferenceEquals(GetLoadedChunk(chunk.X, chunk.Z), chunk))
        {
            if (chunk is Chunk concrete) RegisterChunkMusic(concrete);
            (mobSpawner ??= new MobSpawner(this)).PopulateChunk(chunk);
        }
        return default;
    });

    internal bool IsMobTicking(VectorD position)
    {
        var (x, z) = Region.ChunkOf(position);
        if (GetLoadedChunk(x, z) == null)
            return false;

        var simulationDistance = Configuration.SimulationDistance;
        foreach (var player in Players.Values)
        {
            if (player.GameMode == GameMode.Spectator) continue;
            var playerChunk = Region.ChunkOf(player.Position);
            if (Math.Abs(playerChunk.X - x) <= simulationDistance && Math.Abs(playerChunk.Z - z) <= simulationDistance)
                return true;
        }
        return false;
    }

    internal IChunk? GetLoadedChunk(int chunkX, int chunkZ)
    {
        // Entity initialization calls this under EntityLock; reads must never acquire the fluid tick lock.
        return GetRegionForChunk(chunkX, chunkZ) is Region region
            ? region.GetLoadedChunk(NumericsHelper.Modulo(chunkX, 32), NumericsHelper.Modulo(chunkZ, 32))
            : null;
    }

    internal bool TryMoveEntity(IEntity entity, VectorD from, VectorD to)
    {
        if (Generator is MobTestGenerator && entity is Entity testEntity && !MobTestGenerator.AllowsMovement(testEntity, to))
            return false;
        var destination = GetRegionForLocation(to);
        var origin = GetRegionForLocation(from);
        if (destination == null)
            return false;
        if (ReferenceEquals(origin, destination))
            return true;
        if (!destination.Entities.TryAdd(entity.EntityId, entity))
            return false;
        origin?.Entities.TryRemove(entity.EntityId, out _);
        return true;
    }

    // Chunks whose pending entities (placed by world generation or loaded from disk) spawn on the level's tick rather than
    // on generator or loading threads.
    private readonly ConcurrentQueue<Chunk> entitySpawns = new();



    public AbstractLevel(ILogger logger, IPacketBroadcaster packetBroadcaster, IOptionsMonitor<ServerConfiguration> configuration,
        IEventDispatcher eventDispatcher, ILevelGenerator worldGenerator, string name, string seed)
    {
        this.optionsMonitor = configuration.OnChange(newConfig =>
        {
            this.Configuration = newConfig;
        });

        this.Logger = logger;
        this.PacketBroadcaster = packetBroadcaster;
        this.Configuration = configuration.CurrentValue;
        this.EventDispatcher = eventDispatcher;
        this.Generator = worldGenerator;
        this.Name = name;
        this.Seed = seed;
        this.Fluids = new LevelFluids(this);
        this.Lighting = new RuntimeLightEngine(
            this.Fluids.TickLock, () => this.DimensionType?.Element.HasSkylight ?? true);
        this.Portals = new LevelPortals(this);

        var spawnChunkCount = 2 * this.Configuration.SpawnChunkRadius + 1;
        this.spawnChunks = new long[spawnChunkCount * spawnChunkCount];

        this.Generator.Init(this);
    }

    public ValueTask<bool> DestroyEntityAsync(IEntity entity)
    {
        var destroyed = new RemoveEntitiesPacket(entity.EntityId);

        this.PacketBroadcaster.QueuePacketToLevel(this, destroyed, entity is IPlayer ? [entity.EntityId] : []);

        var (chunkX, chunkZ) = Region.ChunkOf(entity.Position);

        var region = GetRegionForChunk(chunkX, chunkZ);

        return region is null ? ValueTask.FromResult(false) : ValueTask.FromResult(region.Entities.TryRemove(entity.EntityId, out _));
    }

    public abstract Task<bool> LoadAsync(DimensionCodec codec);
    public abstract Task SaveAsync();

    public IRegion? GetRegionForLocation(VectorD location)
    {
        var (chunkX, chunkZ) = Region.ChunkOf(location);
        long key = NumericsHelper.IntsToLong(chunkX >> Region.CubicRegionSizeShift, chunkZ >> Region.CubicRegionSizeShift);
        Regions.TryGetValue(key, out var region);
        return region;
    }

    public IRegion? GetRegionForChunk(int chunkX, int chunkZ)
    {
        long value = NumericsHelper.IntsToLong(chunkX >> Region.CubicRegionSizeShift, chunkZ >> Region.CubicRegionSizeShift);

        return Regions.TryGetValue(value, out var region) ? region : null;
    }

    public IRegion? GetRegionForChunk(Vector location) => GetRegionForChunk(location.X, location.Z);

    public async ValueTask<IChunk?> GetChunkAsync(int chunkX, int chunkZ, bool scheduleGeneration = true)
    {
        var region = GetRegionForChunk(chunkX, chunkZ) ?? LoadRegion(chunkX >> Region.CubicRegionSizeShift, chunkZ >> Region.CubicRegionSizeShift);

        if (region is null)
            return null;

        var (x, z) = (NumericsHelper.Modulo(chunkX, Region.CubicRegionSize), NumericsHelper.Modulo(chunkZ, Region.CubicRegionSize));
        var packedXZ = NumericsHelper.IntsToLong(chunkX, chunkZ);

        var chunk = await region.GetChunkAsync(x, z);

        if (chunk is not null)
        {
            if (!chunk.IsGenerated && scheduleGeneration)
            {
                this.QueueGeneration(packedXZ);
                return null;
            }

            LoadedChunks.Add(packedXZ);
            this.Fluids.Track(chunk);
            return chunk;
        }

        if (scheduleGeneration)
        {
            this.QueueGeneration(packedXZ);
            return null;
        }

        return await region.GetOrAddChunkAsync(x, z, () => new Chunk(chunkX, chunkZ, this.MinY, this.Height, ChunkGenStage.structure_starts));
    }

    public async ValueTask<IBlock?> GetBlockAsync(int x, int y, int z)
    {
        // Like vanilla, everything outside the build range reads as void air.
        if (this.IsOutsideBuildHeight(y))
            return BlocksRegistry.VoidAir;

        var c = await GetChunkAsync(x.ToChunkCoord(), z.ToChunkCoord(), false);
        return c?.GetBlock(x, y, z);
    }

    /// <summary>
    /// Whether <paramref name="y"/> is outside the dimension's build range, where blocks can't be read or placed.
    /// </summary>
    public bool IsOutsideBuildHeight(int y) => y < this.MinY || y >= this.MinY + this.Height;

    public async ValueTask<int?> GetWorldSurfaceHeightAsync(int x, int z)
    {
        var c = await GetChunkAsync(x.ToChunkCoord(), z.ToChunkCoord(), false);
        return c?.Heightmaps[HeightmapType.WorldSurface]
            .GetHeight(NumericsHelper.Modulo(x, 16), NumericsHelper.Modulo(z, 16));
    }

    public ValueTask SetBlockAsync(int x, int y, int z, IBlock block) =>
        this.SetBlockAsync(x, y, z, block, doBlockUpdate: false);

    public ValueTask SetBlockAsync(int x, int y, int z, IBlock block, bool doBlockUpdate) =>
        this.SetBlockAsync(new Vector(x, y, z), block, UpdateFlags(doBlockUpdate) | BlockUpdateFlags.Clients);

    public async ValueTask SetBlockAsync(Vector location, IBlock block, BlockUpdateFlags flags) =>
        await this.TrySetBlockAsync(location, block, flags, canReplace: null);

    /// <summary>
    /// Changes a block like <see cref="SetBlockAsync(Vector, IBlock, BlockUpdateFlags)"/> if
    /// <paramref name="canReplace"/> accepts the block there, deciding and changing it in one step, so that nothing
    /// changes the block in between.
    /// </summary>
    /// <param name="canReplace">
    /// Whether the block there may be replaced, or null for any; it runs under the level's tick lock, so it mustn't
    /// wait on anything.
    /// </param>
    /// <returns>Whether the block was changed.</returns>
    internal async ValueTask<bool> TrySetBlockAsync(
        Vector location,
        IBlock block,
        BlockUpdateFlags flags,
        Func<IBlock, bool>? canReplace,
        BlockPlacement? placement = null)
    {
        if (!await this.TrySetBlockUntrackedAsync(
            location.X, location.Y, location.Z, block, flags, canReplace, placement))
            return false;

        TrackMobEgg(location, block);
        CheckWither(location, block);
        CheckSnowGolem(location, block);
        return true;
    }

    internal void BroadcastBlockChange(IBlock block, Vector location)
    {
        var packet = new BlockUpdatePacket(location, block.GetHashCode());
        foreach (var player in PlayersInRange(location))
        {
            player.SendPacket(packet);
        }
    }

    /// <summary>
    /// Sends a level event (a sound or particle effect, like lava fizzing) to the players that have its chunk.
    /// </summary>
    internal void BroadcastLevelEvent(int type, Vector location, int data)
    {
        var packet = new LevelEventPacket(type, location, data);
        foreach (var player in PlayersInRange(location))
            player.SendPacket(packet);
    }

    public IEnumerable<IPlayer> PlayersInRange(Vector location)
    {
        var (x, z) = location.ToChunkCoord();
        var packedXZ = NumericsHelper.IntsToLong(x, z);

        return this.Players.Values.Where(player => player.LoadedChunks.Contains(packedXZ));
    }

    public ValueTask SetBlockUntrackedAsync(Vector location, IBlock block, bool doBlockUpdate = false) =>
        SetBlockUntrackedAsync(location.X, location.Y, location.Z, block, doBlockUpdate);

    public ValueTask SetBlockUntrackedAsync(int x, int y, int z, IBlock block, bool doBlockUpdate = false) =>
        SetBlockUntrackedAsync(x, y, z, block, UpdateFlags(doBlockUpdate));

    /// <summary>
    /// Changes a block, with the side effects <paramref name="flags"/> ask for (see <see cref="AfterBlockSet"/>).
    /// </summary>
    internal async ValueTask SetBlockUntrackedAsync(int x, int y, int z, IBlock block, BlockUpdateFlags flags) =>
        await this.TrySetBlockUntrackedAsync(x, y, z, block, flags, canReplace: null);

    /// <summary>
    /// Changes a block if <paramref name="canReplace"/> (null for any) accepts the block there, with the side effects
    /// <paramref name="flags"/> ask for (see <see cref="AfterBlockSet"/>).
    /// </summary>
    /// <returns>Whether the block was changed.</returns>
    internal async ValueTask<bool> TrySetBlockUntrackedAsync(
        int x,
        int y,
        int z,
        IBlock block,
        BlockUpdateFlags flags,
        Func<IBlock, bool>? canReplace,
        BlockPlacement? placement = null)
    {
        if (this.IsOutsideBuildHeight(y))
            return false;

        var c = await GetChunkAsync(x.ToChunkCoord(), z.ToChunkCoord(), false);
        if (c is null)
            return false;

        // Under the lock fluid ticks and growth hold, so that neither reads a block, has it changed here, and then
        // writes what it worked out from the old block over the change; and with the change's updates, so that
        // another change's can't run between the block and its own.
        API.BlockEntities.BlockEntity? removed;
        lock (this.Fluids.TickLock)
        {
            if (c is Chunk { EntitiesUnloaded: true })
                return false;

            using var handLock = placement?.LockHand();
            var old = c.GetBlock(x, y, z);
            if (canReplace is not null && !canReplace(old))
                return false;

            using var prepared = placement?.Prepare(block, new Vector(x, y, z), this.Logger);
            if (placement is not null && prepared is null)
                return false;

            // Publish the initialized entity first: lock-free readers must never see a filled box as empty.
            removed = this.UpdateBlockEntity(c, new Vector(x, y, z), block, prepared?.Entity);
            c.SetBlock(x, y, z, block);
            placement?.Consume();
            this.CompleteBlockEntityPlacement(c, prepared?.Entity);
            this.AfterBlockSet(new Vector(x, y, z), old, block, flags);
        }

        if (removed is not null)
            await this.RunBlockEntityRemovalAsync(removed);

        return true;
    }

    public IEnumerable<IEntity> GetEntitiesInRange(VectorD location, float distance = 10f)
    {
        foreach (IPlayer player in GetPlayersInRange(location, distance))
        {
            yield return player;
        }

        foreach (IEntity entity in GetNonPlayerEntitiesInRange(location, distance))
        {
            yield return entity;
        }
    }

    public IEnumerable<IEntity> GetNonPlayerEntitiesInRange(VectorD location, float distance)
    {
        if (!float.IsFinite(distance) || distance < 0f)
        {
            yield break;
        }

        var distanceSquared = (double)distance * distance;
        foreach (var region in GetRegions())
        {
            foreach (var entity in region.Entities.Values)
            {
                if (entity.Type == EntityType.Player)
                    continue;

                var difference = entity.Position - location;
                if (difference.MagnitudeSquared() <= distanceSquared)
                    yield return entity;
            }
        }

        IEnumerable<IRegion> GetRegions()
        {
            // Owner lookups use float.MaxValue. Scan loaded regions before converting such ranges to chunk coordinates.
            if (distance >= Region.CubicRegionSize * 16)
            {
                foreach (var region in Regions.Values)
                    yield return region;
                yield break;
            }

            var (left, top) = Region.ChunkOf(location - new VectorD(distance));
            var (right, bottom) = Region.ChunkOf(location + new VectorD(distance));

            left >>= Region.CubicRegionSizeShift;
            right >>= Region.CubicRegionSizeShift;
            top >>= Region.CubicRegionSizeShift;
            bottom >>= Region.CubicRegionSizeShift;

            for (int x = left; x <= right; x++)
            {
                for (int z = top; z <= bottom; z++)
                {
                    if (GetRegionForChunk(x << Region.CubicRegionSizeShift, z << Region.CubicRegionSizeShift) is not Region region)
                        continue;

                    yield return region;
                }
            }
        }
    }

    public IEnumerable<IPlayer> GetPlayersInRange(VectorD location, float distance)
    {
        if (float.IsNaN(distance) || distance < 0f)
        {
            yield break;
        }

        if (distance == 0f)
        {
            foreach (var player in Players.Values)
            {
                if (player.Position == location)
                {
                    yield return player;
                }
            }
            yield break;
        }

        distance *= distance;

        foreach (var player in Players.Values)
        {
            var difference = player.Position - location;
            if (difference.X * difference.X + difference.Z * difference.Z <= distance)
            {
                yield return player;
            }
        }
    }

    public IEnumerable<IPlayer> GetPlayersInChunkRange(Vector worldPosition)
    {
        var (x, z) = worldPosition.ToChunkCoord();

        var packedXZ = NumericsHelper.IntsToLong(x, z);
        return this.Players.Values.Where(player => player.LoadedChunks.Contains(packedXZ));
    }

    public bool TryAddPlayer(IPlayer player) => Players.TryAdd(player.Uuid, player);

    // Matches the instance, not just the UUID, so a stale reference can't remove a newer player with the same UUID.
    public bool TryRemovePlayer(IPlayer player) => Players.TryRemove(new KeyValuePair<Guid, IPlayer>(player.Uuid, player));

    public async virtual Task DoWorldTickAsync()
    {
        TickStage = "waiting for simulation gate";
        await simulationGate.WaitAsync();
        try
        {
            await TickLevelAsync();
        }
        finally
        {
            simulationGate.Release();
        }
    }

    private async Task TickLevelAsync()
    {
        TickStage = "starting";
        if (LevelData is null)
            return;

        LevelData.Time += this.Configuration.TimeTickSpeedMultiplier;
        LevelData.RainTime -= this.Configuration.TimeTickSpeedMultiplier;

        // Like vanilla, block ticks and then fluid ticks run before entities tick.
        TickStage = "block and fluid ticks";
        this.Fluids.Tick();

        await this.Portals.TickAsync();

        foreach (var region in Regions.Values.OfType<Region>())
            region.TickInhabitedTime(this);

        var actionCount = entityActions.Count;
        for (var index = 0; index < actionCount && entityActions.TryDequeue(out var action); index++)
        {
            TickStage = $"entity action {index + 1}/{actionCount}";
            await action();
        }

        // Deferred unloads keep old-generation state first; a rebound type can respawn it in this same tick.
        this.SpawnPendingEntities();

        await TickEndFightAsync();

        TickStage = "natural spawning";
        (mobSpawner ??= new MobSpawner(this)).Tick();

        // Like vanilla's chunk ticks, with natural spawning and before entities.
        TickStage = "random ticks";
        this.Lighting.Flush();
        this.TickRandomBlocks();

        // Snapshot once so crossing a region boundary cannot tick an entity twice.
        var entities = Regions.Values.SelectMany(region => region.Entities.Values)
            .DistinctBy(entity => entity.EntityId).OrderBy(entity => entity.EntityId).ToArray();
        foreach (var entity in entities)
        {
            if (entity is Mob { HasAi: true } or ItemEntity or ExperienceOrb && !IsMobTicking(entity.Position))
                continue;
            if (GetRegionForLocation(entity.Position)?.Entities.ContainsKey(entity.EntityId) == true)
            {
                TickStage = $"entity {entity.EntityId} ({entity.GetType().Name})";
                await entity.TickAsync();
                await this.TouchPluginBlocksAsync(entity);
            }
        }

        await TickRaidsAsync();
        await TickMobGameEventsAsync();
        await TickMobEggsAsync();

        TickStage = "block entities";
        await TickBlockEntitiesAsync();

        TickStage = "moving entities between regions";
        foreach (var region in Regions.Values.OfType<Region>())
            region.MoveEntitiesToTheirRegions();

        TickStage = "runtime light";
        foreach (var packet in this.Lighting.TakeUpdates())
        {
            var location = new Vector(packet.Chunk.X << 4, 0, packet.Chunk.Z << 4);
            foreach (var watcher in this.PlayersInRange(location))
                watcher.SendPacket(packet);
        }

        foreach (var player in Players.Values)
        {
            TickStage = $"tracking for {player.Username}";
            await player.SynchronizeTrackedEntitiesAsync();
        }
        TickStage = "idle";
    }

    internal string TickStage { get; private set; } = "not started";
    internal bool SavingEntities { get; private set; }

    /// <summary>
    /// Sends a chunk's light to the players that have the chunk, after it changed.
    /// </summary>
    internal void SendLightUpdate(IChunk chunk)
    {
        this.Lighting.QueueChunkUpdate(chunk);
    }

    /// <summary>
    /// Queues a complete chunk's pending entities (<see cref="Chunk.PendingEntities"/>) to spawn on the next tick, like
    /// vanilla when a proto chunk becomes a level chunk or an entity chunk is loaded.
    /// </summary>
    internal void QueueEntitySpawn(Chunk chunk)
    {
        this.entitySpawns.Enqueue(chunk);
    }

    /// <remarks>
    /// Pending entities are loaded like saved ones (see <see cref="EntityNbt.Load"/>), so generated entities get the saved
    /// fields Obsidian's entities model, and keep the others for when they're saved.
    /// </remarks>
    private void SpawnPendingEntities()
    {
        while (this.entitySpawns.TryDequeue(out var chunk))
        {
            // Under the chunk's entity lock, so a save of the chunk finds each entity either pending or spawned.
            using (chunk.EntityLock.EnterScope())
            {
                // An unloaded chunk saved its pending entities; they spawn when it's loaded again.
                if (chunk.EntitiesUnloaded)
                    continue;

                RegisterChunkMusic(chunk);
                RegisterMobEggs(chunk);

                foreach (var pending in chunk.PendingEntities)
                {
                    var tag = EntityNbt.ToNbt(pending);
                    if (EntityNbt.Load(tag, this) is not Entity entity)
                        chunk.UnspawnableEntities.Add(tag);
                    else if ((entity is not Mob { HasAi: true, Alive: false } || entity is EnderDragon) &&
                        !this.Regions.Values.Any(region => region.Entities.Values.Any(existing => existing.Uuid == entity.Uuid)))
                    {
                        // One whose plugin type started unloading since it was loaded stays as saved.
                        if (!this.TrySpawnEntity(entity, hostOwned: true))
                            chunk.UnspawnableEntities.Add(tag);
                    }
                }

                chunk.PendingEntities.Clear();
            }
        }
    }

    public IRegion LoadRegionByChunk(int chunkX, int chunkZ)
    {
        int regionX = chunkX >> Region.CubicRegionSizeShift, regionZ = chunkZ >> Region.CubicRegionSizeShift;
        return LoadRegion(regionX, regionZ);
    }

    public IRegion LoadRegion(int regionX, int regionZ)
    {
        long value = NumericsHelper.IntsToLong(regionX, regionZ);

        if (Regions.TryGetValue(value, out var region))
            return region;

        using (regionLock.EnterScope())
        {
            if (Regions.TryGetValue(value, out region))
                return region;

            region = new Region(regionX, regionZ, FolderPath, this.RegionFolderName, minY: this.MinY, height: this.Height)
            {
                LockChunk = this.Generator.LockChunkAsync,
                FluidTickLock = this.Fluids.TickLock,
                FlushLight = this.Lighting.Flush,
                LightLoaded = this.Lighting.Track,
                LightUnloaded = this.Lighting.Untrack,
                EntitiesLoaded = this.QueueEntitySpawn,
                SaveStructureStarts = this.Generator is IStructureStartStorage storage ? storage.SaveStructureStarts : null
            };

            if (this.Regions.TryAdd(value, region))
                _ = region.InitAsync();
            else
            {
                // Another thread added the region first; discard our copy and return existing
                region = Regions[value]!;
            }

            return region;
        }
    }

    public async Task UnloadRegionAsync(int regionX, int regionZ)
    {
        long value = NumericsHelper.IntsToLong(regionX, regionZ);
        await simulationGate.WaitAsync();
        try
        {
            IRegion? r;
            lock (this.Fluids.TickLock)
            {
                if (!Regions.TryGetValue(value, out r))
                    return;

                // Retire publication and detach lighting before another reader can create a replacement region.
                if (r is Region concrete)
                    concrete.RetireLighting();

                Regions.TryRemove(KeyValuePair.Create(value, r));
            }

            if (r is not null)
                await r.FlushAsync();
        }
        finally
        {
            simulationGate.Release();
        }
    }

    public async Task ManageChunksAsync()
    {
        if (LevelData.Time > 0 && LevelData.Time % (20 * 30) == 0)
        {
            var chunksToKeep = new List<long>();
            Players.Values.ForEach(p =>
            {
                chunksToKeep.AddRange(p.LoadedChunks);
            });

            foreach (var chunk in LoadedChunks.Except(chunksToKeep).Except(this.spawnChunks).Where(chunk => !this.generationPins.ContainsKey(chunk)))
            {
                if (LoadedChunks.TryRemove(chunk))
                {
                    NumericsHelper.LongToInts(chunk, out var cx, out var cz);
                    await simulationGate.WaitAsync();
                    try
                    {
                        var r = GetRegionForChunk(cx, cz);
                        if (r is not null)
                            await r.UnloadChunk(NumericsHelper.Modulo(cx, Region.CubicRegionSize), NumericsHelper.Modulo(cz, Region.CubicRegionSize));
                    }
                    finally
                    {
                        simulationGate.Release();
                    }
                }
            }
        }

        if (Interlocked.Exchange(ref this.generationFailure, null) is Exception failure)
            ExceptionDispatchInfo.Throw(failure);

        this.StartQueuedJobs();
    }

    /// <summary>
    /// Starts queued chunks on free slots without waiting for them, so a slow chunk never holds up the others or the tick.
    /// Each job starts the next when it finishes, so free slots don't wait for the next tick.
    /// </summary>
    private void StartQueuedJobs()
    {
        // Disposal takes every slot as jobs finish, which new jobs would keep it from.
        while (Volatile.Read(ref this.disposed) == 0 && this.generationSlots.Wait(0))
        {
            if (!this.TryTakeNearestJob(out var job))
            {
                this.generationSlots.Release();
                return;
            }

            // On the thread pool, so a job that finishes at once doesn't start the next one inside it, or run on the tick.
            _ = Task.Run(() => this.GenerateQueuedChunkAsync(job, startNext: true));
        }
    }

    /// <summary>
    /// Takes the queued chunk nearest to a player, or the oldest one when no player is in the level.
    /// </summary>
    private bool TryTakeNearestJob(out long job)
    {
        lock (this.waitingJobs)
        {
            while (this.ChunksToGen.TryDequeue(out var queued))
                this.waitingJobs.Add(queued);

            if (this.waitingJobs.Count == 0)
            {
                job = 0;
                return false;
            }

            this.playerChunks.Clear();
            foreach (var player in this.Players.Values)
                this.playerChunks.Add(player.Position.ToChunkCoord());

            // The first of the nearest, so chunks at the same distance go in the order they were asked for.
            var nearest = 0;
            if (this.playerChunks.Count > 0)
            {
                var nearestDistance = long.MaxValue;
                for (var i = 0; i < this.waitingJobs.Count; i++)
                {
                    NumericsHelper.LongToInts(this.waitingJobs[i], out var x, out var z);
                    foreach (var (playerX, playerZ) in this.playerChunks)
                    {
                        long dx = x - playerX, dz = z - playerZ;
                        var distance = dx * dx + dz * dz;
                        if (distance < nearestDistance)
                            (nearest, nearestDistance) = (i, distance);
                    }
                }
            }

            job = this.waitingJobs[nearest];
            this.waitingJobs.RemoveAt(nearest);
            return true;
        }
    }

    private void QueueGeneration(long packedXZ)
    {
        if (this.queuedChunks.TryAdd(packedXZ, 0))
            this.ChunksToGen.Enqueue(packedXZ);
    }

    private void PinGenerationArea(int chunkX, int chunkZ, int delta)
    {
        for (var dx = -2; dx <= 2; dx++)
        {
            for (var dz = -2; dz <= 2; dz++)
            {
                var key = NumericsHelper.IntsToLong(chunkX + dx, chunkZ + dz);
                if (this.generationPins.AddOrUpdate(key, delta, (_, count) => count + delta) == 0)
                    this.generationPins.TryRemove(KeyValuePair.Create(key, 0));
            }
        }
    }

    /// <summary>
    /// Generates a dequeued chunk on one of the generation slots, which it releases when done. Failures are kept for the
    /// next <see cref="ManageChunksAsync"/> to rethrow.
    /// </summary>
    /// <param name="startNext">Whether the next queued chunk starts once this one is done (see <see cref="StartQueuedJobs"/>).</param>
    private async Task GenerateQueuedChunkAsync(long job, bool startNext = false)
    {
        NumericsHelper.LongToInts(job, out var jobX, out var jobZ);
        this.PinGenerationArea(jobX, jobZ, 1);
        try
        {
            var region = this.GetRegionForChunk(jobX, jobZ) ?? this.LoadRegionByChunk(jobX, jobZ);

            var (x, z) = (NumericsHelper.Modulo(jobX, Region.CubicRegionSize), NumericsHelper.Modulo(jobZ, Region.CubicRegionSize));

            var c = await region.GetOrAddChunkAsync(x, z,
                () => new Chunk(jobX, jobZ, this.MinY, this.Height, ChunkGenStage.structure_starts));
            var populate = !c.IsGenerated;
            if (populate)
            {
                c = await this.Generator.GenerateChunkAsync(jobX, jobZ, c);
            }
            region.SetChunk(c);
            // Mojang generation queues population at completion, including chunks generated by the spawn search.
            if (populate && c.IsGenerated && this.Generator is not MojangGenerator)
                QueueChunkPopulation(c);
        }
        catch (Exception ex)
        {
            Interlocked.CompareExchange(ref this.generationFailure, ex, null);
        }
        finally
        {
            this.PinGenerationArea(jobX, jobZ, -1);
            this.queuedChunks.TryRemove(job, out _);
            Interlocked.Increment(ref this.jobsSinceFlush);
            this.generationSlots.Release();

            if (startNext)
                this.StartQueuedJobs();
        }
    }

    public async virtual Task FlushRegionsAsync()
    {
        await simulationGate.WaitAsync();
        try
        {
            Interlocked.Exchange(ref this.jobsSinceFlush, 0);
            SavingEntities = true;
            await Task.WhenAll(Regions.Select(pair => pair.Value.FlushAsync()));
        }
        finally
        {
            SavingEntities = false;
            simulationGate.Release();
        }
    }

    /// <summary>
    /// Spawns a falling block entity of the given block state, centered on the block at the position.
    /// </summary>
    public IEntity SpawnFallingBlock(VectorD position, IBlock block)
    {
        position.X += 0.5f;
        position.Z += 0.5f;

        FallingBlock entity = new(position)
        {
            Type = EntityType.FallingBlock,
            EntityId = Server.GetNextEntityId(),
            Level = this,
            Block = block,
        };

        entity.SpawnEntity(null, entity.Block.GetHashCode());

        TryAddEntity(entity);

        return entity;
    }

    public ValueTask<IBlockEntity?> GetBlockEntityAsync(Vector blockPosition) =>
        GetBlockEntityAsync(blockPosition.X, blockPosition.Y, blockPosition.Z);

    public async ValueTask<IBlockEntity?> GetBlockEntityAsync(int x, int y, int z)
    {
        var c = await GetChunkAsync(x.ToChunkCoord(), z.ToChunkCoord(), false);
        var blockEntity = c?.GetBlockEntity(x, y, z);

        this.AttachBlockEntity(blockEntity);

        return blockEntity;
    }

    public ValueTask SetBlockEntity(Vector blockPosition, IBlockEntity tileEntityData) =>
        SetBlockEntity(blockPosition.X, blockPosition.Y, blockPosition.Z, tileEntityData);

    /// <remarks>
    /// The block entity replaces the one there without the side effects of removing it. One created by plugin code
    /// takes the position, and the type of the block there unless it has one.
    /// </remarks>
    public async ValueTask SetBlockEntity(int x, int y, int z, IBlockEntity tileEntityData)
    {
        var c = await GetChunkAsync(x.ToChunkCoord(), z.ToChunkCoord(), false);
        if (c is null)
            return;

        if (tileEntityData is API.BlockEntities.BlockEntity placed)
        {
            placed.BlockPosition = new Vector(x, y, z);

            if (placed.Id.Length == 0)
            {
                placed.Id = c.GetBlock(x, y, z).BlockEntityType()
                    ?? throw new ArgumentException($"The block at {placed.BlockPosition} has no block entity type.");
            }

            placed.Owner ??= Plugins.PluginProviders.PluginLoadContext.OwnerOf(placed.GetType().Assembly);
            this.AttachBlockEntity(placed);
        }

        var previous = c.GetBlockEntity(x, y, z);
        c.SetBlockEntity(x, y, z, tileEntityData);

        if (previous is API.BlockEntities.BlockEntity replaced && !ReferenceEquals(previous, tileEntityData))
            replaced.IsRemoved = true;
    }

    /// <summary>
    /// The block entity at <paramref name="blockPosition"/>, created for the block there when it has a type but no
    /// block entity, as in worlds saved before the server created them with their blocks.
    /// </summary>
    /// <returns>The block entity, or null when there's none or the chunk isn't loaded.</returns>
    internal async ValueTask<IBlockEntity?> GetOrCreateBlockEntityAsync(Vector blockPosition)
    {
        var (x, y, z) = (blockPosition.X, blockPosition.Y, blockPosition.Z);
        if (await GetChunkAsync(x.ToChunkCoord(), z.ToChunkCoord(), false) is not Chunk chunk)
            return null;

        var blockEntity = chunk.GetBlockEntity(x, y, z);
        var block = chunk.GetBlock(x, y, z);

        if (blockEntity is null && block.HasBlockEntity() && block.BlockEntityType() is string type)
        {
            var created = BlockEntityNbt.Create(type, blockPosition, logger: this.Logger);
            blockEntity = chunk.GetOrAddBlockEntity(x, y, z, created);
        }

        this.AttachBlockEntity(blockEntity);

        return blockEntity;
    }

    public IEntity SpawnEntity(VectorD position, EntityType type)
    {
        if (type == EntityType.FallingBlock)
            return SpawnFallingBlock(position + (0, 20, 0), BlocksRegistry.Sand);

        return GetNewEntitySpawner()
            .WithEntityType(type)
            .AtPosition(position)
            .Spawn();
    }

    public IEntity SpawnEntity(
        VectorD position,
        API.Entities.EntityDefinition definition,
        VectorD motion = default,
        IEntity? owner = null)
    {
        var entity = PluginEntity.Create(definition, this);

        entity.EntityId = Server.GetNextEntityId();
        entity.Position = position;
        entity.Motion = motion;

        if (entity is PluginEntity projectile)
            projectile.SetOwner(owner);

        return this.SpawnEntity(entity);
    }

    public IEntity SpawnEntity(IEntity entity)
    {
        if (!this.TrySpawnEntity(entity))
            throw new InvalidOperationException($"Could not register entity {entity.EntityId}.");

        return entity;
    }

    /// <summary>
    /// Spawns an entity, unless it can't be added to the level (see <see cref="TryAddEntity"/>).
    /// </summary>
    /// <returns>Whether the entity spawned.</returns>
    /// <exception cref="InvalidOperationException">The plugin spawning the entity is unloading.</exception>
    internal bool TrySpawnEntity(IEntity entity, bool hostOwned = false)
    {
        if (entity is EnderDragon dragon) dragon.EnsureMultipartIds();
        if (entity is Mob mob)
        {
            mob.InitializeAi();
            // Summoned vexes, offspring and slime splits inherit the enclosure they spawn in.
            if (Generator is MobTestGenerator testWorld) testWorld.AssignPen(mob);
        }
        if (!TryAddEntity(entity, hostOwned))
            return false;
        entity.SpawnEntity();
        return true;
    }

    public void SpawnExperienceOrbs(VectorD position, short count = 1)
    {
        // Resolve once for the batch. An empty ambient call does not prove there is no plugin caller.
        var spawner = count > 0 ? PluginEntities.FindSpawner() : null;

        while (count > 0)
        {
            var value = ExperienceOrb.SplitValue(count);
            var orb = new ExperienceOrb
            {
                Level = this,
                EntityId = Server.GetNextEntityId(),
                Position = position,
                Value = value,
                SpawnedBy = spawner
            };

            if (!this.TrySpawnEntity(orb, hostOwned: true))
                throw new InvalidOperationException($"Could not register entity {orb.EntityId}.");

            count -= (short)value;
        }
    }

    public void SpawnParticles(
        ParticleData particle,
        VectorD position,
        int count = 1,
        VectorF spread = default,
        float speed = 0,
        bool force = false)
    {
        ArgumentNullException.ThrowIfNull(particle);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var packet = new LevelParticlesPacket
        {
            OverrideLimiter = force,
            Position = position,
            Offset = spread,
            MaxSpeed = speed,
            ParticleCount = count,
            Data = particle
        };

        var range = force ? 512.0 : 32.0;
        var players = this.Players.Values
            .Where(player => IsInParticleRange(player.Position, position, range))
            .Select(player => player.EntityId)
            .ToArray();

        if (players.Length > 0)
            this.PacketBroadcaster.QueuePacketTo(packet, players);
    }

    // Vanilla's ServerLevel.sendParticles range, which decides who sees them: closer than 32 blocks (512 when the
    // limiter is overridden) in 3D, measured from the centre of the block the player stands in.
    internal static bool IsInParticleRange(VectorD player, VectorD particle, double range)
    {
        var x = Math.Floor(player.X) + 0.5 - particle.X;
        var y = Math.Floor(player.Y) + 0.5 - particle.Y;
        var z = Math.Floor(player.Z) + 0.5 - particle.Z;

        return x * x + y * y + z * z < range * range;
    }

    public abstract void Initialize(DimensionCodec codec);

    /// <summary>
    /// Sets the level's dimension name, and takes its build range from its dimension type.
    /// </summary>
    protected void SetDimension(string dimensionName, DimensionCodec codec)
    {
        this.DimensionName = dimensionName;
        this.DimensionType = codec;
        this.MinY = codec.Element.MinY;
        this.Height = codec.Element.Height;
        this.Fluids.Rules = FluidRules.ForDimension(codec);
    }

    /// <summary>
    /// Starts the initial generation of the world, which includes pregenerating chunks in a square around the spawn and loading their regions,
    /// as well as setting the world spawn if specified. This should be called after Initialize and before allowing players to join.
    /// </summary>
    /// <param name="cancellationToken">
    /// Stops generation: no new chunk starts, and <see cref="OperationCanceledException"/> is thrown. The chunks already
    /// generating finish in the background, and disposal waits for them.
    /// </param>
    public async Task GenerateAsync(CancellationToken cancellationToken = default)
    {
        if (this.generated)
            return;

        int pregenerationRange = this.Configuration.PregenerateChunkRange;

        // Generators that know where players spawn pick it first, so pregeneration surrounds the spawn.
        if (LevelData.SpawnPosition.Y == 0)
        {
            var spawn = await Generator.FindSpawnPointAsync();
            if (spawn is not null)
            {
                LevelData.SpawnPosition = spawn.Value;
                Log.SpawnSet(this.Logger, this.Name, spawn.Value);
            }
        }

        var (centerX, centerZ) = LevelData.SpawnPosition.ToChunkCoord();
        int regionPregenRange = (pregenerationRange >> Region.CubicRegionSizeShift) + 1;
        int centerRegionX = centerX >> Region.CubicRegionSizeShift;
        int centerRegionZ = centerZ >> Region.CubicRegionSizeShift;

        foreach (var x in Enumerable.Range(centerRegionX - regionPregenRange, regionPregenRange * 2 + 1))
        {
            for (int z = centerRegionZ - regionPregenRange; z < centerRegionZ + regionPregenRange; z++)
                LoadRegion(x, z);
        }

        for (int x = centerX - pregenerationRange; x < centerX + pregenerationRange; x++)
        {
            for (int z = centerZ - pregenerationRange; z < centerZ + pregenerationRange; z++)
            {
                this.QueueGeneration(NumericsHelper.IntsToLong(x, z));
            }
        }

        var startChunks = this.ChunksToGenCount;
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        Log.Generating(this.Logger, startChunks, this.Name);

        // Generating starts with the staging below, which does most of the work of a noise generator's chunks, so a
        // listener hears of it now rather than once the staging is done.
        this.GenerationProgress?.Invoke(0, startChunks);

        // Most of the work is staged across every core first, without the lock waits of completing chunks one by one, so the
        // jobs below only complete the chunks.
        if (this.Generator is MojangGenerator mojang)
        {
            await mojang.PrepareAreaAsync(centerX - pregenerationRange, centerZ - pregenerationRange,
                centerX + pregenerationRange, centerZ + pregenerationRange, cancellationToken);
        }

        // A window of jobs in queue order: a new job starts as soon as one finishes.
        var jobs = new List<Task>(startChunks);
        var completedChunks = 0;
        var lastPercent = -1;
        var flushedThousands = 0;
        while (this.ChunksToGen.TryDequeue(out var job))
        {
            await this.generationSlots.WaitAsync(cancellationToken);
            jobs.Add(this.GenerateQueuedChunkAsync(job));

            while (completedChunks < jobs.Count && jobs[completedChunks].IsCompleted)
                completedChunks++;

            var pctComplete = completedChunks * 100 / startChunks;
            if (pctComplete != lastPercent)
            {
                lastPercent = pctComplete;

                // Without a listener, the progress goes to the terminal.
                if (this.GenerationProgress is not null)
                    this.GenerationProgress(completedChunks, startChunks);
                else
                {
                    var cps = completedChunks / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);
                    var remain = (startChunks - completedChunks) / (int)Math.Max(cps, 1);
                    System.Console.Write("\r{0} chunks/second - {1}% complete - {2} seconds remaining   ",
                        cps.ToString("###.00"), pctComplete, remain);
                }
            }

            // The flush after the loop saves the last chunks.
            if (completedChunks / 1024 > flushedThousands && !this.ChunksToGen.IsEmpty)
            {
                flushedThousands = completedChunks / 1024;
                await this.FlushRegionsAsync();
            }
        }

        await Task.WhenAll(jobs);
        if (Interlocked.Exchange(ref this.generationFailure, null) is Exception failure)
            ExceptionDispatchInfo.Throw(failure);

        if (this.GenerationProgress is not null)
            this.GenerationProgress(startChunks, startChunks);
        else
        {
            System.Console.Write("\r{0} chunks/second - 100% complete - 0 seconds remaining   ",
                (startChunks / stopwatch.Elapsed.TotalSeconds).ToString("###.00"));
            System.Console.WriteLine();
        }

        await FlushRegionsAsync();
        await SetWorldSpawnAsync();

        {
            var index = 0;
            var (x, z) = LevelData.SpawnPosition.ToChunkCoord();
            for (var cx = x - this.Configuration.SpawnChunkRadius; cx < x + this.Configuration.SpawnChunkRadius; cx++)
                for (var cz = z - this.Configuration.SpawnChunkRadius; cz < z + this.Configuration.SpawnChunkRadius; cz++)
                    this.spawnChunks[index++] = NumericsHelper.IntsToLong(cx, cz);
        }

        this.generated = true;
    }

    private async Task SetWorldSpawnAsync()
    {
        if (LevelData.SpawnPosition.Y != 0)
            return;

        var pregenRange = this.Configuration.PregenerateChunkRange;
        var region = GetRegionForLocation(VectorD.Zero)!;
        foreach (var chunk in region.GeneratedChunks())
        {
            for (int bx = 0; bx < 16; bx++)
            {
                for (int bz = 0; bz < 16; bz++)
                {
                    var by = chunk.Heightmaps[HeightmapType.MotionBlocking].GetHeight(bx, bz);
                    IBlock block = chunk.GetBlock(bx, by, bz);

                    if (by < 64 || !block.Is(BlockIds.GrassBlock) && !block.Is(BlockIds.Sand))
                    {
                        continue;
                    }

                    if (!chunk.GetBlock(bx, by + 1, bz).IsAir || !chunk.GetBlock(bx, by + 2, bz).IsAir)
                    {
                        continue;
                    }

                    var worldPos = new VectorD(bx + 0.5 + (chunk.X * 16), by + 1, bz + 0.5 + (chunk.Z * 16));
                    LevelData.SpawnPosition = worldPos;
                    Log.SpawnSet(this.Logger, this.Name, worldPos);

                    for (int x = chunk.X - pregenRange; x < chunk.X + pregenRange; x++)
                    {
                        for (int z = chunk.Z - pregenRange; z < chunk.Z + pregenRange; z++)
                        {
                            await GetChunkAsync(x, z);
                        }
                    }

                    return;
                }
            }
        }
        if (this is IDimension && await GetChunkAsync(0, 0, false) is { } dimensionChunk)
        {
            for (var x = 0; x < 16; x++)
            for (var z = 0; z < 16; z++)
            {
                var y = dimensionChunk.Heightmaps[HeightmapType.MotionBlocking].GetHeight(x, z);
                var ground = dimensionChunk.GetBlock(x, y, z);
                if (ground.IsAir || ground.IsLiquid || IsOutsideBuildHeight(y + 2) ||
                    !dimensionChunk.GetBlock(x, y + 1, z).IsAir || !dimensionChunk.GetBlock(x, y + 2, z).IsAir) continue;
                LevelData.SpawnPosition = new VectorD(x + 0.5f, y + 1, z + 0.5f);
                Log.SpawnSet(this.Logger, this.Name, LevelData.SpawnPosition);
                return;
            }
        }
        Log.SpawnNotFound(this.Logger, this.Name);
    }

    /// <summary>
    /// Adds an entity to the level, credited to the plugin whose code adds it, if any, so that unloading the plugin
    /// removes it (see <see cref="PluginEntities"/>).
    /// </summary>
    /// <returns>
    /// Whether the entity was added: not when its region isn't loaded, an entity with its id is in it, or it's of a
    /// plugin's entity type whose plugin is unloaded or unloading.
    /// </returns>
    /// <exception cref="InvalidOperationException">The plugin adding the entity is unloading.</exception>
    public bool TryAddEntity(IEntity entity) => this.TryAddEntity(entity, hostOwned: false);

    // Only known host paths or batches that already credited their caller may bypass the attribution stack walk.
    private bool TryAddEntity(IEntity entity, bool hostOwned)
    {
        if (!PluginEntities.HasLoadedType(entity))
            return false;

        var spawner = PluginEntities.Credit(entity, hostOwned);

        if (!PluginEntities.HasLoadedType(entity))
            return false;

        var (chunkX, chunkZ) = Region.ChunkOf(entity.Position);

        var region = GetRegionForChunk(chunkX, chunkZ);

        if (region is null || !region.Entities.TryAdd(entity.EntityId, entity))
            return false;

        // Unloading closes a plugin before removing its entities, so one added meanwhile is removed there or here.
        if (spawner is { IsClosing: true } || !PluginEntities.HasLoadedType(entity))
        {
            region.Entities.TryRemove(new KeyValuePair<int, IEntity>(entity.EntityId, entity));
            spawner?.ThrowIfClosing();

            return false;
        }

        return true;
    }

    /// <summary>
    /// Runs <paramref name="action"/> between the level's ticks, once the tick running now ends.
    /// </summary>
    /// <param name="action">What to run.</param>
    /// <param name="timeout">How long to wait for the tick to end.</param>
    /// <returns>Whether the action ran: not if the tick didn't end in time, or the level was disposed.</returns>
    internal async Task<bool> TryRunBetweenTicksAsync(Action action, TimeSpan timeout)
    {
        try
        {
            if (!await simulationGate.WaitAsync(timeout))
                return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        try
        {
            action();
        }
        finally
        {
            simulationGate.Release();
        }

        return true;
    }

    protected void BroadcastTime() =>
        this.PacketBroadcaster.QueuePacketToLevel(this, new SetTimePacket(LevelData.Time, LevelData.Time % 24000, true));

    public async virtual ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) != 0)
            return;

        GC.SuppressFinalize(this);

        this.optionsMonitor.Dispose();

        // Waits out the generation jobs, holding every slot so no new one starts, and saves what jobs finished since the
        // last save started, including jobs that were still running during the server's save on stop.
        for (var i = 0; i < Environment.ProcessorCount; i++)
            await this.generationSlots.WaitAsync();

        // Nothing generates any more, so the level's generator can let go of what it holds (a plugin's generator, see
        // PluginLevelGenerator); the saves below still lock chunks through it.
        (this.Generator as IDisposable)?.Dispose();

        if (Interlocked.Exchange(ref this.generationFailure, null) is Exception failure)
            Log.ChunkGenerationFailed(this.Logger, failure);

        if (Volatile.Read(ref this.jobsSinceFlush) != 0)
            await this.FlushRegionsAsync();

        foreach (var region in Regions.Values)
        {
            await region.DisposeAsync();
        }
        simulationGate.Dispose();
    }

    public IEntitySpawner GetNewEntitySpawner() => new EntitySpawner(this);
    public ValueTask<IChunk?> GetChunkAsync(Vector worldLocation, bool scheduleGeneration = true) =>
        this.GetChunkAsync(worldLocation.X, worldLocation.Z, scheduleGeneration);

    public ValueTask<IBlock?> GetBlockAsync(Vector location) => this.GetBlockAsync(location.X, location.Y, location.Z);
    public ValueTask SetBlockAsync(Vector location, IBlock block) => this.SetBlockAsync(location.X, location.Y, location.Z, block);
    public ValueTask SetBlockAsync(Vector location, IBlock block, bool doBlockUpdate) =>
        this.SetBlockAsync(location.X, location.Y, location.Z, block, doBlockUpdate);

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Generating {ChunkCount} chunks for {LevelName}")]
        public static partial void Generating(ILogger logger, int chunkCount, string levelName);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Spawn of {LevelName} set to {Position}")]
        public static partial void SpawnSet(ILogger logger, string levelName, VectorD position);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to find a spawn position for {LevelName}")]
        public static partial void SpawnNotFound(ILogger logger, string levelName);

        [LoggerMessage(Level = LogLevel.Error, Message = "Generating a chunk failed")]
        public static partial void ChunkGenerationFailed(ILogger logger, Exception exception);
    }
}
