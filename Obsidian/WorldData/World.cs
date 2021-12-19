﻿using Microsoft.Extensions.Logging;
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

    public ConcurrentQueue<(int X, int Z)> RegionsToLoad { get; private set; } = new();

    public ConcurrentBag<(int X, int Z)> SpawnChunks { get; private set; } = new();

    public string Name { get; }

    public bool Loaded { get; private set; }

    public long Time => Data.Time;

    public Gamemode GameType => Data.GameType;

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
    }

    public int TotalLoadedEntities() => this.Regions.Values.Sum(e => e == null ? 0 : e.Entities.Count);

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
            var chunk = this.GetChunk(chunkLoc.X, chunkLoc.Z);
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
    public Chunk GetChunk(int chunkX, int chunkZ, bool scheduleGeneration = true)
    {
        var region = this.GetRegionForChunk(chunkX, chunkZ);

        if (region is null)
        {
            // region hasn't been loaded yet
            var (rX, rZ) = (chunkX >> Region.cubicRegionSizeShift, chunkZ >> Region.cubicRegionSizeShift);
            if (scheduleGeneration)
            {
                if (!RegionsToLoad.Contains((rX, rZ)))
                    RegionsToLoad.Enqueue((rX, rZ));
                return null;
            }
            // Can't wait for the region to be loaded b/c we want a partial chunk,
            // so just load it now and hold up execution.
            try
            {
                var task = LoadRegionAsync(rX, rZ);
                region = task.Result;
                Regions[NumericsHelper.IntsToLong(rX, rZ)] = region;
            }
            catch (Exception e)
            {
                Server.Logger.LogError(e.Message);
                GetChunk(chunkX, chunkZ, scheduleGeneration);
            }
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
    public Chunk GetChunk(Vector worldLocation, bool scheduleGeneration = true) => this.GetChunk(worldLocation.X.ToChunkCoord(), worldLocation.Z.ToChunkCoord(), scheduleGeneration);

    public Block? GetBlock(Vector location) => GetBlock(location.X, location.Y, location.Z);

    public Block? GetBlock(int x, int y, int z) => GetChunk(x.ToChunkCoord(), z.ToChunkCoord(), false)?.GetBlock(x, y, z);

    public int? GetWorldSurfaceHeight(int x, int z) => GetChunk(x.ToChunkCoord(), z.ToChunkCoord(), false)?
        .Heightmaps[ChunkData.HeightmapType.MotionBlocking]
        .GetHeight(NumericsHelper.Modulo(x, 16), NumericsHelper.Modulo(z, 16));

    public void SetBlock(int x, int y, int z, Block block) => SetBlock(new Vector(x, y, z), block);

    public void SetBlock(Vector location, Block block)
    {
        SetBlockUntracked(location.X, location.Y, location.Z, block);
        Server.BroadcastBlockChange(block, location);
    }

    public void SetBlock(int x, int y, int z, Block block, bool doBlockUpdate) => SetBlock(new Vector(x, y, z), block, doBlockUpdate);

    public void SetBlock(Vector location, Block block, bool doBlockUpdate)
    {
        SetBlockUntracked(location.X, location.Y, location.Z, block, doBlockUpdate);
        Server.BroadcastBlockChange(block, location);
    }

    public void SetBlockUntracked(Vector location, Block block, bool doBlockUpdate = false) => SetBlockUntracked(location.X, location.Y, location.Z, block, doBlockUpdate);

    public void SetBlockUntracked(int x, int y, int z, Block block, bool doBlockUpdate = false)
    {
        if (doBlockUpdate)
        {
            ScheduleBlockUpdate(new BlockUpdate(this, new Vector(x, y, z), block));
            BlockUpdateNeighbors(new BlockUpdate(this, new Vector(x, y, z), block));
        }
        GetChunk(x.ToChunkCoord(), z.ToChunkCoord(), false).SetBlock(x, y, z, block);
    }

    public void SetBlockMeta(int x, int y, int z, BlockMeta meta) => GetChunk(x.ToChunkCoord(), z.ToChunkCoord(), false).SetBlockMeta(x, y, z, meta);

    public void SetBlockMeta(Vector location, BlockMeta meta) => this.SetBlockMeta(location.X, location.Y, location.Z, meta);

    public BlockMeta? GetBlockMeta(int x, int y, int z) => GetChunk(x.ToChunkCoord(), z.ToChunkCoord())?.GetBlockMeta(x, y, z);

    public BlockMeta? GetBlockMeta(Vector location) => this.GetBlockMeta(location.X, location.Y, location.Z);

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

        Parallel.ForEach(SpawnChunks, (c) =>
        {
            GetChunk(c.X, c.Z);
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

    public async Task<Region> LoadRegionByChunkAsync(int chunkX, int chunkZ)
    {
        int regionX = chunkX >> Region.cubicRegionSizeShift, regionZ = chunkZ >> Region.cubicRegionSizeShift;
        return await LoadRegionAsync(regionX, regionZ);
    }

    public async Task<Region> LoadRegionAsync(int regionX, int regionZ)
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
        await region.InitAsync();

        _ = Task.Run(() => region.BeginTickAsync(this.Server.cts.Token));

        this.Regions[value] = region;

        return region;
    }

    public async Task UnloadRegionAsync(int regionX, int regionZ)
    {
        long value = NumericsHelper.IntsToLong(regionX, regionZ);
        if (Regions.TryRemove(value, out var r))
            await r.FlushAsync();
    }

    public void ScheduleBlockUpdate(BlockUpdate bu)
    {
        bu.block ??= GetBlock(bu.position);
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
        if (bu.block is null) { return false; }

        // Todo: this better
        if (Block.GravityAffected.Contains(bu.block.Value.Material))
        {
            return await BlockUpdates.HandleFallingBlock(bu);
        }

        if (bu.block.Value.IsFluid)
        {
            return await BlockUpdates.HandleLiquidPhysics(bu);
        }

        return false;
    }

    internal void BlockUpdateNeighbors(BlockUpdate bu)
    {
        bu.block = null;
        bu.delayCounter = bu.delay;
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

        ScheduleBlockUpdate(north);
        ScheduleBlockUpdate(south);
        ScheduleBlockUpdate(west);
        ScheduleBlockUpdate(east);
        ScheduleBlockUpdate(up);
        ScheduleBlockUpdate(down);
    }

    public async Task ManageChunksAsync()
    {
        // Load regions. Load no more than 4 at a time b/c it's an expensive operation.
        // Regions that are in the process of being loaded will appear in
        // this.Regions, but will be null.
        if (!RegionsToLoad.IsEmpty && Regions.Values.Count(r => r is null) < 4)
        {
            if (RegionsToLoad.TryDequeue(out var job))
            {
                if (!this.Regions.ContainsKey(NumericsHelper.IntsToLong(job.X, job.Z))) // Sanity check
                    await LoadRegionAsync(job.X, job.Z);
            }
        }

        if (ChunksToGen.IsEmpty) { return; }

        // Pull some jobs out of the queue
        var jobs = new List<(int x, int z)>();
        for (int a = 0; a < Environment.ProcessorCount; a++)
        {
            if (ChunksToGen.TryDequeue(out var job))
                jobs.Add(job);
        }

        Parallel.ForEach(jobs, (job) =>
        {
            Region region = GetRegionForChunk(job.x, job.z);
            if (region is null)
            {
                    // Region isn't ready. Try again later
                    ChunksToGen.Enqueue((job.x, job.z));
                return;
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
            Generator.GenerateChunk(job.x, job.z, this, c);
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
            EntityId = TotalLoadedEntities() + 1,
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
                EntityId = this.TotalLoadedEntities() + 1,
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
                EntityId = this.TotalLoadedEntities() + 1,
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
        SetWorldSpawn();
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

    internal void SetWorldSpawn()
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
                                        GetChunk(x, z);
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
