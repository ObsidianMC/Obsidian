using SharpNoise;
using SharpNoise.Modules;
using System.Collections.Generic;

namespace Obsidian.WorldData.Generators.Overworld.BiomeNoise
{
    public class TerrainNoise : BaseBiomeNoise
    {
        public TerrainNoise(OverworldTerrainSettings ots) : base(ots, 240, ots.BiomeTerrainRatio)
        {

        }
    }
}