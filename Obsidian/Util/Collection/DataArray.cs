using System;

namespace Obsidian.Util.Collection
{

    public sealed class DataArray
    {
        private readonly byte BitsPerBlock;
        private readonly ulong BlockMask;

        public ulong[] Storage { get; }

        public int this[int serialIndex]//Changed this to int and everything fixed
        {
            get
            {
                var (indexOffset, bitOffset) = GetOffset(serialIndex);
                var toRead = Math.Min(BitsPerBlock, 64 - bitOffset);
                var value = Storage[indexOffset] >> bitOffset;
                var rest = BitsPerBlock - toRead;
                if (rest > 0)
                    value |= (Storage[indexOffset + 1] & ((1u << rest) - 1)) << toRead;
                return (int)value;
            }

            set
            {
                var stgValue = (uint)value & BlockMask;
                var (indexOffset, bitOffset) = GetOffset(serialIndex);
                var tmpValue = Storage[indexOffset];
                var mask = BlockMask << bitOffset;
                var toWrite = Math.Min(BitsPerBlock, 64 - bitOffset);
                Storage[indexOffset] = (tmpValue & ~mask) | (stgValue << bitOffset);
                var rest = BitsPerBlock - toWrite;
                if (rest > 0)
                {
                    mask = (1u << rest) - 1;
                    tmpValue = Storage[indexOffset + 1];
                    stgValue >>= toWrite;
                    Storage[indexOffset + 1] = (tmpValue & ~mask) | (stgValue & mask);
                }
            }
        }

        public DataArray(byte bitsPerBlock)
        {
            BitsPerBlock = bitsPerBlock;
            BlockMask = (1u << BitsPerBlock) - 1;
            Storage = new ulong[(16 * 16 * 16) * BitsPerBlock / 64];
        }

        private (int indexOffset, int bitOffset) GetOffset(int serialIndex)
        {
            var index = serialIndex * BitsPerBlock;
            return (index / 64, index % 64);
        }
    }
}
