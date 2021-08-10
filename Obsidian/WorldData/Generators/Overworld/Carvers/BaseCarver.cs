
using SharpNoise.Modules;

namespace Obsidian.WorldData.Generators.Overworld.Carvers
{
    public class BaseCarver
    {
        public Module Result { get; protected set; }

        protected readonly OverworldTerrainSettings settings;

        protected BaseCarver()
        {
            this.settings = OverworldGenerator.GeneratorSettings;
        }
    }
}
