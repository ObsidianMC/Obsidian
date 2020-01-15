using fNbt;
using Obsidian.Concurrency;
using Obsidian.PlayerData;
using Obsidian.Util;
using Obsidian.World;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Entities
{
    public class World
    {
        public Level WorldData { get; internal set; }

        public List<Player> Players { get; }

        // This one later comes back in the regions,
        // but might be easier for internal management purposes
        public List<object> Entities { get; }

        internal string folder { get; }
        internal bool Loaded { get; set; }

        private readonly ConcurrentHashSet<Chunk> LoadedChunks;
        private readonly WorldGenerator worldgen;

        public World(string folder, WorldGenerator worldgen)
        {
            this.WorldData = new Level
            {
                Time = 1200,
                Gametype = (int)Gamemode.Survival,
                GeneratorName = WorldType.Default.ToString()
            };

            this.Players = new List<Player>();

            this.Entities = new List<object>();
            this.folder = folder;

            this.LoadedChunks = new ConcurrentHashSet<Chunk>();
            this.worldgen = worldgen;
        }

        public async Task UpdateChunksForClient(Client c)
        {
            int dist = c.ClientSettings?.ViewDistance ?? 1;

            int oldchunkx = transformToChunk(c.Player.PreviousTransform?.X ?? int.MaxValue);
            int chunkx = transformToChunk(c.Player.Transform?.X ?? 0);

            int oldchunkz = transformToChunk(c.Player.PreviousTransform?.Z ?? int.MaxValue);
            int chunkz = transformToChunk(c.Player.Transform?.Z ?? 0);

            //if (Math.Abs(chunkz - oldchunkz) > 4 || Math.Abs(chunkx - oldchunkx) > 4)
            //{
            //    // This is a teleport!!!1 Send full new chunk data.
            //    await resendBaseChunksAsync(dist, oldchunkx, oldchunkz, chunkx, chunkz, c);
            //    return;
            //}

            if (chunkx > oldchunkx)
            {
                for (int i = (chunkz - dist); i < (chunkz + dist); i++)
                {
                    // TODO: implement
                    //await c.UnloadChunkAsync((chunkx - dist), i);

                    await c.SendChunkAsync(getChunk((chunkx + dist), i, c));
                }
            }

            if (chunkx < oldchunkx)
            {
                for (int i = (chunkz - dist); i < (chunkz + dist); i++)
                {
                    // TODO: implement
                    //await c.UnloadChunkAsync((chunkx + dist), i);

                    await c.SendChunkAsync(getChunk((chunkx - dist), i, c));
                }
            }

            if (chunkz > oldchunkz)
            {
                for (int i = (chunkx - dist); i < (chunkx + dist); i++)
                {
                    // TODO: implement
                    //await c.UnloadChunkAsync(i, (chunkz - dist));

                    await c.SendChunkAsync(getChunk(i, (chunkz + dist), c));
                }
            }

            if (chunkz < oldchunkz)
            {
                for (int i = (chunkx - dist); i < (chunkx + dist); i++)
                {
                    // TODO: implement
                    //await c.UnloadChunkAsync(i, (chunkz + dist));

                    await c.SendChunkAsync(getChunk(i, (chunkz - dist), c));
                }
            }
        }

        public async Task resendBaseChunksAsync(int dist, int oldx, int oldz, int x, int z, Client c)
        {
            // unload old chunks
            for (int cx = oldx - dist; cx < oldx + dist; cx++)
            {
                for (int cz = oldz - dist; cz < oldz + dist; cz++)
                {
                    //await c.UnloadChunkAsync(cx, cz);
                }
            }

            // load new chunks
            for (int cx = (x - dist); cx < (x + dist); cx++)
            {
                for (int cz = z - dist; cz < z + dist; cz++)
                {
                    await c.SendChunkAsync(getChunk(cx, cz, c));
                }
            }

            c.Logger.LogDebug($"loaded base chunks for {c.Player.Username} {x - dist} until {x + dist}");
        }

        private Chunk getChunk(int x, int z, Client c)
        {
            // TODO: loading existing chunks
            return c.OriginServer.WorldGenerator.GenerateChunk(new Chunk(x, z));
        }

        public int transformToChunk(double input)
        {
            return (int)Math.Floor(input / 16);
        }

        public void Load()
        {
            var DataPath = Path.Combine(folder, "level.dat");

            var DataFile = new NbtFile();
            DataFile.LoadFromFile(DataPath);

            var levelcompound = DataFile.RootTag;
            this.WorldData = new Level()
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

        public void SetTime(long newTime) => this.WorldData.Time = newTime;

        public void AddPlayer(Player player) => this.Players.Add(player);

        public void RemovePlayer(Player player) => this.Players.Remove(player);

        //TODO
        public void Save() { }

        public void LoadPlayer(Guid uuid)
        {
            var playerfile = Path.Combine(folder, "players", $"{uuid.ToString()}.dat");

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