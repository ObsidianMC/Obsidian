using Obsidian.API;
using Obsidian.Blocks;
using Obsidian.ChunkData;
using Obsidian.Util.Registry;
using System.Runtime.CompilerServices;

namespace Obsidian
{
    public class SebastiansChunk
    {
        private SebastiansCube[] cubes = new SebastiansCube[cubesTotal];

        private const int cubesTotal = cubesHorizontal * cubesHorizontal * cubesVertical;
        private const int cubesHorizontal = 16 / SebastiansCube.width;
        private const int cubesVertical = 256 / SebastiansCube.height;
        private const int xMult = cubesTotal / cubesHorizontal;
        private const int zMult = cubesTotal / (cubesHorizontal * cubesHorizontal);

        public SebastiansChunk()
        {
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

        public Block GetBlock(Position position) => GetBlock((int)position.X, (int)position.Y, (int)position.Z);

        public Block GetBlock(int x, int y, int z)
        {
            short value = GetBlockId(x, y, z);
            return Registry.GetBlock(Registry.Ids[value]);
        }

        public short GetBlockId(int x, int y, int z)
        {
            if (x < 0) x = (x % 16) + 16;
            if (z < 0) z = (z % 16) + 16;
            
            return cubes[ComputeIndex(x, y, z)][x, y, z];
        }

        public void SetBlock(Position position, Block block) => this.SetBlock((int)position.X, (int)position.Y, (int)position.Z, block);

        public void SetBlock(int x, int y, int z, Block block)
        {
            var value = (short)block.Id;
            SetBlockId(x, y, z, value);
        }

        public void SetBlockId(int x, int y, int z, short id)
        {
            if (x < 0) x = (x % 16) + 16;
            if (z < 0) z = (z % 16) + 16;

            cubes[ComputeIndex(x, y, z)][x, y, z] = id;
        }

        public void CalculateHeightmap(Heightmap target)
        {
            for (int x = 0; x < cubesHorizontal * SebastiansCube.width; x++)
            {
                for (int z = 0; z < cubesHorizontal * SebastiansCube.width; z++)
                {
                    for (int y = cubesVertical * SebastiansCube.height - 1; y >= 0; y--)
                    {
                        if (Block.IsIdAir(GetBlockId(x, y, z)))
                            continue;

                        target.Set(x, z, value: y);
                        break;
                    }
                }
            }
        }

        public void CheckHomogeneity()
        {
            for (int i = 0; i < cubes.Length; i++)
            {
                cubes[i].CheckHomogeneity();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ComputeIndex(int x, int y, int z)
        {
            return x / SebastiansCube.width * xMult + z / SebastiansCube.width * zMult + y / cubesVertical;
        }
    }
}
