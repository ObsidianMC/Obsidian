using SharpNoise.Modules;
using System.Collections.Generic;
using static Obsidian.API.Noise.VoronoiBiomes;

namespace Obsidian.API.Noise
{
    public class TerrainSelect : SharpNoise.Modules.Blend
    {
        public Dictionary<BiomeNoiseValue, Module> TerrainModules { get; set; } = new();

        public Module BiomeSelector { get; set; }

        public TerrainSelect()
        {
            Source1 = new Constant { ConstantValue = 0 };
        }

        public override double GetValue(double x, double y, double z)
        {
            var b = (BiomeNoiseValue)BiomeSelector.GetValue(x, y, z);
            Source0 = TerrainModules[b];
            return base.GetValue(x, y, z);
        }
    }
}
