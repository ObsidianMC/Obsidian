using Microsoft.Extensions.Logging;
using Obsidian.Blocks;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using System.IO;

namespace Obsidian.WorldData;

public class World : IWorld
{
    public Level Data { get; internal set; }

    public ConcurrentDictionary<Guid, Player> Players { get; private set; } = new();

    public WorldGenerator Generator { get; internal set; }

    public Server Server { get; }

    public ConcurrentDictionary<long, Region> Regions { get; private set; } = new();

    public ConcurrentQueue<(int X, int Z)> ChunksToGen { get; private set; } = new();

    public ConcurrentBag<(int X, int Z)> SpawnChunks { get; private set; } = new();

    public string Name { get; }

    public bool Loaded { get; private set; }

    public long Time => Data.Time;

    public Gamemode GameType => Data.GameType;

    private float rainLevel = 0f;

    private WorldLight worldLight;

    internal World(string name, Server server)
    {
        this.Data = new Level
        {
            Time = 1200,
            GameType = (int)Gamemode.Survival,
            GeneratorName = WorldType.Default.ToString()
        };

        this.Name = name ?? throw new ArgumentNullException(nameof(name));
        this.Server = server;

        var playerDataPath = Path.Combine(this.Server.ServerFolderPath, this.Name, "playerdata");
        if (!Directory.Exists(playerDataPath))
            Directory.CreateDirectory(playerDataPath);

        worldLight = new(this);
    }

    public int GetTotalLoadedEntities() => this.Regions.Values.Sum(e => e == null ? 0 : e.Entities.Count);

    public async Task UpdateClientChunksAsync(Client c, bool unloadAll = false)
    {
        if (unloadAll)
        {
            foreach (var (X, Z) in c.LoadedChunks)
            {
                await c.UnloadChunkAsync(X, Z);
            }
            c.LoadedChunks.Clear();
        }

        List<(int X, int Z)> clientNeededChunks = new();
        List<(int X, int Z)> clientUnneededChunks = new(c.LoadedChunks);

        (int playerChunkX, int playerChunkZ) = c.Player.Position.ToChunkCoord();
        (int lastPlayerChunkX, int lastPlayerChunkZ) = c.Player.LastPosition.ToChunkCoord();

        int dist = (c.ClientSettings?.ViewDistance ?? 14) - 2;
        for (int x = playerChunkX + dist; x > playerChunkX - dist; x--)
            for (int z = playerChunkZ + dist; z > playerChunkZ - dist; z--)
                clientNeededChunks.Add((x, z));

        clientUnneededChunks = clientUnneededChunks.Except(clientNeededChunks).ToList();
        clientNeededChunks = clientNeededChunks.Except(c.LoadedChunks).ToList();
        clientNeededChunks.Sort((chunk1, chunk2) =>
        {
            return Math.Abs(playerChunkX - chunk1.X) +
            Math.Abs(playerChunkZ - chunk1.Z) <
            Math.Abs(playerChunkX - chunk2.X) +
            Math.Abs(playerChunkZ - chunk2.Z) ? -1 : 1;
        });

        await Parallel.ForEachAsync(clientUnneededChunks, async (chunkLoc, _) =>
        {
            await c.UnloadChunkAsync(chunkLoc.X, chunkLoc.Z);
            c.LoadedChunks.TryRemove(chunkLoc);
        });

        await Parallel.ForEachAsync(clientNeededChunks, async (chunkLoc, _) =>
        {
            var chunk = await this.GetChunkAsync(chunkLoc.X, chunkLoc.Z);
            if (chunk is not null)
            {
                await c.SendChunkAsync(chunk);
                c.LoadedChunks.Add((chunk.X, chunk.Z));
            }
        });
    }

    public Task ResendBaseChunksAsync(Client c) => UpdateClientChunksAsync(c, true);

    public async Task<bool> DestroyEntityAsync(Entity entity)
    {
        var destroyed = new DestroyEntities(entity);

        await this.Server.QueueBroadcastPacketAsync(destroyed);

        var (chunkX, chunkZ) = entity.Position.ToChunkCoord();

        var region = this.GetRegionForChunk(chunkX, chunkZ);

        if (region is null)
            throw new InvalidOperationException("Region is null this wasn't supposed to happen.");

        return region.Entities.TryRemove(entity.EntityId, out _);
    }

    public Region GetRegionForChunk(int chunkX, int chunkZ)
    {
        long value = NumericsHelper.IntsToLong(chunkX >> Region.cubicRegionSizeShift, chunkZ >> Region.cubicRegionSizeShift);

        return this.Regions.SingleOrDefault(x => x.Key == value).Value;
    }

    public Region GetRegionForChunk(Vector location)
    {
        return this.GetRegionForChunk(location.X, location.Z);
    }

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
        var region = this.GetRegionForChunk(chunkX, chunkZ);
        
        if (region is null)
        {
            region = await LoadRegionAsync(chunkX >> Region.cubicRegionSizeShift, chunkZ >> Region.cubicRegionSizeShift);
        }
        
        if (region is null)
        {
            return null;
        }

        (int X, int Z) chunkIndex = (NumericsHelper.Modulo(chunkX, Region.cubicRegionSize), NumericsHelper.Modulo(chunkZ, Region.cubicRegionSize));
        var chunk = region.GetChunk(chunkIndex);

        if (chunk is not null)
        {
            if (!chunk.isGenerated && scheduleGeneration)
            {
                if (!ChunksToGen.Contains((chunkX, chunkZ)))
                    ChunksToGen.Enqueue((chunkX, chunkZ));
                return null;
            }

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
            isGenerated = false // Not necessary; just being explicit.
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
    public async Task<Chunk?> GetChunkAsync(Vector worldLocation, bool scheduleGeneration = true) => await GetChunkAsync(worldLocation.X.ToChunkCoord(), worldLocation.Z.ToChunkCoord(), scheduleGeneration);

    public async Task<Block?> GetBlockAsync(Vector location) => await GetBlockAsync(location.X, location.Y, location.Z);

    public async Task<Block?> GetBlockAsync(int x, int y, int z)
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

    public async Task<NbtCompound?> GetBlockEntityAsync(Vector blockPosition) => await this.GetBlockEntityAsync(blockPosition.X, blockPosition.Y, blockPosition.Z);

    public async Task<NbtCompound?> GetBlockEntityAsync(int x, int y, int z)
    {
        var c = await GetChunkAsync(x.ToChunkCoord(), z.ToChunkCoord(), false);
        return c?.GetBlockEntity(x, y, z);
    }

    public async Task SetBlockEntity(Vector blockPosition, NbtCompound tileEntityData) => await this.SetBlockEntity(blockPosition.X, blockPosition.Y, blockPosition.Z, tileEntityData);
    public async Task SetBlockEntity(int x, int y, int z, NbtCompound tileEntityData)
    {
        var c = await GetChunkAsync(x.ToChunkCoord(), z.ToChunkCoord(), false);
        c?.SetBlockEntity(x, y, z, tileEntityData);
    }

    public async Task SetBlockAsync(int x, int y, int z, Block block) => await SetBlockAsync(new Vector(x, y, z), block);

    public async Task SetBlockAsync(Vector location, Block block)
    {
        await SetBlockUntrackedAsync(location.X, location.Y, location.Z, block);
        Server.BroadcastBlockChange(block, location);
    }

    public async Task SetBlockAsync(int x, int y, int z, Block block, bool doBlockUpdate) => await SetBlockAsync(new Vector(x, y, z), block, doBlockUpdate);

    public async Task SetBlockAsync(Vector location, Block block, bool doBlockUpdate)
    {
        await SetBlockUntrackedAsync(location.X, location.Y, location.Z, block, doBlockUpdate);
        Server.BroadcastBlockChange(block, location);
    }

    public async Task SetBlockUntrackedAsync(Vector location, Block block, bool doBlockUpdate = false) => await SetBlockUntrackedAsync(location.X, location.Y, location.Z, block, doBlockUpdate);

    public async Task SetBlockUntrackedAsync(int x, int y, int z, Block block, bool doBlockUpdate = false)
    {
        if (doBlockUpdate)
        {
            await ScheduleBlockUpdateAsync(new BlockUpdate(this, new Vector(x, y, z), block));
            await BlockUpdateNeighborsAsync(new BlockUpdate(this, new Vector(x, y, z), block));
        }
        var c = await GetChunkAsync(x.ToChunkCoord(), z.ToChunkCoord(), false);
        c?.SetBlock(x, y, z, block);
    }

    public async Task SetBlockMeta(int x, int y, int z, BlockMeta meta)
    {
        var c = await GetChunkAsync(x.ToChunkCoord(), z.ToChunkCoord(), false);
        c?.SetBlockMeta(x, y, z, meta);
    }

    public async Task SetBlockMeta(Vector location, BlockMeta meta) => await this.SetBlockMeta(location.X, location.Y, location.Z, meta);

    public async Task<BlockMeta?> GetBlockMeta(int x, int y, int z)
    {
        var c = await GetChunkAsync(x.ToChunkCoord(), z.ToChunkCoord());
        return c?.GetBlockMeta(x, y, z);
    }

    public async Task<BlockMeta?> GetBlockMeta(Vector location) => await this.GetBlockMeta(location.X, location.Y, location.Z);

    public IEnumerable<Entity> GetEntitiesNear(VectorF location, float distance = 10f)
    {
        var (chunkX, chunkZ) = location.ToChunkCoord();

        var region = this.GetRegionForChunk(chunkX, chunkZ);

        if (region is null)
            return new List<Entity>();

        var selected = region.Entities.Select(x => x.Value).Where(x => VectorF.Distance(location, x.Position) <= distance).ToList();

        selected.AddRange(this.Players.Select(x => x.Value).Where(x => VectorF.Distance(location, x.Position) <= distance));

        return selected;
    }

    public bool AddPlayer(Player player) => this.Players.TryAdd(player.Uuid, player);

    public bool RemovePlayer(Player player) => this.Players.TryRemove(player.Uuid, out _);

    /// <summary>
    /// Method that handles world-specific tick behavior.
    /// </summary>
    /// <returns></returns>
    public async Task DoWorldTickAsync()
    {
        this.Data.Time += this.Server.Config.TimeTickSpeedMultiplier;
        this.Data.RainTime -= this.Server.Config.TimeTickSpeedMultiplier;

        if (Data.RainTime < 1)
        {
            // Raintime passed, toggle weather
            Data.Raining = !Data.Raining;

            int rainTime;
            // amount of ticks in a day is 24000
            if (Data.Raining)
            {
                rainTime = Globals.Random.Next(12000, 24000); // rain lasts 0.5 - 1 day
            }
            else
            {
                rainTime = Globals.Random.Next(12000, 180000); // clear lasts 0.5 - 7.5 day
            }
            Data.RainTime = rainTime;

            this.Server.Logger.LogInformation($"Toggled rain: {this.Data.Raining} for {this.Data.RainTime} ticks.");
        }

        // Gradually increase and decrease rain levels based on
        // whether value is in range and what weather is active
        var oldLevel = this.rainLevel;
        if (!Data.Raining && this.rainLevel > 0f)
            this.rainLevel -= 0.01f;
        else if (Data.Raining && this.rainLevel < 1f)
            this.rainLevel += 0.01f;

        if (oldLevel != this.rainLevel)
        {
            // send new level if updated
            this.Server.BroadcastPacket(new ChangeGameState(ChangeGameStateReason.RainLevelChange, this.rainLevel));
            if (rainLevel < 0.3f && rainLevel > 0.1f)
                this.Server.BroadcastPacket(new ChangeGameState(this.Data.Raining ? ChangeGameStateReason.BeginRaining : ChangeGameStateReason.EndRaining));
        }

        if (this.Data.Time % (20 * this.Server.Config.TimeTickSpeedMultiplier) == 0)
        {
            // Update client time every second / 20 ticks
            this.Server.BroadcastPacket(new TimeUpdate(this.Data.Time, this.Data.Time % 24000));
        }

        await this.ManageChunksAsync();
    }

    #region world loading/saving

    public async Task<bool> LoadAsync()
    {
        var dataPath = Path.Join(Server.ServerFolderPath, Name, "level.dat");

        var fi = new FileInfo(dataPath);

        if (!fi.Exists) { return false; }

        var reader = new NbtReader(fi.OpenRead(), NbtCompression.GZip);

        var levelcompound = reader.ReadNextTag() as NbtCompound;
        this.Data = new Level()
        {
            Hardcore = levelcompound.GetBool("hardcore"),
            MapFeatures = levelcompound.GetBool("MapFeatures"),
            Raining = levelcompound.GetBool("raining"),
            Thundering = levelcompound.GetBool("thundering"),
            GameType = (Gamemode)levelcompound.GetInt("GameType"),
            GeneratorVersion = levelcompound.GetInt("generatorVersion"),
            RainTime = levelcompound.GetInt("rainTime"),
            SpawnPosition = new VectorF(levelcompound.GetInt("SpawnX"), levelcompound.GetInt("SpawnY"), levelcompound.GetInt("SpawnZ")),
            ThunderTime = levelcompound.GetInt("thunderTime"),
            Version = levelcompound.GetInt("version"),
            LastPlayed = levelcompound.GetLong("LastPlayed"),
            RandomSeed = levelcompound.GetLong("RandomSeed"),
            Time = levelcompound.GetLong("Time"),
            GeneratorName = levelcompound.GetString("generatorName"),
            LevelName = levelcompound.GetString("LevelName")
        };

        if (!Server.WorldGenerators.TryGetValue(this.Data.GeneratorName, out WorldGenerator value))
        {
            Server.Logger.LogWarning($"Unknown generator type {this.Data.GeneratorName}");
            return false;
        }
        this.Generator = value;

        Server.Logger.LogInformation($"Loading spawn chunks into memory...");
        for (int rx = -1; rx < 1; rx++)
            for (int rz = -1; rz < 1; rz++)
                _ = await LoadRegionAsync(rx, rz);

        // spawn chunks are radius 12 from spawn,
        var radius = 12;
        var (x, z) = this.Data.SpawnPosition.ToChunkCoord();
        for (var cx = x - radius; cx < x + radius; cx++)
            for (var cz = z - radius; cz < z + radius; cz++)
                SpawnChunks.Add((cx, cz));
        await Parallel.ForEachAsync(SpawnChunks, async (c, cts) =>
        {
            await GetChunkAsync(c.X, c.Z);
            // Update status occasionally so we're not destroying consoleio
            if (c.X % 5 == 0)
                Server.UpdateStatusConsole();
        });

        Loaded = true;
        return true;
    }

    //TODO save world generator settings properly
    public void Save()
    {
        var worldFile = new FileInfo(Path.Join(Server.ServerFolderPath, Name, "level.dat"));

        using var fs = worldFile.OpenWrite();

        var writer = new NbtWriter(fs, NbtCompression.GZip, "");

        writer.WriteBool("hardcore", false);
        writer.WriteBool("MapFeatures", true);
        writer.WriteBool("raining", false);
        writer.WriteBool("thundering", false);

        writer.WriteInt("GameType", (int)Gamemode.Creative);
        writer.WriteInt("generatorVersion", 1);
        writer.WriteInt("rainTime", 0);
        writer.WriteInt("SpawnX", (int)Data.SpawnPosition.X);
        writer.WriteInt("SpawnY", (int)Data.SpawnPosition.Y);
        writer.WriteInt("SpawnZ", (int)Data.SpawnPosition.Z);
        writer.WriteInt("thunderTime", 0);
        writer.WriteInt("version", 19133);

        writer.WriteLong("LastPlayed", DateTimeOffset.Now.ToUnixTimeMilliseconds());
        writer.WriteLong("RandomSeed", 1);
        writer.WriteLong("Time", this.Time);

        writer.WriteString("generatorName", Generator.Id);
        writer.WriteString("LevelName", Name);

        writer.EndCompound();

        writer.TryFinish();
    }

    public async Task UnloadPlayerAsync(Guid uuid)
    {
        this.Players.TryRemove(uuid, out var player);

        await player.SaveAsync();
    }
    #endregion

    public async Task<Region?> LoadRegionByChunkAsync(int chunkX, int chunkZ)
    {
        int regionX = chunkX >> Region.cubicRegionSizeShift, regionZ = chunkZ >> Region.cubicRegionSizeShift;
        return await LoadRegionAsync(regionX, regionZ);
    }

    public async Task<Region?> LoadRegionAsync(int regionX, int regionZ)
    {
        long value = NumericsHelper.IntsToLong(regionX, regionZ);

        if (!this.Regions.TryAdd(value, null))
        {
            if (this.Regions[value] is not null)
            {
                return this.Regions[value];
            }
        }

        var region = new Region(regionX, regionZ, Path.Join(Server.ServerFolderPath, Name));
        if (await region.InitAsync()) 
        {
            _ = Task.Run(() => region.BeginTickAsync(this.Server.cts.Token));
            this.Regions[value] = region;
        }
        return this.Regions[value];
    }

    public async Task UnloadRegionAsync(int regionX, int regionZ)
    {
        long value = NumericsHelper.IntsToLong(regionX, regionZ);
        if (Regions.TryRemove(value, out var r))
            await r.FlushAsync();
    }

    public async Task ScheduleBlockUpdateAsync(BlockUpdate bu)
    {
        bu.Block ??= await GetBlockAsync(bu.position);
        var r = GetRegionForChunk(bu.position.X.ToChunkCoord(), bu.position.Z.ToChunkCoord());
        r.AddBlockUpdate(bu);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="worldLoc"></param>
    /// <returns>Whether to update neighbor blocks.</returns>
    internal async Task<bool> HandleBlockUpdate(BlockUpdate bu)
    {
        if (bu.Block is null) { return false; }

        // Todo: this better
        if (Block.GravityAffected.Contains(bu.Block.Value.Material))
        {
            return await BlockUpdates.HandleFallingBlock(bu);
        }

        if (bu.Block.Value.IsFluid)
        {
            return await BlockUpdates.HandleLiquidPhysicsAsync(bu);
        }

        return false;
    }

    internal async Task BlockUpdateNeighborsAsync(BlockUpdate bu)
    {
        bu.Block = null;
        bu.delayCounter = bu.Delay;
        var north = bu;
        north.position += Vector.Forwards;

        var south = bu;
        south.position += Vector.Backwards;

        var west = bu;
        west.position += Vector.Left;

        var east = bu;
        east.position += Vector.Right;

        var up = bu;
        up.position += Vector.Up;

        var down = bu;
        down.position += Vector.Down;

        await ScheduleBlockUpdateAsync(north);
        await ScheduleBlockUpdateAsync(south);
        await ScheduleBlockUpdateAsync(west);
        await ScheduleBlockUpdateAsync(east);
        await ScheduleBlockUpdateAsync(up);
        await ScheduleBlockUpdateAsync(down);
    }

    public async Task ManageChunksAsync()
    {
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
            Region region = GetRegionForChunk(job.x, job.z);
            if (region is null)
            {
                region = await LoadRegionByChunkAsync(job.x, job.z);
            }

            (int X, int Z) chunkIndex = (NumericsHelper.Modulo(job.x, Region.cubicRegionSize), NumericsHelper.Modulo(job.z, Region.cubicRegionSize));
            Chunk c = region.GetChunk(chunkIndex);
            if (c is null)
            {
                c = new Chunk(job.x, job.z)
                {
                    isGenerated = false // Not necessary; just being explicit.
                };
                // Set chunk now so that it no longer comes back as null. #threadlyfe
                region.SetChunk(c);
            }
            c = await Generator.GenerateChunkAsync(job.x, job.z, this, c);
            await worldLight.ProcessSkyLightForChunk(c);
            region.SetChunk(c);
        });
    }

    public async Task FlushRegionsAsync()
    {
        await Task.WhenAll(Regions.Select(pair => pair.Value.FlushAsync()));
    }

    public IEntity SpawnFallingBlock(VectorF position, Material mat)
    {
        // offset position so it spawns in the right spot
        position.X += 0.5f;
        position.Z += 0.5f;
        FallingBlock entity = new()
        {
            Type = EntityType.FallingBlock,
            Position = position,
            EntityId = GetTotalLoadedEntities() + 1,
            Server = Server,
            BlockMaterial = mat
        };

        Server.BroadcastPacket(new SpawnEntityPacket
        {
            EntityId = entity.EntityId,
            Uuid = entity.Uuid,
            Type = entity.Type,
            Position = entity.Position,
            Pitch = 0,
            Yaw = 0,
            Data = new Block(mat).StateId
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
                EntityId = this.GetTotalLoadedEntities() + 1,
                Server = this.Server
            };

            if (type == EntityType.ExperienceOrb || type == EntityType.ExperienceBottle)
            {
                //TODO
            }
            else
            {
                await this.Server.QueueBroadcastPacketAsync(new SpawnEntityPacket
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
                EntityId = this.GetTotalLoadedEntities() + 1,
                Type = type
            };

            await this.Server.QueueBroadcastPacketAsync(new SpawnLivingEntity
            {
                EntityId = entity.EntityId,
                Uuid = entity.Uuid,
                Type = type,
                Position = position,
                Pitch = 0,
                Yaw = 0,
                HeadPitch = 0,
                Velocity = new Velocity(0, 0, 0)
            });
        }

        this.TryAddEntity(entity);

        return entity;
    }

    internal async Task Init(WorldGenerator gen)
    {
        // Make world directory
        Directory.CreateDirectory(Path.Join(Server.ServerFolderPath, Name));
        this.Generator = gen;
        await GenerateWorld();
        await SetWorldSpawn();
    }

    internal async Task GenerateWorld()
    {
        this.Server.Logger.LogInformation($"Generating world... (Config pregeneration size is {Server.Config.PregenerateChunkRange})");
        int pregenerationRange = Server.Config.PregenerateChunkRange;

        int regionPregenRange = (pregenerationRange >> Region.cubicRegionSizeShift) + 1;

        Parallel.For(-regionPregenRange, regionPregenRange, async x =>
        {
            for (int z = -regionPregenRange; z < regionPregenRange; z++)
            {
                await LoadRegionAsync(x, z);
            };
        });

        // I don't know why we still have null regions when we get here ¯\_(ツ)_/¯
        while (Regions.Any(r => r.Value is null))
        {
            await Task.Delay(100);
        }

        for (int x = -pregenerationRange; x < pregenerationRange; x++)
        {
            for (int z = -pregenerationRange; z < pregenerationRange; z++)
            {
                ChunksToGen.Enqueue((x, z));
            }
        }

        while (!ChunksToGen.IsEmpty)
        {
            await ManageChunksAsync();
            Server.UpdateStatusConsole();
        }

        await FlushRegionsAsync();
    }

    internal async Task SetWorldSpawn()
    {
        if (Data.SpawnPosition.Y != 0) { return; }

        foreach (var r in Regions.Values)
        {
            foreach (var c in r.GeneratedChunks())
            {
                for (int bx = 0; bx < 16; bx++)
                {
                    for (int bz = 0; bz < 16; bz++)
                    {
                        var by = c.Heightmaps[ChunkData.HeightmapType.MotionBlocking].GetHeight(bx, bz);
                        Block block = c.GetBlock(bx, by, bz);
                        if (by >= 64 && (block.Is(Material.GrassBlock) || block.Is(Material.Sand)))
                        {
                            if (c.GetBlock(bx, by + 1, bz).IsAir && c.GetBlock(bx, by + 2, bz).IsAir)
                            {
                                var worldPos = new VectorF(bx + 0.5f + (c.X * 16), by + 1, bz + 0.5f + (c.Z * 16));
                                this.Data.SpawnPosition = worldPos;
                                this.Server.Logger.LogInformation($"World Spawn set to {worldPos}");

                                // Should spawn be far from (0,0), queue up chunks in generation range.
                                // Just feign a request for a chunk and if it doesn't exist, it'll get queued for gen.
                                for (int x = c.X - Server.Config.PregenerateChunkRange; x < c.X + Server.Config.PregenerateChunkRange; x++)
                                {
                                    for (int z = c.Z - Server.Config.PregenerateChunkRange; z < c.Z + Server.Config.PregenerateChunkRange; z++)
                                    {
                                        await GetChunkAsync(x, z);
                                    }
                                }

                                return;
                            }
                        }
                    }
                }
            }
        }
    }

    internal bool TryAddEntity(Entity entity)
    {
        var (chunkX, chunkZ) = entity.Position.ToChunkCoord();

        var region = this.GetRegionForChunk(chunkX, chunkZ);

        if (region is null)
            throw new InvalidOperationException("Region is null this wasn't supposed to happen.");

        return region.Entities.TryAdd(entity.EntityId, entity);
    }

    public async Task SpawnExperienceOrbs(VectorF position, short count = 1)
    {
        await this.Server.QueueBroadcastPacketAsync(new SpawnExperienceOrb(count, position));
    }

    public async Task SpawnPainting(Vector position, Painting painting, PaintingDirection direction, Guid uuid = default)
    {
        if (uuid == Guid.Empty) uuid = Guid.NewGuid();
        await this.Server.QueueBroadcastPacketAsync(new SpawnPainting(uuid, painting.Id, position, direction));
    }
}
