using Obsidian.API;
using Obsidian.WorldData.Generators.Overworld.BiomeNoise;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public class WarmOceanDecorator : OceanDecorator
    {

        public WarmOceanDecorator(Biomes biome, Chunk chunk, Vector surfacePos, BaseBiomeNoise noise) : base(biome, chunk, surfacePos, noise)
        {
            primarySurface = sand;
            secondarySurface = dirt;
            hasSeaGrass = hasKelp = true;
            hasMagma = true;
        }
    }
}
