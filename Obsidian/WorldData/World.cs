using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.Blocks;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Nbt.Tags;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Utilities;
using Obsidian.Utilities.Extensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            var worldDir = Path.Join(Server.ServerFolderPath, Name);
            var DataPath = Path.Combine(worldDir, "level.dat");
            if (!File.Exists(DataPath)) { return false; }

            var DataFile = new NbtFile();
            DataFile.LoadFromFile(DataPath);

            var levelcompound = DataFile.RootTag;
            this.Data = new Level()
            {
                Hardcore = levelcompound["hardcore"].ByteValue == 1, // lel lazy bool conversion I guess
                MapFeatures = levelcompound["MapFeatures"].ByteValue == 1,
                Raining = levelcompound["raining"].ByteValue == 1,
                Thundering = levelcompound["thundering"].ByteValue == 1,
                GameType = (Gamemode)levelcompound["GameType"].IntValue,
                GeneratorVersion = levelcompound["generatorVersion"].IntValue,
                RainTime = levelcompound["rainTime"].IntValue,
                SpawnX = levelcompound["SpawnX"].IntValue,
                SpawnY = levelcompound["SpawnY"].IntValue,
                SpawnZ = levelcompound["SpawnZ"].IntValue,
                ThunderTime = levelcompound["thunderTime"].IntValue,
                Version = levelcompound["version"].IntValue,
                LastPlayed = levelcompound["LastPlayed"].LongValue,
                RandomSeed = levelcompound["RandomSeed"].LongValue,
                Time = levelcompound["Time"].LongValue,
                GeneratorName = levelcompound["generatorName"].StringValue,
                LevelName = levelcompound["LevelName"].StringValue
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
            var worldFile = Path.Join(Server.ServerFolderPath, Name, "level.dat");
            var dataFile = new NbtFile();
            var levelCompound = new NbtCompound("Data")
            {
                new NbtByte("hardcore", 1),
                new NbtByte("MapFeatures", 1),
                new NbtByte("raining", 0),
                new NbtByte("thundering", 0),
                new NbtInt("GameType", (int)Gamemode.Creative),
                new NbtInt("generatorVersion", 1),
                new NbtInt("rainTime", 0),
                new NbtInt("SpawnX", Data.SpawnX),
                new NbtInt("SpawnY", Data.SpawnY),
                new NbtInt("SpawnZ", Data.SpawnZ),
                new NbtInt("thunderTime", 0),
                new NbtInt("version", 19133),
                new NbtLong("LastPlayed", DateTimeOffset.Now.ToUnixTimeMilliseconds()),
                new NbtLong("RandomSeed", 1),
                new NbtLong("Time", 0),
                new NbtString("generatorName", Generator.Id),
                new NbtString("LevelName", Name)
            };

            dataFile.RootTag = levelCompound;
            dataFile.SaveToFile(worldFile, NbtCompression.GZip);
        }

        public void LoadPlayer(Guid uuid)
        {
            var playerfile = Path.Combine(Server.ServerFolderPath, Name, "players", $"{uuid}.dat");

            var PFile = new NbtFile();
            PFile.LoadFromFile(playerfile);
            var playercompound = PFile.RootTag;
            // filenames are player UUIDs. ???
            var player = new Player(uuid, Path.GetFileNameWithoutExtension(playerfile), null)//TODO: changes
            {
                OnGround = playercompound["OnGround"].ByteValue == 1,
                Sleeping = playercompound["Sleeping"].ByteValue == 1,
                Air = playercompound["Air"].ShortValue,
                AttackTime = playercompound["AttackTime"].ShortValue,
                DeathTime = playercompound["DeathTime"].ShortValue,
                //Fire = playercompound["Fire"].ShortValue,
                Health = playercompound["Health"].ShortValue,
                HurtTime = playercompound["HurtTime"].ShortValue,
                SleepTimer = playercompound["SleepTimer"].ShortValue,
                Dimension = playercompound["Dimension"].IntValue,
                FoodLevel = playercompound["foodLevel"].IntValue,
                FoodTickTimer = playercompound["foodTickTimer"].IntValue,
                Gamemode = (Gamemode)playercompound["playerGameType"].IntValue,
                XpLevel = playercompound["XpLevel"].IntValue,
                XpTotal = playercompound["XpTotal"].IntValue,
                FallDistance = playercompound["FallDistance"].FloatValue,
                FoodExhastionLevel = playercompound["foodExhastionLevel"].FloatValue,
                FoodSaturationLevel = playercompound["foodSaturationLevel"].FloatValue,
                Score = playercompound["XpP"].IntValue
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
                var index = (x: NumericsHelper.Modulo(c.X, Region.cubicRegionSize), z: NumericsHelper.Modulo(c.Z, Region.cubicRegionSize));
                region.LoadedChunks[index.x, index.z] = c;
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
            this.Server.Logger.LogInformation("Generating world...");
            for (int x = -Region.cubicRegionSize; x < Region.cubicRegionSize; x++)
            {
                for (int z = -Region.cubicRegionSize; z < Region.cubicRegionSize; z++)
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
