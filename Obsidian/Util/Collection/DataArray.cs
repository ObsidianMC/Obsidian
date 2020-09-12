using System;

namespace Obsidian.Util.Collection
{

    public sealed class DataArray
    {
        private readonly byte BitsPerBlock;
        private readonly ulong BlockMask;

        public ulong[] Storage { get; }

        public DataArray(byte bitsPerBlock, int arraySize = (16 * 16 * 16))
        {
            this.BitsPerBlock = bitsPerBlock;
            this.BlockMask = (1u << BitsPerBlock) - 1;
            this.Storage = new ulong[arraySize * this.BitsPerBlock / 64];
        }

        private (int indexOffset, int bitOffset) GetOffset(int serialIndex)
        {
            var index = serialIndex * this.BitsPerBlock;
            return (index / 64, index % 64);
        }

        public int this[int serialIndex]//Changed this to int and everything fixed
        {
            get
            {
                var (indexOffset, bitOffset) = this.GetOffset(serialIndex);
                var toRead = Math.Min(this.BitsPerBlock, 64 - bitOffset);
                var value = this.Storage[indexOffset] >> bitOffset;
                var rest = this.BitsPerBlock - toRead;
                if (rest > 0)
                    value |= (this.Storage[indexOffset + 1] & ((1u << rest) - 1)) << toRead;
                return (int)value;
            }

            set
            {
                var stgValue = (uint)value & BlockMask;
                var (indexOffset, bitOffset) = this.GetOffset(serialIndex);
                var tmpValue = this.Storage[indexOffset];
                var mask = this.BlockMask << bitOffset;
                var toWrite = Math.Min(this.BitsPerBlock, 64 - bitOffset);
                this.Storage[indexOffset] = (tmpValue & ~mask) | (stgValue << bitOffset);
                var rest = this.BitsPerBlock - toWrite;
                if (rest > 0)
                {
                    mask = (1u << rest) - 1;
                    tmpValue = this.Storage[indexOffset + 1];
                    stgValue >>= toWrite;
                    this.Storage[indexOffset + 1] = (tmpValue & ~mask) | (stgValue & mask);
                }
            }
        }
    }
}
