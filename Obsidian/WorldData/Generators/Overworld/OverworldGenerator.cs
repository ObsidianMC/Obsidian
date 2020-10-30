using Obsidian.Blocks;
using Obsidian.Util.Registry;
using Obsidian.WorldData.Generators.Overworld;
using System;

namespace Obsidian.WorldData.Generators
{
    public class OverworldGenerator : WorldGenerator
    {
        private OverworldTerrain terrain = new OverworldTerrain();
        public OverworldGenerator() : base("overworld") {}

        public override Chunk GenerateChunk(int cx, int cz)
        {
            var chunk = new Chunk(cx, cz);

            // Build terrain map for this chunk
            var terrainHeightmap = new double[16, 16];
            var rockHeightmap = new double[16, 16];
            var bedrockHeightmap = new double[16, 16];
            double tY = 60;
            double rY = 2;
            double bY = 1;
            for (int bx=0; bx<16; bx++)
            {
                for (int bz=0; bz<16; bz++)
                {
                    tY = terrain.Terrain(bx + (cx * 16), bz + (cz * 16));
                    rY = terrain.Underground(bx + (cx * 16), bz + (cz * 16)) - 5 + tY;
                    bY = terrain.Bedrock(bx + (cx * 16), bz + (cz * 16)) + 1;

                    terrainHeightmap[bx, bz] = tY;
                    rockHeightmap[bx, bz] = rY;
                    bedrockHeightmap[bx, bz] = bY;
                }
            }

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
                        if (by <= rockHeightmap[bx, bz])
                        {
                            chunk.SetBlock(bx, by, bz, Registry.GetBlock(Materials.Stone));
                            continue;
                        }

                        Materials m = Materials.WetSponge;

                        // Ocean/River
                        if (terrainY <= 60)
                        {
                            if (by > terrainY)
                            {
                                m = Materials.Water;
                            }
                            else if (by <= terrainY)
                            {
                                m = Materials.Gravel;
                            }
                        }
                        
                        // Beach
                        else if (terrainY < 63.85) // magic decimals are for blending
                        { 
                            m = Materials.Sand;
                        }

                        // Grass
                        else if (terrainY < 88.35)
                        {
                            if (by == (int)terrainY)
                            {
                                m = Materials.GrassBlock;
                                //chunk.SetBlock(bx, by+1, bz, Registry.GetBlock(Materials.Grass));
                            }
                            else
                            {
                                m = Materials.Dirt;
                            }
                        }
                        
                        // Mountains
                        else if (terrainY < 94.35)
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

            GenerateCoal(chunk, rockHeightmap);
            CarveCaves(chunk, terrainHeightmap, bedrockHeightmap);
            return chunk;
        }

        private void CarveCaves(Chunk chunk, double[,] thm, double[,] bhm)
        {
            for (int bx = 0; bx < 16; bx++)
            {
                for (int bz = 0; bz < 16; bz++)
                {
                    int tY = (int)thm[bx, bz];
                    int brY = Math.Min((int)bhm[bx, bz], 70);
                    for (int by = brY; by < tY; by++)
                    {
                        bool caveAir = terrain.Cave(bx + (chunk.X * 16), by, bz + (chunk.Z * 16));
                        if (caveAir)
                        {
                            chunk.SetBlock(bx, by, bz, Registry.GetBlock(Materials.CaveAir));
                        }
                    }                    
                }
            }
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
                        bool isCoal = terrain.Coal(worldX, by, worldZ);
                        if(isCoal)
                        {
                            chunk.SetBlock(bx, by, bz, Registry.GetBlock(Materials.GrassBlock));
                        }
                    }
                }
            }
        }
    }
}