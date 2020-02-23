// This is the data about one world. It gets saved as level.dat.

namespace Obsidian.World
{
    public class Level
    {
        public bool Hardcore { get; set; }
        public bool MapFeatures { get; set; } // Structure generation??
        public bool Raining { get; set; }
        public bool Thundering { get; set; }
        public int Gametype { get; set; }
        public int GeneratorVersion { get; set; }
        public int RainTime { get; set; } // Ticks remaining until rain stops
        public int SpawnX { get; set; }
        public int SpawnY { get; set; }
        public int SpawnZ { get; set; }
        public int ThunderTime { get; set; }
        public int Version { get; set; } // 19113 for 1.2.5 so wtf is 1.13.2?? ._.
        public long LastPlayed { get; set; }
        public long RandomSeed { get; set; } // Seed, obv
        public long SizeOnDisk { get; set; } // Always 0 for 1.2.5 so maybe always 0 for all versions?? :eyes:
        public long Time { get; set; } // Total ticks
        public string GeneratorName { get; set; }
        public string LevelName { get; set; } = "world";
    }
}
