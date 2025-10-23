using Obsidian.API.Utilities;
using System.Diagnostics;
using System.Threading;

namespace Obsidian.API.ChunkData;

public abstract class DataContainer<T>
{
    private readonly Lock dataLock = new();
    public virtual bool IsEmpty { get; }
    public byte BitsPerEntry => (byte)this.Palette.BitCount;

    public byte MinBitsPerEntry { get; }
    public byte MaxBitsPerEntry { get; }
    public int MaxEntryCount { get; }
    public Func<byte, IPalette<T>> PaletteFactory { get; }

    public bool IsSingleValued => this.Palette is SingleValuePalette<T>;

    public abstract IPalette<T> Palette { get; internal set; }

    internal abstract DataArray? DataArray { get; private protected set; }

    public DataContainer(byte minBitsPerEntry, byte maxBitsPerEntry, int maxEntryCount, Func<byte, IPalette<T>> paletteFactory)
    {
        this.MinBitsPerEntry = minBitsPerEntry;
        this.MaxBitsPerEntry = maxBitsPerEntry;
        this.MaxEntryCount = maxEntryCount;
        this.PaletteFactory = paletteFactory;
    }

    public virtual int GetIndex(int x, int y, int z) => (y << this.BitsPerEntry | z) << this.BitsPerEntry | x;

    public bool TryGrow(int receivedIndex)
    {
        if (this.Palette is SingleValuePalette<T> singleValuePalette)
        {
            if (receivedIndex != -1)
                return false;

            this.Palette = this.PaletteFactory(this.MinBitsPerEntry);
            this.Palette.GetOrAddId(singleValuePalette.Value);//Add the old value

            this.DataArray = new(this.MinBitsPerEntry, this.MaxEntryCount);//Initialize the data array

            return true;
        }

        //This should never happen, but just in case
        if (this.DataArray is null)
            throw new UnreachableException("Data array in unintialized.");

        if (Palette.BitCount <= DataArray.BitsPerEntry)
            return false;

        DataArray = DataArray.Grow(Palette.BitCount);

        return true;
    }

    public virtual void Set(int x, int y, int z, T value)
    {
        lock (dataLock)
        {
            var index = GetIndex(x, y, z);

            int paletteId = Palette.GetOrAddId(value);

            if (this.TryGrow(paletteId))
                paletteId = Palette.GetOrAddId(value);

            if (!this.IsSingleValued)
                this.DataArray[index] = paletteId;
        }
    }

    public virtual T Get(int x, int y, int z)
    {
        lock (dataLock)
        {
            if (this.IsSingleValued)
                return this.Palette.GetValueFromIndex(0);

            int storageId = DataArray[GetIndex(x, y, z)];

            return Palette.GetValueFromIndex(storageId);
        }
    }

    public virtual void WriteTo(INetStreamWriter writer)
    {
        writer.WriteByte(BitsPerEntry);

        Palette.WriteTo(writer);

        if (!this.IsSingleValued)
            writer.WriteLongArray(DataArray.storage);
    }

    public abstract DataContainer<T> Clone();
}
