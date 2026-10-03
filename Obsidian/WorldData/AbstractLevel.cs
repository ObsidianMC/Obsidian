using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Obsidian.API.Configuration;
using Obsidian.API.Entities;
using Obsidian.API.Inventory;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.Entities;
using Obsidian.Entities.Factories;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData.Fluids;
using Obsidian.WorldData.Generators;
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

    // Generation jobs run in the background, at most one per core: each job fans out over its neighbors' steps itself.
    private readonly SemaphoreSlim generationSlots = new(Environment.ProcessorCount);

    // A failed background generation job, rethrown by the next ManageChunksAsync so failures still reach the server.
    private Exception? generationFailure;

    // The chunks generation jobs may write, counted per job, which aren't unloaded meanwhile: a job writes up to 2 chunks
    // from its own (its neighbors' decorations reach theirs), and writes to an unloaded chunk would be lost.
    private readonly ConcurrentDictionary<long, int> generationPins = [];

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
    public int ChunksToGenCount => this.ChunksToGen.Count;
    public int LoadedChunkCount => this.Regions.Values.Sum(x => x.LoadedChunkCount);

    public IPacketBroadcaster PacketBroadcaster { get; }
    public IEventDispatcher EventDispatcher { get; }
    public ServerConfiguration Configuration { get; private set; }
    public Gamemode DefaultGamemode => LevelData.DefaultGamemode;

    public string DimensionName { get; protected set; } = string.Empty;

    /// <summary>
    /// The dimension's lowest block Y.
    /// </summary>
    public int MinY { get; private set; } = -64;

    /// <summary>
    /// The dimension's build height in blocks.
    /// </summary>
    public int Height { get; private set; } = 384;

    public string LevelDataFilePath { get; protected set; }

    protected ILogger Logger { get; }

    /// <summary>
    /// The level's fluids and their scheduled ticks.
    /// </summary>
    internal LevelFluids Fluids { get; }

    private readonly IDisposable optionsMonitor;
    private readonly Lock regionLock = new();
    private readonly ConcurrentQueue<Func<ValueTask>> entityActions = new();
    private MobSpawner? mobSpawner;
    private readonly SemaphoreSlim simulationGate = new(1, 1);

    internal void EnqueueEntityAction(Func<ValueTask> action) => entityActions.Enqueue(action);

    internal bool IsMobTicking(VectorF position)
    {
        var (x, z) = Region.ChunkOf(position);
        if (GetLoadedChunk(x, z) == null)
            return false;

        // ponytail: player proximity covers normal ticking; use chunk tickets for forced entity ticking.
        return Players.Values.Any(player => player.Gamemode != Gamemode.Spectator &&
            Math.Abs(Region.ChunkOf(player.Position).X - x) <= Configuration.SimulationDistance &&
            Math.Abs(Region.ChunkOf(player.Position).Z - z) <= Configuration.SimulationDistance);
    }

    internal IChunk? GetLoadedChunk(int chunkX, int chunkZ) => GetRegionForChunk(chunkX, chunkZ) is Region region
        ? region.GetLoadedChunk(NumericsHelper.Modulo(chunkX, 32), NumericsHelper.Modulo(chunkZ, 32))
        : null;

    internal bool TryMoveEntity(IEntity entity, VectorF from, VectorF to)
    {
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

        var spawnChunkCount = 2 * this.Configuration.SpawnChunkRadius + 1;
        this.spawnChunks = new long[spawnChunkCount * spawnChunkCount];

        this.Generator.Init(this);
    }

    public ValueTask<bool> DestroyEntityAsync(IEntity entity)
    {
        var destroyed = new RemoveEntitiesPacket(entity.EntityId);

        this.PacketBroadcaster.QueuePacketToLevel(this, destroyed);

        var (chunkX, chunkZ) = Region.ChunkOf(entity.Position);

        var region = GetRegionForChunk(chunkX, chunkZ);

        return region is null ? ValueTask.FromResult(false) : ValueTask.FromResult(region.Entities.TryRemove(entity.EntityId, out _));
    }

    public abstract Task<bool> LoadAsync(DimensionCodec codec);
    public abstract Task SaveAsync();

    public IRegion? GetRegionForLocation(VectorF location)
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
            return BlocksRegistry.Get(Material.VoidAir);

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

    public async ValueTask SetBlockAsync(int x, int y, int z, IBlock block)
    {
        if (this.IsOutsideBuildHeight(y))
            return;

        await SetBlockUntrackedAsync(x, y, z, block);
        this.BroadcastBlockChange(block, new(x, y, z));
    }

    public async ValueTask SetBlockAsync(int x, int y, int z, IBlock block, bool doBlockUpdate)
    {
        if (this.IsOutsideBuildHeight(y))
            return;

        await SetBlockUntrackedAsync(x, y, z, block, doBlockUpdate);
        this.BroadcastBlockChange(block, new(x, y, z));
    }

    internal void BroadcastBlockChange(IBlock block, Vector location)
    {
        var packet = new BlockUpdatePacket(location, block.GetHashCode());
        foreach (Player player in PlayersInRange(location).Cast<Player>())
        {
            player.Client.SendPacket(packet);
        }
    }

    /// <summary>
    /// Sends a level event (a sound or particle effect, like lava fizzing) to the players that have its chunk.
    /// </summary>
    internal void BroadcastLevelEvent(int type, Vector location, int data)
    {
        var packet = new LevelEventPacket(type, location, data);
        foreach (Player player in PlayersInRange(location).Cast<Player>())
            player.Client.SendPacket(packet);
    }

    public IEnumerable<IPlayer> PlayersInRange(Vector location)
    {
        var (x, z) = location.ToChunkCoord();
        var packedXZ = NumericsHelper.IntsToLong(x, z);

        return this.Players.Values.Where(player => player.LoadedChunks.Contains(packedXZ));
    }

    public ValueTask SetBlockUntrackedAsync(Vector location, IBlock block, bool doBlockUpdate = false) => SetBlockUntrackedAsync(location.X, location.Y, location.Z, block, doBlockUpdate);

    public async ValueTask SetBlockUntrackedAsync(int x, int y, int z, IBlock block, bool doBlockUpdate = false)
    {
        if (this.IsOutsideBuildHeight(y))
            return;

        if (doBlockUpdate)
        {
            await ScheduleBlockUpdateAsync(new BlockUpdate(this, new Vector(x, y, z), block));
            await BlockUpdateNeighborsAsync(new BlockUpdate(this, new Vector(x, y, z), block));
        }
        var c = await GetChunkAsync(x.ToChunkCoord(), z.ToChunkCoord(), false);
        if (c is null)
            return;

        c.SetBlock(x, y, z, block);

        // Generated block entity data (e.g. a dungeon chest's loot) doesn't carry over to another block.
        if (c.GetBlockEntity(x, y, z) is DataBlockEntity data && data.Id != block.BlockEntityType())
            c.RemoveBlockEntity(x, y, z);

        if (doBlockUpdate)
            this.Fluids.OnBlockChanged(new Vector(x, y, z), block);
    }

    public IEnumerable<IEntity> GetEntitiesInRange(VectorF location, float distance = 10f)
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

    public IEnumerable<IEntity> GetNonPlayerEntitiesInRange(VectorF location, float distance)
    {
        if (float.IsNaN(distance) || distance < 0f)
        {
            yield break;
        }

        var (left, top) = Region.ChunkOf(location - new VectorF(distance));
        var (right, bottom) = Region.ChunkOf(location + new VectorF(distance));

        left >>= Region.CubicRegionSizeShift;
        right >>= Region.CubicRegionSizeShift;
        top >>= Region.CubicRegionSizeShift;
        bottom >>= Region.CubicRegionSizeShift;

        distance *= distance;

        for (int x = left; x <= right; x++)
        {
            for (int z = top; z <= bottom; z++)
            {
                if (GetRegionForChunk(x << Region.CubicRegionSizeShift, z << Region.CubicRegionSizeShift) is not Region region)
                    continue;

                foreach (var entity in region.Entities.Values)
                {
                    if (entity.Type == EntityType.Player)
                        continue;

                    var locationDifference = LocationDiff.GetDifference(entity.Position, location);

                    if (locationDifference.CalculatedDifference <= distance)
                    {
                        yield return entity;
                    }
                }
            }
        }
    }

    public IEnumerable<IPlayer> GetPlayersInRange(VectorF location, float distance)
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
            var locationDifference = LocationDiff.GetDifference(player.Position, location);

            if (locationDifference.CalculatedDifference <= distance)
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

    public bool TryRemovePlayer(IPlayer player) => Players.TryRemove(player.Uuid, out _);

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

        this.SpawnPendingEntities();

        // Like vanilla, fluid ticks run before entities tick.
        this.Fluids.Tick();

        foreach (var region in Regions.Values.OfType<Region>())
            region.TickInhabitedTime(this);

        var actionCount = entityActions.Count;
        for (var index = 0; index < actionCount && entityActions.TryDequeue(out var action); index++)
        {
            TickStage = $"entity action {index + 1}/{actionCount}";
            await action();
        }

        TickStage = "natural spawning";
        (mobSpawner ??= new MobSpawner(this)).Tick();

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
            }
        }

        foreach (var region in Regions.Values)
        {
            if (region is Region concrete)
            {
                TickStage = "block updates";
                await concrete.TickBlocksAsync();
            }
        }
        foreach (var player in Players.Values.OfType<Player>())
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
        var packet = new LightUpdatePacket(chunk);
        foreach (Player player in this.GetPlayersInChunkRange(new Vector(chunk.X << 4, 0, chunk.Z << 4)).Cast<Player>())
            player.Client.SendPacket(packet);
    }

    /// <summary>
    /// Queues a complete chunk's pending entities (<see cref="Chunk.PendingEntities"/>) to spawn on the next tick, like
    /// vanilla when a proto chunk becomes a level chunk or an entity chunk is loaded.
    /// </summary>
    internal void QueueEntitySpawn(Chunk chunk) => this.entitySpawns.Enqueue(chunk);

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

                foreach (var pending in chunk.PendingEntities)
                {
                    var tag = EntityNbt.ToNbt(pending);
                    if (EntityNbt.Load(tag, this) is not Entity entity)
                        chunk.UnspawnableEntities.Add(tag);
                    else if (entity is not Mob { HasAi: true, Alive: false } &&
                        !this.Regions.Values.Any(region => region.Entities.Values.Any(existing => existing.Uuid == entity.Uuid)))
                        this.SpawnEntity(entity);
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

            region = new Region(regionX, regionZ, FolderPath, minY: this.MinY, height: this.Height)
            {
                LockChunk = this.Generator.LockChunkAsync,
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
            if (Regions.TryRemove(value, out var r))
                await r.FlushAsync();
        }
        finally
        {
            simulationGate.Release();
        }
    }

    public async ValueTask ScheduleBlockUpdateAsync(IBlockUpdate blockUpdate)
    {
        blockUpdate.Block ??= await GetBlockAsync(blockUpdate.Position);
        (int chunkX, int chunkZ) = blockUpdate.Position.ToChunkCoord();
        var region = GetRegionForChunk(chunkX, chunkZ);
        region?.AddBlockUpdate(blockUpdate);
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

        // Starts queued chunks on free slots without waiting for them, so a slow chunk never holds up the others or the tick.
        while (!this.ChunksToGen.IsEmpty && this.generationSlots.Wait(0))
        {
            if (this.ChunksToGen.TryDequeue(out var job))
                _ = this.GenerateQueuedChunkAsync(job);
            else
                this.generationSlots.Release();
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
    private async Task GenerateQueuedChunkAsync(long job)
    {
        NumericsHelper.LongToInts(job, out var jobX, out var jobZ);
        this.PinGenerationArea(jobX, jobZ, 1);
        try
        {
            var region = this.GetRegionForChunk(jobX, jobZ) ?? this.LoadRegionByChunk(jobX, jobZ);

            var (x, z) = (NumericsHelper.Modulo(jobX, Region.CubicRegionSize), NumericsHelper.Modulo(jobZ, Region.CubicRegionSize));

            var populate = false;
            var c = await region.GetOrAddChunkAsync(x, z, () =>
            {
                populate = true;
                return new Chunk(jobX, jobZ, this.MinY, this.Height, ChunkGenStage.structure_starts);
            });
            if (!c.IsGenerated)
            {
                c = await this.Generator.GenerateChunkAsync(jobX, jobZ, c);
            }
            region.SetChunk(c);
            if (populate && c.IsGenerated)
                EnqueueEntityAction(() =>
                {
                    (mobSpawner ??= new MobSpawner(this)).PopulateChunk(c);
                    return default;
                });
        }
        catch (Exception ex)
        {
            Interlocked.CompareExchange(ref this.generationFailure, ex, null);
        }
        finally
        {
            this.PinGenerationArea(jobX, jobZ, -1);
            this.queuedChunks.TryRemove(job, out _);
            this.generationSlots.Release();
        }
    }

    public async virtual Task FlushRegionsAsync()
    {
        await simulationGate.WaitAsync();
        try
        {
            SavingEntities = true;
            await Task.WhenAll(Regions.Select(pair => pair.Value.FlushAsync()));
        }
        finally
        {
            SavingEntities = false;
            simulationGate.Release();
        }
    }

    public IEntity SpawnFallingBlock(VectorF position, Material mat)
    {
        position.X += 0.5f;
        position.Z += 0.5f;

        FallingBlock entity = new(position)
        {
            Type = EntityType.FallingBlock,
            EntityId = Server.GetNextEntityId(),
            Level = this,
            Block = BlocksRegistry.Get(mat),
        };

        entity.SpawnEntity(null, entity.Block.GetHashCode());

        TryAddEntity(entity);

        return entity;
    }

    public ValueTask<IBlockEntity?> GetBlockEntityAsync(Vector blockPosition) => GetBlockEntityAsync(blockPosition.X, blockPosition.Y, blockPosition.Z);

    public async ValueTask<IBlockEntity?> GetBlockEntityAsync(int x, int y, int z)
    {
        var c = await GetChunkAsync(x.ToChunkCoord(), z.ToChunkCoord(), false);
        return c?.GetBlockEntity(x, y, z);
    }

    public ValueTask SetBlockEntity(Vector blockPosition, IBlockEntity tileEntityData) => SetBlockEntity(blockPosition.X, blockPosition.Y, blockPosition.Z, tileEntityData);
    public async ValueTask SetBlockEntity(int x, int y, int z, IBlockEntity tileEntityData)
    {
        var c = await GetChunkAsync(x.ToChunkCoord(), z.ToChunkCoord(), false);
        c?.SetBlockEntity(x, y, z, tileEntityData);
    }

    public IEntity SpawnEntity(VectorF position, EntityType type)
    {
        if (type == EntityType.FallingBlock)
            return SpawnFallingBlock(position + (0, 20, 0), Material.Sand);

        return GetNewEntitySpawner()
            .WithEntityType(type)
            .AtPosition(position)
            .Spawn();
    }

    public IEntity SpawnEntity(IEntity entity)
    {
        if (entity is Mob mob)
            mob.InitializeAi();
        if (!TryAddEntity(entity))
            throw new InvalidOperationException($"Could not register entity {entity.EntityId}.");
        entity.SpawnEntity();
        return entity;
    }

    public void SpawnExperienceOrbs(VectorF position, short count = 1)
    {
        while (count > 0)
        {
            var value = ExperienceOrb.SplitValue(count);
            SpawnEntity(new ExperienceOrb { Level = this, EntityId = Server.GetNextEntityId(), Position = position, Value = value });
            count -= (short)value;
        }
    }

    public async ValueTask<bool> HandleBlockUpdateAsync(IBlockUpdate update)
    {
        if (update.Block is not IBlock block)
            return false;

        // Fluids react to block changes through their scheduled ticks instead (see LevelFluids).
        if (block.IsGravityAffected())
            return await BlockUpdates.HandleFallingBlock(update);

        return false;
    }

    public async ValueTask BlockUpdateNeighborsAsync(IBlockUpdate update)
    {
        update.Block = null;

        Vector[] directions = Vector.AllDirections;
        for (int i = 0; i < directions.Length; i++)
        {
            update.Position = update.Position + directions[i];
            await ScheduleBlockUpdateAsync(update);
        }
    }

    public abstract void Initialize(DimensionCodec codec);

    /// <summary>
    /// Takes the dimension's name and build range from its codec.
    /// </summary>
    protected void SetDimension(DimensionCodec codec)
    {
        this.DimensionName = codec.Name;
        this.MinY = codec.Element.MinY;
        this.Height = codec.Element.Height;
        this.Fluids.Rules = FluidRules.ForDimension(codec.Name, codec.Element.Ultrawarm);
    }

    /// <summary>
    /// Starts the initial generation of the world, which includes pregenerating chunks in a square around the spawn and loading their regions,
    /// as well as setting the world spawn if specified. This should be called after Initialize and before allowing players to join.
    /// </summary>
    /// <param name="setWorldSpawn">Whether to set the world spawn after generation.</param>
    public async Task GenerateAsync()
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

        // A window of jobs in queue order: a new job starts as soon as one finishes.
        var jobs = new List<Task>(startChunks);
        var completedChunks = 0;
        var lastPercent = -1;
        var flushedThousands = 0;
        while (this.ChunksToGen.TryDequeue(out var job))
        {
            await this.generationSlots.WaitAsync();
            jobs.Add(this.GenerateQueuedChunkAsync(job));

            while (completedChunks < jobs.Count && jobs[completedChunks].IsCompleted)
                completedChunks++;

            var pctComplete = completedChunks * 100 / startChunks;
            if (pctComplete != lastPercent)
            {
                lastPercent = pctComplete;
                var cps = completedChunks / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);
                var remain = (startChunks - completedChunks) / (int)Math.Max(cps, 1);
                System.Console.Write("\r{0} chunks/second - {1}% complete - {2} seconds remaining   ", cps.ToString("###.00"), pctComplete, remain);
            }

            if (completedChunks / 1024 > flushedThousands)
            {
                flushedThousands = completedChunks / 1024;
                await this.FlushRegionsAsync();
            }
        }

        await Task.WhenAll(jobs);
        if (Interlocked.Exchange(ref this.generationFailure, null) is Exception failure)
            ExceptionDispatchInfo.Throw(failure);

        System.Console.Write("\r{0} chunks/second - 100% complete - 0 seconds remaining   ", (startChunks / stopwatch.Elapsed.TotalSeconds).ToString("###.00"));
        System.Console.WriteLine();

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
        var region = GetRegionForLocation(VectorF.Zero)!;
        foreach (var chunk in region.GeneratedChunks())
        {
            for (int bx = 0; bx < 16; bx++)
            {
                for (int bz = 0; bz < 16; bz++)
                {
                    var by = chunk.Heightmaps[HeightmapType.MotionBlocking].GetHeight(bx, bz);
                    IBlock block = chunk.GetBlock(bx, by, bz);

                    if (by < 64 || !block.Is(Material.GrassBlock) && !block.Is(Material.Sand))
                    {
                        continue;
                    }

                    if (!chunk.GetBlock(bx, by + 1, bz).IsAir || !chunk.GetBlock(bx, by + 2, bz).IsAir)
                    {
                        continue;
                    }

                    var worldPos = new VectorF(bx + 0.5f + (chunk.X * 16), by + 1, bz + 0.5f + (chunk.Z * 16));
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
        Log.SpawnNotFound(this.Logger, this.Name);
    }

    public bool TryAddEntity(IEntity entity)
    {
        var (chunkX, chunkZ) = Region.ChunkOf(entity.Position);

        var region = GetRegionForChunk(chunkX, chunkZ);

        return region is not null && region.Entities.TryAdd(entity.EntityId, entity);
    }

    protected void BroadcastTime() => this.PacketBroadcaster.QueuePacketToLevel(this, new SetTimePacket(LevelData.Time, LevelData.Time % 24000, true));

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        this.optionsMonitor.Dispose();

        // Waits out the generation jobs, holding every slot so no new one starts, and saves what they generated since the
        // last save.
        var generating = this.generationSlots.CurrentCount < Environment.ProcessorCount;
        for (var i = 0; i < Environment.ProcessorCount; i++)
            await this.generationSlots.WaitAsync();

        if (Interlocked.Exchange(ref this.generationFailure, null) is Exception failure)
            Log.ChunkGenerationFailed(this.Logger, failure);

        if (generating)
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
    public ValueTask SetBlockAsync(Vector location, IBlock block, bool doBlockUpdate) => this.SetBlockAsync(location.X, location.Y, location.Z, block, doBlockUpdate);

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Generating {ChunkCount} chunks for {LevelName}")]
        public static partial void Generating(ILogger logger, int chunkCount, string levelName);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Spawn of {LevelName} set to {Position}")]
        public static partial void SpawnSet(ILogger logger, string levelName, VectorF position);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to find a spawn position for {LevelName}")]
        public static partial void SpawnNotFound(ILogger logger, string levelName);

        [LoggerMessage(Level = LogLevel.Error, Message = "Generating a chunk failed")]
        public static partial void ChunkGenerationFailed(ILogger logger, Exception exception);
    }
}
