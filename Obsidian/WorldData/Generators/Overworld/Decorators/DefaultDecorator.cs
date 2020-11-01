using Obsidian.ChunkData;
using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public class DefaultDecorator : BaseDecorator
    {
        public DefaultDecorator(Biomes biome) : base(biome)
        {
        }

        public override void Decorate(Chunk chunk, double terrainY, (int x, int z) pos)
        {
            throw new NotImplementedException();
        }
    }
}
