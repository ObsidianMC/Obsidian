﻿using Obsidian.WorldData.Generators.Overworld.BiomeNoise;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class DeepLukewarmOceanDecorator : OceanDecorator
{

    public DeepLukewarmOceanDecorator(Biomes biome, Chunk chunk, Vector surfacePos, BaseBiomeNoise noise) : base(biome, chunk, surfacePos, noise)
    {
        hasMagma = true;
        hasKelp = false;
        hasSeaGrass = true;
        primarySurface = sand;
        secondarySurface = sand;
        tertiarySurface = gravel;
    }
}
