﻿using Microsoft.Extensions.Logging;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.API.Utilities;
using Obsidian.Blocks;
using Obsidian.Concurrency;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Services;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Obsidian.WorldData;

public sealed class World : IWorld
{
    public IWorldManager WorldManager { get; }

    private float rainLevel = 0f;
    private bool initialized = false;

    private ConcurrentDictionary<(int x, int z), (int x, int z)> moduloCache = new();


    internal Dictionary<string, World> dimensions = new();

    public Level LevelData { get; internal set; }

    public ConcurrentDictionary<Guid, Player> Players { get; private set; } = new();

    public IWorldGenerator Generator { get; internal set; }

    public ConcurrentDictionary<long, Region> Regions { get; private set; } = new();

    public ConcurrentQueue<(int X, int Z)> ChunksToGen { get; private set; } = new();

    public ConcurrentHashSet<(int X, int Z)> SpawnChunks { get; private set; } = new();

    public ConcurrentHashSet<(int X, int Z)> LoadedChunks { get; private set; } = new();

    public required string Name { get; init; }
    public required string Seed { get; init; }
    public string FolderPath { get; private set; }
    public string PlayerDataPath { get; private set; }
    public string LevelDataFilePath { get; private set; }

    public bool Loaded { get; private set; }

    public long Time => LevelData.Time;

    public int RegionCount => this.Regions.Count;
    public int ChunksToGenCount => this.ChunksToGen.Count;
    public int LoadedChunkCount => this.Regions.Values.Sum(x => x.LoadedChunkCount);

    public required IPacketBroadcaster PacketBroadcaster { get; init; }
    public required IServerConfiguration Configuration { get; init; }

    public Gamemode DefaultGamemode => LevelData.DefaultGamemode;

    public string DimensionName { get; private set; }

    public string? ParentWorldName { get; private set; }
    private WorldLight worldLight;

    private static Semaphore _regionLock;


    /// <summary>
    /// Used to log actions caused by the client.
    /// </summary>
    protected ILogger Logger { get; }

    internal World(ILogger logger, Type generatorType, IWorldManager worldManager)
    {
        Logger = logger;
        Generator = Activator.CreateInstance(generatorType) as IWorldGenerator ?? throw new ArgumentException("Invalid generator type.", nameof(generatorType));
        worldLight = new(this);
        _regionLock = new(1, 1);

        this.WorldManager = worldManager;
    }

    public int GetTotalLoadedEntities() => Regions.Values.Sum(e => e == null ? 0 : e.Entities.Count);

    public void InitGenerator() => this.Generator.Init(this);

    public ValueTask<bool> DestroyEntityAsync(Entity entity)
    {
        var destroyed = new RemoveEntitiesPacket(entity);

        this.PacketBroadcaster.QueuePacketToWorld(this, destroyed);

        var (chunkX, chunkZ) = entity.Position.ToChunkCoord();

        Region? region = GetRegionForChunk(chunkX, chunkZ);

        if (region is null)
            throw new InvalidOperationException("Region is null this wasn't supposed to happen.");

        return ValueTask.FromResult(region.Entities.TryRemove(entity.EntityId, out _));
    }

    public Region? GetRegionForLocation(VectorF location)
    {
        (int chunkX, int chunkZ) = location.ToChunkCoord();
        long key = NumericsHelper.IntsToLong(chunkX >> Region.CubicRegionSizeShift, chunkZ >> Region.CubicRegionSizeShift);
        Regions.TryGetValue(key, out Region? region);
        return region;
    }

    public Region? GetRegionForChunk(int chunkX, int chunkZ)
    {
        long value = NumericsHelper.IntsToLong(chunkX >> Region.CubicRegionSizeShift, chunkZ >> Region.CubicRegionSizeShift);

        return Regions.TryGetValue(value, out Region? region) ? region : null;
    }

    public Region? GetRegionForChunk(Vector location) => GetRegionForChunk(location.X, location.Z);

    /// <summary>
    /// Gets a Chunk from a Region.
    /// If the Chunk doesn't exist, it will be scheduled for generation unless scheduleGeneration is false.
    /// </summary>
    /// <param name="scheduleGeneration">
    /// Whether to enqueue a job to generate the chunk if it doesn't exist and return null.
    /// When set to false, a partial Chunk is returned.</param>
    /// <returns>Null if the region or chunk doesn't exist yet. Otherwise the full chunk or a partial chunk.</returns>
    public async Task<Chunk?> GetChunkAsync(int chunkX, int chunkZ, bool scheduleGeneration = true)
    {
        Region? region = GetRegionForChunk(chunkX, chunkZ) ?? LoadRegion(chunkX >> Region.CubicRegionSizeShift, chunkZ >> Region.CubicRegionSizeShift);

        if (region is null)
            return null;

        var (x, z) = (NumericsHelper.Modulo(chunkX, Region.CubicRegionSize), NumericsHelper.Modulo(chunkZ, Region.CubicRegionSize));

        var chunk = await region.GetChunkAsync(x, z);

        if (chunk is not null)
        {
            if (!chunk.IsGenerated && scheduleGeneration)
            {
                if (!ChunksToGen.Contains((chunkX, chunkZ)))
                    ChunksToGen.Enqueue((chunkX, chunkZ));
                return null;
            }

            LoadedChunks.Add((chunkX, chunkZ));
            return chunk;
        }

        // Chunk hasn't been generated yet.
        if (scheduleGeneration)
        {
            if (!ChunksToGen.Contains((chunkX, chunkZ)))
                ChunksToGen.Enqueue((chunkX, chunkZ));
            return null;
        }

        // Create a partial chunk.
        chunk = new Chunk(chunkX, chunkZ)
        {
            chunkStatus = ChunkStatus.structure_starts
        };
        region.SetChunk(chunk);
        return chunk;
    }

    /// <summary>
    /// Gets a Chunk from a Region.
    /// If the Chunk doesn't exist, it will be scheduled for generation unless scheduleGeneration is false.
    /// </summary>
    /// <param name="scheduleGeneration">When set to false, a partial Chunk is returned.</param>
    /// <returns>Null if the region or chunk doesn't exist yet. Otherwise the full chunk or a partial chunk.</returns>
    public Task<Chunk?> GetChunkAsync(Vector worldLocation, bool scheduleGeneration = true) => GetChunkAsync(worldLocation.X.ToChunkCoord(), worldLocation.Z.ToChunkCoord(), scheduleGeneration);

    public Task<IBlock?> GetBlockAsync(Vector location) => GetBlockAsync(location.X, location.Y, location.Z);

    public async Task<IBlock?> GetBlockAsync(int x, int y, int z)
    {
        var c = await GetChunkAsync(x.ToChunkCoord(), z.ToChunkCoord(), false);
        return c?.GetBlock(x, y, z);
    }

    public async Task<int?> GetWorldSurfaceHeightAsync(int x, int z)
    {
        var c = await GetChunkAsync(x.ToChunkCoord(), z.ToChunkCoord(), false);
        return c?.Heightmaps[ChunkData.HeightmapType.MotionBlocking]
        .GetHeight(NumericsHelper.Modulo(x, 16), NumericsHelper.Modulo(z, 16));
    }

    public Task<NbtCompound?> GetBlockEntityAsync(Vector blockPosition) => GetBlockEntityAsync(blockPosition.X, blockPosition.Y, blockPosition.Z);

    public async Task<NbtCompound?> GetBlockEntityAsync(int x, int y, int z)
    {
        var c = await GetChunkAsync(x.ToChunkCoord(), z.ToChunkCoord(), false);
        return c?.GetBlockEntity(x, y, z);
    }

    public Task SetBlockEntity(Vector blockPosition, NbtCompound tileEntityData) => SetBlockEntity(blockPosition.X, blockPosition.Y, blockPosition.Z, tileEntityData);
    public async Task SetBlockEntity(int x, int y, int z, NbtCompound tileEntityData)
    {
        var c = await GetChunkAsync(x.ToChunkCoord(), z.ToChunkCoord(), false);
        c?.SetBlockEntity(x, y, z, tileEntityData);
    }

    public Task SetBlockAsync(int x, int y, int z, IBlock block) => SetBlockAsync(new Vector(x, y, z), block);

    public async Task SetBlockAsync(Vector location, IBlock block)
    {
        await SetBlockUntrackedAsync(location.X, location.Y, location.Z, block);

        this.BroadcastBlockChange(block, location);
    }

    public Task SetBlockAsync(int x, int y, int z, IBlock block, bool doBlockUpdate) => SetBlockAsync(new Vector(x, y, z), block, doBlockUpdate);

    public async Task SetBlockAsync(Vector location, IBlock block, bool doBlockUpdate)
    {
        await SetBlockUntrackedAsync(location.X, location.Y, location.Z, block, doBlockUpdate);
        this.BroadcastBlockChange(block, location);
    }

    //TODO ?????
    private void BroadcastBlockChange(IBlock block, Vector location)
    {
        var packet = new BlockUpdatePacket(location, block.GetHashCode());
        foreach (Player player in this.PlayersInRange(location))
        {
            player.client.SendPacket(packet);
        }
    }

    public IEnumerable<Player> PlayersInRange(Vector location) =>
        this.Players.Values.Where(player => player.client.LoadedChunks.Contains(location.ToChunkCoord()));

    public Task SetBlockUntrackedAsync(Vector location, IBlock block, bool doBlockUpdate = false) => SetBlockUntrackedAsync(location.X, location.Y, location.Z, block, doBlockUpdate);

    public async Task SetBlockUntrackedAsync(int x, int y, int z, IBlock block, bool doBlockUpdate = false)
    {
        if (doBlockUpdate)
        {
            await ScheduleBlockUpdateAsync(new BlockUpdate(this, new Vector(x, y, z), block));
            await BlockUpdateNeighborsAsync(new BlockUpdate(this, new Vector(x, y, z), block));
        }
        var c = await GetChunkAsync(x.ToChunkCoord(), z.ToChunkCoord(), false);
        c?.SetBlock(x, y, z, block);
    }

    public async Task SetBlockMetaAsync(int x, int y, int z, BlockMeta meta)
    {
        var c = await GetChunkAsync(x.ToChunkCoord(), z.ToChunkCoord(), false);
        c?.SetBlockMeta(x, y, z, meta);
    }

    public Task SetBlockMetaAsync(Vector location, BlockMeta meta) => SetBlockMetaAsync(location.X, location.Y, location.Z, meta);

    public async Task<BlockMeta?> GetBlockMeta(int x, int y, int z)
    {
        var c = await GetChunkAsync(x.ToChunkCoord(), z.ToChunkCoord());
        return c?.GetBlockMeta(x, y, z);
    }

    public Task<BlockMeta?> GetBlockMeta(Vector location) => GetBlockMeta(location.X, location.Y, location.Z);

    public IEnumerable<Entity> GetEntitiesInRange(VectorF location, float distance = 10f)
    {
        foreach (Player player in GetPlayersInRange(location, distance))
        {
            yield return player;
        }

        foreach (Entity entity in GetNonPlayerEntitiesInRange(location, distance))
        {
            yield return entity;
        }
    }

    public IEnumerable<Entity> GetNonPlayerEntitiesInRange(VectorF location, float distance)
    {
        if (float.IsNaN(distance) || distance < 0f)
        {
            yield break;
        }

        // Get corner chunk coordinates
        (int left, int top) = (location - new VectorF(distance)).ToChunkCoord();
        (int right, int bottom) = (location + new VectorF(distance)).ToChunkCoord();

        distance *= distance; // distance^2 <= deltaX^2 + deltaY^2

        // Iterate over chunks, taking one from each region, then getting the region itself
        for (int x = left; x <= right; x += Region.CubicRegionSize)
        {
            for (int y = top; y >= bottom; y -= Region.CubicRegionSize)
            {
                if (GetRegionForChunk(x, y) is not Region region)
                    continue;

                // Return entities in range
                foreach ((_, Entity entity) in region.Entities)
                {
                    VectorF entityLocation = entity.Position;
                    float differenceX = entityLocation.X - location.X;
                    float differenceY = entityLocation.Y - location.Y;

                    if (differenceX * differenceX + differenceY * differenceY <= distance)
                    {
                        yield return entity;
                    }
                }
            }
        }
    }

    public IEnumerable<Player> GetPlayersInRange(VectorF location, float distance)
    {
        if (float.IsNaN(distance) || distance < 0f)
        {
            yield break;
        }

        if (distance == 0f)
        {
            foreach ((_, Player player) in Players)
            {
                if (player.Position == location)
                {
                    yield return player;
                }
            }
            yield break;
        }

        distance *= distance; // distance^2 <= deltaX^2 + deltaY^2

        foreach ((_, Player player) in Players)
        {
            VectorF playerLocation = player.Position;
            float differenceX = playerLocation.X - location.X;
            float differenceY = playerLocation.Y - location.Y;

            if (differenceX * differenceX + differenceY * differenceY <= distance)
            {
                yield return player;
            }
        }
    }

    public bool TryAddPlayer(Player player) => Players.TryAdd(player.Uuid, player);

    public bool TryRemovePlayer(Player player) => Players.TryRemove(player.Uuid, out _);

    /// <summary>
    /// Method that handles world-specific tick behavior.
    /// </summary>
    /// <returns></returns>
    public async Task DoWorldTickAsync()
    {
        if (LevelData is null)
            return;

        LevelData.Time += this.Configuration.TimeTickSpeedMultiplier;
        LevelData.RainTime -= this.Configuration.TimeTickSpeedMultiplier;

        if (LevelData.RainTime < 1)
        {
            // Raintime passed, toggle weather
            LevelData.Raining = !LevelData.Raining;

            int rainTime;
            // amount of ticks in a day is 24000
            if (LevelData.Raining)
            {
                rainTime = Globals.Random.Next(12000, 24000); // rain lasts 0.5 - 1 day
            }
            else
            {
                rainTime = Globals.Random.Next(12000, 180000); // clear lasts 0.5 - 7.5 day
            }
            LevelData.RainTime = rainTime;

            Logger.LogInformation("Toggled rain: {raining} for {rainTime} ticks.", LevelData.Raining, LevelData.RainTime);
        }

        // Gradually increase and decrease rain levels based on
        // whether value is in range and what weather is active
        var oldLevel = rainLevel;
        if (!LevelData.Raining && rainLevel > 0f)
            rainLevel -= 0.01f;
        else if (LevelData.Raining && rainLevel < 1f)
            rainLevel += 0.01f;

        if (oldLevel != rainLevel)
        {
            // send new level if updated
            this.PacketBroadcaster.QueuePacketToWorld(this, new GameEventPacket(ChangeGameStateReason.RainLevelChange, rainLevel));
            if (rainLevel < 0.3f && rainLevel > 0.1f)
                this.PacketBroadcaster.QueuePacketToWorld(this, new GameEventPacket(LevelData.Raining ? ChangeGameStateReason.BeginRaining : ChangeGameStateReason.EndRaining));
        }

        if (LevelData.Time % (20 * this.Configuration.TimeTickSpeedMultiplier) == 0)
        {
            // Update client time every second / 20 ticks
            this.PacketBroadcaster.QueuePacketToWorld(this, new UpdateTimePacket(LevelData.Time, LevelData.Time % 24000));
        }

        //Tick regions within the world manager
        await Task.WhenAll(this.Regions.Values.Select(r => r.BeginTickAsync()));

        //// Check for chunks to load every second
        //if (LevelData.Time % 20 == 0)
        //{
        //    await ManageChunksAsync();
        //}
    }

    #region world loading/saving

    public async Task<bool> LoadAsync(DimensionCodec codec)
    {
        DimensionName = codec.Name;

        Init(codec);

        var dataPath = Path.Combine("worlds", ParentWorldName ?? Name, "level.dat");

        var fi = new FileInfo(dataPath);

        if (!fi.Exists)
            return false;

        var reader = new NbtReader(fi.OpenRead(), NbtCompression.GZip);

        var levelCompound = (reader.ReadNextTag() as NbtCompound)!;
        LevelData = new Level()
        {
            Hardcore = levelCompound.GetBool("hardcore"),
            MapFeatures = levelCompound.GetBool("MapFeatures"),
            Raining = levelCompound.GetBool("raining"),
            Thundering = levelCompound.GetBool("thundering"),
            DefaultGamemode = (Gamemode)levelCompound.GetInt("GameType"),
            GeneratorVersion = levelCompound.GetInt("generatorVersion"),
            RainTime = levelCompound.GetInt("rainTime"),
            SpawnPosition = new VectorF(levelCompound.GetInt("SpawnX"), levelCompound.GetInt("SpawnY"), levelCompound.GetInt("SpawnZ")),
            ThunderTime = levelCompound.GetInt("thunderTime"),
            Version = levelCompound.GetInt("version"),
            LastPlayed = levelCompound.GetLong("LastPlayed"),
            RandomSeed = levelCompound.GetLong("RandomSeed"),
            Time = levelCompound.GetLong("Time"),
            GeneratorName = levelCompound.GetString("generatorName"),
            LevelName = levelCompound.GetString("LevelName")
        };

        if (levelCompound.TryGetTag("Version", out var tag))
            LevelData.VersionData = tag as NbtCompound;

        Logger.LogInformation("Loading spawn chunks into memory...");
        for (int rx = -1; rx < 1; rx++)
            for (int rz = -1; rz < 1; rz++)
                LoadRegion(rx, rz);

        // spawn chunks are radius 12 from spawn,
        var radius = 12;
        var (x, z) = LevelData.SpawnPosition.ToChunkCoord();
        for (var cx = x - radius; cx < x + radius; cx++)
            for (var cz = z - radius; cz < z + radius; cz++)
                SpawnChunks.Add((cx, cz));

        await Parallel.ForEachAsync(SpawnChunks, async (c, cts) =>
        {
            await GetChunkAsync(c.X, c.Z);
            //// Update status occasionally so we're not destroying consoleio
            //// Removing this for now
            //if (c.X % 5 == 0)
            //    Server.UpdateStatusConsole();
        });

        Loaded = true;
        return true;
    }

    //TODO save world generator settings properly
    public async Task SaveAsync()
    {
        var worldFile = new FileInfo(LevelDataFilePath);

        if (worldFile.Exists)
        {
            worldFile.CopyTo($"{LevelDataFilePath}.old", true);
            worldFile.Delete();
        }

        await using var fs = worldFile.Create();
        await using var writer = new NbtWriter(fs, NbtCompression.GZip, "");

        writer.WriteBool("hardcore", LevelData.Hardcore);
        writer.WriteBool("MapFeatures", LevelData.MapFeatures);
        writer.WriteBool("raining", LevelData.Raining);
        writer.WriteBool("thundering", LevelData.Thundering);

        writer.WriteInt("GameType", (int)LevelData.DefaultGamemode);
        writer.WriteInt("generatorVersion", LevelData.GeneratorVersion);
        writer.WriteInt("rainTime", LevelData.RainTime);
        writer.WriteInt("SpawnX", (int)LevelData.SpawnPosition.X);
        writer.WriteInt("SpawnY", (int)LevelData.SpawnPosition.Y);
        writer.WriteInt("SpawnZ", (int)LevelData.SpawnPosition.Z);
        writer.WriteInt("thunderTime", LevelData.ThunderTime);
        writer.WriteInt("version", LevelData.Version);

        writer.WriteLong("LastPlayed", DateTimeOffset.Now.ToUnixTimeMilliseconds());
        writer.WriteLong("RandomSeed", LevelData.RandomSeed);
        writer.WriteLong("Time", Time);

        //this.WriteWorldGenSettings(writer);

        writer.WriteString("generatorName", Generator.Id);
        writer.WriteString("LevelName", Name);

        writer.EndCompound();

        await writer.TryFinishAsync();
    }

    public async Task UnloadPlayerAsync(Guid uuid)
    {
        Players.TryRemove(uuid, out var player);

        await player.SaveAsync();
    }
    #endregion

    public Region LoadRegionByChunk(int chunkX, int chunkZ)
    {
        int regionX = chunkX >> Region.CubicRegionSizeShift, regionZ = chunkZ >> Region.CubicRegionSizeShift;
        return LoadRegion(regionX, regionZ);
    }

    public Region LoadRegion(int regionX, int regionZ)
    {
        _regionLock.WaitOne();
        long value = NumericsHelper.IntsToLong(regionX, regionZ);

        if (Regions.TryGetValue(value, out var region))
        {
            _regionLock.Release();
            return region;
        }

        this.Logger.LogDebug("Trying to add {x}:{z}", regionX, regionZ);

        region = new Region(regionX, regionZ, FolderPath);

        if (Regions.TryAdd(value, region))
            this.Logger.LogDebug("Added region {x}:{z}", regionX, regionZ);

        //DOesn't need to be blocking
        _ = region.InitAsync();

        _regionLock.Release();
        return region;
    }

    public async Task UnloadRegionAsync(int regionX, int regionZ)
    {
        long value = NumericsHelper.IntsToLong(regionX, regionZ);
        if (Regions.TryRemove(value, out var r))
            await r.FlushAsync();
    }

    public async Task ScheduleBlockUpdateAsync(BlockUpdate blockUpdate)
    {
        blockUpdate.Block ??= await GetBlockAsync(blockUpdate.position);
        (int chunkX, int chunkZ) = blockUpdate.position.ToChunkCoord();
        Region? region = GetRegionForChunk(chunkX, chunkZ);
        region?.AddBlockUpdate(blockUpdate);
    }


    public async Task ManageChunksAsync()
    {
        // Check for chunks to unload every 30 seconds
        if (LevelData.Time > 0 && LevelData.Time % (20 * 30) == 0)
        {
            List<(int X, int Z)> chunksToKeep = new();
            Players.Where(p => p.Value.World == this).ForEach(p =>
            {
                chunksToKeep.AddRange(p.Value.client.LoadedChunks);
            });
            //TODO: Task.WhenAll for the slow IO ops 
            LoadedChunks.Except(chunksToKeep).Except(SpawnChunks).ForEach(async c =>
            {
                if (LoadedChunks.TryRemove(c))
                {
                    var r = GetRegionForChunk(c.X, c.Z);
                    await r.UnloadChunk(c.X, c.Z);
                }
            });
        }

        if (ChunksToGen.IsEmpty) { return; }

        // Pull some jobs out of the queue
        var jobs = new List<(int x, int z)>();
        for (int a = 0; a < Environment.ProcessorCount; a++)
        {
            if (ChunksToGen.TryDequeue(out var job))
                jobs.Add(job);
        }

        await Parallel.ForEachAsync(jobs, async (job, _) =>
        {
            Region region = GetRegionForChunk(job.x, job.z) ?? LoadRegionByChunk(job.x, job.z);

            var (x, z) = (NumericsHelper.Modulo(job.x, Region.CubicRegionSize), NumericsHelper.Modulo(job.z, Region.CubicRegionSize));

            Chunk c = await region.GetChunkAsync(x, z);
            if (c is null)
            {
                c = new Chunk(job.x, job.z)
                {
                    chunkStatus = ChunkStatus.structure_starts
                };
                // Set chunk now so that it no longer comes back as null. #threadlyfe
                region.SetChunk(c);
            }
            if (!c.IsGenerated)
            {
                c = await Generator.GenerateChunkAsync(job.x, job.z, c);
            }
            region.SetChunk(c);
        });
    }

    public Task FlushRegionsAsync() => Task.WhenAll(Regions.Select(pair => pair.Value.FlushAsync()));

    public IEntity SpawnFallingBlock(VectorF position, Material mat)
    {
        // offset position so it spawns in the right spot
        position.X += 0.5f;
        position.Z += 0.5f;
        FallingBlock entity = new(position)
        {
            Type = EntityType.FallingBlock,
            EntityId = GetTotalLoadedEntities() + 1,
            World = this,
            PacketBroadcaster = this.PacketBroadcaster,
            Block = BlocksRegistry.Get(mat)
        };

        this.PacketBroadcaster.QueuePacketToWorld(this, new SpawnEntityPacket
        {
            EntityId = entity.EntityId,
            Uuid = entity.Uuid,
            Type = entity.Type,
            Position = entity.Position,
            Pitch = 0,
            Yaw = 0,
            Data = entity.Block.GetHashCode()
        });

        TryAddEntity(entity);

        return entity;
    }

    public async Task<IEntity> SpawnEntityAsync(VectorF position, EntityType type)
    {
        // Arrow, Boat, DragonFireball, AreaEffectCloud, EndCrystal, EvokerFangs, ExperienceOrb, 
        // FireworkRocket, FallingBlock, Item, ItemFrame, Fireball, LeashKnot, LightningBolt,
        // LlamaSpit, Minecart, ChestMinecart, CommandBlockMinecart, FurnaceMinecart, HopperMinecart
        // SpawnerMinecart, TntMinecart, Painting, Tnt, ShulkerBullet, SpectralArrow, EnderPearl, Snowball, SmallFireball,
        // Egg, ExperienceBottle, Potion, Trident, FishingBobber, EyeOfEnder

        if (type == EntityType.FallingBlock)
        {
            return SpawnFallingBlock(position + (0, 20, 0), Material.Sand);
        }

        Entity entity;
        if (type.IsNonLiving())
        {
            entity = new Entity
            {
                Type = type,
                Position = position,
                EntityId = GetTotalLoadedEntities() + 1,
                World = this,
                PacketBroadcaster = this.PacketBroadcaster
            };

            if (type == EntityType.ExperienceOrb || type == EntityType.ExperienceBottle)
            {
                //TODO
            }
            else
            {
                this.PacketBroadcaster.QueuePacketToWorld(this, new SpawnEntityPacket
                {
                    EntityId = entity.EntityId,
                    Uuid = entity.Uuid,
                    Type = entity.Type,
                    Position = position,
                    Pitch = 0,
                    Yaw = 0,
                    Data = 0,
                    Velocity = new Velocity(0, 0, 0)
                });
            }
        }
        else
        {
            entity = new Living
            {
                Position = position,
                EntityId = GetTotalLoadedEntities() + 1,
                Type = type,
                World = this,
                PacketBroadcaster = this.PacketBroadcaster
            };

            this.PacketBroadcaster.QueuePacketToWorld(this, new SpawnEntityPacket
            {
                EntityId = entity.EntityId,
                Uuid = entity.Uuid,
                Type = type,
                Position = position,
                Pitch = 0,
                Yaw = 0,
                HeadYaw = 0,
                Velocity = new Velocity(0, 0, 0)
            });
        }

        TryAddEntity(entity);

        return entity;
    }

    public void RegisterDimension(DimensionCodec codec, string? worldGeneratorId = null)
    {
        if (dimensions.ContainsKey(codec.Name))
            throw new ArgumentException($"World already contains dimension with name: {codec.Name}");

        if (!this.WorldManager.WorldGenerators.TryGetValue(worldGeneratorId ?? codec.Name.TrimResourceTag(true), out var generatorType))
            throw new ArgumentException($"Failed to find generator with id: {worldGeneratorId}.");

        //TODO CREATE NEW TYPE CALLED DIMENSION AND IDIMENSION
        var dimensionWorld = new World(this.Logger, generatorType, this.WorldManager)
        {
            PacketBroadcaster = this.PacketBroadcaster,
            Configuration = this.Configuration,
            Name = codec.Name.TrimResourceTag(true),
            Seed = this.Seed
        };

        dimensionWorld.Init(codec, Name);

        dimensions.Add(codec.Name, dimensionWorld);
    }

    public void SpawnExperienceOrbs(VectorF position, short count = 1) => this.PacketBroadcaster.QueuePacketToWorld(this, new SpawnExperienceOrbPacket(count, position));

    /// <summary>
    /// 
    /// </summary>
    /// <param name="worldLoc"></param>
    /// <returns>Whether to update neighbor blocks.</returns>
    internal async ValueTask<bool> HandleBlockUpdateAsync(BlockUpdate update)
    {
        if (update.Block is not IBlock block)
            return false;

        // Todo: this better
        if (TagsRegistry.Blocks.GravityAffected.Entries.Contains(block.RegistryId))
            return await BlockUpdates.HandleFallingBlock(update);

        if (block.IsLiquid)
            return await BlockUpdates.HandleLiquidPhysicsAsync(update);

        return false;
    }

    internal async Task BlockUpdateNeighborsAsync(BlockUpdate update)
    {
        update = update with
        {
            Block = null,
            delayCounter = update.Delay
        };

        Vector[] directions = Vector.AllDirections;
        for (int i = 0; i < directions.Length; i++)
        {
            await ScheduleBlockUpdateAsync(update with { position = update.position + directions[i] });
        }
    }

    /// <summary>
    /// Initilizes properties, creates directories and registers default dimensions if necessary.
    /// </summary>
    /// <param name="dimensionId">The dimension this world will represent.</param>
    /// <param name="parentWorldName">The parent world name of this world(dimension). Only used if this world is a child dimension of another world.</param>
    /// <exception cref="ArgumentException">Thrown when no valid dimensions were found with the supplied <paramref name="dimensionId"/>.</exception>
    /// <remarks>This method should be called before and not after trying to generate the world.</remarks>
    internal void Init(DimensionCodec codec, string? parentWorldName = null)
    {
        // Make sure we set the right paths
        if (string.IsNullOrWhiteSpace(parentWorldName))
        {
            FolderPath = Path.Combine("worlds", Name);
            LevelDataFilePath = Path.Combine(FolderPath, "level.dat");
            PlayerDataPath = Path.Combine(FolderPath, "playerdata");
        }
        else
        {
            FolderPath = Path.Combine("worlds", parentWorldName, Name);
            LevelDataFilePath = Path.Combine("worlds", parentWorldName, "level.dat");
            PlayerDataPath = Path.Combine("worlds", parentWorldName, "playerdata");
        }

        ParentWorldName = parentWorldName;
        DimensionName = codec.Name;

        //TODO configure all dim data
        LevelData = new Level
        {
            Time = codec.Element.FixedTime ?? 0,
            DefaultGamemode = Gamemode.Survival,
            GeneratorName = Generator.Id
        };

        Directory.CreateDirectory(FolderPath);
        Directory.CreateDirectory(PlayerDataPath);

        initialized = true;
    }

    internal async Task GenerateWorldAsync(bool setWorldSpawn = false)
    {
        if (!initialized)
            throw new InvalidOperationException("World hasn't been initialized please call World.Init() before trying to generate the world.");

        Logger.LogInformation("Generating world... (Config pregeneration size is {pregenRange})", this.Configuration.PregenerateChunkRange);
        int pregenerationRange = this.Configuration.PregenerateChunkRange;

        int regionPregenRange = (pregenerationRange >> Region.CubicRegionSizeShift) + 1;

        await Parallel.ForEachAsync(Enumerable.Range(-regionPregenRange, regionPregenRange * 2 + 1), (x, _) =>
        {
            for (int z = -regionPregenRange; z < regionPregenRange; z++)
                LoadRegion(x, z);

            return ValueTask.CompletedTask;
        });

        for (int x = -pregenerationRange; x < pregenerationRange; x++)
        {
            for (int z = -pregenerationRange; z < pregenerationRange; z++)
            {
                ChunksToGen.Enqueue((x, z));
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
            int remain = ChunksToGenCount / (int)cps;
            Console.Write("\r{0} chunks/second - {1}% complete - {2} seconds remaining   ", cps.ToString("###.00"), pctComplete, remain);
            if (completedChunks % 1024 == 0) { // For Jon when he's doing large world gens
                await FlushRegionsAsync();
            }
        }
        Console.WriteLine();
        await FlushRegionsAsync();

        if (setWorldSpawn)
        {
            await SetWorldSpawnAsync();
            // spawn chunks are radius 12 from spawn,
            var radius = 12;
            var (x, z) = LevelData.SpawnPosition.ToChunkCoord();
            for (var cx = x - radius; cx < x + radius; cx++)
                for (var cz = z - radius; cz < z + radius; cz++)
                    SpawnChunks.Add((cx, cz));
        }
    }

    internal async Task SetWorldSpawnAsync()
    {
        if (LevelData.SpawnPosition.Y != 0) { return; }

        var pregenRange = this.Configuration.PregenerateChunkRange;
        var region = GetRegionForLocation(VectorF.Zero)!;
        foreach (var chunk in region.GeneratedChunks())
        {
            for (int bx = 0; bx < 16; bx++)
            {
                for (int bz = 0; bz < 16; bz++)
                {
                    // Get topmost block
                    var by = chunk.Heightmaps[ChunkData.HeightmapType.MotionBlocking].GetHeight(bx, bz);
                    IBlock block = chunk.GetBlock(bx, by, bz);

                    // Block must be high enough and either grass or sand
                    if (by < 64 || !block.Is(Material.GrassBlock) && !block.Is(Material.Sand))
                    {
                        continue;
                    }

                    // Block must have enough empty space above for player to spawn in
                    if (!chunk.GetBlock(bx, by + 1, bz).IsAir || !chunk.GetBlock(bx, by + 2, bz).IsAir)
                    {
                        continue;
                    }

                    var worldPos = new VectorF(bx + 0.5f + (chunk.X * 16), by + 1, bz + 0.5f + (chunk.Z * 16));
                    LevelData.SpawnPosition = worldPos;
                    Logger.LogInformation("World Spawn set to {worldPos}", worldPos);

                    // Should spawn be far from (0,0), queue up chunks in generation range.
                    // Just feign a request for a chunk and if it doesn't exist, it'll get queued for gen.
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

    internal bool TryAddEntity(Entity entity)
    {
        var (chunkX, chunkZ) = entity.Position.ToChunkCoord();

        Region? region = GetRegionForChunk(chunkX, chunkZ);

        if (region is null)
            throw new InvalidOperationException("Region is null, this wasn't supposed to happen.");

        return region.Entities.TryAdd(entity.EntityId, entity);
    }

    //TODO 
    private void LoadWorldGenSettings(NbtCompound levelCompound)
    {
        if (!levelCompound.TryGetTag("WorldGenSettings", out var genTag))
            return;

        var worldGenSettings = genTag as NbtCompound;

        // bonus_chest
        // seed
        // generate_features

        if (worldGenSettings.TryGetTag("dimensions", out var dimensionsTag))
        {
            var dimensions = dimensionsTag as NbtCompound;

            foreach (var (_, childDimensionTag) in dimensions)
            {
                var childDimensionCompound = childDimensionTag as NbtCompound;
            }

            var dimensionType = dimensions.GetString("type");
        }
    }

    private void WriteWorldGenSettings(NbtWriter writer)
    {
        if (!CodecRegistry.TryGetDimension(DimensionName, out var codec))
            return;

        var dimensionsCompound = new NbtCompound("dimensions")
        {
            new NbtCompound(codec.Name)
            {
                new NbtTag<string>("type", codec.Name),
            }
        };

        foreach (var (id, _) in dimensions)
        {
            CodecRegistry.TryGetDimension(id, out var childDimensionCodec);

            dimensionsCompound.Add(new NbtCompound(childDimensionCodec.Name)
            {
                new NbtTag<string>("type", childDimensionCodec.Name),
            });
        }

        var worldGenSettingsCompound = new NbtCompound("WorldGenSettings")
        {
            //THE SEED SHOULD BE NUMERICAL
            new NbtTag<string>("seed", Seed),

            dimensionsCompound
        };

        writer.WriteTag(worldGenSettingsCompound);
    }

    public async ValueTask DisposeAsync()
    {
        foreach ((_, Region region) in Regions)
        {
            await region.DisposeAsync();
        }
    }
}
