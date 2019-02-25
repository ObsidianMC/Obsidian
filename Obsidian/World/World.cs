using System.Collections.Generic;
using System.IO;
using fNbt;
using System;

namespace Obsidian.Entities
{
    public class World
    {
        public Level WorldData { get; internal set; }

        public List<MinecraftPlayer> Players { get; }

        // This one later comes back in the regions, 
        // but might be easier for internal management purposes
        public List<object> Entities { get; }

        internal string folder { get; }
        internal bool Loaded { get; set; }

        public World(string folder)
        {
            this.WorldData.Time = 1200;
            this.Players = new List<MinecraftPlayer>();
            this.WorldData.Gametype = (int)Gamemode.Survival;
            this.WorldData.GeneratorName = WorldType.Default.ToString();
            this.Entities = new List<object>();
            this.folder = folder;
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

        public void AddPlayer(MinecraftPlayer player) => this.Players.Add(player);
        public void RemovePlayer(MinecraftPlayer player) => this.Players.Remove(player);

        //TODO
        public void Save() { }

        public void LoadPlayer(string uuid)
        {
            var playerfile = Path.Combine(folder, "players", $"{uuid}.dat");

            var PFile = new NbtFile();
            PFile.LoadFromFile(playerfile);
            var playercompound = PFile.RootTag;
            // filenames are player UUIDs.
            var player = new MinecraftPlayer("", Path.GetFileNameWithoutExtension(playerfile))
            {
                UUID = uuid,
                OnGround = playercompound["OnGround"].ByteValue == 1,
                Sleeping = playercompound["Sleeping"].ByteValue == 1,
                Air = playercompound["Air"].ShortValue,
                AttackTime = playercompound["AttackTime"].ShortValue,
                DeathTime = playercompound["DeathTime"].ShortValue,
                Fire = playercompound["Fire"].ShortValue,
                Health = playercompound["Health"].ShortValue,
                HurtTime = playercompound["HurtTime"].ShortValue,
                SleepTimer = playercompound["SleepTimer"].ShortValue,
                Dimension = playercompound["Dimension"].IntValue,
                FoodLevel = playercompound["foodLevel"].IntValue,
                FoodTickTimer = playercompound["foodTickTimer"].IntValue,
                PlayerGameType = playercompound["playerGameType"].IntValue,
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
        public void UnloadPlayer(string uuid)
        {
            // TODO save changed data to file [uuid].dat
            this.Players.RemoveAll(x => x.UUID == uuid);
        }
    }

    public enum WorldType
    {
        Default,
        Flat,
        LargeBiomes,
        Amplified
    }
}
