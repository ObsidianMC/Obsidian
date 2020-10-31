using SharpNoise.Modules;
using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    internal class GrassDecorator : IOverworldDecorator
    {
        double[,] TerrainHeightmap { get; set; }

        public void Decorate(Chunk chunk, Module mod)
        {
            throw new NotImplementedException();
        }
    }
}
