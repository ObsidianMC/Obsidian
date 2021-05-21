using Obsidian.API;
using Obsidian.Blocks;
using Obsidian.ChunkData;
using Obsidian.Nbt;
using Obsidian.Util;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Obsidian.WorldData
{
    public class Chunk
    {
        public int X { get; }
        public int Z { get; }

        public BiomeContainer BiomeContainer { get; private set; } = new BiomeContainer();

        private readonly Cube[] cubes = new Cube[cubesTotal];
        private const int cubesTotal = cubesHorizontal * cubesHorizontal * cubesVertical;
        private const int cubesHorizontal = 16 / Cube.width;
        private const int cubesVertical = 256 / Cube.height;
        private const int xMult = cubesTotal / cubesHorizontal;
        private const int zMult = cubesTotal / (cubesHorizontal * cubesHorizontal);

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

            int index = 0;
            for (int x = 0; x < cubesHorizontal; x++)
            {
                for (int z = 0; z < cubesHorizontal; z++)
                {
                    for (int y = 0; y < cubesVertical; y++, index++)
                    {
                        cubes[index] = new Cube(x * Cube.width, y * Cube.height, z * Cube.width);
                    }
                }
            }
        }

        public Block GetBlock(Vector position) => GetBlock(position.X, position.Y, position.Z);

        public Block GetBlock(int x, int y, int z)
        {
            short value = GetBlockStateId(x, y, z);
            return new Block(value);
        }

        public void SetBlock(Vector position, Block block) => SetBlock(position.X, position.Y, position.Z, block);

        public void SetBlock(int x, int y, int z, Block block)
        {
            SetBlockStateId(x, y, z, block.StateId);

            x = Helpers.Modulo(x, 16);
            z = Helpers.Modulo(z, 16);

            Sections[y >> 4].SetBlock(x, y & 15, z, block);
        }


        public BlockMeta GetBlockMeta(int x, int y, int z)
        {
            x = Helpers.Modulo(x, 16);
            z = Helpers.Modulo(z, 16);
            var value = (short)((x << 8) | (z << 4) | y);

            return this.BlockMetaStore.GetValueOrDefault(value);
        }

        public BlockMeta GetBlockMeta(Vector position) => this.GetBlockMeta((int)position.X, (int)position.Y, (int)position.Z);

        public void SetBlockMeta(int x, int y, int z, BlockMeta meta)
        {
            x = Helpers.Modulo(x, 16);
            z = Helpers.Modulo(z, 16);
            var value = (short)((x << 8) | (z << 4) | y);

            this.BlockMetaStore[value] = meta;
        }

        public void SetBlockMeta(Vector position, BlockMeta meta) => this.SetBlockMeta((int)position.X, (int)position.Y, (int)position.Z, meta);

        public void CalculateHeightmap()
        {
            Heightmap target = Heightmaps[HeightmapType.MotionBlocking];
            for (int x = 0; x < cubesHorizontal * Cube.width; x++)
            {
                for (int z = 0; z < cubesHorizontal * Cube.width; z++)
                {
                    for (int y = cubesVertical * Cube.height - 1; y >= 0; y--)
                    {
                        var block = new Block(GetBlockStateId(x, y, z));
                        if (block.IsAir)
                            continue;

                        target.Set(x, z, value: y);
                        break;
                    }
                }
            }
        }

        public short GetBlockStateId(int x, int y, int z)
        {
            x %= 16;
            z %= 16;
            if (x < 0) x += 16;
            if (z < 0) z += 16;

            return cubes[ComputeIndex(x, y, z)][x, y, z];
        }

        public void SetBlockStateId(int x, int y, int z, short id)
        {
            x %= 16;
            z %= 16;
            if (x < 0) x += 16;
            if (z < 0) z += 16;

            cubes[ComputeIndex(x, y, z)][x, y, z] = id;
        }

        public void CheckHomogeneity()
        {
            for (int i = 0; i < cubesTotal; i++)
            {
                cubes[i].CheckHomogeneity();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ComputeIndex(int x, int y, int z)
        {
            return x / Cube.width * xMult + z / Cube.width * zMult + y / cubesVertical;
        }
    }
}