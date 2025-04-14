using Obsidian.API.Utilities;

namespace Obsidian.API.ChunkData;
public abstract class DataContainer<T>
{
    public bool IsEmpty { get; }
    public byte BitsPerEntry => (byte)DataArray.BitsPerEntry;

    public abstract IPalette<T> Palette { get; internal set; }

    internal abstract DataArray DataArray { get; private protected set; }

    public virtual int GetIndex(int x, int y, int z) => (y << this.BitsPerEntry | z) << this.BitsPerEntry | x;

    public void GrowDataArray()
    {
        if (Palette.BitCount <= DataArray.BitsPerEntry)
            return;

        DataArray = DataArray.Grow(Palette.BitCount);
    }

    public abstract void Set(int x, int y, int z, T blockState);

    public abstract T Get(int x, int y, int z);

    public abstract void WriteTo(INetStreamWriter writer);

    public abstract DataContainer<T> Clone();
}
