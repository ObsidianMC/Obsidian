using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.Blocks;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Nbt.Tags;
using Obsidian.Net.Packets.Play.Client;
using Obsidian.Util;
using Obsidian.Util.Extensions;
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

        public ConcurrentDictionary<Guid, Player> Players { get; private set; } = new ConcurrentDictionary<Guid, Player>();

        public WorldGenerator Generator { get; internal set; }

        public Server Server { get; }

        public ConcurrentDictionary<long, Region> Regions { get; private set; } = new ConcurrentDictionary<long, Region>();

        public string Name { get; }
        public bool Loaded { get; private set; }

        public long Time => Data.Time;
        public Gamemode GameType => (Gamemode)Data.GameType;

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

        public int TotalLoadedEntities() => this.Regions.Select(x => x.Value).Sum(e => e.Entities.Count);

        public async Task UpdateClientChunksAsync(Client c, bool forcereload = false)
        {
            if (forcereload)
            {
                foreach (var chunkLoc in c.LoadedChunks)
                {
                    await c.UnloadChunkAsync(chunkLoc.Item1, chunkLoc.Item2);
                }
                c.LoadedChunks = new List<(int, int)>();
            }

            List<(int, int)> clientNeededChunks = new List<(int, int)>();
            List<(int, int)> clientUnneededChunks = new List<(int, int)>(c.LoadedChunks);
            
            (int playerChunkX, int playerChunkZ) = c.Player.Location.ToChunkCoord();
            (int lastPlayerChunkX, int lastPlayerChunkZ) = c.Player.LastLocation.ToChunkCoord();

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

            clientNeededChunks.ForEach(async chunkLoc => await c.SendChunkAsync(this.GetChunk(chunkLoc.Item1, chunkLoc.Item2)));
            c.LoadedChunks.AddRange(clientNeededChunks);
            
            clientUnneededChunks.ForEach(async chunkLoc => {
                await c.UnloadChunkAsync(chunkLoc.Item1, chunkLoc.Item2);
                c.LoadedChunks.Remove(chunkLoc);
                });

            if (!(playerChunkX == lastPlayerChunkX && playerChunkZ == lastPlayerChunkZ))
            {
                await c.SendPacketAsync(new UpdateViewPosition(playerChunkX, playerChunkZ));
            }
        }


        public async Task ResendBaseChunksAsync(int distance, int oldx, int oldz, int x, int z, Client c, bool unload = true)
        {
            var dist = distance + 3; // for genarator gaps
            // unload old chunks
            if (unload)
            {
                for (int cx = oldx - dist; cx < oldx + dist; cx++)
                {
                    for (int cz = oldz - dist; cz < oldz + dist; cz++)
                    {
                        await c.UnloadChunkAsync(cx, cz);
                    }
                }
            }

            // load new chunks
            var chunksToGen = new List<Position>();

            for (int cx = (x - dist); cx < (x + dist); cx++)
            {
                for (int cz = z - dist; cz < z + dist; cz++)
                {
                    var chk = GetChunk(cx, cz);
                    if (chk is null)
                    {
                        chunksToGen.Add(new Position(cx, 0, cz));
                    }
                    else
                    {
                        await c.SendChunkAsync(chk);
                    }
                }
            }

            if (chunksToGen.Count != 0)
            {
                var chunks = GenerateChunks(chunksToGen);
                foreach (var chunk in chunks)
                {
                    await c.SendChunkAsync(chunk);
                }
            }

            await c.SendPacketAsync(new UpdateViewPosition(x, z));

            c.Logger.LogDebug($"loaded base chunks for {c.Player.Username} {x - dist} until {x + dist}");
        }
        public async Task ResendBaseChunksAsync(Client c)
        {
            await UpdateClientChunksAsync(c, true);
        }

        public async Task<bool> DestroyEntityAsync(Entity entity)
        {
            var destroyed = new DestroyEntities { Count = 1 };
            destroyed.AddEntity(entity);

            await this.Server.BroadcastPacketAsync(destroyed);

            var (chunkX, chunkZ) = entity.Location.ToChunkCoord();

            var region = this.GetRegion(chunkX, chunkZ);

            if (region is null)
                throw new InvalidOperationException("Region is null this wasn't supposed to happen.");

            return region.Entities.TryRemove(entity.EntityId, out _);
        }

        public Region GetRegion(int chunkX, int chunkZ)
        {
            long value = Helpers.IntsToLong(chunkX >> Region.CUBIC_REGION_SIZE_SHIFT, chunkZ >> Region.CUBIC_REGION_SIZE_SHIFT);

            return this.Regions.SingleOrDefault(x => x.Key == value).Value;
        }

        public Region GetRegion(Position location)
        {
            var (chunkX, chunkZ) = location.ToChunkCoord();

            return this.GetRegion(chunkX, chunkZ);
        }

        public Chunk GetChunk(int chunkX, int chunkZ)
        {
            if (this.Generator.GetType() == typeof(Obsidian.WorldData.Generators.SuperflatGenerator))
            {
                return this.Generator.GenerateChunk(chunkX, chunkZ);
            }
            var region = this.GetRegion(chunkX, chunkZ) ?? LoadRegion(chunkX, chunkZ);

            var index = (Helpers.Modulo(chunkX, Region.CUBIC_REGION_SIZE), Helpers.Modulo(chunkZ, Region.CUBIC_REGION_SIZE));
            var chunk = region.LoadedChunks[index.Item1, index.Item2];
            if (chunk is null) 
            {
                chunk = Generator.GenerateChunk(chunkX, chunkZ);                
                region.LoadedChunks[index.Item1, index.Item2] = chunk;
            }
            return chunk;
        }

        public Chunk GetChunk(Position worldLocation) => this.GetChunk((int)worldLocation.X.ToChunkCoord(), (int)worldLocation.Z.ToChunkCoord());

        public Block GetBlock(int x, int y, int z)
        {
            var chunk = this.GetChunk(x.ToChunkCoord(), z.ToChunkCoord());

            return chunk.GetBlock(x, y, z);
        }

        public Block GetBlock(Position location) => this.GetBlock((int)location.X, (int)location.Y, (int)location.Z);

        public void SetBlock(int x, int y, int z, Block block)
        {
            int chunkX = x.ToChunkCoord(), chunkZ = z.ToChunkCoord();

            long value = Helpers.IntsToLong(chunkX >> Region.CUBIC_REGION_SIZE_SHIFT, chunkZ >> Region.CUBIC_REGION_SIZE_SHIFT);

            this.Regions[value].LoadedChunks[chunkX, chunkZ].SetBlock(x, y, z, block);
        }

        public void SetBlock(Position location, Block block) => this.SetBlock((int)location.X, (int)location.Y, (int)location.Z, block);

        public IEnumerable<Entity> GetEntitiesNear(Position location, double distance = 10)
        {
            var (chunkX, chunkZ) = location.ToChunkCoord();

            var region = this.GetRegion(chunkX, chunkZ);

            if (region is null)
                return new List<Entity>();

            return region.Entities.Select(x => x.Value).Where(x => Position.DistanceTo(location, x.Location) <= distance);
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
            for (var rx = -1; rx < 1; rx++)
            {
                for (var rz = -1; rz < 1; rz++)
                {
                    GenerateRegion(rx, rz);
                }
            }

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
                XpP = playercompound["XpP"].FloatValue
                // TODO: NBTCompound(inventory), NBTList(Motion), NBTList(Pos), NBTList(Rotation)
            };
            this.Players.TryAdd(uuid, player);
        }

        public void UnloadPlayer(Guid uuid)
        {
            // TODO save changed data to file [uuid].dat
            this.Players.TryRemove(uuid, out Player player);
        }
        #endregion

        public Region LoadRegion(int chunkX, int chunkZ)
        {
            int regionX = chunkX >> Region.CUBIC_REGION_SIZE_SHIFT, regionZ = chunkZ >> Region.CUBIC_REGION_SIZE_SHIFT;
            return GenerateRegion(regionX, regionZ);
        }

        public Region GenerateRegion(int regionX, int regionZ)
        {
            this.Server.Logger.LogInformation($"Loading region {regionX}, {regionZ}");
            long value = Helpers.IntsToLong(regionX, regionZ);

            if (this.Regions.ContainsKey(value))
                return this.Regions[value];

            var region = new Region(regionX, regionZ, Path.Join(Server.ServerFolderPath, Name));

            _ = Task.Run(() => region.BeginTickAsync(this.Server.cts.Token));

            this.Regions.TryAdd(value, region);
            return region;
        }

        public List<Chunk> GenerateChunks(List<Position> chunkLocs, Region region = null)
        {
            ConcurrentBag<Chunk> chunks = new ConcurrentBag<Chunk>();
            Parallel.ForEach(chunkLocs, (loc) =>
            {
                var c = Generator.GenerateChunk((int)loc.X, (int)loc.Z);
                chunks.Add(c);
            });
            return chunks.ToList();
        }

        internal void Init(WorldGenerator gen)
        {
            // Make world directory
            Directory.CreateDirectory(Path.Join(Server.ServerFolderPath, Name));
            this.Generator = gen;
            GenerateWorld();
            SetWorldSpawn();
            foreach (var r in this.Regions.Values) {  r.Flush(); }
        }

        internal void GenerateWorld()
        {
            this.Server.Logger.LogInformation("Generating world...");
            for (int x = -Region.CUBIC_REGION_SIZE; x < Region.CUBIC_REGION_SIZE; x++)
            {
                for (int z = -Region.CUBIC_REGION_SIZE; z < Region.CUBIC_REGION_SIZE; z++)
                {
                    GetChunk(x, z);
                }
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
                            if (by > 58 && (block.Type == Materials.GrassBlock || block.Type == Materials.Sand))
                            {
                                Data.SpawnX = bx;
                                Data.SpawnY = by + 1;
                                Data.SpawnY = by+2;
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
            var (chunkX, chunkZ) = entity.Location.ToChunkCoord();

            var region = this.GetRegion(chunkX, chunkZ);

            if (region is null)
                throw new InvalidOperationException("Region is null this wasn't supposed to happen.");

            return region.Entities.TryAdd(entity.EntityId, entity);
        }
    }

    public enum WorldType
    {
        Default,
        Flat,
        LargeBiomes,
        Amplified
    }

    public enum Dimension : int
    {
        Nether = -1,

        Overworld,

        End
    }
}
