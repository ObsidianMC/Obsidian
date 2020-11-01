using Obsidian.ChunkData;
using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public abstract class BaseDecorator
    {
        private Biomes biome;

        protected BaseDecorator(Biomes biome)
        {
            this.biome = biome;
        }

        public abstract void Decorate(Chunk chunk, double terrainY, (int x, int z) pos);
    }
}
