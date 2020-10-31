using Obsidian.Blocks;
using Obsidian.ChunkData;
using Obsidian.Util.Registry;
using Obsidian.WorldData.Generators.Overworld;
using System;
using System.Linq;

namespace Obsidian.WorldData.Generators
{
    public class OverworldGenerator : WorldGenerator
    {
        private OverworldNoise noiseGen = new OverworldNoise();
        public OverworldGenerator() : base("overworld") {}

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
                        chunk.SetBlock(bx, 181, bz, Registry.GetBlock(Materials.LightBlueStainedGlass));

                    if (noiseGen.isMountain(bx + (cx * 16), bz + (cz * 16)))
                        chunk.SetBlock(bx, 180, bz, Registry.GetBlock(Materials.GrayStainedGlass));

                    if (noiseGen.isHills(bx + (cx * 16), bz + (cz * 16)))
                        chunk.SetBlock(bx, 179, bz, Registry.GetBlock(Materials.GreenStainedGlass));
                    
                    if (noiseGen.isPlains(bx + (cx * 16), bz + (cz * 16)))
                        chunk.SetBlock(bx, 178, bz, Registry.GetBlock(Materials.LimeStainedGlass));
                    
                    if (noiseGen.isBadlands(bx + (cx * 16), bz + (cz * 16)))
                        chunk.SetBlock(bx, 177, bz, Registry.GetBlock(Materials.BrownStainedGlass));
                }
            }

            var avgHeight = terrainHeights.Average();
            var heightStdDev = Math.Sqrt(terrainHeights.Average(v => Math.Pow(v - avgHeight, 2)));
            int biome = 0;
            if (avgHeight <= 45) { biome = (int) Biomes.DeepOcean; }
            else if (avgHeight <= 56) { biome = (int) Biomes.Ocean; }
            else if (avgHeight <=63)
            {
                if (heightStdDev > 0.6)
                {
                    biome = (int) Biomes.Badlands; 
                }
                else
                {
                    biome = (int) Biomes.Plains;
                }
            }
            else if (avgHeight <= 73)
            {
                if (heightStdDev > 1)
                {
                    biome = (int)Biomes.WoodedHills;
                }
                else
                {
                    biome = (int) Biomes.Forest;
                }
            }
            else if (avgHeight <= 85)
            {
                if (heightStdDev > 1.5)
                {
                    biome = (int) Biomes.MountainEdge;
                } else
                {
                    biome = (int) Biomes.GravellyMountains;
                }
            }
            else {  biome = (int) Biomes.Mountains; }

            for (int i = 0; i < 1024; i++)
                chunk.BiomeContainer.Biomes.Add(biome);

            ChunkBuilder.FillChunk(chunk, terrainHeightmap, rockHeightmap, bedrockHeightmap);

            GenerateCoal(chunk, rockHeightmap);
            ChunkBuilder.CarveCaves(noiseGen, chunk, rockHeightmap, bedrockHeightmap);
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