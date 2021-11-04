using SharpNoise.Modules;
using System.Collections.Generic;

namespace Obsidian.API.Noise
{
    public class TerrainSelect : SharpNoise.Modules.Blend
    {
        internal Dictionary<int, Module> TerrainModules { get; set; } = new();

        public Module BiomeSelector { get; set; }

        public TerrainSelect(Module biomeSelector)
        {
            Source1 = new Constant { ConstantValue = 0 };
            BiomeSelector = biomeSelector;
        }

        public override double GetValue(double x, double y, double z)
        {
            var b = (int)BiomeSelector.GetValue(x, y, z);
            Source0 = TerrainModules[b];
            return base.GetValue(x, y, z);
        }
    }
}
