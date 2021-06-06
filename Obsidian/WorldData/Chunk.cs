using Obsidian.API;
using Obsidian.Blocks;
using Obsidian.ChunkData;
using Obsidian.Nbt;
using Obsidian.Utilities;
using System.Collections.Generic;

namespace Obsidian.WorldData
{
    public class Chunk
    {
        public int X { get; }
        public int Z { get; }

        public BiomeContainer BiomeContainer { get; private set; } = new BiomeContainer();

        private const int width = 16;
        private const int height = 16;
        private const int cubesHorizontal = 16 / width;
        private const int cubesVertical = 256 / height;
        
        public Dictionary<short, BlockMeta> BlockMetaStore { get; private set; } = new Dictionary<short, BlockMeta>();

        public ChunkSection[] Sections { get; private set; } = new ChunkSection[16];
        public List<INbtTag> BlockEntities { get; private set; } = new List<INbtTag>();

        public Dictionary<HeightmapType, Heightmap> Heightmaps { get; private set; } = new Dictionary<HeightmapType, Heightmap>();

        public Chunk(int x, int z)
        {
            this.X = x;
            this.Z = z;

            this.Heightmaps.Add(HeightmapType.MotionBlocking, new Heightmap(HeightmapType.MotionBlocking, this));
            this.Heightmaps.Add(HeightmapType.OceanFloor, new Heightmap(HeightmapType.OceanFloor, this));
            this.Heightmaps.Add(HeightmapType.WorldSurface, new Heightmap(HeightmapType.WorldSurface, this));

            this.Init();
        }

        private void Init()
        {
            for (int i = 0; i < 16; i++)
                this.Sections[i] = new ChunkSection(4, i);
        }

        public Block GetBlock(Vector position) => GetBlock(position.X, position.Y, position.Z);

        public Block GetBlock(int x, int y, int z)
        {
            x = NumericsHelper.Modulo(x, 16);
            z = NumericsHelper.Modulo(z, 16);

            return Sections[y >> 4].GetBlock(x, y & 15, z);
        }

        public void SetBlock(Vector position, Block block) => SetBlock(position.X, position.Y, position.Z, block);

        public void SetBlock(int x, int y, int z, Block block)
        {
            x = NumericsHelper.Modulo(x, 16);
            z = NumericsHelper.Modulo(z, 16);

            Sections[y >> 4].SetBlock(x, y & 15, z, block);
        }


        public BlockMeta GetBlockMeta(int x, int y, int z)
        {
            x = NumericsHelper.Modulo(x, 16);
            z = NumericsHelper.Modulo(z, 16);
            var value = (short)((x << 8) | (z << 4) | y);

            return this.BlockMetaStore.GetValueOrDefault(value);
        }

        public BlockMeta GetBlockMeta(Vector position) => this.GetBlockMeta((int)position.X, (int)position.Y, (int)position.Z);

        public void SetBlockMeta(int x, int y, int z, BlockMeta meta)
        {
            x = NumericsHelper.Modulo(x, 16);
            z = NumericsHelper.Modulo(z, 16);
            var value = (short)((x << 8) | (z << 4) | y);

            this.BlockMetaStore[value] = meta;
        }

        public void SetBlockMeta(Vector position, BlockMeta meta) => this.SetBlockMeta((int)position.X, (int)position.Y, (int)position.Z, meta);

        public void CalculateHeightmap()
        {
            Heightmap target = Heightmaps[HeightmapType.MotionBlocking];
            for (int x = 0; x < cubesHorizontal * width; x++)
            {
                for (int z = 0; z < cubesHorizontal * width; z++)
                {
                    for (int y = cubesVertical * height - 1; y >= 0; y--)
                    {
                        var block = this.GetBlock(x, y, z);
                        if (block.IsAir)
                            continue;

                        target.Set(x, z, value: y);
                        break;
                    }
                }
            }
        }
    }
}