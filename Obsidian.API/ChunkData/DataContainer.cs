using Obsidian.API.Crafting.Builders.Interfaces;
using Obsidian.API.Utilities;
using System.Diagnostics;
using System.Threading;

namespace Obsidian.API.ChunkData;

public abstract class DataContainer<T>(byte minBitsPerEntry, byte maxBitsPerEntry, int maxEntryCount, Func<byte, IPalette<T>> paletteFactory)
{
    private readonly Lock dataLock = new();
    public virtual bool IsEmpty { get; }
    public byte BitsPerEntry => (byte)this.Palette.BitCount;

    public byte MinBitsPerEntry { get; } = minBitsPerEntry;
    public byte MaxBitsPerEntry { get; } = maxBitsPerEntry;
    public int MaxEntryCount { get; } = maxEntryCount;
    public Func<byte, IPalette<T>> PaletteFactory { get; } = paletteFactory;

    public bool IsSingleValued => this.Palette is SingleValuePalette<T>;

    public abstract IPalette<T> Palette { get; internal set; }

    internal DataArray? DataArray { get; private protected set; }

    public DataContainer(byte initialBitsPerEntry, byte minBitsPerEntry, byte maxBitsPerEntry, int maxEntryCount, Func<byte, IPalette<T>> paletteFactory)
        : this(minBitsPerEntry, maxBitsPerEntry, maxEntryCount, paletteFactory)
    {
        this.Palette = this.PaletteFactory(initialBitsPerEntry);

        if (!this.IsSingleValued)
        {
            if (this.MaxEntryCount <= 0)
                throw new InvalidOperationException("Cannot create a data array with a maximum entry count of 0 or less.");

            this.DataArray = new(this.MinBitsPerEntry, this.MaxEntryCount);
        }
    }

    public virtual int GetIndex(int x, int y, int z) => (y << this.BitsPerEntry | z) << this.BitsPerEntry | x;

    public bool TryGrow()
    {
        if (this.Palette is SingleValuePalette<T> singleValuePalette)
        {
            if (!singleValuePalette.ShouldGrow)
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
        lock (this.dataLock)
        {
            var index = GetIndex(x, y, z);

            int paletteId = this.Palette.GetOrAddId(value);

            if (this.TryGrow())
                paletteId = this.Palette.GetOrAddId(value);

            if (!this.IsSingleValued)
                this.DataArray[index] = paletteId;
        }
    }

    public virtual void Add(T value)
    {
        lock (this.dataLock)
        {
            int paletteId = this.Palette.GetOrAddId(value);

            if (this.TryGrow())
                this.Palette.GetOrAddId(value);
        }
    }

    public virtual T Get(int x, int y, int z)
    {
        lock (this.dataLock)
        {
            if (this.IsSingleValued)
                return this.Palette.GetValueFromIndex(0);

            int storageId = this.DataArray[GetIndex(x, y, z)];

            return this.Palette.GetValueFromIndex(storageId);
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
