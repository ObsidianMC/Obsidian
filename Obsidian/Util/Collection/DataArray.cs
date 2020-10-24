using System;

namespace Obsidian.Util.Collection
{
    public class DataArray
    {
        private static readonly int[] field_232981_a_ = new int[] { -1, -1, 0, int.MinValue, 0, 0, 1431655765, 1431655765, 0, int.MinValue, 0, 1, 858993459, 858993459, 0, 715827882, 715827882, 0, 613566756, 613566756, 0, int.MinValue, 0, 2, 477218588, 477218588, 0, 429496729, 429496729, 0, 390451572, 390451572, 0, 357913941, 357913941, 0, 330382099, 330382099, 0, 306783378, 306783378, 0, 286331153, 286331153, 0, int.MinValue, 0, 3, 252645135, 252645135, 0, 238609294, 238609294, 0, 226050910, 226050910, 0, 214748364, 214748364, 0, 204522252, 204522252, 0, 195225786, 195225786, 0, 186737708, 186737708, 0, 178956970, 178956970, 0, 171798691, 171798691, 0, 165191049, 165191049, 0, 159072862, 159072862, 0, 153391689, 153391689, 0, 148102320, 148102320, 0, 143165576, 143165576, 0, 138547332, 138547332, 0, int.MinValue, 0, 4, 130150524, 130150524, 0, 126322567, 126322567, 0, 122713351, 122713351, 0, 119304647, 119304647, 0, 116080197, 116080197, 0, 113025455, 113025455, 0, 110127366, 110127366, 0, 107374182, 107374182, 0, 104755299, 104755299, 0, 102261126, 102261126, 0, 99882960, 99882960, 0, 97612893, 97612893, 0, 95443717, 95443717, 0, 93368854, 93368854, 0, 91382282, 91382282, 0, 89478485, 89478485, 0, 87652393, 87652393, 0, 85899345, 85899345, 0, 84215045, 84215045, 0, 82595524, 82595524, 0, 81037118, 81037118, 0, 79536431, 79536431, 0, 78090314, 78090314, 0, 76695844, 76695844, 0, 75350303, 75350303, 0, 74051160, 74051160, 0, 72796055, 72796055, 0, 71582788, 71582788, 0, 70409299, 70409299, 0, 69273666, 69273666, 0, 68174084, 68174084, 0, int.MinValue, 0, 5 };
        public readonly ulong[] Storage;
        private readonly int bitsPerEntry;
        private readonly ulong maxEntryValue;
        private readonly int arraySize;
        private readonly int field_232982_f_;
        private readonly int field_232983_g_;
        private readonly int field_232984_h_;
        private readonly int field_232985_i_;

        public DataArray(int bitsPerEntryIn, int arraySizeIn) : this(bitsPerEntryIn, arraySizeIn, (ulong[])null) { }

        public DataArray(int bitsPerEntryIn, int arraySizeIn, ulong[] data)
        {
            this.arraySize = arraySizeIn;
            this.bitsPerEntry = bitsPerEntryIn;
            this.maxEntryValue = (1ul << bitsPerEntryIn) - 1ul;
            this.field_232982_f_ = (char)(64 / bitsPerEntryIn);
            int i = 3 * (this.field_232982_f_ - 1);
            this.field_232983_g_ = field_232981_a_[i + 0];
            this.field_232984_h_ = field_232981_a_[i + 1];
            this.field_232985_i_ = field_232981_a_[i + 2];
            int j = (arraySizeIn + this.field_232982_f_ - 1) / this.field_232982_f_;
            if (data != null)
            {
                if (data.Length != j)
                {
                    throw new InvalidOperationException("Invalid length given for storage, got: " + data.Length + " but expected: " + j);
                }

                this.Storage = data;
            }
            else
            {
                this.Storage = new ulong[j];
            }

        }

        private int func_232986_b_(int p_232986_1_)
        {
            var i = (ulong)(this.field_232983_g_) & 0xffffffffL;
            var j = (ulong)(this.field_232984_h_) & 0xffffffffL;
            return (int)((ulong)p_232986_1_ * i + j >> 32 >> this.field_232985_i_);
        }

        public int this[int index]
        {
            get
            {
                int i = this.func_232986_b_(index);
                var j = this.Storage[i];
                int k = (index - i * this.field_232982_f_) * this.bitsPerEntry;
                return (int)(j >> k & this.maxEntryValue);
            }

            set
            {
                int i = this.func_232986_b_(index);

                var j = this.Storage[i];
                int k = (index - i * this.field_232982_f_) * this.bitsPerEntry;
                this.Storage[i] = j & ~(this.maxEntryValue << k) | ((ulong)value & this.maxEntryValue) << k;
            }
        }
    }

    /* public sealed class DataArray
     {
         private readonly byte BitsPerBlock;
         private readonly ulong BlockMask;

         public ulong[] Storage { get; }

         public DataArray(byte bitsPerBlock, int arraySize = 4096)
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

         public int this[int serialIndex]
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
     }*/
}
