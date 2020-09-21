using Microsoft.Extensions.Logging;
using Obsidian.Blocks;
using Obsidian.ChunkData;
using Obsidian.Concurrency;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.PlayerData;
using Obsidian.Util.DataTypes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian.WorldData
{
    public class World
    {
        public Level Data { get; internal set; }

        public List<Player> Players { get; }

        public WorldGenerator Generator { get; internal set; }

        public Server Server { get; set; }

        // This one later comes back in the regions,
        // but might be easier for internal management purposes
        public List<object> Entities { get; }

        internal string folder { get; }
        internal bool Loaded { get; set; }

        public ConcurrentHashSet<Chunk> LoadedChunks { get; private set; } = new ConcurrentHashSet<Chunk>();

        public World(string folder, Server server)
        {
            this.Data = new Level
            {
                Time = 1200,
                Gametype = (int)Gamemode.Survival,
                GeneratorName = WorldType.Default.ToString()
            };

            this.Players = new List<Player>();

            this.Entities = new List<object>();
            this.folder = folder;
            this.Server = server;
        }

        public async Task UpdateChunksForClientAsync(Client c)
        {
            int dist = c.ClientSettings?.ViewDistance ?? 8;

            int oldchunkx = this.TransformToChunk(c.Player.LastPosition?.X ?? 0);
            int chunkx = this.TransformToChunk(c.Player.Position?.X ?? 0);

            int oldchunkz = this.TransformToChunk(c.Player.LastPosition?.Z ?? 0);
            int chunkz = this.TransformToChunk(c.Player.Position?.Z ?? 0);

            if (Math.Abs(chunkz - oldchunkz) > dist || Math.Abs(chunkx - oldchunkx) > dist)
            {
                // This is a teleport!!!1 Send full new chunk data.
                await this.ResendBaseChunksAsync(dist, oldchunkx, oldchunkz, chunkx, chunkz, c);
                return;
            }

            if (chunkx > oldchunkx)
            {
                for (int i = (chunkz - dist); i < (chunkz + dist); i++)
                {
                    // TODO: implement
                    //                    await c.UnloadChunkAsync((chunkx - dist), i);

                    //await c.SendChunkAsync(this.GetChunk((chunkx + dist), i, c));
                }
            }

            if (chunkx < oldchunkx)
            {
                for (int i = (chunkz - dist); i < (chunkz + dist); i++)
                {
                    // TODO: implement
                    //await c.UnloadChunkAsync((chunkx + dist), i);

                   // await c.SendChunkAsync(this.GetChunk((chunkx - dist), i, c));
                }
            }

            if (chunkz > oldchunkz)
            {
                for (int i = (chunkx - dist); i < (chunkx + dist); i++)
                {
                    // TODO: implement
                    //await c.UnloadChunkAsync(i, (chunkz - dist));

                    //await c.SendChunkAsync(this.GetChunk(i, (chunkz + dist), c));
                }
            }

            if (chunkz < oldchunkz)
            {
                for (int i = (chunkx - dist); i < (chunkx + dist); i++)
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
            for (int cx = (x - dist); cx < (x + dist); cx++)
            {
                for (int cz = z - dist; cz < z + dist; cz++)
                {
                    //await c.SendChunkAsync(this.GetChunk(cx, cz, c));
                }
            }

            c.Logger.LogDebug($"loaded base chunks for {c.Player.Username} {x - dist} until {x + dist}");
        }

        public Block GetBlock(int x, int y, int z)
        {
            var chunk = this.GetChunk(x >> 4, z >> 4);

            return chunk.GetBlock(x, y, z);
        }

        public Block GetBlock(Position location)
        {
            var chunk = this.GetChunk((int)location.X >> 4, (int)location.Z >> 4);

            return chunk.GetBlock(location);
        }

        private Chunk GetChunk(int x, int z) => this.LoadedChunks.FirstOrDefault(c => c.X == x && c.Z == z);

        public int TransformToChunk(double input)
        {
            return (int)Math.Floor(input / 16);
        }

        public void Load()
        {
            var DataPath = Path.Combine(folder, "level.dat");

            var DataFile = new NbtFile();
            DataFile.LoadFromFile(DataPath);

            var levelcompound = DataFile.RootTag;
            this.Data = new Level()
            {
                Hardcore = levelcompound["hardcore"].ByteValue == 1, // lel lazy bool conversion I guess
                MapFeatures = levelcompound["MapFeatures"].ByteValue == 1,
                Raining = levelcompound["raining"].ByteValue == 1,
                Thundering = levelcompound["thundering"].ByteValue == 1,
                Gametype = levelcompound["GameType"].IntValue,
                GeneratorVersion = levelcompound["generatorVersion"].IntValue,
                RainTime = levelcompound["rainTime"].IntValue,
                SpawnX = levelcompound["SpawnX"].IntValue,
                SpawnY = levelcompound["SpawnY"].IntValue,
                SpawnZ = levelcompound["SpawnZ"].IntValue,
                ThunderTime = levelcompound["thunderTime"].IntValue,
                Version = levelcompound["version"].IntValue,
                LastPlayed = levelcompound["LastPlayed"].LongValue,
                RandomSeed = levelcompound["RandomSeed"].LongValue,
                SizeOnDisk = levelcompound["SizeOnDisk"].LongValue,
                Time = levelcompound["Time"].LongValue,
                GeneratorName = levelcompound["generatorName"].StringValue,
                LevelName = levelcompound["LevelName"].StringValue
            };

            this.Loaded = true;
        }

        public void SetTime(long newTime) => this.Data.Time = newTime;

        public void AddPlayer(Player player) => this.Players.Add(player);

        public void RemovePlayer(Player player) => this.Players.Remove(player);

        //TODO
        public void Save() { }

        public void LoadPlayer(Guid uuid)
        {
            var playerfile = Path.Combine(folder, "players", $"{uuid}.dat");

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
            this.Players.Add(player);
        }

        internal void GenerateWorld()
        {
            this.Server.Logger.LogInformation("Generating chunk..");
            var chunk = this.Generator.GenerateChunk(0, 0);

            for (int i = 0; i < 16; i++)
                chunk.AddSection(new ChunkSection()
                {
                    YBase = i >> 4
                }.FillWithLight());

            for (int x = 0; x < 16; x++)
            {
                for (int y = 0; y < 16; y++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        var block = chunk.Blocks[x, y, z];

                        chunk.Sections[0].SetBlock(x, y, z, block);
                    }
                }
            }

            for (int i = 0; i < 1024; i++)
                chunk.BiomeContainer.Biomes.Add(0); //TODO: Add proper biomes & for some reason not all the block biomes get set properly...

            this.LoadedChunks.Add(chunk);
        }

        // This would also save the file back to the world folder.
        public void UnloadPlayer(Guid uuid)
        {
            // TODO save changed data to file [uuid].dat
            this.Players.RemoveAll(x => x.Uuid == uuid);
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