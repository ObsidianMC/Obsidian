using Obsidian.API;
using Obsidian.ChunkData;
using Obsidian.Utilities.Registry;
using System;

namespace Obsidian.WorldData.Generators.Overworld
{
    public static class ChunkBuilder
    {
        public static void FillChunk(Chunk chunk, double[,] terrainHeightmap, double[,] undergroundHeightmap, double[,] bedrockHeightmap, bool debug = false)
        {
            var air = Registry.GetBlock(Material.Air);
            var bedrock = Registry.GetBlock(Material.Bedrock);
            var stone = Registry.GetBlock(Material.Stone);
            var water = Registry.GetBlock(Material.Water);
            var coarseDirt = Registry.GetBlock(Material.CoarseDirt);
            var dirt = Registry.GetBlock(Material.Dirt);
            var sand = Registry.GetBlock(Material.Sand);
            var clay = Registry.GetBlock(Material.Clay);
            var grassBlock = Registry.GetBlock(9);
            var snowBlock = Registry.GetBlock(Material.SnowBlock);

            int highestY = 0;

            for (int x = 0; x < 16; x++)
                for (int z = 0; z < 16; z++)
                    highestY = Math.Max(highestY, (int)terrainHeightmap[x, z]);

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
                                b = (chunk.BiomeContainer.Biomes[0]) switch
                                {
                                    (int)Biomes.DeepOcean => coarseDirt,
                                    (int)Biomes.Ocean => dirt,
                                    (int)Biomes.River => sand,
                                    _ => clay,
                                };
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

        public static void CarveCaves(OverworldNoise noiseGen, Chunk chunk, double[,] rhm, double[,] bhm, bool debug = false)
        {
            var b = Registry.GetBlock(Material.CaveAir);
            for (int bx = 0; bx < 16; bx++)
            {
                for (int bz = 0; bz < 16; bz++)
                {
                    int tY = Math.Min((int)rhm[bx, bz], 64);
                    int brY = (int)bhm[bx, bz];
                    for (int by = brY; by < tY; by++)
                    {
                        bool caveAir = noiseGen.Cave(bx + (chunk.X * 16), by, bz + (chunk.Z * 16));
                        if (caveAir)
                        {
                            if (debug) { b = Registry.GetBlock(Material.LightGrayStainedGlass); }
                            chunk.SetBlock(bx, by, bz, b);
                        }
                    }
                }
            }
        }
    }
}
