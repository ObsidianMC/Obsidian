using Obsidian.BlockData;
using Obsidian.Net;
using Obsidian.Util.Collection;
using System.Threading.Tasks;

namespace Obsidian.ChunkData
{
    public class BlockStateContainer
    {
        // private const byte BitsPerEntry = 14;

        public byte BitsPerBlock { get; }

        public DataArray BlockStorage { get; set; }

        public IBlockStatePalette Palette { get; }

        public BlockStateContainer(byte bitsPerBlock = 14)
        {
            this.BitsPerBlock = bitsPerBlock;

            this.BlockStorage = new DataArray(bitsPerBlock);

            this.Palette = bitsPerBlock.DeterminePalette();
        }

        public void Set(int x, int y, int z, BlockState blockState)
        {
            var blockIndex = this.GetIndex(x, y, z);

            int paletteIndex = this.Palette.GetIdFromState((Block)blockState);

            this.BlockStorage[blockIndex] = paletteIndex;
        }

        public void Set(double x, double y, double z, BlockState blockState) => this.Set((int)x, (int)y, (int)z, blockState);

        public BlockState Get(int x, int y, int z)
        {
            int storageId = this.BlockStorage[this.GetIndex(x, y, z)];

            return this.Palette.GetStateFromIndex(storageId);
        }

        public int GetIndex(int x, int y, int z) => ((y * 16) + z) * 16 + x;

        //private int GetIndex(int x, int y, int z) => (y << 8) | (z << 4) | x;

        public async Task WriteToAsync(MinecraftStream stream)
        {
            await stream.WriteByteAsync((sbyte)this.BitsPerBlock);

            await this.Palette.WriteToAsync(stream);

            await stream.WriteVarIntAsync(this.BlockStorage.Storage.Length);
            await stream.WriteLongArrayAsync(this.BlockStorage.Storage);
        }
    }
}
