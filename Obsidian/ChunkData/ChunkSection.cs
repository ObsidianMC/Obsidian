using Obsidian.API;
using Obsidian.Blocks;
using Obsidian.Util.Collection;

namespace Obsidian.ChunkData
{
    public class ChunkSection : BlockStateContainer
    {
        public NibbleArray BlockLightArray = new NibbleArray(16 * 16 * 16);
        public NibbleArray SkyLightArray = new NibbleArray(16 * 16 * 16);

        public bool Overworld = true;

        public int? YBase { get; }
        public override byte BitsPerBlock { get; }
        public override DataArray BlockStorage { get;  }

        public override IBlockStatePalette Palette { get; }

        public ChunkSection(byte bitsPerBlock = 4, int? yBase = null)
        {
            this.BitsPerBlock = bitsPerBlock;

            this.BlockStorage = new DataArray(bitsPerBlock, 4096);

            this.Palette = bitsPerBlock.DeterminePalette();

            this.YBase = YBase;
        }

        public Block GetBlock(Position pos) => this.GetBlock((int)pos.X, (int)pos.Y, (int)pos.Z);
        public Block GetBlock(int x, int y, int z) => (Block)this.Get(x, y, z);

        public void SetBlock(Position pos, BlockState blockState) => this.SetBlock((int)pos.X, (int)pos.Y, (int)pos.Z, blockState);
        public void SetBlock(int x, int y, int z, BlockState blockState) => this.Set(x, y, z, blockState);

        public int GetHighestBlock(int x, int z)
        {
            for (int y = 15; y >= 0; y--)
            {
                var block = this.Get(x, y, z);

                if (block != null)
                    return y;
            }

            return -1;
        }

        public ChunkSection FillWithLight()
        {
            for (int i = 0; i < this.BlockLightArray.Data.Length; i++)
                this.BlockLightArray.Data[i] = 255;

            for (int i = 0; i < this.SkyLightArray.Data.Length; i++)
                this.SkyLightArray.Data[i] = 255;

            return this;
        }
    }
}