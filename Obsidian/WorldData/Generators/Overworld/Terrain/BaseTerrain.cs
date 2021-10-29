using SharpNoise.Modules;

namespace Obsidian.WorldData.Generators.Overworld.Terrain
{
    public class BaseTerrain
    {
        public Module Result { get; protected set; }

        protected readonly OverworldTerrainSettings settings;

        protected BaseTerrain()
        {
            this.settings = OverworldGenerator.GeneratorSettings;
        }
    }
}
