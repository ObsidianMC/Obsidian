using SharpNoise.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.WorldData.Generators.Overworld.Terrain
{
    public class BaseTerrain
    {
        public Module Result { get; protected set; }

        protected readonly OverworldTerrainSettings settings;

        protected BaseTerrain(OverworldTerrainSettings ots)
        {
            this.settings = ots;
        }
    }
}
