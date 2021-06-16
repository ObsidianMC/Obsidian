using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.Blocks;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Obsidian.WorldData
{
    public class World : IWorld
    {
        public Level Data { get; internal set; }

        public ConcurrentDictionary<Guid, Player> Players { get; private set; } = new();

        public WorldGenerator Generator { get; internal set; }

        public Server Server { get; }

        public ConcurrentDictionary<long, Region> Regions { get; private set; } = new();

        public ConcurrentQueue<(int, int)> ChunksToGen { get; private set; } = new();

        public ConcurrentQueue<(int, int)> RegionsToLoad { get; private set; } = new();

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
        }

        public int TotalLoadedEntities() => this.Regions.Values.Sum(e => e == null ? 0 : e.Entities.Count);

        public async Task UpdateClientChunksAsync(Client c, bool unloadAll = false)
        {
            if (unloadAll)
            {
                foreach (var chunkLoc in c.LoadedChunks)
                {
                    await c.UnloadChunkAsync(chunkLoc.Item1, chunkLoc.Item2);
                }
                c.LoadedChunks = new List<(int, int)>();
            }

            List<(int, int)> clientNeededChunks = new List<(int, int)>();
            List<(int, int)> clientUnneededChunks = new List<(int, int)>(c.LoadedChunks);

            (int playerChunkX, int playerChunkZ) = c.Player.Position.ToChunkCoord();
            (int lastPlayerChunkX, int lastPlayerChunkZ) = c.Player.LastPosition.ToChunkCoord();

            int dist = c.ClientSettings?.ViewDistance ?? 8;
            for (int x = playerChunkX - dist; x < playerChunkX + dist; x++)
                for (int z = playerChunkZ - dist; z < playerChunkZ + dist; z++)
                    clientNeededChunks.Add((x, z));

            clientUnneededChunks = clientUnneededChunks.Except(clientNeededChunks).ToList();
            clientNeededChunks = clientNeededChunks.Except(c.LoadedChunks).ToList();
            clientNeededChunks.Sort((chunk1, chunk2) =>
            {
                return Math.Abs(playerChunkX - chunk1.Item1) +
                Math.Abs(playerChunkZ - chunk1.Item2) <
                Math.Abs(playerChunkX - chunk2.Item1) +
                Math.Abs(playerChunkZ - chunk2.Item2) ? -1 : 1;
            });

            clientNeededChunks.ForEach(async chunkLoc =>
            {
                var chunk = this.GetChunk(chunkLoc.Item1, chunkLoc.Item2);
                if (chunk is not null)
                {
                    await c.SendChunkAsync(chunk);
                    c.LoadedChunks.Add((chunk.X, chunk.Z));
                }
            });

            clientUnneededChunks.ForEach(async chunkLoc =>
            {
                await c.UnloadChunkAsync(chunkLoc.Item1, chunkLoc.Item2);
                c.LoadedChunks.Remove(chunkLoc);
            });

            if (!(playerChunkX == lastPlayerChunkX && playerChunkZ == lastPlayerChunkZ))
            {
                c.SendPacket(new UpdateViewPosition(playerChunkX, playerChunkZ));
            }
        }
        public Task ResendBaseChunksAsync(Client c) => UpdateClientChunksAsync(c, true);

        public async Task<bool> DestroyEntityAsync(Entity entity)
        {
            var destroyed = new DestroyEntities();
            destroyed.AddEntity(entity);

            await this.Server.BroadcastPacketAsync(destroyed);

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
        /// If the Chunk doesn't exist, it will be scheduled for generation.
        /// </summary>
        /// <returns>Null if the region or chunk doesn't exist yet. Otherwise the chunk.</returns>
        public Chunk GetChunk(int chunkX, int chunkZ)
        {
            var region = this.GetRegionForChunk(chunkX, chunkZ);

            // region hasn't been loaded yet
            if (region is null)
            {
                var regionCoords = (chunkX >> Region.cubicRegionSizeShift, chunkZ >> Region.cubicRegionSizeShift);
                if (!RegionsToLoad.Contains(regionCoords))
                    RegionsToLoad.Enqueue(regionCoords);
                return null;
            }

            var index = (NumericsHelper.Modulo(chunkX, Region.cubicRegionSize), NumericsHelper.Modulo(chunkZ, Region.cubicRegionSize));
            var chunk = region.LoadedChunks[index.Item1, index.Item2];

            // chunk hasn't been generated yet
            if (chunk is null && !ChunksToGen.Contains((chunkX, chunkZ))) { ChunksToGen.Enqueue((chunkX, chunkZ)); }
            return chunk;
        }

        /// <summary>
        /// Gets a Chunk from a Region.
        /// If the Chunk doesn't exist, it will be scheduled for generation.
        /// </summary>
        /// <param name="worldLocation">World location of the chunk.</param>
        /// <returns>Null if the region or chunk doesn't exist yet. Otherwise the chunk.</returns>
        public Chunk GetChunk(Vector worldLocation) => this.GetChunk(worldLocation.X.ToChunkCoord(), worldLocation.Z.ToChunkCoord());

        public Block GetBlock(Vector location) => GetBlock(location.X, location.Y, location.Z);

        public Block GetBlock(int x, int y, int z)
        {
            var chunk = this.GetChunk(x.ToChunkCoord(), z.ToChunkCoord());

            return chunk is null ? Block.Air : chunk.GetBlock(x, y, z);
        }

        public void SetBlock(Vector location, Block block) => SetBlock(location.X, location.Y, location.Z, block);

        public void SetBlock(int x, int y, int z, Block block)
        {
            int chunkX = x.ToChunkCoord(), chunkZ = z.ToChunkCoord();

            long value = NumericsHelper.IntsToLong(chunkX >> Region.cubicRegionSizeShift, chunkZ >> Region.cubicRegionSizeShift);

            this.Regions[value].LoadedChunks[chunkX, chunkZ].SetBlock(x, y, z, block);
            this.Regions[value].IsDirty = true;
        }

        public void SetBlockMeta(int x, int y, int z, BlockMeta meta)
        {
            int chunkX = x.ToChunkCoord(), chunkZ = z.ToChunkCoord();

            long value = NumericsHelper.IntsToLong(chunkX >> Region.cubicRegionSizeShift, chunkZ >> Region.cubicRegionSizeShift);

            this.Regions[value].LoadedChunks[chunkX, chunkZ].SetBlockMeta(x, y, z, meta);
        }

        public void SetBlockMeta(Vector location, BlockMeta meta) => this.SetBlockMeta(location.X, location.Y, location.Z, meta);

        public BlockMeta GetBlockMeta(int x, int y, int z)
        {
            int chunkX = x.ToChunkCoord(), chunkZ = z.ToChunkCoord();

            long value = NumericsHelper.IntsToLong(chunkX >> Region.cubicRegionSizeShift, chunkZ >> Region.cubicRegionSizeShift);

            return this.Regions[value].LoadedChunks[chunkX, chunkZ].GetBlockMeta(x, y, z);
        }

        public BlockMeta GetBlockMeta(Vector location) => this.GetBlockMeta(location.X, location.Y, location.Z);

        public IEnumerable<Entity> GetEntitiesNear(VectorF location, float distance = 10f)
        {
            var (chunkX, chunkZ) = location.ToChunkCoord();

            var region = this.GetRegionForChunk(chunkX, chunkZ);

            if (region is null)
                return new List<Entity>();

            return region.Entities.Select(x => x.Value).Where(x => VectorF.Distance(location, x.Position) <= distance);
        }

        public bool AddPlayer(Player player) => this.Players.TryAdd(player.Uuid, player);

        public bool RemovePlayer(Player player) => this.Players.TryRemove(player.Uuid, out _);

        #region world loading/saving
        //TODO
        public bool Load()
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
                SpawnX = levelcompound.GetInt("SpawnX"),
                SpawnY = levelcompound.GetInt("SpawnY"),
                SpawnZ = levelcompound.GetInt("SpawnZ"),
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

            Server.Logger.LogInformation($"Loading spawn chunks into memory...");
            // spawn chunks are radius 12 from spawn. That's a lot for us... so let's do 4 instead.
            var radius = 4;
            (int X, int Z) spawnChunk = (this.Data.SpawnX.ToChunkCoord(), this.Data.SpawnZ.ToChunkCoord());
            for (var cx = spawnChunk.X - radius; cx < spawnChunk.X + radius; cx++)
                for (var cz = spawnChunk.Z - radius; cz < spawnChunk.Z + radius; cz++)
                    GetChunk(cx, cz);

            this.Generator = value;
            this.Loaded = true;
            return true;
        }

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
            writer.WriteInt("SpawnX", Data.SpawnX);
            writer.WriteInt("SpawnY", Data.SpawnY);
            writer.WriteInt("SpawnZ", Data.SpawnZ);
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

        public void LoadPlayer(Guid uuid)
        {
            var path = Path.Combine(Server.ServerFolderPath, Name, "players", $"{uuid}.dat");
            var playerFile = new FileInfo(path);

            var pfile = new NbtReader(playerFile.OpenRead(), NbtCompression.GZip);

            var playercompound = pfile.ReadNextTag() as NbtCompound;
            // filenames are player UUIDs. ???
            var player = new Player(uuid, Path.GetFileNameWithoutExtension(path), null)//TODO: changes
            {
                OnGround = playercompound.GetBool("OnGround"),
                Sleeping = playercompound.GetBool("Sleeping"),
                Air = playercompound.GetShort("Air"),
                AttackTime = playercompound.GetShort("AttackTime"),
                DeathTime = playercompound.GetShort("DeathTime"),
                //Fire = playercompound["Fire"].ShortValue,
                Health = playercompound.GetShort("Health"),
                HurtTime = playercompound.GetShort("HurtTime"),
                SleepTimer = playercompound.GetShort("SleepTimer"),
                Dimension = playercompound.GetInt("Dimension"),
                FoodLevel = playercompound.GetInt("foodLevel"),
                FoodTickTimer = playercompound.GetInt("foodTickTimer"),
                Gamemode = (Gamemode)playercompound.GetInt("playerGameType"),
                XpLevel = playercompound.GetInt("XpLevel"),
                XpTotal = playercompound.GetInt("XpTotal"),
                FallDistance = playercompound.GetFloat("FallDistance"),
                FoodExhastionLevel = playercompound.GetFloat("foodExhastionLevel"),
                FoodSaturationLevel = playercompound.GetFloat("foodSaturationLevel"),
                Score = playercompound.GetInt("XpP")
                // TODO: NBTCompound(inventory), NBTList(Motion), NBTList(Pos), NBTList(Rotation)
            };
            this.Players.TryAdd(uuid, player);
        }

        public void UnloadPlayer(Guid uuid)
        {
            // TODO save changed data to file [uuid].dat
            this.Players.TryRemove(uuid, out _);
        }
        #endregion

        public Region LoadRegionByChunk(int chunkX, int chunkZ)
        {
            int regionX = chunkX >> Region.cubicRegionSizeShift, regionZ = chunkZ >> Region.cubicRegionSizeShift;
            return LoadRegion(regionX, regionZ);
        }

        public Region LoadRegion(int regionX, int regionZ)
        {
            long value = NumericsHelper.IntsToLong(regionX, regionZ);
            this.Regions.TryAdd(value, null);
            if (this.Regions.ContainsKey(value))
                if (this.Regions[value] is not null)
                    return this.Regions[value];

            this.Server.Logger.LogInformation($"Loading region {regionX}, {regionZ}");
            var region = new Region(regionX, regionZ, Path.Join(Server.ServerFolderPath, Name));

            _ = Task.Run(() => region.BeginTickAsync(this.Server.cts.Token));

            this.Regions[value] = region;
            return region;
        }

        public void ManageChunks()
        {
            // Run this thread with high priority so as to prioritize chunk generation over the minecraft client.
            Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;

            // Load regions. Load no more than 2 at a time b/c it's an expensive operation.
            // Regions that are in the process of being loaded will appear in
            // this.Regions, but will be null.
            if (!RegionsToLoad.IsEmpty && Regions.Values.Count(r => r is null) < 2)
            {
                if (RegionsToLoad.TryDequeue(out var job))
                {
                    if (!this.Regions.ContainsKey(NumericsHelper.IntsToLong(job.Item1, job.Item2))) // Sanity check
                        LoadRegion(job.Item1, job.Item2);
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
                Chunk c = Generator.GenerateChunk(job.x, job.z);
                var (x, z) = (NumericsHelper.Modulo(c.X, Region.cubicRegionSize), NumericsHelper.Modulo(c.Z, Region.cubicRegionSize));
                region.LoadedChunks[x, z] = c;
            });
        }

        internal void Init(WorldGenerator gen)
        {
            // Make world directory
            Directory.CreateDirectory(Path.Join(Server.ServerFolderPath, Name));
            this.Generator = gen;
            GenerateWorld();
            foreach (var r in this.Regions.Values) { r.Flush(); }
            SetWorldSpawn();
        }

        internal void GenerateWorld()
        {
            this.Server.Logger.LogInformation($"Generating world... (Config pregeneration size is {Server.Config.PregenerateChunkRange})");
            int pregenerationRange = Server.Config.PregenerateChunkRange;

            for (int x = -pregenerationRange; x < pregenerationRange; x++)
            {
                for (int z = -pregenerationRange; z < pregenerationRange; z++)
                {
                    if (!ChunksToGen.Contains((x, z)))
                        ChunksToGen.Enqueue((x, z));
                    var regionCoords = (x >> Region.cubicRegionSizeShift, z >> Region.cubicRegionSizeShift);
                    if (!RegionsToLoad.Contains(regionCoords))
                        RegionsToLoad.Enqueue(regionCoords);
                }
            }
            while (!ChunksToGen.IsEmpty)
            {
                ManageChunks();
                Server.Logger.LogInformation($"Chunk Queue length: {ChunksToGen.Count}");
            }
        }

        internal void SetWorldSpawn()
        {
            if (Data.SpawnY != 0) { return; }
            Data.SpawnX = Data.SpawnZ = 0;
            Data.SpawnY = 128;
            foreach (var r in Regions.Values)
            {
                foreach (var c in r.LoadedChunks)
                {
                    for (int bx = 0; bx < 16; bx++)
                    {
                        for (int bz = 0; bz < 16; bz++)
                        {
                            var by = c.Heightmaps[ChunkData.HeightmapType.WorldSurface].GetHeight(bx, bz);
                            Block block = c.GetBlock(bx, by, bz);
                            if (by > 58 && (block.Is(Material.GrassBlock) || block.Is(Material.Sand)))
                            {
                                Data.SpawnX = bx;
                                Data.SpawnY = by + 2;
                                Data.SpawnZ = bz;
                                this.Server.Logger.LogInformation($"World Spawn set to {bx} {by} {bz}");
                                return;
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
            await this.Server.BroadcastPacketAsync(new SpawnExperienceOrb(count, position));
        }

        public async Task SpawnPainting(Vector position, Painting painting, PaintingDirection direction, Guid uuid = default)
        {
            if (uuid == Guid.Empty) uuid = Guid.NewGuid();
            await this.Server.BroadcastPacketAsync(new SpawnPainting(uuid, painting.Id, position, direction));
        }
    }
}
