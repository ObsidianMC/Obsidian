using Obsidian.Blocks;
using Obsidian.ChunkData;
using Obsidian.Util.Registry;
using Obsidian.WorldData.Generators.Overworld;
using System;
using System.Linq;

namespace Obsidian.WorldData.Generators
{
    public class OverworldDebugGenerator : WorldGenerator
    {
        private OverworldNoise noiseGen = new OverworldNoise();
        public OverworldDebugGenerator() : base("overworlddebug") {}

        public override Chunk GenerateChunk(int cx, int cz)
        {
            var chunk = new Chunk(cx, cz);

            // Build terrain map for this chunk
            var terrainHeightmap = new double[16, 16];
            var rockHeightmap = new double[16, 16];
            var bedrockHeightmap = new double[16, 16];

            var terrainHeights = new double[256];

            for (int bx=0; bx<16; bx++)
            {
                for (int bz=0; bz<16; bz++)
                {
                    terrainHeights[(bx*16)+bz] = terrainHeightmap[bx, bz] = noiseGen.Terrain(bx + (cx * 16), bz + (cz * 16));
                    rockHeightmap[bx, bz] = noiseGen.Underground(bx + (cx * 16), bz + (cz * 16)) + terrainHeightmap[bx, bz] - 5;
                    bedrockHeightmap[bx, bz] = noiseGen.Bedrock(bx + (cx * 16), bz + (cz * 16)) + 1;
                    
                    if (noiseGen.isRiver(bx + (cx * 16), bz + (cz * 16)))
                        chunk.SetBlock(bx, 26, bz, Registry.GetBlock(Materials.LightBlueStainedGlass));

                    if (noiseGen.isMountain(bx + (cx * 16), bz + (cz * 16)))
                        chunk.SetBlock(bx, 25, bz, Registry.GetBlock(Materials.LightGrayStainedGlass));

                    if (noiseGen.isHills(bx + (cx * 16), bz + (cz * 16)))
                        chunk.SetBlock(bx, 24, bz, Registry.GetBlock(Materials.RedStainedGlass));
                    
                    if (noiseGen.isBadlands(bx + (cx * 16), bz + (cz * 16)))
                        chunk.SetBlock(bx, 23, bz, Registry.GetBlock(Materials.BlackStainedGlass));
                    
                    if (noiseGen.isPlains(bx + (cx * 16), bz + (cz * 16)))
                        chunk.SetBlock(bx, 22, bz, Registry.GetBlock(Materials.WhiteStainedGlass));
                    else
                        chunk.SetBlock(bx, 21, bz, Registry.GetBlock(Materials.BlueStainedGlass));

                    var biometemp = noiseGen.GetBiomeTemp(bx + (cx * 16), 0, bz + (cz * 16));
                    var biomeHumidity = noiseGen.GetBiomeHumidity(bx + (cx * 16), 255, bz + (cz * 16));
                    if (biometemp > 0)
                    {
                        chunk.SetBlock(bx, 30, bz, Registry.GetBlock(Materials.RedStainedGlass));
                    }
                    else
                    {
                        chunk.SetBlock(bx, 30, bz, Registry.GetBlock(Materials.GreenStainedGlass));
                    }
                    if (biomeHumidity > 0)
                    {
                        chunk.SetBlock(bx, 31, bz, Registry.GetBlock(Materials.BlueStainedGlass));
                    }
                    else
                    {
                        chunk.SetBlock(bx, 31, bz, Registry.GetBlock(Materials.YellowStainedGlass));
                    }
                }
            }

/*            for (int bx = 0; bx < 16; bx++)
            {
                for (int bz = 0; bz < 16; bz++)
                {
                    chunk.SetBlock(bx, (int)terrainHeightmap[bx,bz], bz, Registry.GetBlock(Materials.GreenStainedGlass));
                    chunk.SetBlock(bx, (int)rockHeightmap[bx, bz], bz, Registry.GetBlock(Materials.BrownStainedGlass));
                    chunk.SetBlock(bx, (int)bedrockHeightmap[bx, bz], bz, Registry.GetBlock(Materials.Bedrock));
                }
            }*/

            for (int i = 0; i < 1024; i++)
                chunk.BiomeContainer.Biomes.Add(0);

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
                        if(isCoal)
                        {
                            chunk.SetBlock(bx, by, bz, Registry.GetBlock(Materials.CoalOre));
                        }
                    }
                }
            }
        }
    }
}