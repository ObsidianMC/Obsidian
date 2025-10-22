using Obsidian.API.Utilities;
using System.Threading;

namespace Obsidian.API.ChunkData;

public abstract class DataContainer<T>(byte minBitsPerEntry, byte maxBitsPerEntry, int maxEntryCount, Func<byte, IPalette<T>> paletteFactory)
{
    private readonly Lock dataLock = new();
    public virtual bool IsEmpty { get; }
    public byte BitsPerEntry => (byte)DataArray.BitsPerEntry;

    public byte MinBitsPerEntry { get; } = minBitsPerEntry;
    public byte MaxBitsPerEntry { get; } = maxBitsPerEntry;
    public int MaxEntryCount { get; } = maxEntryCount;

    public Func<byte, IPalette<T>> PaletteFactory { get; } = paletteFactory;

    public bool IsSingleValued => this.Palette is SingleValuePalette<T>;

    public abstract IPalette<T> Palette { get; internal set; }

    internal abstract DataArray? DataArray { get; private protected set; }

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
