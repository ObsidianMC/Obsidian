using Microsoft.Extensions.Logging;
using Obsidian.Blocks;
using Obsidian.ChunkData;
using Obsidian.Concurrency;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Client;
using Obsidian.PlayerData;
using Obsidian.Util;
using Obsidian.Util.Collection;
using Obsidian.Util.DataTypes;
using Obsidian.Util.Extensions;
using Obsidian.Util.Registry;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Obsidian.WorldData
{
    public class World
    {
        public Level Data { get; internal set; }

        public ConcurrentDictionary<Guid, Player> Players { get; private set; } = new ConcurrentDictionary<Guid, Player>();

        public WorldGenerator Generator { get; internal set; }

        public Server Server { get; }

        public ConcurrentDictionary<long, Region> Regions { get; private set; } = new ConcurrentDictionary<long, Region>();

        internal string Name { get; }
        internal bool Loaded { get; set; }

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

            this.Init();
        }

        public int TotalLoadedEntities() => this.Regions.Select(x => x.Value).Sum(e => e.Entities.Count);

        public async Task UpdateChunksForClientAsync(Client c)
        {
            int dist = c.ClientSettings?.ViewDistance ?? 8;

            (int oldChunkX, int oldChunkZ) = c.Player.LastLocation.ToChunkCoord();

            (int newChunkX, int newChunkZ) = c.Player.Location.ToChunkCoord();


            if (Math.Abs(newChunkZ - oldChunkZ) > dist || Math.Abs(newChunkX - oldChunkX) > dist)
            {
                // This is a teleport!!!1 Send full new chunk data.
                await this.ResendBaseChunksAsync(dist, oldChunkX, oldChunkZ, newChunkX, newChunkZ, c);
                return;
            }

            if (newChunkX > oldChunkX)
            {
                for (int i = (newChunkZ - dist); i < (newChunkZ + dist); i++)
                {
                    // TODO: implement
                    //                    await c.UnloadChunkAsync((chunkx - dist), i);

                    //await c.SendChunkAsync(this.GetChunk((chunkx + dist), i, c));
                }
            }

            if (newChunkX < oldChunkZ)
            {
                for (int i = (newChunkZ - dist); i < (newChunkZ + dist); i++)
                {
                    // TODO: implement
                    //await c.UnloadChunkAsync((chunkx + dist), i);

                    // await c.SendChunkAsync(this.GetChunk((chunkx - dist), i, c));
                }
            }

            if (newChunkZ > oldChunkZ)
            {
                for (int i = (newChunkX - dist); i < (newChunkX + dist); i++)
                {
                    // TODO: implement
                    //await c.UnloadChunkAsync(i, (chunkz - dist));

                    //await c.SendChunkAsync(this.GetChunk(i, (chunkz + dist), c));
                }
            }

            if (newChunkZ < oldChunkZ)
            {
                for (int i = (newChunkX - dist); i < (newChunkX + dist); i++)
                {
                    // TODO: implement
                    //await c.UnloadChunkAsync(i, (chunkz + dist));

                    //await c.SendChunkAsync(this.GetChunk(i, (chunkz - dist), c));
                }
            }
        }

        public async Task ResendBaseChunksAsync(int dist, int oldx, int oldz, int x, int z, Client c)
        {
            // unload old chunks
            //for (int cx = oldx - dist; cx < oldx + dist; cx++)
            //{
            //    for (int cz = oldz - dist; cz < oldz + dist; cz++)
            //    {
            //        await c.UnloadChunkAsync(cx, cz);
            //    }
            //}

            // load new chunks
            var chunksToGen = new List<Position>();

            for (int cx = (x - dist); cx < (x + dist); cx++)
            {
                for (int cz = z - dist; cz < z + dist; cz++)
                {
                    var chk = GetChunk(cx * 16, cz * 16);
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

/*
            chk = this.Generator.GenerateChunk(cx, cz);
            for (int i = 0; i < 1024; i++)
                chk.BiomeContainer.Biomes.Add(127);
*/
            c.Logger.LogDebug($"loaded base chunks for {c.Player.Username} {x - dist} until {x + dist}");
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
            long value = Helpers.IntsToLong(chunkX >> 5, chunkZ >> 5);

            return this.Regions.SingleOrDefault(x => x.Key == value).Value;
        }

        public Region GetRegion(Position location)
        {
            var (chunkX, chunkZ) = location.ToChunkCoord();

            return this.GetRegion(chunkX, chunkZ);
        }

        public Chunk GetChunk(int x, int z)
        {
            int chunkX = x.ToChunkCoord(), chunkZ = z.ToChunkCoord();

            var region = this.GetRegion(chunkX, chunkZ);

            if (region == null)
                return null;

            var chunk = region.LoadedChunks[chunkX, chunkZ];
            return chunk;
        }

        public Chunk GetChunk(Position location) => this.GetChunk((int)location.X, (int)location.Z);

        public Block GetBlock(int x, int y, int z)
        {
            var chunk = this.GetChunk(x, z);

            return chunk.GetBlock(x, y, z);
        }

        public Block GetBlock(Position location) => this.GetBlock((int)location.X, (int)location.Y, (int)location.Z);

        public void SetBlock(int x, int y, int z, Block block)
        {
            int chunkX = x.ToChunkCoord(), chunkZ = z.ToChunkCoord();

            long value = Helpers.IntsToLong(chunkX >> 5, chunkZ >> 5);

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
        public void Load()
        {
            var DataPath = Path.Combine(Name, "level.dat");

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

            this.Loaded = true;
        }

        public void Save()
        {

        }

        public void LoadPlayer(Guid uuid)
        {
            var playerfile = Path.Combine(Name, "players", $"{uuid}.dat");

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

        public Region GenerateRegion(Chunk chunk)
        {
            int regionX = chunk.X >> 5, regionZ = chunk.Z >> 5;

            long value = Helpers.IntsToLong(regionX, regionZ);

            this.Server.Logger.LogInformation($"Generating region {regionX}, {regionZ}");

            var region = new Region(regionX, regionZ);

            _ = Task.Run(() => region.BeginTickAsync(this.Server.cts.Token));

            if (this.Regions.ContainsKey(value))
                return this.Regions[value];

            region.LoadedChunks[chunk.X, chunk.Z] = chunk;

            this.Regions.TryAdd(value, region);

            return region;
        }

        public Region GenerateRegion(int regionX, int regionZ)
        {
            long value = Helpers.IntsToLong(regionX, regionZ);

            this.Server.Logger.LogInformation($"Generating region {regionX}, {regionZ}");

            var region = new Region(regionX, regionZ);

            _ = Task.Run(() => region.BeginTickAsync(this.Server.cts.Token));

            if (this.Regions.ContainsKey(value))
                return this.Regions[value];

            List<Position> chunksToGen = new List<Position>();
            for (int x=0; x<16; x++)
            {
                for (int z=0; z<16; z++)
                {
                    int cx = (regionX * 16) + x;
                    int cz = (regionZ * 16) + z;
                    chunksToGen.Add(new Position(cx, 0, cz));
                }
            }
            var chunks = GenerateChunks(chunksToGen);

            foreach (Chunk chunk in chunks)
            {
                region.LoadedChunks[chunk.X, chunk.Z] = chunk;
            }

            this.Regions.TryAdd(value, region);

            return region;
        }

        public List<Chunk> GenerateChunks(List<Position> chunkLocs)
        {
            ConcurrentBag<Chunk> chunks = new ConcurrentBag<Chunk>();
            Parallel.ForEach(chunkLocs, (loc) =>
            {
                this.Server.Logger.LogInformation($"Generating chunk {loc}");
                var c = Generator.GenerateChunk((int)loc.X, (int)loc.Y);
                for (int i = 0; i < 1024; i++)
                    c.BiomeContainer.Biomes.Add(127);
                chunks.Add(c);
            });
            return chunks.ToList();
        }

        internal void Init()
        {

        }

        internal void GenerateWorld()
        {
            this.Server.Logger.LogInformation("Generating world..");
            this.GenerateRegion(0, 0);

            /*var chunk = this.Generator.GenerateChunk(0, 0);

            for (int i = 0; i < 1024; i++)
                chunk.BiomeContainer.Biomes.Add(127);

            this.GenerateRegion(chunk);
*/
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
