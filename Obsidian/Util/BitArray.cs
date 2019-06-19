using System;

namespace Obsidian.Util
{
    public class BitArray
    {
        public long[] LongArray;

        public int BitsPerEntry;

        public long MaxEntryValue;

        public int ArraySize;

        public BitArray(int bitsPerEntryIn, int arraySizeIn)
        {
            if (bitsPerEntryIn < 1 || bitsPerEntryIn > 32)
                throw new InvalidOperationException();
            
            this.ArraySize = arraySizeIn;
            this.BitsPerEntry = bitsPerEntryIn;
            this.MaxEntryValue = (1L << bitsPerEntryIn) - 1L;
            this.LongArray = new long[(int)Math.Ceiling((arraySizeIn * bitsPerEntryIn) / 64.0)];
        }

        public long this[int index] { get => LongArray[index]; set => LongArray[index] = value; }

       
        public void Set(int index, int value)
        {
            if (index < 0 || index >= this.ArraySize)
                throw new IndexOutOfRangeException();
            if (value < 0 || value >= this.MaxEntryValue)
                throw new IndexOutOfRangeException();

            int i = index * this.BitsPerEntry;
            int j = i / 64;
            int k = ((index + 1) * this.BitsPerEntry - 1) / 64;
            int l = i % 64;
            this.LongArray[j] = this.LongArray[j] & ~(this.MaxEntryValue << l) | ((long)value & this.MaxEntryValue) << l;

            if (j != k)
            {
                int i1 = 64 - l;
                int j1 = this.BitsPerEntry - i1;
                this.LongArray[k] = this.LongArray[k].GetUnsignedRightShift(j1) << j1 | ((long)value & this.MaxEntryValue) >> i1;
            }
        }

        public int Get(int index)
        {
            if (index < 0 || index >= this.ArraySize)
                throw new IndexOutOfRangeException();

            int i = index * this.BitsPerEntry;
            int j = i / 64;
            int k = ((index + 1) * this.BitsPerEntry - 1) / 64;
            int l = i % 64;

            if (j == k)
            {
                return (int)(this.LongArray[j].GetUnsignedRightShift(l) & this.MaxEntryValue);
            }
            else
            {
                int i1 = 64 - l;
                return (int)((this.LongArray[j].GetUnsignedRightShift(l) | this.LongArray[k] << i1) & this.MaxEntryValue);
            }
        }

        public long[] getBackingLongArray()
        {
            return this.LongArray;
        }

        public int size()
        {
            return this.ArraySize;
        }
    }
}
