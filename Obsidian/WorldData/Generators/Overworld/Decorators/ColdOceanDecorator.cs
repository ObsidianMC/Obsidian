using Obsidian.API;
using Obsidian.WorldData.Generators.Overworld.BiomeNoise;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public class ColdOceanDecorator : OceanDecorator
    {

        public ColdOceanDecorator(Biomes biome, Chunk chunk, Vector surfacePos, BaseBiomeNoise noise) : base(biome, chunk, surfacePos, noise)
        {
            hasMagma = false;
            hasKelp = false;
            hasSeaGrass = true;
            primarySurface = clay;
            secondarySurface = dirt;
            tertiarySurface = gravel;
        }
    }
}
