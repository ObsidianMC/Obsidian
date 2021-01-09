namespace Obsidian.Util.Collection
{
    public sealed class DataArray
    {
        private static readonly int[] yes = new int[] { -1, -1, 0, int.MinValue, 0, 0, 1431655765, 1431655765, 0, int.MinValue, 0, 1, 858993459, 858993459, 0, 715827882, 715827882, 0, 613566756, 613566756, 0, int.MinValue, 0, 2, 477218588, 477218588, 0, 429496729, 429496729, 0, 390451572, 390451572, 0, 357913941, 357913941, 0, 330382099, 330382099, 0, 306783378, 306783378, 0, 286331153, 286331153, 0, int.MinValue, 0, 3, 252645135, 252645135, 0, 238609294, 238609294, 0, 226050910, 226050910, 0, 214748364, 214748364, 0, 204522252, 204522252, 0, 195225786, 195225786, 0, 186737708, 186737708, 0, 178956970, 178956970, 0, 171798691, 171798691, 0, 165191049, 165191049, 0, 159072862, 159072862, 0, 153391689, 153391689, 0, 148102320, 148102320, 0, 143165576, 143165576, 0, 138547332, 138547332, 0, int.MinValue, 0, 4, 130150524, 130150524, 0, 126322567, 126322567, 0, 122713351, 122713351, 0, 119304647, 119304647, 0, 116080197, 116080197, 0, 113025455, 113025455, 0, 110127366, 110127366, 0, 107374182, 107374182, 0, 104755299, 104755299, 0, 102261126, 102261126, 0, 99882960, 99882960, 0, 97612893, 97612893, 0, 95443717, 95443717, 0, 93368854, 93368854, 0, 91382282, 91382282, 0, 89478485, 89478485, 0, 87652393, 87652393, 0, 85899345, 85899345, 0, 84215045, 84215045, 0, 82595524, 82595524, 0, 81037118, 81037118, 0, 79536431, 79536431, 0, 78090314, 78090314, 0, 76695844, 76695844, 0, 75350303, 75350303, 0, 74051160, 74051160, 0, 72796055, 72796055, 0, 71582788, 71582788, 0, 70409299, 70409299, 0, 69273666, 69273666, 0, 68174084, 68174084, 0, int.MinValue, 0, 5 };

        internal long[] Storage;
        private readonly int bitsPerEntry;
        private readonly long maxEntryValue;

        private int specialIndex;

        private readonly int magicNumber;

        public DataArray(int bitsPerEntryIn, int arraySizeIn)
        {
            this.bitsPerEntry = bitsPerEntryIn;
            this.maxEntryValue = (1L << bitsPerEntryIn) - 1L;
            this.magicNumber = (char)(64 / bitsPerEntryIn);

            this.specialIndex = 3 * (this.magicNumber - 1);

            int storageSize = (arraySizeIn + this.magicNumber - 1) / this.magicNumber;

            this.Storage = new long[storageSize];
        }

        public int this[int index]
        {
            get
            {
                int i = this.GetIndex(index);
                long j = this.Storage[i];
                int k = (index - i * this.magicNumber) * this.bitsPerEntry;
                return (int)(j >> k & this.maxEntryValue);
            }

            set
            {
                int i = this.GetIndex(index);
                long j = this.Storage[i];
                int k = (index - i * this.magicNumber) * this.bitsPerEntry;
                this.Storage[i] = j & ~(this.maxEntryValue << k) | (value & this.maxEntryValue) << k;
            }
        }

        private int GetIndex(int index)
        {
            long i = yes[specialIndex] & 0xffffffffL;
            long j = yes[specialIndex + 1] & 0xffffffffL;
            return (int)(index * i + j >> 32 >> yes[specialIndex + 2]);
        }
    }
}
