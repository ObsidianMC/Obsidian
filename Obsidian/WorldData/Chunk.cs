using Obsidian.ChunkData;
using Obsidian.Nbt.Tags;
using Obsidian.API;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Obsidian.Blocks;
using Obsidian.Util;

namespace Obsidian.WorldData
{
    public class Chunk
    {
        public int X { get; }
        public int Z { get; }

        public BiomeContainer BiomeContainer { get; private set; } = new BiomeContainer();

        private SebastiansCube[] cubes = new SebastiansCube[cubesTotal];
        private const int cubesTotal = cubesHorizontal * cubesHorizontal * cubesVertical;
        private const int cubesHorizontal = 16 / SebastiansCube.width;
        private const int cubesVertical = 256 / SebastiansCube.height;
        private const int xMult = cubesTotal / cubesHorizontal;
        private const int zMult = cubesTotal / (cubesHorizontal * cubesHorizontal);

        public Dictionary<short, BlockMeta> BlockMetaStore { get; private set; } = new Dictionary<short, BlockMeta>();

        public ChunkSection[] Sections { get; private set; } = new ChunkSection[16];
        public List<NbtTag> BlockEntities { get; private set; } = new List<NbtTag>();

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
                        cubes[index] = new SebastiansCube(x * SebastiansCube.width, y * SebastiansCube.height, z * SebastiansCube.width);
                    }
                }
            }
        }

        public Block GetBlock(PositionF position) => GetBlock((int)position.X, (int)position.Y, (int)position.Z);

        public Block GetBlock(int x, int y, int z)
        {
            short value = GetBlockStateId(x, y, z);
            return new Block(value);
        }

        public void SetBlock(PositionF position, Block block) => SetBlock((int)position.X, (int)position.Y, (int)position.Z, block);

        public void SetBlock(int x, int y, int z, Block block)
        {
            SetBlockStateId(x, y, z, block.StateId);
            Sections[y >> 4].SetBlock(x, y & 15, z, block);
        }


        public BlockMeta GetBlockMeta(int x, int y, int z)
        {
            x = Helpers.Modulo(x, 16);
            z = Helpers.Modulo(z, 16);
            var value = (short)((x << 8) | (z << 4) | y);

            return this.BlockMetaStore.GetValueOrDefault(value);
        }

        public BlockMeta GetBlockMeta(Position position) => this.GetBlockMeta((int)position.X, (int)position.Y, (int)position.Z);

        public void SetBlockMeta(int x, int y, int z, BlockMeta meta)
        {
            x = Helpers.Modulo(x, 16);
            z = Helpers.Modulo(z, 16);
            var value = (short)((x << 8) | (z << 4) | y);

            this.BlockMetaStore[value] = meta;
        }

        public void SetBlockMeta(Position position, BlockMeta meta) => this.SetBlockMeta((int)position.X, (int)position.Y, (int)position.Z, meta);

        public void CalculateHeightmap()
        {
            Heightmap target = Heightmaps[HeightmapType.MotionBlocking];
            for (int x = 0; x < cubesHorizontal * SebastiansCube.width; x++)
            {
                for (int z = 0; z < cubesHorizontal * SebastiansCube.width; z++)
                {
                    for (int y = cubesVertical * SebastiansCube.height - 1; y >= 0; y--)
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
            return x / SebastiansCube.width * xMult + z / SebastiansCube.width * zMult + y / cubesVertical;
        }
    }
}