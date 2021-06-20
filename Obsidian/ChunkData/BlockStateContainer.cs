using Obsidian.API.Blocks;
using Obsidian.Net;
using Obsidian.Utilities.Collection;
using System.Threading.Tasks;

namespace Obsidian.ChunkData
{
    public abstract class BlockStateContainer
    {
        public abstract byte BitsPerBlock { get; }

        public abstract DataArray BlockStorage { get; }

        public abstract IBlockStatePalette Palette { get; internal set; }

        protected bool Set(int x, int y, int z, Block blockState)
        {
            y %= 16;
            var blockIndex = GetIndex(x, y, z);

            int paletteIndex = this.Palette.GetIdFromState(blockState);
            if (paletteIndex == -1) { return false; }

            this.BlockStorage[blockIndex] = paletteIndex;
            return true;
        }

        protected Block Get(int x, int y, int z)
        {
            y %= 16;
            int storageId = this.BlockStorage[GetIndex(x, y, z)];

            return this.Palette.GetStateFromIndex(storageId);
        }

        public static int GetIndex(int x, int y, int z) => ((y * 16) + z) * 16 + x;

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

                        if (!block.IsAir)
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

        public void WriteTo(MinecraftStream stream)
        {
            short validBlockCount = 0;
            for (int x = 0; x < 16; x++)
            {
                for (int y = 0; y < 16; y++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        var block = this.Get(x, y, z);

                        if (!block.IsAir)
                            validBlockCount++;
                    }
                }
            }

            stream.WriteShort(validBlockCount);
            stream.WriteUnsignedByte(BitsPerBlock);

            Palette.WriteToAsync(stream);

            stream.WriteVarInt(BlockStorage.Storage.Length);

            long[] storage = BlockStorage.Storage;
            for (int i = 0; i < storage.Length; i++)
            {
                stream.WriteLong(storage[i]);
            }
        }
    }
}
