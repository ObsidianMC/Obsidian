using Obsidian.Blocks;
using Obsidian.Util.Collection;
using Obsidian.Util.DataTypes;
using Obsidian.Util.Extensions;
using Obsidian.WorldData;
using System;

namespace Obsidian.ChunkData
{
    //TODO make better impl of heightmaps
    public class Heightmap
    {
        public const string MOTION_BLOCKING = "MOTION_BLOCKING";
        public string HeightmapType { get; set; }//TODO turn this into an enum

        internal readonly DataArray data = new DataArray(9, 256);

        private Chunk chunk;

        public Predicate<BlockState> Predicate;

        public Heightmap(string type, Chunk chunk)
        {
            this.HeightmapType = type;
            this.chunk = chunk;

            if (type == MOTION_BLOCKING)
                this.Predicate = (block) => block.NotAir() || block.NotFluid();
        }

        public bool Update(int x, int y, int z, BlockState blockState)
        {
            int height = this.GetHeight(x, z);

            if (y <= height - 2)
                return false;

            if (this.Predicate(blockState))
            {
                if (y >= height)
                {
                    this.Set(x, z, y + 1);
                    return true;
                }
            }
            else if (height - 1 == y)
            {
                Position pos;

                for (int i = y - 1; i >= 0; --i)
                {
                    pos = new Position(x, i, z);
                    var otherBlock = this.chunk.GetBlock(pos);

                    if (this.Predicate(otherBlock))
                    {
                        this.Set(x, z, i + 1);

                        return true;
                    }
                }

                this.Set(x, z, 0);

                return true;
            }

            return false;
        }

        public void Set(int x, int z, int value) => this.data[this.GetIndex(x, z)] = value;

        public int GetHeight(int x, int z) => this.GetHeight(this.GetIndex(x, z));

        private int GetHeight(int value) => this.data[value];

        private int GetIndex(int x, int z) => x + z * 16;

        public ulong[] GetDataArray()
        {
            return this.data.Storage;
        }
    }
}
