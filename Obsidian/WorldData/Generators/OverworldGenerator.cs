using Obsidian.Blocks;
using Obsidian.Util.Registry;
using Obsidian.WorldData.Generators.Overworld;
using Obsidian.WorldData.Generators.Overworld.Decorators;
using System;
using System.Security.Cryptography;
using System.Text;

namespace Obsidian.WorldData.Generators
{
    public class OverworldGenerator : WorldGenerator
    {
        private OverworldNoise noiseGen;
        public OverworldGenerator(string seed) : base("overworld") 
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

            for (int bx=0; bx<16; bx++)
            {
                for (int bz=0; bz<16; bz++)
                {
                    int worldX = bx + (cx << 4);
                    int worldZ = bz + (cz << 4);
                    terrainHeightmap[bx, bz] = noiseGen.Terrain(worldX, worldZ);
                    rockHeightmap[bx, bz] = noiseGen.Underground(worldX, worldZ) + terrainHeightmap[bx, bz] - 5;
                    bedrockHeightmap[bx, bz] = noiseGen.Bedrock(worldX, worldZ) + 1;

                    // Determine Biome

                    if (bx % 4 == 0 && bz % 4 == 0) // Biomes are in 4x4x4 blocks. Do a 2D array for now and just copy it vertically.
                    {
                        var b = ChunkBiome.GetBiome(worldX, worldZ, noiseGen);
                        for (int y = 0; y < 256; y+=4)
                        {
                            chunk.BiomeContainer.SetBiome(bx, y, bz, b);
                        }
                    }
                }
            }

            ChunkBuilder.FillChunk(chunk, terrainHeightmap, rockHeightmap, bedrockHeightmap);
            OverworldDecorator.Decorate(chunk, terrainHeightmap, noiseGen);
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