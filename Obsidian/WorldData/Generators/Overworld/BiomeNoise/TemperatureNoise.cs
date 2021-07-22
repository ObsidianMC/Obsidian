using SharpNoise;
using SharpNoise.Modules;
using System.Collections.Generic;

namespace Obsidian.WorldData.Generators.Overworld.BiomeNoise
{
    public class TemperatureNoise : BaseBiomeNoise
    {
        public TemperatureNoise(OverworldTerrainSettings ots) : base(ots, 220, ots.BiomeTemperatureRatio)
        {

        }
    }
}