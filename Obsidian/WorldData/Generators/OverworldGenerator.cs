using Obsidian.API;
using Obsidian.Utilities.Registry;
using Obsidian.WorldData.Generators.Overworld;
using Obsidian.WorldData.Generators.Overworld.Decorators;
using Obsidian.WorldData.Generators.Overworld.Terrain;
using System;
using System.Security.Cryptography;
using System.Text;

namespace Obsidian.WorldData.Generators
{
    public class OverworldGenerator : WorldGenerator
    {
        private OverworldTerrain terrainGen;
        public OverworldGenerator(string seed) : base("overworld")
        {
            var seedHash = BitConverter.ToInt32(MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(seed)));
            OverworldTerrainSettings generatorSettings = new OverworldTerrainSettings();
            generatorSettings.Seed = seedHash;
            terrainGen = new OverworldTerrain(generatorSettings);
        }

        public override Chunk GenerateChunk(int cx, int cz)
        {
            var chunk = new Chunk(cx, cz);

            // Build terrain map for this chunk
            var terrainHeightmap = new double[16, 16];
            var rockHeightmap = new double[16, 16];
            var bedrockHeightmap = new double[16, 16];

            for (int bx = 0; bx < 16; bx++)
            {
                for (int bz = 0; bz < 16; bz++)
                {
                    int worldX = bx + (cx << 4);
                    int worldZ = bz + (cz << 4);
                    terrainHeightmap[bx, bz] = terrainGen.GetValue(worldX, worldZ);
                    chunk.Heightmaps[ChunkData.HeightmapType.WorldSurface].Set(bx, bz, (int)terrainHeightmap[bx, bz]);
                    //chunk.Heightmaps[ChunkData.HeightmapType.OceanFloor].Set(bx, bz, noiseGen.OceanFloor(bx, bz));
                    rockHeightmap[bx, bz] = terrainGen.GetValue(worldX, worldZ) - 5;
                    bedrockHeightmap[bx, bz] = 3; // noiseGen.Bedrock(worldX, worldZ) + 1;

                    // Determine Biome
                    if (bx % 4 == 0 && bz % 4 == 0) // Biomes are in 4x4x4 blocks. Do a 2D array for now and just copy it vertically.
                    {
                        var b = ChunkBiome.GetBiome(worldX, worldZ, terrainGen);
                        for (int y = 0; y < 256; y += 4)
                        {
                            chunk.BiomeContainer.SetBiome(bx, y, bz, b);
                        }
                    }
                }
            }

            ChunkBuilder.FillChunk(chunk, terrainHeightmap, bedrockHeightmap);
            ChunkBuilder.CarveCaves(terrainGen, chunk, rockHeightmap, bedrockHeightmap);

            OverworldDecorator.Decorate(chunk, terrainHeightmap, terrainGen);
            //GenerateCoal(chunk, rockHeightmap);


            for (int bx = 0; bx < 16; bx++)
            {
                for (int bz = 0; bz < 16; bz++)
                {
                    chunk.Heightmaps[ChunkData.HeightmapType.MotionBlocking].Set(bx, bz, (int)terrainHeightmap[bx, bz]);
                }
            }
            return chunk;
        }



        /*private void GenerateCoal(Chunk chunk, double[,] rockHeighmap)
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
                        bool isCoal = terrainGen.Coal(worldX, by, worldZ);
                        if (isCoal)
                        {
                            chunk.SetBlock(bx, by, bz, Registry.GetBlock(Material.CoalOre));
                        }
                    }
                }
            }
        }*/
    }
}