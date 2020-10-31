using Obsidian.ChunkData;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public static class OverworldDecorator
    {
        public static void Decorate(Chunk chunk, double[,] terrainHeightMap, OverworldNoise noise)
        {

            for (int x = 0; x < 16; x++)
            {
                for (int z = 0; z < 16; z++)
                {
                    var b = ChunkBiome.GetBiome((chunk.X << 4) + x, (chunk.Z << 4) + z, noise);
                    switch (b)
                    {
                        case Biomes.Plains:
                        case Biomes.Badlands:
                            PlainsDecorator.Decorate(chunk, terrainHeightMap[x, z], (x, z), noise);
                            break;
                        case Biomes.River:
                            RiverDecorator.Decorate(chunk, terrainHeightMap[x, z], (x, z), noise);
                            break;
                        case Biomes.FrozenRiver:
                            FrozenRiverDecorator.Decorate(chunk, terrainHeightMap[x, z], (x, z), noise);
                            break;
                        case Biomes.Desert:
                            DesertDecorator.Decorate(chunk, terrainHeightMap[x, z], (x, z), noise);
                            break;
                    }

                }
            }
        }
    }
}
