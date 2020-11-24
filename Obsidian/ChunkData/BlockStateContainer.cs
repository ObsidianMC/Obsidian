using Obsidian.Blocks;
using Obsidian.Net;
using Obsidian.Util.Collection;
using System.Threading.Tasks;

namespace Obsidian.ChunkData
{
    public abstract class BlockStateContainer
    {
        public abstract byte BitsPerBlock { get; }

        public abstract DataArray BlockStorage { get; }

        public abstract IBlockStatePalette Palette { get; internal set; }

        protected void Set(int x, int y, int z, BlockState blockState)
        {
            y %= 16;
            var blockIndex = this.GetIndex(x, y, z);

            int paletteIndex = this.Palette.GetIdFromState((Block)blockState);

            this.BlockStorage[blockIndex] = paletteIndex;
        }

        protected void Set(double x, double y, double z, BlockState blockState) => this.Set((int)x, (int)y, (int)z, blockState);

        protected BlockState Get(int x, int y, int z)
        {
            y %= 16;
            int storageId = this.BlockStorage[this.GetIndex(x, y, z)];

            return this.Palette.GetStateFromIndex(storageId);
        }

        public int GetIndex(int x, int y, int z) => ((y * 16) + z) * 16 + x;

        public async Task WriteToAsync(MinecraftStream stream)
        {
            short validBlockCount = 0;
            for (int x = 0; x < 16; x++)
            {
                for (int y = 0; y < 16; y++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        var block = this.Get(x, y, z);

                        if (block != null && !block.IsAir)
                            validBlockCount++;
                    }
                }
            }

            await stream.WriteShortAsync(validBlockCount);
            await stream.WriteUnsignedByteAsync(this.BitsPerBlock);

            await this.Palette.WriteToAsync(stream);

            await stream.WriteVarIntAsync(this.BlockStorage.Storage.Length);
            await stream.WriteLongArrayAsync(this.BlockStorage.Storage);
        }
    }
}
