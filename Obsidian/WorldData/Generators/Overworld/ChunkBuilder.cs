using Obsidian.Blocks;
using Obsidian.ChunkData;
using Obsidian.Util.Registry;
using System;

namespace Obsidian.WorldData.Generators.Overworld
{
    public static class ChunkBuilder
    {
        public static void FillChunk(Chunk chunk, double[,] terrainHeightmap, double[,] undergroundHeightmap, double[,] bedrockHeightmap)
        {
            for (int bx = 0; bx < 16; bx++)
            {
                for (int bz = 0; bz < 16; bz++)
                {
                    double terrainY = terrainHeightmap[bx, bz];
                    for (int by = 255; by >= 0; by--)
                    {
                        // Air
                        if (by > terrainY && by > 60)
                        {
                            continue;
                        }

                        // Bedrock
                        if (by < bedrockHeightmap[bx, bz])
                        {
                            chunk.SetBlock(bx, by, bz, Registry.GetBlock(Materials.Bedrock));
                            continue;
                        }

                        // Underground
                        if (by <= undergroundHeightmap[bx, bz])
                        {
                            chunk.SetBlock(bx, by, bz, Registry.GetBlock(Materials.Stone));
                            continue;
                        }

                        Materials m = Materials.WetSponge;

                        // Ocean/River
                        if (terrainY <= 59.66)
                        {
                            if (by > terrainY)
                            {
                                m = Materials.Water; //TODO: switch for temperature (IE: Ice on top of water)
                            }
                            else if (by <= terrainY)
                            {
                                switch (chunk.BiomeContainer.Biomes[0])
                                {
                                    case (int)Biomes.DeepOcean:
                                        m = Materials.CoarseDirt;
                                        break;
                                    case (int)Biomes.Ocean:
                                        m = Materials.Dirt;
                                        break;
                                    case (int)Biomes.River:
                                        m = Materials.Sand;
                                        break;
                                    default:
                                        m = Materials.Clay;
                                        break;
                                }
                            }
                        }

                        // Beach
                        else if (terrainY < 60.66) // magic decimals are for blending
                        {
                            m = Materials.Sand;
                        }

                        // Plains
                        else if (terrainY < 64.33)
                        {
                            if (by == (int)terrainY)
                            {
                                m = Materials.GrassBlock;
                                chunk.SetBlock(bx, by+1, bz, Registry.GetBlock(Materials.Grass));
                            }
                            else
                            {
                                m = Materials.Dirt;
                            }
                        }

                        // Hills
                        else if (terrainY < 88.35)
                        {
                            if (by == (int)terrainY)
                            {
                                m = Materials.GrassBlock;
                                //chunk.SetBlock(bx, by+1, bz, Registry.GetBlock(Materials.Grass));
                            }
                            else
                            {
                                m = Materials.Stone;
                            }
                        }

                        // Mountains
                        else if (terrainY < 96.35)
                        {
                            m = Materials.Stone;
                        }

                        // Snow caps
                        else
                        {
                            m = Materials.SnowBlock;
                        }

                        chunk.SetBlock(bx, by, bz, Registry.GetBlock(m));
                    }
                }
            }
        }

        public static void CarveCaves(OverworldNoise noiseGen, Chunk chunk, double[,] rhm, double[,] bhm)
        {
            for (int bx = 0; bx < 16; bx++)
            {
                for (int bz = 0; bz < 16; bz++)
                {
                    int tY = (int)rhm[bx, bz];
                    int brY = Math.Min((int)bhm[bx, bz], 70);
                    for (int by = brY; by < tY; by++)
                    {
                        bool caveAir = noiseGen.Cave(bx + (chunk.X * 16), by, bz + (chunk.Z * 16));
                        if (caveAir)
                        {
                            chunk.SetBlock(bx, by, bz, Registry.GetBlock(Materials.CaveAir));
                        }
                    }
                }
            }
        }
    }
}
