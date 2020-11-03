using Obsidian.API;
using Obsidian.Blocks;
using Obsidian.ChunkData;
using Obsidian.Util.Registry;

namespace Obsidian
{
    public class SebastiansChunk
    {
        private SebastiansCube[] cubes = new SebastiansCube[cubesHorizontal * cubesHorizontal * cubesVertical];

        private const int cubesHorizontal = 16 / SebastiansCube.width;
        private const int cubesVertical = 256 / SebastiansCube.height;

        private const int xDiv = cubesHorizontal * 16;
        private const int zDiv = cubesHorizontal * 16;
        private const int yDiv = cubesVertical;

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
            short value = GetValue(x, y, z);
            return Registry.GetBlock(value);
        }

        private short GetValue(int x, int y, int z)
        {
            return cubes[x / xDiv + z / zDiv + y / yDiv][x, y, z];
        }

        public void SetBlock(Position position, Block block) => this.SetBlock((int)position.X, (int)position.Y, (int)position.Z, block);

        public void SetBlock(int x, int y, int z, Block block)
        {
            var value = (short)block.Id;
            cubes[x / xDiv + z / zDiv + y / yDiv][x, y, z] = value;
        }

        public void CalculateHeightmap(Heightmap target)
        {
            for (int x = 0; x < cubesHorizontal * SebastiansCube.width; x++)
            {
                for (int z = 0; z < cubesHorizontal * SebastiansCube.width; z++)
                {
                    for (int y = cubesVertical * SebastiansCube.height; y >= 0; y--)
                    {
                        if (Block.IsIdAir(GetValue(x, y, z)))
                            continue;

                        target.Set(x, z, value: y);
                        break;
                    }
                }
            }
        }
    }
}
