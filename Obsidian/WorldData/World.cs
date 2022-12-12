using Microsoft.Extensions.Logging;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.Blocks;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Utilities.Registry;
using System.IO;

namespace Obsidian.WorldData;

public class World : IWorld
{
    private float rainLevel = 0f;
    private bool initialized = false;

    internal Dictionary<string, World> dimensions = new();

    public Level LevelData { get; internal set; }

    public ConcurrentDictionary<Guid, Player> Players { get; private set; } = new();

    public IWorldGenerator Generator { get; internal set; }

    public Server Server { get; }

    public ConcurrentDictionary<long, Region> Regions { get; private set; } = new();

    public ConcurrentQueue<(int X, int Z)> ChunksToGen { get; private set; } = new();

    public ConcurrentBag<(int X, int Z)> SpawnChunks { get; private set; } = new();

    public string Name { get; }
    public string Seed { get; }
    public string FolderPath { get; private set; }
    public string PlayerDataPath { get; private set; }
    public string LevelDataFilePath { get; private set; }

    public bool Loaded { get; private set; }

    public long Time => LevelData.Time;

    public Gamemode DefaultGamemode => LevelData.DefaultGamemode;

    public string DimensionName { get; private set; }

    public string? ParentWorldName { get; private set; }
    private WorldLight worldLight;

    internal World(string name, Server server, string seed, Type generatorType)
    {
        this.Name = name ?? throw new ArgumentNullException(nameof(name));
        this.Server = server;

        this.Seed = seed ?? throw new ArgumentNullException(nameof(seed));

        this.Generator = (IWorldGenerator)Activator.CreateInstance(generatorType);
        Generator.Init(this);
        worldLight = new(this);
    }

    public int GetTotalLoadedEntities() => this.Regions.Values.Sum(e => e == null ? 0 : e.Entities.Count);

    public async Task<bool> DestroyEntityAsync(Entity entity)
    {
        var destroyed = new RemoveEntitiesPacket(entity);

        await this.Server.QueueBroadcastPacketAsync(destroyed);

        var (chunkX, chunkZ) = entity.Position.ToChunkCoord();

        Region? region = this.GetRegionForChunk(chunkX, chunkZ);

        if (region is null)
            throw new InvalidOperationException("Region is null this wasn't supposed to happen.");

        return region.Entities.TryRemove(entity.EntityId, out _);
    }

    public Region? GetRegionForChunk(int chunkX, int chunkZ)
    {
        long value = NumericsHelper.IntsToLong(chunkX >> Region.cubicRegionSizeShift, chunkZ >> Region.cubicRegionSizeShift);

        return Regions.TryGetValue(value, out Region? region) ? region : null;
    }

    public Region? GetRegionForChunk(Vector location) => this.GetRegionForChunk(location.X, location.Z);

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
        Region? region = this.GetRegionForChunk(chunkX, chunkZ);

        if (region is null)
        {
            region = await LoadRegionAsync(chunkX >> Region.cubicRegionSizeShift, chunkZ >> Region.cubicRegionSizeShift);
        }

        if (region is null)
        {
            return null;
        }

        var (x, z) = (NumericsHelper.Modulo(chunkX, Region.cubicRegionSize), NumericsHelper.Modulo(chunkZ, Region.cubicRegionSize));

        var chunk = await region.GetChunkAsync(x, z);

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
    public Task<Chunk?> GetChunkAsync(Vector worldLocation, bool scheduleGeneration = true) => GetChunkAsync(worldLocation.X.ToChunkCoord(), worldLocation.Z.ToChunkCoord(), scheduleGeneration);

    public Task<Block?> GetBlockAsync(Vector location) => GetBlockAsync(location.X, location.Y, location.Z);

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

    public Task<NbtCompound?> GetBlockEntityAsync(Vector blockPosition) => this.GetBlockEntityAsync(blockPosition.X, blockPosition.Y, blockPosition.Z);

    public async Task<NbtCompound?> GetBlockEntityAsync(int x, int y, int z)
    {
        var c = await GetChunkAsync(x.ToChunkCoord(), z.ToChunkCoord(), false);
        return c?.GetBlockEntity(x, y, z);
    }

    public Task SetBlockEntity(Vector blockPosition, NbtCompound tileEntityData) => this.SetBlockEntity(blockPosition.X, blockPosition.Y, blockPosition.Z, tileEntityData);
    public async Task SetBlockEntity(int x, int y, int z, NbtCompound tileEntityData)
    {
        var c = await GetChunkAsync(x.ToChunkCoord(), z.ToChunkCoord(), false);
        c?.SetBlockEntity(x, y, z, tileEntityData);
    }

    public Task SetBlockAsync(int x, int y, int z, Block block) => SetBlockAsync(new Vector(x, y, z), block);

    public async Task SetBlockAsync(Vector location, Block block)
    {
        await SetBlockUntrackedAsync(location.X, location.Y, location.Z, block);
        Server.BroadcastBlockChange(this, block, location);
    }

    public Task SetBlockAsync(int x, int y, int z, Block block, bool doBlockUpdate) => SetBlockAsync(new Vector(x, y, z), block, doBlockUpdate);

    public async Task SetBlockAsync(Vector location, Block block, bool doBlockUpdate)
    {
        await SetBlockUntrackedAsync(location.X, location.Y, location.Z, block, doBlockUpdate);
        Server.BroadcastBlockChange(this, block, location);
    }

    public Task SetBlockUntrackedAsync(Vector location, Block block, bool doBlockUpdate = false) => SetBlockUntrackedAsync(location.X, location.Y, location.Z, block, doBlockUpdate);

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

    public async Task SetBlockMetaAsync(int x, int y, int z, BlockMeta meta)
    {
        var c = await GetChunkAsync(x.ToChunkCoord(), z.ToChunkCoord(), false);
        c?.SetBlockMeta(x, y, z, meta);
    }

    public Task SetBlockMetaAsync(Vector location, BlockMeta meta) => this.SetBlockMetaAsync(location.X, location.Y, location.Z, meta);

    public async Task<BlockMeta?> GetBlockMeta(int x, int y, int z)
    {
        var c = await GetChunkAsync(x.ToChunkCoord(), z.ToChunkCoord());
        return c?.GetBlockMeta(x, y, z);
    }

    public Task<BlockMeta?> GetBlockMeta(Vector location) => this.GetBlockMeta(location.X, location.Y, location.Z);

    public IEnumerable<Entity> GetEntitiesNear(VectorF location, float distance = 10f)
    {
        var (chunkX, chunkZ) = location.ToChunkCoord();

        Region? region = this.GetRegionForChunk(chunkX, chunkZ);

        if (region is null)
            return new List<Entity>();

        var selected = region.Entities.Select(x => x.Value).Where(x => VectorF.Distance(location, x.Position) <= distance).ToList();

        selected.AddRange(this.Players.Select(x => x.Value).Where(x => VectorF.Distance(location, x.Position) <= distance));

        return selected;
    }

    public bool TryAddPlayer(Player player) => this.Players.TryAdd(player.Uuid, player);

    public bool TryRemovePlayer(Player player) => this.Players.TryRemove(player.Uuid, out _);

    /// <summary>
    /// Method that handles world-specific tick behavior.
    /// </summary>
    /// <returns></returns>
    public async Task DoWorldTickAsync()
    {
        this.LevelData.Time += this.Server.Config.TimeTickSpeedMultiplier;
        this.LevelData.RainTime -= this.Server.Config.TimeTickSpeedMultiplier;

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

            this.Server.Logger.LogInformation($"Toggled rain: {this.LevelData.Raining} for {this.LevelData.RainTime} ticks.");
        }

        // Gradually increase and decrease rain levels based on
        // whether value is in range and what weather is active
        var oldLevel = this.rainLevel;
        if (!LevelData.Raining && this.rainLevel > 0f)
            this.rainLevel -= 0.01f;
        else if (LevelData.Raining && this.rainLevel < 1f)
            this.rainLevel += 0.01f;

        if (oldLevel != this.rainLevel)
        {
            // send new level if updated
            this.Server.BroadcastPacket(new GameEventPacket(ChangeGameStateReason.RainLevelChange, this.rainLevel));
            if (rainLevel < 0.3f && rainLevel > 0.1f)
                this.Server.BroadcastPacket(new GameEventPacket(this.LevelData.Raining ? ChangeGameStateReason.BeginRaining : ChangeGameStateReason.EndRaining));
        }

        if (this.LevelData.Time % (20 * this.Server.Config.TimeTickSpeedMultiplier) == 0)
        {
            // Update client time every second / 20 ticks
            this.Server.BroadcastPacket(new UpdateTimePacket(this.LevelData.Time, this.LevelData.Time % 24000));
        }

        await this.ManageChunksAsync();
    }

    #region world loading/saving

    public async Task<bool> LoadAsync(DimensionCodec codec)
    {
        this.DimensionName = codec.Name;

        this.Init(codec);

        var dataPath = Path.Join(Server.ServerFolderPath, this.ParentWorldName ?? this.Name, "level.dat");

        var fi = new FileInfo(dataPath);

        if (!fi.Exists)
            return false;

        var reader = new NbtReader(fi.OpenRead(), NbtCompression.GZip);

        var levelCompound = reader.ReadNextTag() as NbtCompound;
        this.LevelData = new Level()
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
            this.LevelData.VersionData = tag as NbtCompound;

        Server.Logger.LogInformation($"Loading spawn chunks into memory...");
        for (int rx = -1; rx < 1; rx++)
            for (int rz = -1; rz < 1; rz++)
                _ = await LoadRegionAsync(rx, rz);

        // spawn chunks are radius 12 from spawn,
        var radius = 12;
        var (x, z) = this.LevelData.SpawnPosition.ToChunkCoord();
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
    public async Task SaveAsync()
    {
        var worldFile = new FileInfo(this.LevelDataFilePath);

        if (worldFile.Exists)
        {
            worldFile.CopyTo($"{this.LevelDataFilePath}.old", true);
            worldFile.Delete();
        }

        using var fs = worldFile.Create();
        using var writer = new NbtWriter(fs, NbtCompression.GZip, "");

        writer.WriteBool("hardcore", this.LevelData.Hardcore);
        writer.WriteBool("MapFeatures", this.LevelData.MapFeatures);
        writer.WriteBool("raining", this.LevelData.Raining);
        writer.WriteBool("thundering", this.LevelData.Thundering);

        writer.WriteInt("GameType", (int)this.LevelData.DefaultGamemode);
        writer.WriteInt("generatorVersion", this.LevelData.GeneratorVersion);
        writer.WriteInt("rainTime", this.LevelData.RainTime);
        writer.WriteInt("SpawnX", (int)LevelData.SpawnPosition.X);
        writer.WriteInt("SpawnY", (int)LevelData.SpawnPosition.Y);
        writer.WriteInt("SpawnZ", (int)LevelData.SpawnPosition.Z);
        writer.WriteInt("thunderTime", this.LevelData.ThunderTime);
        writer.WriteInt("version", this.LevelData.Version);

        writer.WriteLong("LastPlayed", DateTimeOffset.Now.ToUnixTimeMilliseconds());
        writer.WriteLong("RandomSeed", this.LevelData.RandomSeed);
        writer.WriteLong("Time", this.Time);

        //this.WriteWorldGenSettings(writer);

        writer.WriteString("generatorName", this.Generator.Id);
        writer.WriteString("LevelName", this.Name);

        writer.EndCompound();

        await writer.TryFinishAsync();
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

        var region = new Region(regionX, regionZ, this.FolderPath);
        if (await region.InitAsync())
        {
            _ = Task.Run(() => region.BeginTickAsync(this.Server._cancelTokenSource.Token));
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

    public async Task ScheduleBlockUpdateAsync(BlockUpdate blockUpdate)
    {
        blockUpdate.Block ??= await GetBlockAsync(blockUpdate.position);
        (int chunkX, int chunkZ) = blockUpdate.position.ToChunkCoord();
        Region? region = GetRegionForChunk(chunkX, chunkZ);
        region?.AddBlockUpdate(blockUpdate);
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
            Region region = GetRegionForChunk(job.x, job.z) ?? await LoadRegionByChunkAsync(job.x, job.z);

            var (x, z) = (NumericsHelper.Modulo(job.x, Region.cubicRegionSize), NumericsHelper.Modulo(job.z, Region.cubicRegionSize));

            Chunk c = await region.GetChunkAsync(x, z);
            if (c is null)
            {
                c = new Chunk(job.x, job.z)
                {
                    isGenerated = false // Not necessary; just being explicit.
                };
                // Set chunk now so that it no longer comes back as null. #threadlyfe
                region.SetChunk(c);
            }
            c = await Generator.GenerateChunkAsync(job.x, job.z, c);
            region.SetChunk(c);
            await worldLight.ProcessSkyLightForChunk(c);
        });
    }

    public Task FlushRegionsAsync() => Task.WhenAll(Regions.Select(pair => pair.Value.FlushAsync()));

    public IEntity SpawnFallingBlock(VectorF position, Material mat)
    {
        // offset position so it spawns in the right spot
        position.X += 0.5f;
        position.Z += 0.5f;
        FallingBlock entity = new(this)
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

            await this.Server.QueueBroadcastPacketAsync(new SpawnEntityPacket
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

        this.TryAddEntity(entity);

        return entity;
    }

    public void RegisterDimension(DimensionCodec codec, string? worldGeneratorId = null)
    {
        if (this.dimensions.ContainsKey(codec.Name))
            throw new ArgumentException($"World already contains dimension with name: {codec.Name}");

        if (!this.Server.WorldGenerators.TryGetValue(worldGeneratorId ?? codec.Name.TrimResourceTag(true), out var generatorType))
            throw new ArgumentException($"Failed to find generator with id: {worldGeneratorId}.");

        var dimensionWorld = new World(codec.Name.TrimResourceTag(true), this.Server, this.Seed, generatorType);

        dimensionWorld.Init(codec, this.Name);

        this.dimensions.Add(codec.Name, dimensionWorld);
    }

    public Task SpawnExperienceOrbs(VectorF position, short count = 1) =>
        this.Server.QueueBroadcastPacketAsync(new SpawnExperienceOrbPacket(count, position));

    /// <summary>
    /// 
    /// </summary>
    /// <param name="worldLoc"></param>
    /// <returns>Whether to update neighbor blocks.</returns>
    internal async Task<bool> HandleBlockUpdateAsync(BlockUpdate bu)
    {
        if (bu.Block is not Block block)
            return false;

        // Todo: this better
        if (TagsRegistry.Blocks.GravityAffected.Entries.Contains(block.StateId))
            return await BlockUpdates.HandleFallingBlock(bu);

        if (block.IsFluid)
            return await BlockUpdates.HandleLiquidPhysicsAsync(bu);

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
            this.FolderPath = Path.Combine(Path.Combine(this.Server.ServerFolderPath, "worlds"), this.Name);
            this.LevelDataFilePath = Path.Combine(this.FolderPath, "level.dat");
            this.PlayerDataPath = Path.Combine(this.FolderPath, "playerdata");
        }
        else
        {
            this.FolderPath = Path.Combine(Path.Combine(this.Server.ServerFolderPath, "worlds"), parentWorldName, this.Name);
            this.LevelDataFilePath = Path.Combine(Path.Combine(this.Server.ServerFolderPath, "worlds"), parentWorldName, "level.dat");
            this.PlayerDataPath = Path.Combine(Path.Combine(this.Server.ServerFolderPath, "worlds"), parentWorldName, "playerdata");
        }

        this.ParentWorldName = parentWorldName;
        this.DimensionName = codec.Name;

        //TODO configure all dim data
        this.LevelData = new Level
        {
            Time = codec.Element.FixedTime ?? 0,
            DefaultGamemode = Gamemode.Survival,
            GeneratorName = this.Generator.Id
        };

        Directory.CreateDirectory(this.FolderPath);
        Directory.CreateDirectory(this.PlayerDataPath);

        this.initialized = true;
    }

    internal async Task GenerateWorldAsync(bool setWorldSpawn = false)
    {
        if (!this.initialized)
            throw new InvalidOperationException("World hasn't been initialized please call World.Init() before trying to generate the world.");

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

        if (setWorldSpawn)
            await SetWorldSpawnAsync();
    }

    internal async Task SetWorldSpawnAsync()
    {
        if (LevelData.SpawnPosition.Y != 0) { return; }

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
                                this.LevelData.SpawnPosition = worldPos;
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

        Region? region = this.GetRegionForChunk(chunkX, chunkZ);

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
        if (!Registry.TryGetDimensionCodec(this.DimensionName, out var codec))
            return;

        var dimensionsCompound = new NbtCompound("dimensions")
        {
            new NbtCompound(codec.Name)
            {
                new NbtTag<string>("type", codec.Name),
            }
        };

        foreach (var (id, _) in this.dimensions)
        {
            Registry.TryGetDimensionCodec(id, out var childDimensionCodec);

            dimensionsCompound.Add(new NbtCompound(childDimensionCodec.Name)
            {
                new NbtTag<string>("type", childDimensionCodec.Name),
            });
        }

        var worldGenSettingsCompound = new NbtCompound("WorldGenSettings")
        {
            //THE SEED SHOULD BE NUMERICAL
            new NbtTag<string>("seed", this.Seed),

            dimensionsCompound
        };

        writer.WriteTag(worldGenSettingsCompound);
    }
}
