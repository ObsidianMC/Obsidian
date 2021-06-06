using Obsidian.API;
using Obsidian.ChunkData;
using Obsidian.Utilities.Registry;
using Obsidian.WorldData.Generators.Overworld.BiomeNoise;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public class LukewarmOceanDecorator : OceanDecorator
    {

        public LukewarmOceanDecorator(Biomes biome, Chunk chunk, Vector surfacePos, BaseBiomeNoise noise) : base(biome, chunk, surfacePos, noise)
        {
            primarySurface = sand;
            secondarySurface = clay;
            tertiarySurface = gravel;
            hasMagma = false;
            hasSeaGrass = hasKelp = true;
        }
    }
}
