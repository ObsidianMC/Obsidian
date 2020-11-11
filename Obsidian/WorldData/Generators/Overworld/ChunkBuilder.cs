using Obsidian.Blocks;
using Obsidian.ChunkData;
using Obsidian.Util.Registry;
using System;
using System.Reflection.PortableExecutable;

namespace Obsidian.WorldData.Generators.Overworld
{
    public static class ChunkBuilder
    {
        public static void FillChunk(Chunk chunk, double[,] terrainHeightmap, double[,] undergroundHeightmap, double[,] bedrockHeightmap, bool debug=false)
        {
            var air = Registry.GetBlock(Materials.Air);
            var bedrock = Registry.GetBlock(Materials.Bedrock);
            var stone = Registry.GetBlock(Materials.Stone);
            var water = Registry.GetBlock(Materials.Water);
            var coarseDirt = Registry.GetBlock(Materials.CoarseDirt);
            var dirt = Registry.GetBlock(Materials.Dirt);
            var sand = Registry.GetBlock(Materials.Sand);
            var clay = Registry.GetBlock(Materials.Clay);
            var grassBlock = Registry.GetBlock(Materials.GrassBlock);
            var snowBlock = Registry.GetBlock(Materials.SnowBlock);

            int highestY = 0;

            for (int x = 0; x < 16; x++)
                for (int z = 0; z < 16; z++)
                    highestY = Math.Max(highestY, (int) terrainHeightmap[x,z]);

            var skipAbove = ((highestY >> 4) + 1) << 4;

            for (int bx = 0; bx < 16; bx++)
            {
                for (int bz = 0; bz < 16; bz++)
                {
                    double terrainY = terrainHeightmap[bx, bz];
                    for (int by = 0; by < 256; by++)
                    {
                        // Air
                        if (by > terrainY && by > 60)
                        {
                            chunk.SetBlock(bx, by, bz, air);
                            continue;
                        }

                        // Bedrock
                        if (by < bedrockHeightmap[bx, bz])
                        {
                            chunk.SetBlock(bx, by, bz, bedrock);
                            continue;
                        }

                        // Underground
                        if (by <= undergroundHeightmap[bx, bz])
                        {
                            if (!debug) { chunk.SetBlock(bx, by, bz, stone); }
                            continue;
                        }

                        var b = bedrock;

                        // Ocean/River
                        if (terrainY <= 59.66)
                        {
                            if (by > terrainY)
                            {
                                b = water; //TODO: switch for temperature (IE: Ice on top of water)
                            }
                            else if (by <= terrainY)
                            {
                                switch (chunk.BiomeContainer.Biomes[0])
                                {
                                    case (int)Biomes.DeepOcean:
                                        b = coarseDirt;
                                        break;
                                    case (int)Biomes.Ocean:
                                        b = dirt;
                                        break;
                                    case (int)Biomes.River:
                                        b = sand;
                                        break;
                                    default:
                                        b = clay;
                                        break;
                                }
                            }
                        }

                        // Beach
                        else if (terrainY < 60.66) // magic decimals are for blending
                        {
                            b = sand;
                        }

                        // Plains
                        else if (terrainY < 64.33)
                        {
                            if (by == (int)terrainY)
                            {
                                b = grassBlock;
                            }
                            else
                            {
                                b = dirt;
                            }
                        }

                        // Hills
                        else if (terrainY < 88.35)
                        {
                            if (by == (int)terrainY)
                            {
                                b = grassBlock;
                            }
                            else
                            {
                                b = stone;
                            }
                        }

                        // Mountains
                        else if (terrainY < 96.35)
                        {
                            b = stone;
                        }

                        // Snow caps
                        else
                        {
                            b = snowBlock;
                        }

                        chunk.SetBlock(bx, by, bz, b);
                    }
                }
            }
        }

        public static void CarveCaves(OverworldNoise noiseGen, Chunk chunk, double[,] rhm, double[,] bhm, bool debug=false)
        {
            var b = Registry.GetBlock(Materials.CaveAir);
            for (int bx = 0; bx < 16; bx++)
            {
                for (int bz = 0; bz < 16; bz++)
                {
                    int tY = Math.Min((int)rhm[bx, bz],64);
                    int brY = (int)bhm[bx, bz];
                    for (int by = brY; by < tY; by++)
                    {
                        bool caveAir = noiseGen.Cave(bx + (chunk.X * 16), by, bz + (chunk.Z * 16));
                        if (caveAir)
                        {
                            if(debug) { b = Registry.GetBlock(Materials.LightGrayStainedGlass); }
                            chunk.SetBlock(bx, by, bz, b);
                        }
                    }
                }
            }
        }
    }
}
