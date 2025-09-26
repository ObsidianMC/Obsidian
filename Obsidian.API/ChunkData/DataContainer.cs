using Obsidian.API.Utilities;
using System.Threading;

namespace Obsidian.API.ChunkData;

public abstract class DataContainer<T>
{
    private readonly Lock storageLock = new();

    public int EntryCount { get; protected set; }

    public virtual bool IsEmpty { get; }
    public byte BitsPerEntry => (byte)this.Palette.BitCount;

    public abstract IPalette<T> Palette { get; internal set; }

    internal virtual DataArray DataArray { get; private protected set; }

    public virtual int GetIndex(int x, int y, int z) => (y << this.BitsPerEntry | z) << this.BitsPerEntry | x;

    public DataContainer(int entryCount, IPalette<T> palette)
    {
        this.EntryCount = entryCount;
        this.Palette = palette;
        this.DataArray = BitsPerEntry != 0 ? new DataArray(BitsPerEntry, entryCount) : null;
    }

    public DataContainer(int entryCount, IPalette<T> palette, T defaultValue) : this(entryCount, palette)
    {
        this.Palette.GetOrAddId(defaultValue);
    }

    public void GrowDataArray()
    {
        if (Palette.BitCount <= DataArray.BitsPerEntry)
            return;

        DataArray = DataArray.Grow(Palette.BitCount);
    }

    public void InitializeDataArray()
    {
        if (this.Palette.BitCount == 0)
            return;

        this.DataArray = new DataArray(this.Palette.BitCount, this.EntryCount);
    }

    public virtual void Set(int x, int y, int z, T blockState)
    {
        lock (this.storageLock)
        {
            var blockIndex = GetIndex(x, y, z);

            int paletteId = Palette.GetOrAddId(blockState);

            if (this.Palette.BitCount == 0)
                return;

            DataArray ??= new DataArray(this.Palette.BitCount, this.EntryCount);

            this.GrowDataArray();

            DataArray[blockIndex] = paletteId;
        }
    }

    public virtual T Get(int x, int y, int z)
    {
        lock (this.storageLock)
        {
            if (this.Palette.BitCount == 0)
                return Palette.GetValueFromIndex(0);

            var index = GetIndex(x, y, z);
            int storageId = DataArray[index];

            return Palette.GetValueFromIndex(storageId);
        }
    }

    public virtual void WriteTo(INetStreamWriter writer)
    {
        writer.WriteByte(this.BitsPerEntry);

        this.Palette.WriteTo(writer);

        if (this.DataArray != null)
            writer.WriteLongArray(this.DataArray.storage);
    }

    public abstract DataContainer<T> Clone();
}
