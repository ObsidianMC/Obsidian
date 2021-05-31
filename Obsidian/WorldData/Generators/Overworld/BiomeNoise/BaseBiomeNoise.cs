using SharpNoise.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.WorldData.Generators.Overworld.BiomeNoise
{
    public class BaseBiomeNoise
    {
        public Module BiomeSelector { get; protected set; }

        public Module RiverSelector { get; protected set; }

        protected readonly OverworldTerrainSettings settings;

        protected readonly double biomeScale = 0.812;

        protected BaseBiomeNoise(OverworldTerrainSettings ots)
        {
            this.settings = ots;
        }
    }
}
