using SharpNoise;
using SharpNoise.Modules;
using System.Collections.Generic;

namespace Obsidian.WorldData.Generators.Overworld.BiomeNoise
{
    public class HumidityNoise : BaseBiomeNoise
    {
        public HumidityNoise(OverworldTerrainSettings ots) : base(ots, 200, ots.BiomeHumidityRatio)
        {

        }
    }
}