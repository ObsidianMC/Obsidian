using Obsidian.Blocks;
using Obsidian.ChunkData;
using Obsidian.Nbt.Tags;
using Obsidian.Util.DataTypes;
using Obsidian.Util.Registry;
using System;
using System.Collections.Generic;

namespace Obsidian.World
{
    public class Chunk
    {
        public int X { get; }
        public int Z { get; }

        public Block[,,] Blocks { get; private set; } = new Block[16, 16, 16];

        public List<ChunkSection> Sections { get; private set; } = new List<ChunkSection>();
        public List<int> Biomes { get; private set; } = new List<int>(16 * 16);
        public List<NbtTag> BlockEntities { get; private set; } = new List<NbtTag>();

        public Dictionary<string, Heightmap> Heightmaps { get; private set; } = new Dictionary<string, Heightmap>();

        public Chunk(int x, int z)
        {
            this.X = x;
            this.Z = z;

            for (int chunkX = 0; chunkX < 16; chunkX++)
            {
                for (int chunkY = 0; chunkY < 16; chunkY++)
                {
                    for (int chunkZ = 0; chunkZ < 16; chunkZ++)
                    {
                        this.Blocks[chunkX, chunkY, chunkZ] = BlockRegistry.GetBlock(Materials.Air);
                    }
                }
            }

            this.Heightmaps.Add("MOTION_BLOCKING", new Heightmap("MOTION_BLOCKING", this));
        }

        public void CalculateHeightmap()
        {
            for (int y = 15; y >= 0; y--)
            {
                var section = this.Sections[y];

                if (section == null)
                    continue;

                for (int x = 0; x < 16; x++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        if (this.Sections[y].GetHighestBlock(X, z) != 0)
                            continue;

                        int height = section.GetHighestBlock(x, z);

                        if (height == -1)
                            continue;

                        this.Heightmaps["MOTION_BLOCKING"].Set(x, z, y * 16 + height + 1);
                    }
                }
            }
        }

        public Block GetBlock(Position position) => this.GetBlock((int)position.X, (int)position.Y, (int)position.Z);

        public Block GetBlock(int x, int y, int z) => this.Blocks[x, y, z];

        public void SetBlock(Position position, Block block) => this.SetBlock((int)position.X, (int)position.Y, (int)position.Z, block);

        public void SetBlock(int x, int y, int z, Block block)
        {
            this.Blocks[x, y, z] = block;

            this.Heightmaps["MOTION_BLOCKING"].Update(x, y, z, block);
        }

        internal void AddSection(ChunkSection section) => this.Sections.Add(section);
    }
}