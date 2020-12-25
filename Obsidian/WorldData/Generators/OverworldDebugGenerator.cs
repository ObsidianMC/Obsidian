using Obsidian.API;
using Obsidian.Util.Registry;
using Obsidian.WorldData.Generators.Overworld;
using System;
using System.Security.Cryptography;
using System.Text;

namespace Obsidian.WorldData.Generators
{
    public class OverworldDebugGenerator : WorldGenerator
    {
        private OverworldNoise noiseGen;
        public OverworldDebugGenerator(string seed) : base("overworlddebug")
        {
            var seedHash = BitConverter.ToInt32(MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(seed)));
            noiseGen = new OverworldNoise(seedHash);
        }

        public override Chunk GenerateChunk(int cx, int cz)
        {
            var chunk = new Chunk(cx, cz);

            // Build terrain map for this chunk
            var terrainHeightmap = new double[16, 16];
            var rockHeightmap = new double[16, 16];
            var bedrockHeightmap = new double[16, 16];

            var terrainHeights = new double[256];

            for (int bx = 0; bx < 16; bx++)
            {
                for (int bz = 0; bz < 16; bz++)
                {
                    for (int by = 0; by < 256; by++)
                        chunk.SetBlock(bx, 26, bz, Registry.GetBlock(Materials.Air));


                    terrainHeights[(bx * 16) + bz] = terrainHeightmap[bx, bz] = noiseGen.Terrain(bx + (cx * 16), bz + (cz * 16));
                    /*                    rockHeightmap[bx, bz] = noiseGen.Underground(bx + (cx * 16), bz + (cz * 16)) + terrainHeightmap[bx, bz] - 5;
                                        bedrockHeightmap[bx, bz] = noiseGen.Bedrock(bx + (cx * 16), bz + (cz * 16)) + 1;
                    */
                    if (noiseGen.IsRiver(bx + (cx * 16), bz + (cz * 16)))
                        chunk.SetBlock(bx, 26, bz, Registry.GetBlock(Materials.LightBlueStainedGlass));

                    if (noiseGen.IsMountain(bx + (cx * 16), bz + (cz * 16)))
                        chunk.SetBlock(bx, 25, bz, Registry.GetBlock(Materials.BlackStainedGlass));

                    if (noiseGen.IsHills(bx + (cx * 16), bz + (cz * 16)))
                        chunk.SetBlock(bx, 24, bz, Registry.GetBlock(Materials.RedStainedGlass));

                    if (noiseGen.IsBadlands(bx + (cx * 16), bz + (cz * 16)))
                        chunk.SetBlock(bx, 23, bz, Registry.GetBlock(Materials.LightGrayStainedGlass));

                    if (noiseGen.IsPlains(bx + (cx * 16), bz + (cz * 16)))
                        chunk.SetBlock(bx, 22, bz, Registry.GetBlock(Materials.WhiteStainedGlass));

                    if (noiseGen.IsOcean(bx + (cx * 16), bz + (cz * 16)))
                        chunk.SetBlock(bx, 21, bz, Registry.GetBlock(Materials.BlueStainedGlass));

                    if (noiseGen.IsDeepOcean(bx + (cx * 16), bz + (cz * 16)))
                        chunk.SetBlock(bx, 21, bz, Registry.GetBlock(Materials.BlueStainedGlass));
                }
            }
            /*
                                var biometemp = noiseGen.GetBiomeTemp(bx + (cx * 16), 0, bz + (cz * 16));
                                var biomeHumidity = noiseGen.GetBiomeHumidity(bx + (cx * 16), 255, bz + (cz * 16));
                                if (biometemp > 0.75)
                                {
                                    chunk.SetBlock(bx, 31, bz, Registry.GetBlock(Materials.RedStainedGlass));
                                }
                                else if (biometemp > 0.33)
                                {
                                    chunk.SetBlock(bx, 31, bz, Registry.GetBlock(Materials.YellowStainedGlass));
                                }
                                else if (biometemp > -0.15)
                                {
                                    chunk.SetBlock(bx, 31, bz, Registry.GetBlock(Materials.GreenStainedGlass));
                                }
                                else if (biometemp > -0.5)
                                {
                                    chunk.SetBlock(bx, 31, bz, Registry.GetBlock(Materials.CyanStainedGlass));
                                }
                                else
                                {
                                    chunk.SetBlock(bx, 31, bz, Registry.GetBlock(Materials.BlueStainedGlass));
                                }

                                if (biomeHumidity > 0.33)
                                {
                                    chunk.SetBlock(bx, 30, bz, Registry.GetBlock(Materials.WhiteStainedGlass));
                                }
                                else if (biomeHumidity > -0.33)
                                {
                                    chunk.SetBlock(bx, 30, bz, Registry.GetBlock(Materials.LightGrayStainedGlass));
                                }
                                else
                                {
                                    chunk.SetBlock(bx, 30, bz, Registry.GetBlock(Materials.GrayStainedGlass));
                                }
                            }
                        }

                        for (int bx = 0; bx < 16; bx++)
                        {
                            for (int bz = 0; bz < 16; bz++)
                            {
                                chunk.SetBlock(bx, (int)terrainHeightmap[bx,bz], bz, Registry.GetBlock(Materials.GreenStainedGlass));
                                chunk.SetBlock(bx, (int)rockHeightmap[bx, bz], bz, Registry.GetBlock(Materials.BrownStainedGlass));
                                chunk.SetBlock(bx, (int)bedrockHeightmap[bx, bz], bz, Registry.GetBlock(Materials.Bedrock));
                            }
                        }*/

            //ChunkBuilder.FillChunk(chunk, terrainHeightmap, rockHeightmap, bedrockHeightmap, true);

            //GenerateCoal(chunk, rockHeightmap);
            //ChunkBuilder.CarveCaves(noiseGen, chunk, rockHeightmap, bedrockHeightmap, true);
            return chunk;
        }



        private void GenerateCoal(Chunk chunk, double[,] rockHeighmap)
        {
            for (int bx = 0; bx < 16; bx++)
            {
                for (int bz = 0; bz < 16; bz++)
                {
                    var worldX = (chunk.X * 16) + bx;
                    var worldZ = (chunk.Z * 16) + bz;
                    var rockY = (int)rockHeighmap[bx, bz];

                    for (int by = 24; by < rockY; by++)
                    {
                        bool isCoal = noiseGen.Coal(worldX, by, worldZ);
                        if (isCoal)
                        {
                            chunk.SetBlock(bx, by, bz, Registry.GetBlock(Materials.CoalOre));
                        }
                    }
                }
            }
        }
    }
}