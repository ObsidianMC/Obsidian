using SharpNoise.Modules;
using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    interface IOverworldDecorator
    {
        public abstract void Decorate(Chunk chunk, Module mod);
    }
}
