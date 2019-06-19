using Obsidian.BlockData;
using Obsidian.Net;
using Obsidian.Util;
using System.Threading.Tasks;

namespace Obsidian.ChunkData
{

    public class ChunkSection : ISerializable
    {
        public BlockStateContainer BlockStateContainer = new BlockStateContainer();
        public NibbleArray BlockLightArray = new NibbleArray(16 * 16 * 16);
        public NibbleArray SkyLightArray = new NibbleArray(16 * 16 * 16);

        public bool Overworld = true;//TODO

        public async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WriteAsync(await BlockStateContainer.ToArrayAsync());
                await stream.WriteAsync(BlockLightArray.Data);

                if (Overworld)
                {
                    await stream.WriteAsync(SkyLightArray.Data);
                }

                return stream.ToArray();
            }
        }
    }

    public class BlockStateContainer
    {
        private IBlockStatePalette Palette;
        private BitArray Storage;

        private int BitCount = 0;

        public BlockStateContainer()
        {
            SetBitCount(14);
        }

        private void SetBitCount(int newBitCount)
        {
            if (newBitCount == BitCount) return;

            if (newBitCount <= 8)
            {
                if (newBitCount <= 4)
                {
                    BitCount = 4;
                }
                else
                {
                    BitCount = newBitCount;
                }
                Palette = new LinearBlockStatePalette(BitCount);
            }
            else
            {
                BitCount = 14; // TODO once you add all the blocks do the log thing to find this
                Palette = new GlobalBlockStatePalette();
            }

            Palette.IdFromState(Blocks.Air); // Air is the default block
            Storage = new BitArray(BitCount, 16 * 16 * 16);
        }

        private void Resize(int newBitCount)
        {
            BitArray oldBitArray = this.Storage;
            IBlockStatePalette oldPalette = this.Palette;
            SetBitCount(newBitCount);

            for (int i = 0; i < oldBitArray.ArraySize; i++)
            {
                BlockState blockState = oldPalette.StateFromIndex((int)oldBitArray[i]);

                if (blockState != null)
                {
                    this.Set(i, blockState);
                }
            }
        }

        public void Set(int x, int y, int z, BlockState blockState)
        {
            Set(GetIndex(x, y, z), blockState);
        }

        public BlockState Get(int x, int y, int z)
        {
            return Get(GetIndex(x, y, z));
        }

        private void Set(int index, BlockState blockState)
        {
            int paletteId = this.Palette.IdFromState(blockState);
            if (paletteId == -1)
            {
                this.Resize(BitCount + 1);
                this.Set(index, blockState);
                return;
            }

            this.Storage.Set(index, paletteId);
        }

        private BlockState Get(int index)
        {
            return this.Palette.StateFromIndex(this.Storage.Get(index));
        }

        private int GetIndex(int x, int y, int z) => (y << 8) | (z << 4) | x;

        public async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WriteByteAsync((sbyte)BitCount);
                await stream.WriteAsync(await this.Palette.ToArrayAsync());

                await stream.WriteVarIntAsync(this.Storage.LongArray.Length);
                await stream.WriteLongArrayAsync(this.Storage.LongArray);

                return stream.ToArray();
            }
        }
    }
}
