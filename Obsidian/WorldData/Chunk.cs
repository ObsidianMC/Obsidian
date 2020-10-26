using Obsidian.Blocks;
using Obsidian.ChunkData;
using Obsidian.Nbt.Tags;
using Obsidian.Util.DataTypes;
using Obsidian.Util.Registry;
using System.Collections.Generic;

namespace Obsidian.WorldData
{
    public class Chunk
    {
        public int X { get; }
        public int Z { get; }

        public BiomeContainer BiomeContainer { get; private set; } = new BiomeContainer();

        public Dictionary<short, Block> Blocks { get; private set; } = new Dictionary<short, Block>();

        public ChunkSection[] Sections { get; private set; } = new ChunkSection[16];
        public List<NbtTag> BlockEntities { get; private set; } = new List<NbtTag>();

        public Dictionary<HeightmapType, Heightmap> Heightmaps { get; private set; } = new Dictionary<HeightmapType, Heightmap>();

        public Chunk(int x, int z)
        {
            this.X = x;
            this.Z = z;

            this.Heightmaps.Add(HeightmapType.MotionBlocking, new Heightmap(HeightmapType.MotionBlocking, this));

            this.Init();
        }

        private void Init()
        {
            for (int i = 0; i < 16; i++)
                this.Sections[i] = new ChunkSection();

            for (int blockX = 0; blockX < 16; blockX++)
            {
                for (int blockY = 0; blockY < 256; blockY++)
                {
                    for (int blockZ = 0; blockZ < 16; blockZ++)
                    {
                        this.SetBlock(blockX, blockY, blockZ, Registry.GetBlock(Materials.Air));
                    }
                }
            }
        }

        public Block GetBlock(Position position) => this.GetBlock((int)position.X, (int)position.Y, (int)position.Z);

        public Block GetBlock(int x, int y, int z)
        {
            var value = (short)((x << 8) | (z << 4) | y);
            return this.Blocks.GetValueOrDefault(value) ?? this.Sections[y >> 4].GetBlock(x, y, z);
        }

        public void SetBlock(Position position, Block block) => this.SetBlock((int)position.X, (int)position.Y, (int)position.Z, block);

        public void SetBlock(int x, int y, int z, Block block)
        {
            var value = (short)((x << 8) | (z << 4) | y);

            this.Blocks[value] = block; this.Blocks[value] = block;

            this.Sections[y >> 4].SetBlock(x, y & 15, z, block);
        }

        public void CalculateHeightmap()
        {
            for (int x = 0; x < 16; x++)
            {
                for (int z = 0; z < 16; z++)
                {
                    var key = (short)((x << 8) | (z << 4) | 255);
                    for (int y = 255; y >= 0; y--, key--)
                    {
                        if (this.Blocks.TryGetValue(key, out var block))
                        {
                            if (block.IsAir)
                                continue;

                            this.Heightmaps[HeightmapType.MotionBlocking].Set(x, z, y);
                            break;
                        }
                    }
                }
            }
        }
    }
}