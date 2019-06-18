using System;

namespace Obsidian.Util
{
    public class VariableValueArray
    {
        public long[] Backing { get; }

        public int Capacity { get; }
        public int BitsPerValue { get; }

        public long ValueMask { get; }

        public VariableValueArray(int bitsPerValue, int capacity)
        {
            if (capacity < 0)
                throw new ArgumentOutOfRangeException("capacity must not be negative");
            if (bitsPerValue < 1)
                throw new ArgumentOutOfRangeException("bitsPerValue must not be smaller then 1"));
            if (bitsPerValue > 64)
                throw new ArgumentOutOfRangeException("bitsPerValue must not be greater then 64");

            this.Backing = new long[(int)Math.Ceiling((bitsPerValue * capacity) / 64.0)];
            this.BitsPerValue = bitsPerValue;
            this.ValueMask = (1L << bitsPerValue) - 1L;
            this.Capacity = capacity;
        }

        public int Get(int index)
        {
            this.CheckIndex(index);

            index *= this.BitsPerValue;
            int i0 = index >> 6;
            int i1 = index & 0x3f;

            long value = (int)((uint)this.Backing[i0] >> i1);
            int i2 = i1 + this.BitsPerValue;
            // The value is divided over two long values
            if (i2 > 64)
            {
                value |= this.Backing[++i0] << (64 - i1);
            }

            return (int)(value & this.ValueMask);
        }

        public void Set(int index, int value)
        {
            this.CheckIndex(index);

            if (value < 0)
                throw new ArgumentOutOfRangeException("value must not be negative");

            if (value > this.ValueMask)
                throw new ArgumentOutOfRangeException($"value must not be greater then {this.ValueMask}");


            index *= this.BitsPerValue;
            int i0 = index >> 6;
            int i1 = index & 0x3f;

            this.Backing[i0] = (this.Backing[i0] & ~(this.ValueMask << i1)) | (value & this.ValueMask) << i1;
            int i2 = i1 + this.BitsPerValue;
            // The value is divided over two long values
            if (i2 > 64)
            {
                i0++;
                this.Backing[i0] = (this.Backing[i0] & ~((1L << (i2 - 64)) - 1L)) | (value >> (64 - i1));
            }
        }

        private void CheckIndex(int index)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException("index must not be negative");

            if (index >= this.Capacity)
                throw new ArgumentOutOfRangeException($"index must not be greater then the capacity {this.Capacity}");

        }
    }
}
