using System;

namespace Obsidian.Util.Collection
{

    public sealed class DataArray
    {
        private readonly byte bitsPerEntry;
        private readonly ulong blockMask;

        public ulong[] Storage { get; set; }

        public DataArray(byte bitsPerBlock, int arraySizeIn = (16 * 16 * 16))
        {
            this.bitsPerEntry = bitsPerBlock;
            this.blockMask = (1u << this.bitsPerEntry) - 1;
            this.Storage = new ulong[arraySizeIn * this.bitsPerEntry / 64];
        }

        private (int indexOffset, int bitOffset) GetOffset(int serialIndex)
        {
            var index = serialIndex * this.bitsPerEntry;
            return (index / 64, index % 64);
        }

        public int this[int serialIndex]
        {
            get
            {
                var (indexOffset, bitOffset) = this.GetOffset(serialIndex);
                var toRead = Math.Min(this.bitsPerEntry, 64 - bitOffset);
                var value = this.Storage[indexOffset] >> bitOffset;
                var rest = this.bitsPerEntry - toRead;
                if (rest > 0)
                    value |= (this.Storage[indexOffset + 1] & ((1u << rest) - 1)) << toRead;
                return (int)value;
            }

            set
            {
                var stgValue = (uint)value & this.blockMask;
                var (indexOffset, bitOffset) = this.GetOffset(serialIndex);
                var tmpValue = this.Storage[indexOffset];
                var mask = this.blockMask << bitOffset;
                var toWrite = Math.Min(this.bitsPerEntry, 64 - bitOffset);
                this.Storage[indexOffset] = (tmpValue & ~mask) | (stgValue << bitOffset);
                var rest = this.bitsPerEntry - toWrite;
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
