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
    private readonly ConcurrentDictionary<long, GenerationDemand> queuedChunks = [];

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

    /// <summary>
    /// Whether the level was disposed: saved a last time, with no ticks to come.
    /// </summary>
    internal bool IsDisposed => Volatile.Read(ref this.disposed) != 0;

    // The chunks kept loaded around the spawn, filled in once the spawn is known.
    protected readonly long[] spawnChunks;

    public ConcurrentHashSet<long> LoadedChunks { get; protected set; } = [];

    /// <summary>
    /// The level's non-player entities by chunk, for range queries.
    /// </summary>
    internal EntityGrid EntityGrid { get; } = new();

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

    /// <summary>
    /// Runs a spawner minecart's spawner for a tick, from the block it's in (see <see cref="SpawnerMinecart"/>).
    /// </summary>
    internal void TickSpawnerMinecart(DataBlockEntity spawner) =>
        (mobSpawner ??= new MobSpawner(this)).TickSpawnerMinecart(spawner);
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

        if (this.IsTicketTicking(x, z))
            return true;

        var simulationDistance = Configuration.SimulationDistance;
        foreach (var (_, player) in Players)
        {
            if (player.GameMode == GameMode.Spectator) continue;
            var playerChunk = Region.ChunkOf(player.Position);
            if (Math.Abs(playerChunk.X - x) <= simulationDistance && Math.Abs(playerChunk.Z - z) <= simulationDistance)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Has the next flush write every loaded chunk (see <see cref="Region.MarkChunksUnsaved"/>).
    /// </summary>
    internal void MarkChunksUnsaved()
    {
        foreach (var region in this.Regions.Values.OfType<Region>())
            region.MarkChunksUnsaved();
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
        if (destination is not Region target || !target.TryAddEntity(entity))
            return false;
        (origin as Region)?.TryRemoveEntity(entity);
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
        lock (ItemEntity.TransferLock)
            return ValueTask.FromResult(this.RemoveEntity(entity, transferring: false));
    }

    // Transfers reuse the instance and its attachment holder; only a real removal retires storage.
    // The caller holds TransferLock, shared with transfer commits and the entity save snapshot.
    internal bool RemoveEntity(IEntity entity, bool transferring)
    {
        if (this.GetRegionForLocation(entity.Position) is not Region region || !region.TryRemoveEntity(entity))
            return false;

        EntityGrid.Leave(entity);

        if (entity is Entity concrete)
            concrete.CancelEntityPortalTravel();

        if (!transferring && entity is not IPlayer)
            API.Data.AttachmentStorage.For(entity).Retire();

        this.PacketBroadcaster.QueuePacketToLevel(
            this, new RemoveEntitiesPacket(entity.EntityId), entity is IPlayer ? [entity.EntityId] : []);
        return true;
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

    public ValueTask<IChunk?> GetChunkAsync(int chunkX, int chunkZ, bool scheduleGeneration = true) =>
        this.GetChunkAsync(chunkX, chunkZ, scheduleGeneration, ticketOnly: false);

    private async ValueTask<IChunk?> GetChunkAsync(
        int chunkX, int chunkZ, bool scheduleGeneration, bool ticketOnly)
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
                this.QueueGeneration(packedXZ, ticketOnly);
                return null;
            }

            LoadedChunks.Add(packedXZ);
            this.Fluids.Track(chunk);
            return chunk;
        }

        if (scheduleGeneration)
        {
            this.QueueGeneration(packedXZ, ticketOnly);
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

    public IEnumerable<IPlayer> PlayersInRange(Vector location) => this.PlayersWithChunk(location);

    // The players that have the chunk at the location loaded. It reads the players as it goes rather than copying them
    // first: a copy of a ConcurrentDictionary's values takes all its locks, and this runs for every block change sent.
    private IEnumerable<IPlayer> PlayersWithChunk(Vector location)
    {
        var (x, z) = location.ToChunkCoord();
        var packedXZ = NumericsHelper.IntsToLong(x, z);

        foreach (var (_, player) in this.Players)
        {
            if (player.LoadedChunks.Contains(packedXZ))
                yield return player;
        }
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
            if (!this.Fluids.IsResident(c) || c is Chunk { EntitiesUnloaded: true })
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

        // Owner lookups use float.MaxValue: those look through every region rather than the chunks in range.
        if (distance >= Region.CubicRegionSize * 16)
        {
            foreach (var (_, region) in Regions)
            {
                foreach (var (_, entity) in region.Entities)
                {
                    if (entity.Type != EntityType.Player && (entity.Position - location).MagnitudeSquared() <= distanceSquared)
                        yield return entity;
                }
            }

            yield break;
        }

        // The entities in the chunks in range, by entity id (see EntityGrid).
        foreach (var entity in this.EntityGrid.InChunksAround(location - new VectorD(distance), location + new VectorD(distance)))
        {
            if (entity.Type != EntityType.Player && (entity.Position - location).MagnitudeSquared() <= distanceSquared)
                yield return entity;
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
            foreach (var (_, player) in Players)
            {
                if (player.Position == location)
                {
                    yield return player;
                }
            }
            yield break;
        }

        distance *= distance;

        foreach (var (_, player) in Players)
        {
            var difference = player.Position - location;
            if (difference.X * difference.X + difference.Z * difference.Z <= distance)
            {
                yield return player;
            }
        }
    }

    public IEnumerable<IPlayer> GetPlayersInChunkRange(Vector worldPosition) => this.PlayersWithChunk(worldPosition);

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

    /// <summary>
    /// Runs the entity actions queued so far, populating complete chunks among them, then spawns the entities complete
    /// chunks keep pending: the tick's entity work, which pregeneration does too before unloading the chunks it's done
    /// with. Called under the simulation gate.
    /// </summary>
    /// <param name="timings">The tick's timings, which time the actions apart from the spawns; none outside a tick.</param>
    private async Task RunEntityActionsAsync(TickTimings? timings = null)
    {
        var actionCount = entityActions.Count;
        for (var index = 0; index < actionCount && entityActions.TryDequeue(out var action); index++)
        {
            TickStage = $"entity action {index + 1}/{actionCount}";
            await action();
        }
        timings?.Lap("entity actions");

        // Deferred unloads keep old-generation state first; a rebound type can respawn it in this same tick.
        this.SpawnPendingEntities();
    }

    private async Task TickLevelAsync()
    {
        TickStage = "starting";
        if (LevelData is null)
            return;
        var timings = new TickTimings();

        LevelData.Time += this.Configuration.TimeTickSpeedMultiplier;
        LevelData.RainTime -= this.Configuration.TimeTickSpeedMultiplier;

        // Like vanilla's ServerLevel, clients get the time every second, so their clocks don't drift from the server's.
        if (++this.timeSyncTicks % 20 == 0)
            this.BroadcastTime();

        // Like vanilla, block ticks and then fluid ticks run before entities tick.
        TickStage = "block and fluid ticks";
        this.Fluids.Tick();
        timings.Lap("fluids");

        await this.Portals.TickAsync();
        timings.Lap("portals");

        foreach (var region in Regions.Values.OfType<Region>())
            region.TickInhabitedTime(this);

        await this.RunEntityActionsAsync(timings);

        timings.Lap("pending spawns");
        await TickEndFightAsync();
        timings.Lap("end fight");

        TickStage = "natural spawning";
        (mobSpawner ??= new MobSpawner(this)).Tick();
        timings.Lap("natural spawning");

        // Like vanilla's chunk ticks, with natural spawning and before entities.
        TickStage = "random ticks";
        this.Lighting.Flush(LightBorderBudget);
        timings.Lap("lighting");
        this.TickRandomBlocks();
        timings.Lap("random ticks");

        foreach (var entity in this.SnapshotEntities())
        {
            if (entity is Mob { HasAi: true } or ItemEntity or ExperienceOrb && !IsMobTicking(entity.Position))
                continue;
            if (GetRegionForLocation(entity.Position)?.Entities.ContainsKey(entity.EntityId) == true)
            {
                this.tickingEntity = entity;
                await entity.TickAsync();
                await this.TouchPluginBlocksAsync(entity);
                if (entity is Entity concrete && entity is not IPlayer && entity.Level == this)
                    await concrete.TickEntityPortalAsync();
                timings.LapEntity(entity.GetType().Name);
            }
        }
        timings.Lap("entities");

        TickStage = "sleeping";
        await TickSleepingAsync();

        await TickRaidsAsync();
        await TickMobGameEventsAsync();
        await TickMobEggsAsync();
        timings.Lap("raids, game events, eggs");

        TickStage = "block entities";
        await TickBlockEntitiesAsync();
        timings.Lap("block entities");

        TickStage = "moving entities between regions";
        foreach (var region in Regions.Values.OfType<Region>())
            region.MoveEntitiesToTheirRegions();
        timings.Lap("region moves");

        TickStage = "runtime light";
        foreach (var packet in this.Lighting.TakeUpdates(LightBorderBudget))
        {
            var location = new Vector(packet.Chunk.X << 4, 0, packet.Chunk.Z << 4);
            foreach (var watcher in this.PlayersInRange(location))
                watcher.SendPacket(packet);
        }

        foreach (var player in Players.Values)
        {
            TickStage = $"tracking for {player.Username}";
            await player.SynchronizeTrackedEntitiesAsync();
            player.SynchronizeOwnState();
        }
        timings.Lap("light and tracking");
        TickStage = "idle";
        timings.ReportIfSlow(this.Logger, DimensionName);
    }

    /// <summary>
    /// Where a level tick's time went, logged when the tick runs past its 50 ms budget so lag can be traced to a stage
    /// or an entity type.
    /// </summary>
    private sealed class TickTimings
    {
        private const double BudgetMs = 50;
        private readonly long start = Stopwatch.GetTimestamp();
        private long lap = Stopwatch.GetTimestamp();
        private long entityLap;
        private readonly List<(string Stage, double Ms)> stages = [];
        private Dictionary<string, (double Ms, int Count)>? entityTypes;

        public void Lap(string stage)
        {
            var now = Stopwatch.GetTimestamp();
            stages.Add((stage, Stopwatch.GetElapsedTime(lap, now).TotalMilliseconds));
            lap = entityLap = now;
        }

        public void LapEntity(string type)
        {
            var now = Stopwatch.GetTimestamp();
            if (entityLap == 0) entityLap = lap;
            entityTypes ??= [];
            var (ms, count) = entityTypes.GetValueOrDefault(type);
            entityTypes[type] = (ms + Stopwatch.GetElapsedTime(entityLap, now).TotalMilliseconds, count + 1);
            entityLap = now;
        }

        public void ReportIfSlow(ILogger logger, string level)
        {
            var total = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (total <= BudgetMs || !logger.IsEnabled(LogLevel.Warning)) return;
            var slowest = string.Join(", ", stages.Where(item => item.Ms >= 1).OrderByDescending(item => item.Ms)
                .Select(item => $"{item.Stage} {item.Ms:0.#}ms"));
            var types = entityTypes == null ? "" : string.Join(", ", entityTypes.OrderByDescending(item => item.Value.Ms).Take(5)
                .Select(item => $"{item.Key} x{item.Value.Count} {item.Value.Ms:0.#}ms"));
            logger.LogWarning("Slow tick in {Level}: {Total:0.#}ms ({Stages}). Top entity types: {Types}",
                level, total, slowest, types.Length == 0 ? "none" : types);
        }
    }

    // How long a tick spends reconciling light along newly loaded chunks' borders; the rest waits for later ticks.
    private static readonly TimeSpan LightBorderBudget = TimeSpan.FromMilliseconds(10);

    // The stage the level's tick is at, for the stuck tick warning. The entity being ticked is kept rather than
    // described up front, since this changes for every entity on every tick and is rarely read.
    internal string TickStage
    {
        get => this.tickingEntity is IEntity entity ? $"entity {entity.EntityId} ({entity.GetType().Name})" : this.tickStage;
        private set
        {
            this.tickStage = value;
            this.tickingEntity = null;
        }
    }

    private string tickStage = "not started";
    private IEntity? tickingEntity;

    // Reused by every tick's entity snapshot; ticks of a level never overlap (see simulationGate).
    private readonly List<IEntity> entitySnapshot = [];
    private readonly HashSet<int> entitySnapshotIds = [];

    /// <summary>
    /// The level's entities in entity id order, each once, even while crossing a region boundary. Taken once per tick,
    /// so entities spawned during the tick wait for the next one. Valid until the next call.
    /// </summary>
    private List<IEntity> SnapshotEntities()
    {
        this.entitySnapshot.Clear();
        this.entitySnapshotIds.Clear();
        foreach (var (_, region) in this.Regions)
        {
            foreach (var (_, entity) in region.Entities)
            {
                if (this.entitySnapshotIds.Add(entity.EntityId))
                    this.entitySnapshot.Add(entity);
            }
        }

        this.entitySnapshot.Sort(static (left, right) => left.EntityId.CompareTo(right.EntityId));
        return this.entitySnapshot;
    }

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
                LightLoaded = this.TrackPublishedChunk,
                LightUnloaded = this.Lighting.Untrack,
                BlockAccessUnloaded = this.Fluids.Untrack,
                BlockEntityUnloaded = this.OnBlockEntityUnloaded,
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
            // The region's final save has its block entities' changes made since the last tick.
            this.TakeBlockEntityChanges();

            IRegion? r;
            lock (this.Fluids.TickLock)
            {
                if (!Regions.TryGetValue(value, out r))
                    return;

                if (this.HasRegionTickets(regionX, regionZ))
                    return;

                // Retire publication and detach lighting before another reader can create a replacement region.
                if (r is Region concrete)
                    concrete.RetireLighting();

                Regions.TryRemove(KeyValuePair.Create(value, r));
            }

            if (r is not null)
            {
                await r.FlushAsync();
                await r.DisposeAsync();
            }
        }
        finally
        {
            simulationGate.Release();
        }
    }

    /// <summary>
    /// Whether the server paused chunk management (see <see cref="WorldManager.PauseChunkManagementAsync"/>): the
    /// generation jobs running finish, but don't start the queued ones.
    /// </summary>
    internal bool ChunkManagementPaused
    {
        get => Volatile.Read(ref this.chunkManagementPaused);
        set => Volatile.Write(ref this.chunkManagementPaused, value);
    }

    private bool chunkManagementPaused;

    public async Task ManageChunksAsync()
    {
        if (LevelData.Time > 0 && LevelData.Time % (20 * 30) == 0)
        {
            var chunksToKeep = new List<long>();
            Players.Values.ForEach(p =>
            {
                chunksToKeep.AddRange(p.LoadedChunks);
            });

            foreach (var chunk in LoadedChunks.Except(chunksToKeep).Except(this.spawnChunks))
            {
                await simulationGate.WaitAsync();
                try
                {
                    // Recheck after entering the gate, against tickets acquired since the candidate snapshot.
                    if (!this.TryReleaseUnneededChunk(chunk))
                        continue;

                    // The chunk's final save has its block entities' changes made since the last tick.
                    this.TakeBlockEntityChanges();

                    NumericsHelper.LongToInts(chunk, out var cx, out var cz);
                    var r = GetRegionForChunk(cx, cz);
                    if (r is not null)
                        await r.UnloadChunk(
                            NumericsHelper.Modulo(cx, Region.CubicRegionSize),
                            NumericsHelper.Modulo(cz, Region.CubicRegionSize));
                }
                finally
                {
                    simulationGate.Release();
                }
            }
        }

        if (Interlocked.Exchange(ref this.generationFailure, null) is Exception failure)
            ExceptionDispatchInfo.Throw(failure);

        await this.LoadTicketChunksAsync();
        this.StartQueuedJobs();
    }

    /// <summary>
    /// Starts queued chunks on free slots without waiting for them, so a slow chunk never holds up the others or the tick.
    /// Each job starts the next when it finishes, so free slots don't wait for the next tick.
    /// </summary>
    private void StartQueuedJobs()
    {
        // Disposal takes every slot as jobs finish, which new jobs would keep it from. While chunk management is
        // paused, a job that finishes doesn't start the next.
        while (Volatile.Read(ref this.disposed) == 0
            && !this.ChunkManagementPaused
            && this.generationSlots.Wait(0))
        {
            if (!this.TryTakeNearestJob(out var job))
            {
                this.generationSlots.Release();
                return;
            }

            // Started apart, so a job that finishes at once doesn't start the next one inside it, or run on the tick.
            _ = this.RunGenerationJob(() => this.GenerateQueuedChunkAsync(job, startNext: true));
        }
    }

    /// <summary>
    /// Runs a generation job: the Mojang generator's on the generation threads (see <see cref="Generators.GenerationThreads"/>),
    /// so generating never takes the thread pool from the network and the ticks. Plugins' level generators run on the
    /// thread pool, as they always have: their code may block waiting for asynchronous work, whose continuations would
    /// wait for the generation thread it blocks.
    /// </summary>
    private Task RunGenerationJob(Func<Task> job) =>
        this.Generator is MojangGenerator ? Generators.GenerationThreads.Run(job) : Task.Run(job);

    /// <summary>
    /// Takes the queued chunk nearest to a player, or the oldest one when no player is in the level.
    /// </summary>
    private bool TryTakeNearestJob(out long job)
    {
        lock (this.chunkTicketLock)
        lock (this.waitingJobs)
        {
            this.DiscardUnneededGeneration(moveToWaiting: true);

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
            this.queuedChunks[job] = GenerationDemand.Running;
            return true;
        }
    }

    private void QueueGeneration(long packedXZ, bool ticketOnly = false)
    {
        lock (this.chunkTicketLock)
        lock (this.waitingJobs)
        {
            // An asynchronous ticket load may return after its last handle was released.
            if (ticketOnly && !this.IsTicketLoaded(packedXZ))
                return;

            this.QueueGenerationLocked(packedXZ, ticketOnly ? GenerationDemand.Ticket : GenerationDemand.Retained);
        }
    }

    // Called under chunkTicketLock and waitingJobs. A request kept regardless of tickets and views replaces one that
    // isn't.
    private void QueueGenerationLocked(long packedXZ, GenerationDemand demand)
    {
        if (this.queuedChunks.TryAdd(packedXZ, demand))
            this.ChunksToGen.Enqueue(packedXZ);
        else if (demand == GenerationDemand.Retained
            && this.queuedChunks.TryGetValue(packedXZ, out var existing)
            && existing is GenerationDemand.Ticket or GenerationDemand.Player)
            this.queuedChunks[packedXZ] = GenerationDemand.Retained;
    }

    /// <summary>
    /// Queues the chunks a player's view needs that aren't complete in memory, to be loaded or generated nearest to a
    /// player first, until no player's view has them (see <see cref="Entities.Player.WantsChunk"/>).
    /// </summary>
    internal void RequestChunks(IEnumerable<long> chunks)
    {
        lock (this.chunkTicketLock)
        lock (this.waitingJobs)
        {
            foreach (var chunk in chunks)
            {
                NumericsHelper.LongToInts(chunk, out var x, out var z);
                if (this.GetLoadedChunk(x, z) is null)
                    this.QueueGenerationLocked(chunk, GenerationDemand.Player);
            }
        }
    }

    /// <summary>
    /// The chunk at (<paramref name="chunkX"/>, <paramref name="chunkZ"/>) if it's in memory and complete, without
    /// loading or generating it: what a player's view sends (see <see cref="RequestChunks"/>).
    /// </summary>
    internal IChunk? GetReadyChunk(int chunkX, int chunkZ)
    {
        if (this.GetLoadedChunk(chunkX, chunkZ) is not IChunk chunk)
            return null;

        // Like GetChunkAsync finding a complete chunk.
        this.LoadedChunks.Add(NumericsHelper.IntsToLong(chunkX, chunkZ));
        this.Fluids.Track(chunk);
        return chunk;
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

            // Like GetChunkAsync finding a complete chunk: one loaded from disk ticks from now on, whether or not a
            // player was sent it yet.
            this.Fluids.Track(c);

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

    /// <remarks>
    /// Takes in the changes block entities announced since the last tick first (see
    /// <see cref="TakeBlockEntityChanges"/>), like the tick's end does, so that one changed between ticks (by plugin
    /// code off the tick, a command or a test) is saved with its own data as it is now, like its items.
    /// </remarks>
    public async virtual Task FlushRegionsAsync()
    {
        Region[] written = [];
        try
        {
            await simulationGate.WaitAsync();
            try
            {
                this.TakeBlockEntityChanges();

                Interlocked.Exchange(ref this.jobsSinceFlush, 0);
                SavingEntities = true;
                var regions = Regions.Values.ToArray();
                written = [.. regions.OfType<Region>()];
                await Task.WhenAll(regions.Select(region => region is Region concrete
                    ? concrete.WriteChangesAsync()
                    : region.FlushAsync()));
                this.SavePluginData();
            }
            finally
            {
                SavingEntities = false;
                simulationGate.Release();
            }
        }
        catch
        {
            // What was written still reaches the disk when another region or the plugin data fails; that failure is
            // the one reported.
            await SyncToDiskAsync(written).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            throw;
        }

        // Like vanilla's autosave, which leaves the disk to its IO workers, the level ticks again while the files
        // reach the disk; the flush still ends once they have.
        await SyncToDiskAsync(written);
    }

    private static Task SyncToDiskAsync(Region[] regions) => Task.WhenAll(regions.Select(region => region.SyncToDiskAsync()));

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
        {
            replaced.IsRemoved = true;
            await replaced.WaitForLiveCallAsync();
        }
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

        // Mojang generation goes a tile at a time (see PregenerationTiles), so the chunks are queued in that order.
        var mojang = this.Generator as MojangGenerator;
        var tiles = mojang is null
            ? [PregenerationSquare(centerX - pregenerationRange, centerZ - pregenerationRange, 2 * pregenerationRange)]
            : PregenerationTiles(centerX, centerZ, pregenerationRange);
        foreach (var tile in tiles)
        {
            foreach (var (x, z) in tile)
                this.QueueGeneration(NumericsHelper.IntsToLong(x, z));
        }

        var startChunks = this.ChunksToGenCount;
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        Log.Generating(this.Logger, startChunks, this.Name);

        // Generating starts with the staging below, which does most of the work of a noise generator's chunks, so a
        // listener hears of it now rather than once the staging is done.
        this.GenerationProgress?.Invoke(0, startChunks);

        // A window of jobs in queue order: a new job starts as soon as one finishes.
        var jobs = new List<Task>(startChunks);
        var completedChunks = 0;
        var lastPercent = -1;
        var flushedThousands = 0;

        if (mojang is not null)
        {
            // Most of a tile's work is staged across the generation threads first, without the lock waits of completing
            // chunks one by one, so its jobs only complete its chunks. Then the tile is saved, and the chunks no tile left
            // needs are unloaded, so memory stays that of a tile or two however large the area.
            var remaining = tiles.SelectMany(tile => tile).Select(chunk => NumericsHelper.IntsToLong(chunk.X, chunk.Z)).ToHashSet();

            // The spawn chunks stay loaded, as they would once the level loads (see LoadAsync).
            var radius = this.Configuration.SpawnChunkRadius;
            var spawnArea = PregenerationSquare(centerX - radius, centerZ - radius, 2 * radius)
                .Select(chunk => NumericsHelper.IntsToLong(chunk.X, chunk.Z))
                .ToHashSet();
            foreach (var tile in tiles)
            {
                await mojang.PrepareAsync(tile, cancellationToken);
                await CompleteQueuedAsync(tile.Count, flushEvery: false);
                await Task.WhenAll(jobs);

                foreach (var (x, z) in tile)
                    remaining.Remove(NumericsHelper.IntsToLong(x, z));

                await this.FlushRegionsAsync();
                await this.UnloadPregeneratedAsync(remaining, spawnArea);
            }
        }
        else
        {
            await CompleteQueuedAsync(startChunks, flushEvery: true);
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

        // Starts up to count queued jobs, reporting progress as they complete, and with flushEvery, saving every 1,024
        // chunks.
        async Task CompleteQueuedAsync(int count, bool flushEvery)
        {
            for (var started = 0; started < count && this.ChunksToGen.TryDequeue(out var job); started++)
            {
                await this.generationSlots.WaitAsync(cancellationToken);
                jobs.Add(this.RunGenerationJob(() => this.GenerateQueuedChunkAsync(job)));

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
                if (flushEvery && completedChunks / 1024 > flushedThousands && !this.ChunksToGen.IsEmpty)
                {
                    flushedThousands = completedChunks / 1024;
                    await this.FlushRegionsAsync();
                }
            }
        }

        {
            var index = 0;
            var (x, z) = LevelData.SpawnPosition.ToChunkCoord();
            for (var cx = x - this.Configuration.SpawnChunkRadius; cx < x + this.Configuration.SpawnChunkRadius; cx++)
                for (var cz = z - this.Configuration.SpawnChunkRadius; cz < z + this.Configuration.SpawnChunkRadius; cz++)
                    this.spawnChunks[index++] = NumericsHelper.IntsToLong(cx, cz);
        }

        this.generated = true;
    }

    // The width, in chunks, of the tiles pregeneration stages: a region's, so finished regions are saved whole.
    private const int PregenerationTileSize = Region.CubicRegionSize;

    /// <summary>
    /// The tiles pregeneration stages the square of <paramref name="range"/> chunks around (<paramref name="centerX"/>,
    /// <paramref name="centerZ"/>) in: squares of <see cref="PregenerationTileSize"/> chunks, cut to the area, nearest
    /// to the center first, so the spawn's are ready first.
    /// </summary>
    private static List<List<(int X, int Z)>> PregenerationTiles(int centerX, int centerZ, int range)
    {
        var (minX, minZ, maxX, maxZ) = (centerX - range, centerZ - range, centerX + range, centerZ + range);
        var tiles = new List<List<(int X, int Z)>>();
        for (var tileX = minX; tileX < maxX; tileX += PregenerationTileSize)
        {
            for (var tileZ = minZ; tileZ < maxZ; tileZ += PregenerationTileSize)
            {
                var width = Math.Min(PregenerationTileSize, maxX - tileX);
                var depth = Math.Min(PregenerationTileSize, maxZ - tileZ);
                tiles.Add([.. PregenerationSquare(tileX, tileZ, width, depth)]);
            }
        }

        return [.. tiles.OrderBy(tile =>
        {
            var (x, z) = tile[tile.Count / 2];
            return (long)(x - centerX) * (x - centerX) + (long)(z - centerZ) * (z - centerZ);
        })];
    }

    private static List<(int X, int Z)> PregenerationSquare(int minX, int minZ, int width, int? depth = null)
    {
        var square = new List<(int X, int Z)>();
        for (var x = minX; x < minX + width; x++)
        {
            for (var z = minZ; z < minZ + (depth ?? width); z++)
                square.Add((x, z));
        }

        return square;
    }

    /// <summary>
    /// Unloads the chunks pregeneration is done with: those more than 2 chunks from every chunk left to generate,
    /// whose staging decorates and post-processes chunks within 2 of it, besides those in <paramref name="kept"/>.
    /// They're saved first if they changed.
    /// </summary>
    /// <remarks>
    /// The level hasn't ticked yet, so the entity work it does for complete chunks (populating them with mobs, spawning
    /// the entities generation placed) runs first, as the tick would before they could unload.
    /// </remarks>
    private async Task UnloadPregeneratedAsync(HashSet<long> remaining, HashSet<long> kept)
    {
        await simulationGate.WaitAsync();
        try
        {
            await this.RunEntityActionsAsync();
            this.TakeBlockEntityChanges();
            foreach (var chunk in this.LoadedChunks.ToArray())
            {
                NumericsHelper.LongToInts(chunk, out var cx, out var cz);
                if (kept.Contains(chunk) || Needed(cx, cz) || !this.TryReleaseUnneededChunk(chunk))
                    continue;

                if (GetRegionForChunk(cx, cz) is Region region)
                {
                    await region.UnloadChunk(
                        NumericsHelper.Modulo(cx, Region.CubicRegionSize),
                        NumericsHelper.Modulo(cz, Region.CubicRegionSize));
                }
            }
        }
        finally
        {
            simulationGate.Release();
        }

        bool Needed(int cx, int cz)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                for (var dz = -2; dz <= 2; dz++)
                {
                    if (remaining.Contains(NumericsHelper.IntsToLong(cx + dx, cz + dz)))
                        return true;
                }
            }

            return false;
        }
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
    internal bool TryAddEntity(IEntity entity, bool hostOwned)
    {
        if (!PluginEntities.HasLoadedType(entity))
            return false;

        var spawner = PluginEntities.Credit(entity, hostOwned);

        if (!PluginEntities.HasLoadedType(entity))
            return false;

        var (chunkX, chunkZ) = Region.ChunkOf(entity.Position);

        if (GetRegionForChunk(chunkX, chunkZ) is not Region region || !region.TryAddEntity(entity))
            return false;

        this.EntityGrid.Add(entity);

        // Unloading closes a plugin before removing its entities, so one added meanwhile is removed there or here.
        if (spawner is { IsClosing: true } || !PluginEntities.HasLoadedType(entity))
        {
            region.TryRemoveEntity(entity);
            EntityGrid.Leave(entity);
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

    private int timeSyncTicks;

    protected void BroadcastTime() =>
        this.PacketBroadcaster.QueuePacketToLevel(this, new SetTimePacket(LevelData.Time, LevelData.Time % 24000, true));

    public async virtual ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) != 0)
            return;

        GC.SuppressFinalize(this);

        this.ClearChunkTickets();
        this.optionsMonitor.Dispose();
        this.SavePluginData();

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
