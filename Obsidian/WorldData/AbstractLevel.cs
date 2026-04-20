using Microsoft.Extensions.Logging;
using Obsidian.API.Configuration;
using Obsidian.API.Entities;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.Entities;
using Obsidian.Entities.Factories;
using Obsidian.Net.Packets.Play.Clientbound;
using System.Diagnostics;

namespace Obsidian.WorldData;

public abstract class AbstractLevel : ILevel
{
    private bool generated;

    private const int SpawnChunkRadius = 12;

    public LevelData LevelData { get; internal set; } = default!;

    public ConcurrentDictionary<Guid, IPlayer> Players { get; protected set; } = [];

    public ILevelGenerator Generator { get; internal set; } 

    public ConcurrentDictionary<long, IRegion> Regions { get; protected set; } = [];

    public ConcurrentQueue<long> ChunksToGen { get; protected set; } = [];

    public long[] SpawnChunks { get; } = new long[SpawnChunkRadius * 48];

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
    public ServerConfiguration Configuration { get; }
    public Gamemode DefaultGamemode => LevelData.DefaultGamemode;

    public string DimensionName { get; protected set; } = string.Empty;

    public string LevelDataFilePath { get; protected set; } 

    protected ILogger Logger { get; }

    public AbstractLevel(ILogger logger, IPacketBroadcaster packetBroadcaster, ServerConfiguration configuration,
        IEventDispatcher eventDispatcher, ILevelGenerator worldGenerator, string name, string seed)
    {
        this.Logger = logger;
        this.PacketBroadcaster = packetBroadcaster;
        this.Configuration = configuration;
        this.EventDispatcher = eventDispatcher;
        this.Generator = worldGenerator;
        this.Name = name;
        this.Seed = seed;

        this.Generator.Init(this);
    }

    public ValueTask<bool> DestroyEntityAsync(IEntity entity)
    {
        var destroyed = new RemoveEntitiesPacket(entity.EntityId);

        this.PacketBroadcaster.QueuePacketToLevel(this, destroyed);

        var (chunkX, chunkZ) = entity.Position.ToChunkCoord();

        var region = GetRegionForChunk(chunkX, chunkZ);

        return region is null ? ValueTask.FromResult(false) : ValueTask.FromResult(region.Entities.TryRemove(entity.EntityId, out _));
    }

    public abstract Task<bool> LoadAsync(DimensionCodec codec);
    public abstract Task SaveAsync();

    public IRegion? GetRegionForLocation(VectorF location)
    {
        (int chunkX, int chunkZ) = location.ToChunkCoord();
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
                if (!ChunksToGen.Contains(packedXZ))
                    ChunksToGen.Enqueue(packedXZ);
                return null;
            }

            LoadedChunks.Add(packedXZ);
            return chunk;
        }

        if (scheduleGeneration)
        {
            if (!ChunksToGen.Contains(packedXZ))
                ChunksToGen.Enqueue(packedXZ);
            return null;
        }

        chunk = new Chunk(chunkX, chunkZ, ChunkGenStage.structure_starts);
        region.SetChunk(chunk);
        return chunk;
    }

    public async ValueTask<IBlock?> GetBlockAsync(int x, int y, int z)
    {
        var c = await GetChunkAsync(x.ToChunkCoord(), z.ToChunkCoord(), false);
        return c?.GetBlock(x, y, z);
    }

    public async ValueTask<int?> GetWorldSurfaceHeightAsync(int x, int z)
    {
        var c = await GetChunkAsync(x.ToChunkCoord(), z.ToChunkCoord(), false);
        return c?.Heightmaps[HeightmapType.WorldSurface]
            .GetHeight(NumericsHelper.Modulo(x, 16), NumericsHelper.Modulo(z, 16));
    }

    public async ValueTask SetBlockAsync(int x, int y, int z, IBlock block)
    {
        await SetBlockUntrackedAsync(x, y, z, block);
        this.BroadcastBlockChange(block, new(x, y, z));
    }

    public async ValueTask SetBlockAsync(int x, int y, int z, IBlock block, bool doBlockUpdate)
    {
        await SetBlockUntrackedAsync(x, y, z, block, doBlockUpdate);
        this.BroadcastBlockChange(block, new(x, y, z));
    }

    private void BroadcastBlockChange(IBlock block, Vector location)
    {
        var packet = new BlockUpdatePacket(location, block.GetHashCode());
        foreach (Player player in PlayersInRange(location).Cast<Player>())
        {
            player.Client.SendPacket(packet);
        }
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
        if (doBlockUpdate)
        {
            await ScheduleBlockUpdateAsync(new BlockUpdate(this, new Vector(x, y, z), block));
            await BlockUpdateNeighborsAsync(new BlockUpdate(this, new Vector(x, y, z), block));
        }
        var c = await GetChunkAsync(x.ToChunkCoord(), z.ToChunkCoord(), false);
        c?.SetBlock(x, y, z, block);
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

        (int left, int top) = (location - new VectorF(distance)).ToChunkCoord();
        (int right, int bottom) = (location + new VectorF(distance)).ToChunkCoord();

        distance *= distance;

        for (int x = left; x <= right; x += Region.CubicRegionSize)
        {
            for (int z = top; z >= bottom; z -= Region.CubicRegionSize)
            {
                if (GetRegionForChunk(x, z) is not Region region)
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
        if (LevelData is null)
            return;

        LevelData.Time += this.Configuration.TimeTickSpeedMultiplier;
        LevelData.RainTime -= this.Configuration.TimeTickSpeedMultiplier;

        await Task.WhenAll(this.Regions.Values.Select(r => r.BeginTickAsync()));
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

        this.Logger.LogDebug("Trying to add {x}:{z}", regionX, regionZ);

        var newRegion = new Region(regionX, regionZ, FolderPath);
        region = Regions.GetOrAdd(value, newRegion);

        if (ReferenceEquals(region, newRegion))
        {
            this.Logger.LogDebug("Added region {x}:{z}", regionX, regionZ);
            _ = region.InitAsync();
        }

        return region;
    }

    public async Task UnloadRegionAsync(int regionX, int regionZ)
    {
        long value = NumericsHelper.IntsToLong(regionX, regionZ);
        if (Regions.TryRemove(value, out var r))
            await r.FlushAsync();
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

            LoadedChunks.Except(chunksToKeep).Except(SpawnChunks).ForEach(async c =>
            {
                if (LoadedChunks.TryRemove(c))
                {
                    NumericsHelper.LongToInts(c, out var cx, out var cz);
                    var r = GetRegionForChunk(cx, cz);
                    await r.UnloadChunk(cx, cz);
                }
            });
        }

        if (ChunksToGen.IsEmpty)
            return;

        var jobs = new List<long>();
        for (int a = 0; a < Environment.ProcessorCount; a++)
        {
            if (ChunksToGen.TryDequeue(out var job))
                jobs.Add(job);
        }

        await Parallel.ForEachAsync(jobs, async (job, _) =>
        {
            NumericsHelper.LongToInts(job, out var jobX, out var jobZ);
            var region = GetRegionForChunk(jobX, jobZ) ?? LoadRegionByChunk(jobX, jobZ);

            var (x, z) = (NumericsHelper.Modulo(jobX, Region.CubicRegionSize), NumericsHelper.Modulo(jobZ, Region.CubicRegionSize));

            var c = await region.GetChunkAsync(x, z);
            if (c is null)
            {
                c = new Chunk(jobX, jobZ, ChunkGenStage.structure_starts);
                region.SetChunk(c);
            }
            if (!c.IsGenerated)
            {
                c = await Generator.GenerateChunkAsync(jobX, jobZ, c);
            }
            region.SetChunk(c);
        });
    }

    public Task FlushRegionsAsync() => Task.WhenAll(Regions.Select(pair => pair.Value.FlushAsync()));

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
        if (type == EntityType.ExperienceOrb)
            throw new NotImplementedException($"EntityType {type} is not supported.");

        if (type == EntityType.FallingBlock)
            return SpawnFallingBlock(position + (0, 20, 0), Material.Sand);

        return GetNewEntitySpawner()
            .WithEntityType(type)
            .AtPosition(position)
            .Spawn();
    }

    public IEntity SpawnEntity(IEntity entity)
    {
        entity.SpawnEntity();
        TryAddEntity(entity as Entity);
        return entity;
    }

    public void SpawnExperienceOrbs(VectorF position, short count = 1)
    {
    }

    public async ValueTask<bool> HandleBlockUpdateAsync(IBlockUpdate update)
    {
        if (update.Block is not IBlock block)
            return false;

        if (TagsRegistry.Block.GravityAffected.Entries.Contains(block.RegistryId))
            return await BlockUpdates.HandleFallingBlock(update);

        if (block.IsLiquid)
            return await BlockUpdates.HandleLiquidPhysicsAsync(update);

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
    /// Starts the initial generation of the world, which includes pregenerating chunks in a square around the spawn and loading their regions,
    /// as well as setting the world spawn if specified. This should be called after Initialize and before allowing players to join.
    /// </summary>
    /// <param name="setWorldSpawn">Whether to set the world spawn after generation.</param>
    public async Task GenerateAsync()
    {
        if (this.generated)
            return;

        Logger.LogInformation("Generating world... (Config pregeneration size is {pregenRange})", this.Configuration.PregenerateChunkRange);
        int pregenerationRange = this.Configuration.PregenerateChunkRange;

        int regionPregenRange = (pregenerationRange >> Region.CubicRegionSizeShift) + 1;

        Parallel.ForEach(Enumerable.Range(-regionPregenRange, regionPregenRange * 2 + 1), (x, _) =>
        {
            for (int z = -regionPregenRange; z < regionPregenRange; z++)
                LoadRegion(x, z);
        });

        for (int x = -pregenerationRange; x < pregenerationRange; x++)
        {
            for (int z = -pregenerationRange; z < pregenerationRange; z++)
            {
                ChunksToGen.Enqueue(NumericsHelper.IntsToLong(x, z));
            }
        }

        float startChunks = ChunksToGenCount;
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        Logger.LogInformation("{startChunks} chunks to generate...", startChunks);
        while (!ChunksToGen.IsEmpty)
        {
            await ManageChunksAsync();
            var pctComplete = (int)((1.0 - ChunksToGenCount / startChunks) * 100);
            var completedChunks = startChunks - ChunksToGenCount;
            var cps = completedChunks / (stopwatch.ElapsedMilliseconds / 1000.0);
            int remain = ChunksToGenCount / (int)Math.Max(cps, 1);
            Console.Write("\r{0} chunks/second - {1}% complete - {2} seconds remaining   ", cps.ToString("###.00"), pctComplete, remain);
            if (completedChunks % 1024 == 0)
            {
                await FlushRegionsAsync();
            }
        }
        Console.WriteLine();

        await FlushRegionsAsync();
        await SetWorldSpawnAsync();

        {
            var index = 0;
            var (x, z) = LevelData.SpawnPosition.ToChunkCoord();
            for (var cx = x - SpawnChunkRadius; cx < x + SpawnChunkRadius; cx++)
                for (var cz = z - SpawnChunkRadius; cz < z + SpawnChunkRadius; cz++)
                    SpawnChunks[index++] = NumericsHelper.IntsToLong(cx, cz);
        }

        this.generated = true;
    }

    internal async Task SetWorldSpawnAsync()
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
                    Logger.LogInformation("World Spawn set to {worldPos}", worldPos);

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
        Logger.LogWarning("Failed to set World Spawn.");
    }

    public bool TryAddEntity(IEntity entity)
    {
        var (chunkX, chunkZ) = entity.Position.ToChunkCoord();

        var region = GetRegionForChunk(chunkX, chunkZ);

        return region is not null && region.Entities.TryAdd(entity.EntityId, entity);
    }

    protected void BroadcastTime() => this.PacketBroadcaster.QueuePacketToLevel(this, new SetTimePacket(LevelData.Time, LevelData.Time % 24000, true));

    public async ValueTask DisposeAsync()
    {
        foreach (var region in Regions.Values)
        {
            await region.DisposeAsync();
        }
    }

    public IEntitySpawner GetNewEntitySpawner() => new EntitySpawner(this);
    public ValueTask<IChunk?> GetChunkAsync(Vector worldLocation, bool scheduleGeneration = true) =>
        this.GetChunkAsync(worldLocation.X, worldLocation.Z, scheduleGeneration);

    public ValueTask<IBlock?> GetBlockAsync(Vector location) => this.GetBlockAsync(location.X, location.Y, location.Z);
    public ValueTask SetBlockAsync(Vector location, IBlock block) => this.SetBlockAsync(location.X, location.Y, location.Z, block);
    public ValueTask SetBlockAsync(Vector location, IBlock block, bool doBlockUpdate) => this.SetBlockAsync(location.X, location.Y, location.Z, block, doBlockUpdate);
}
