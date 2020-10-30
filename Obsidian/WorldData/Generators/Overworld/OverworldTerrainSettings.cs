// Thank you https://github.com/rthome/SharpNoise/tree/master/Examples/ComplexPlanetExample

namespace Obsidian.WorldData.Generators.Overworld
{
    public class OverworldTerrainSettings
    {
        /// <summary>
        /// Planet seed.  Change this to generate a different planet.
        /// </summary>
        public int Seed { get; set; }

        /// <summary>
        /// Minimum elevation on the planet, in meters.  This value is approximate.
        /// </summary>
        public double MinElev { get; set; }

        /// <summary>
        /// Maximum elevation on the planet, in meters.  This value is approximate.
        /// </summary>
        public double MaxElev { get; set; }

        /// <summary>
        /// Frequency of the planet's continents.  Higher frequency produces smaller,
        /// more numerous continents.  This value is measured in radians.
        /// </summary>
        public double ContinentFrequency { get; set; }

        /// <summary>
        /// Lacunarity of the planet's continents.  Changing this value produces
        /// slightly different continents.  For the best results, this value should
        /// be random, but close to 2.0.
        /// </summary>
        public double ContinentLacunarity { get; set; }

        /// <summary>
        /// Lacunarity of the planet's mountains.  Changing this value produces
        /// slightly different mountains.  For the best results, this value should
        /// be random, but close to 2.0.
        /// </summary>
        public double MountainLacunarity { get; set; }

        /// <summary>
        /// Lacunarity of the planet's hills.  Changing this value produces slightly
        /// different hills.  For the best results, this value should be random, but
        /// close to 2.0.
        /// </summary>
        public double HillsLacunarity { get; set; }

        /// <summary>
        /// Lacunarity of the planet's plains.  Changing this value produces slightly
        /// different plains.  For the best results, this value should be random, but
        /// close to 2.0.
        /// </summary>
        public double PlainsLacunarity { get; set; }

        /// <summary>
        /// Lacunarity of the planet's badlands.  Changing this value produces
        /// slightly different badlands.  For the best results, this value should be
        /// random, but close to 2.0.
        /// </summary>
        public double BadlandsLacunarity { get; set; }

        /// <summary>
        /// Specifies the "twistiness" of the mountains.
        /// </summary>
        public double MountainsTwist { get; set; }

        /// <summary>
        /// Specifies the "twistiness" of the hills.
        /// </summary>
        public double HillsTwist { get; set; }

        /// <summary>
        /// Specifies the "twistiness" of the badlands.
        /// </summary>
        public double BadlandsTwist { get; set; }

        /// <summary>
        /// Specifies the planet's sea level.  This value must be between -1.0
        /// (minimum planet elevation) and +1.0 (maximum planet elevation.)
        /// </summary>
        public double SeaLevel { get; set; }

        /// <summary>
        /// Specifies the level on the planet in which continental shelves appear.
        /// This value must be between -1.0 (minimum planet elevation) and +1.0
        /// (maximum planet elevation), and must be less than SeaLevel.
        /// </summary>
        public double ShelfLevel { get; set; }

        /// <summary>
        /// Determines the amount of mountainous terrain that appears on the
        /// planet.  Values range from 0.0 (no mountains) to 1.0 (all terrain is
        /// covered in mountains).  Mountainous terrain will overlap hilly terrain.
        /// Because the badlands terrain may overlap parts of the mountainous
        /// terrain, setting MountainsAmount to 1.0 may not completely cover the
        /// terrain in mountains.
        /// </summary>
        public double MountainsAmount { get; set; }

        /// <summary>
        /// Determines the amount of hilly terrain that appears on the planet.
        /// Values range from 0.0 (no hills) to 1.0 (all terrain is covered in
        /// hills).  This value must be less than MountainsAmount.  Because the
        /// mountainous terrain will overlap parts of the hilly terrain, and
        /// the badlands terrain may overlap parts of the hilly terrain, setting
        /// HillsAmount to 1.0 may not completely cover the terrain in hills.
        /// </summary>
        public double HillsAmount { get; set; }

        /// <summary>
        /// Determines the amount of badlands terrain that covers the planet.
        /// Values range from 0.0 (no badlands) to 1.0 (all terrain is covered in
        /// badlands.)  Badlands terrain will overlap any other type of terrain.
        /// </summary>
        public double BadlandsAmount { get; set; }

        /// <summary>
        /// Offset to apply to the terrain type definition.  Low values (less than 1.0) cause
        /// the rough areas to appear only at high elevations.  High values (greater than 2.0)
        /// cause the rough areas to appear at any elevation.  The percentage of
        /// rough areas on the planet are independent of this value.
        /// </summary>
        public double TerrainOffset { get; set; }

        /// <summary>
        /// Specifies the amount of "glaciation" on the mountains.  This value
        /// should be close to 1.0 and greater than 1.0.
        /// </summary>
        public double MountainGlaciation { get; set; }

        /// <summary>
        /// Scaling to apply to the base continent elevations, in planetary elevation
        /// units.
        /// </summary>
        public double ContinentHeightScale { get; set; }

        /// <summary>
        /// Maximum depth of the rivers, in planetary elevation units.
        /// </summary>
        public double RiverDepth { get; set; }

        /// <summary>
        /// Percentage of ground consumed by caves.
        /// </summary>
        public double CaveFillPercent { get; set; }

        /// <summary>
        /// Height of caves. Values below 1 make caves smaller.
        /// </summary>
        public double CaveVertStretch { get; set; }

        /// <summary>
        /// Horizontal stretch applied to caves.
        /// </summary>
        public double CaveHorizStretch { get; set; }

        /// <summary>
        /// Horizontal stretch applied to terrain.
        /// </summary>
        public double TerrainHorizStretch { get; set; }

        /// <summary>
        /// Vertical stretch applied to terrain.
        /// </summary>
        public double TerrainVertStretch { get; set; }


        public OverworldTerrainSettings()
        {
            Seed = 685100205;
            MinElev = 0;
            MaxElev = 128;
            ContinentFrequency = 0.85;

            ContinentHeightScale = 1;
            TerrainOffset = 1;

            RiverDepth = 0.00234; // 6 / 512.0;
            SeaLevel = -0 / 256.0;
            ShelfLevel = -68 / 256.0;

            MountainsAmount = 0.52;
            HillsAmount = 0.75;
            BadlandsAmount = 0.05;

            MountainGlaciation = 1.075; 
            MountainsTwist = 1.9;
            HillsTwist = 0.9;
            BadlandsTwist = 1;

            ContinentLacunarity = 2.208984375;
            MountainLacunarity = 2.01;
            HillsLacunarity = 0.162109375;
            PlainsLacunarity = 2.814453125;
            BadlandsLacunarity = 2.000890625;

            CaveFillPercent = 0.25;
            CaveVertStretch = 0.020;
            CaveHorizStretch = 0.010;

            TerrainHorizStretch = 0.001195;
            TerrainVertStretch = 0.60;
        }
    }
}
