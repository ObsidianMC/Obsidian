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

        public Block GetBlock(int x, int y, int z)
        {
            short value = GetBlockStateId(x, y, z);
            return new Block(value);
        }

        public short GetBlockStateId(int x, int y, int z)
        {
            x %= 16;
            z %= 16;
            if (x < 0) x += 16;
            if (z < 0) z += 16;
            
            return cubes[ComputeIndex(x, y, z)][x, y, z];
        }

        public void SetBlock(int x, int y, int z, Block block)
        {
            SetBlockStateId(x, y, z, block.StateId);
        }

        public void SetBlockStateId(int x, int y, int z, short id)
        {
            x %= 16;
            z %= 16;
            if (x < 0) x += 16;
            if (z < 0) z += 16;

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
                        var block = new Block(GetBlockStateId(x, y, z));
                        if (block.IsAir)
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
