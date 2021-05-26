using Obsidian.API;
using Obsidian.Utilities.Collection;

namespace Obsidian.ChunkData
{
    public class ChunkSection : BlockStateContainer
    {
        public bool Overworld = true;

        public int? YBase { get; }
        public override byte BitsPerBlock { get; }
        public override DataArray BlockStorage { get; }

        public override IBlockStatePalette Palette { get; internal set; }

        public ChunkSection(byte bitsPerBlock = 4, int? yBase = null)
        {
            this.BitsPerBlock = bitsPerBlock;

            this.BlockStorage = new DataArray(bitsPerBlock, 4096);

            this.Palette = bitsPerBlock.DeterminePalette();

            this.YBase = yBase;

            this.FillWithAir();
        }

        public Block GetBlock(Vector position) => this.GetBlock(position.X, position.Y, position.Z);
        public Block GetBlock(int x, int y, int z) => this.Get(x, y, z);

        public void SetBlock(Vector position, Block block) => this.SetBlock(position.X, position.Y, position.Z, block);
        public void SetBlock(int x, int y, int z, Block block) => this.Set(x, y, z, block);

        private void FillWithAir()
        {
            var air = Block.Air;
            for (int x = 0; x < 16; x++)
            {
                for (int y = 0; y < 16; y++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        this.Set(x, y, z, air);
                    }
                }
            }
        }
    }
}